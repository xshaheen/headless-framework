// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Caching;
using Headless.Checks;
using Headless.Context;
using Headless.Messaging;
using Headless.MultiTenancy;
using Headless.Settings.Resources;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Settings;

/// <summary>
/// Core implementation of <see cref="ISettingManager"/> that resolves, encrypts, and persists setting values across the
/// registered provider stack. Also backs <c>unit.Settings</c> (<see cref="IUnitOfWorkSettings"/>), whose writes run
/// inside the caller's unit of work.
/// </summary>
public sealed class SettingManager(
    ISettingDefinitionManager definitionManager,
    ISettingValueStore valueStore,
    ISettingValueProviderManager valueProviderManager,
    ISettingEncryptionService encryptionService,
    ISettingErrorsDescriptor errorsDescriptor,
    IHostIdentityAccessor hostIdentity,
    ICurrentTenant currentTenant,
    IBus? bus = null,
    ILogger<SettingManager>? logger = null
) : ISettingManager, IUnitOfWorkSettings
{
    // A system message: a change made from host scope carries no tenant, and its announcement must still reach peers
    // when the host requires a tenant on every publish.
    private static readonly PublishOptions _ChangedPublishOptions = new() { IsSystemMessage = true };

    private readonly ILogger _logger = logger ?? NullLogger<SettingManager>.Instance;

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="settingName"/> is <see langword="null"/>.</exception>
    /// <exception cref="Headless.ConflictException">The setting named <paramref name="settingName"/> is not defined.</exception>
    public Task<SettingValue> GetAsync(
        string settingName,
        string? providerName = null,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return _CoreGetOrDefaultAsync(settingName, providerName, providerKey, fallback, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="settingNames"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="settingNames"/> is empty.</exception>
    public async Task<Dictionary<string, SettingValue>> GetAllAsync(
        HashSet<string> settingNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(settingNames);

        var allSettingDefinitions = await definitionManager.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var settingDefinitions = allSettingDefinitions.Where(x => settingNames.Contains(x.Name)).ToList();
        var definitionMap = settingDefinitions.ToDictionary(x => x.Name, StringComparer.Ordinal);

        // Accumulate the resolved value and its attributing provider per setting first, then build the
        // immutable SettingValue records at the end (Value is init-only on the record, so it cannot be
        // patched in place). The batch read carries no explicit provider key, so the attribution records
        // only the resolving provider name (Key stays null), matching GetAsync when called without a key.
        var resolvedValues = settingDefinitions.ToDictionary(x => x.Name, _ => (string?)null, StringComparer.Ordinal);
        var resolvedProviders = settingDefinitions.ToDictionary(
            x => x.Name,
            _ => (SettingValueProvider?)null,
            StringComparer.Ordinal
        );

        var processedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in valueProviderManager.Providers)
        {
            var supportedDefinitions = settingDefinitions
                .Where(x => !processedNames.Contains(x.Name) && _AllowsProvider(x, provider.Name))
                .ToArray();

            // A provider that supports none of the remaining definitions has nothing to read, and the store
            // refuses an empty name set, so asking it would fail the whole read.
            if (supportedDefinitions.Length == 0)
            {
                continue;
            }

            var settingValues = await provider
                .GetAllAsync(supportedDefinitions, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var notNullValues = settingValues.Where(x => x.Value != null).ToList();

            foreach (var settingValue in notNullValues)
            {
                var settingDefinition = definitionMap[settingValue.Name];

                // Only providers that persist manager-encrypted writes hold ciphertext; plaintext sources
                // (defaults, configuration) would throw on decrypt — same rule as _CoreGetOrDefaultAsync.
                var value =
                    settingDefinition.IsEncrypted && provider.StoresEncryptedValues
                        ? await encryptionService
                            .DecryptAsync(
                                settingDefinition,
                                settingValue.Value,
                                provider.Name,
                                providerKey: null,
                                cancellationToken
                            )
                            .ConfigureAwait(false)
                        : settingValue.Value;

                // Providers are ordered highest priority first, so the first non-null value wins.
                if (resolvedValues.TryGetValue(settingValue.Name, out var existing) && existing is null)
                {
                    resolvedValues[settingValue.Name] = value;
                    resolvedProviders[settingValue.Name] = new SettingValueProvider(provider.Name, Key: null);
                }
            }

            foreach (var sv in notNullValues)
            {
                processedNames.Add(sv.Name);
            }

            if (processedNames.Count >= settingDefinitions.Count)
            {
                break;
            }
        }

        return settingDefinitions.ToDictionary(
            x => x.Name,
            x => new SettingValue(x.Name, resolvedValues[x.Name], resolvedProviders[x.Name]),
            StringComparer.Ordinal
        );
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="providerName"/> is <see langword="null"/>.</exception>
    public async Task<IReadOnlyList<SettingValue>> GetAllAsync(
        string providerName,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(providerName);

        var settingDefinitions = await definitionManager.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return await _GetAllFromProviderAsync(
                settingDefinitions,
                providerName,
                providerKey,
                fallback,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="settingNames"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="providerName"/> is <see langword="null"/>.</exception>
    public async Task<IReadOnlyList<SettingValue>> GetAllAsync(
        HashSet<string> settingNames,
        string providerName,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(settingNames);
        Argument.IsNotNull(providerName);

        var allDefinitions = await definitionManager.GetAllAsync(cancellationToken).ConfigureAwait(false);

        // Narrowing to the requested names before the provider walk is the whole point: the caller pays
        // for the settings it asked for rather than for every setting the application happens to define.
        var settingDefinitions = allDefinitions.Where(x => settingNames.Contains(x.Name)).ToList();

        return await _GetAllFromProviderAsync(
                settingDefinitions,
                providerName,
                providerKey,
                fallback,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SettingValue>> _GetAllFromProviderAsync(
        IReadOnlyList<SettingDefinition> settingDefinitions,
        string providerName,
        string? providerKey,
        bool fallback,
        CancellationToken cancellationToken
    )
    {
        var providers = valueProviderManager.Providers.SkipWhile(c =>
            !string.Equals(c.Name, providerName, StringComparison.Ordinal)
        );

        if (!fallback)
        {
            providers = providers.TakeWhile(c => string.Equals(c.Name, providerName, StringComparison.Ordinal));
        }

        var providerList = providers.ToList();

        if (providerList.Count == 0)
        {
            return [];
        }

        var settingValues = new Dictionary<string, SettingValue>(StringComparer.Ordinal);

        // Non-inherited settings only ever consult the head of the chain while inherited ones walk it until
        // a value appears, so the two groups are resolved separately. Each provider is then asked once with
        // a batch instead of once per definition: every provider implements GetAllAsync as a single store
        // round-trip, so the previous nested loop cost one round-trip per definition per provider.
        var inherited = new List<SettingDefinition>();
        var notInherited = new List<SettingDefinition>();

        foreach (var setting in settingDefinitions)
        {
            (setting.IsInherited ? inherited : notInherited).Add(setting);
        }

        // SkipWhile guarantees the head of the chain is the requested provider, so it takes providerKey.
        var headProvider = providerList[0];
        notInherited = notInherited.FindAll(setting => _AllowsProvider(setting, headProvider.Name));

        if (notInherited.Count != 0)
        {
            var provider = headProvider;
            var values = await provider
                .GetAllAsync([.. notInherited], providerKey, cancellationToken)
                .ConfigureAwait(false);

            var resolved = _ToResolvedValues(values);

            foreach (var setting in notInherited)
            {
                if (resolved.TryGetValue(setting.Name, out var value))
                {
                    await _AddSettingValueAsync(settingValues, setting, value, provider, providerKey, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        var pending = inherited;

        foreach (var provider in providerList)
        {
            if (pending.Count == 0)
            {
                break;
            }

            var pk = string.Equals(provider.Name, providerName, StringComparison.Ordinal) ? providerKey : null;
            var allowed = pending.FindAll(setting => _AllowsProvider(setting, provider.Name));

            // A setting restricted to other providers skips this one rather than reading a value it may not hold.
            if (allowed.Count == 0)
            {
                continue;
            }

            var values = await provider.GetAllAsync([.. allowed], pk, cancellationToken).ConfigureAwait(false);
            var resolved = _ToResolvedValues(values);

            if (resolved.Count == 0)
            {
                continue;
            }

            var stillPending = new List<SettingDefinition>(pending.Count);

            foreach (var setting in pending)
            {
                if (_AllowsProvider(setting, provider.Name) && resolved.TryGetValue(setting.Name, out var value))
                {
                    await _AddSettingValueAsync(settingValues, setting, value, provider, pk, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    stillPending.Add(setting);
                }
            }

            pending = stillPending;
        }

        // Emit in definition order: the previous per-definition loop populated the dictionary that way.
        var orderedValues = new List<SettingValue>(settingValues.Count);

        foreach (var setting in settingDefinitions)
        {
            if (settingValues.TryGetValue(setting.Name, out var settingValue))
            {
                orderedValues.Add(settingValue);
            }
        }

        // The spread wraps the list in the compiler's throw-on-mutate read-only view, matching the previous
        // implementation's return shape — callers must not be able to downcast and mutate manager state.
        return [.. orderedValues];
    }

    /// <summary>Whether <paramref name="setting"/> may be read or written through the provider named <paramref name="providerName"/>; an empty list allows every provider.</summary>
    private static bool _AllowsProvider(SettingDefinition setting, string providerName)
    {
        return setting.Providers.Count == 0 || setting.Providers.Contains(providerName, StringComparer.Ordinal);
    }

    /// <summary>Indexes the values a provider actually stored; a <see langword="null"/> value means "not set here".</summary>
    private static Dictionary<string, string> _ToResolvedValues(List<SettingValue> providerValues)
    {
        var resolved = new Dictionary<string, string>(providerValues.Count, StringComparer.Ordinal);

        foreach (var providerValue in providerValues)
        {
            if (providerValue.Value is not null)
            {
                resolved[providerValue.Name] = providerValue.Value;
            }
        }

        return resolved;
    }

    /// <summary>Decrypts when required and records the resolved value against the provider that supplied it.</summary>
    private async ValueTask _AddSettingValueAsync(
        Dictionary<string, SettingValue> settingValues,
        SettingDefinition setting,
        string? value,
        ISettingValueReadProvider resolvedProvider,
        string? resolvedProviderKey,
        CancellationToken cancellationToken
    )
    {
        if (setting.IsEncrypted && resolvedProvider.StoresEncryptedValues)
        {
            value = await encryptionService
                .DecryptAsync(setting, value, resolvedProvider.Name, resolvedProviderKey, cancellationToken)
                .ConfigureAwait(false);
        }

        if (value is not null)
        {
            settingValues[setting.Name] = new SettingValue(
                setting.Name,
                value,
                new SettingValueProvider(resolvedProvider.Name, resolvedProviderKey)
            );
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="settingName"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="Headless.ConflictException">The setting named <paramref name="settingName"/> is not defined, the provider named <paramref name="providerName"/> is not registered, or the resolved provider does not support write operations.</exception>
    public Task SetAsync(
        string settingName,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(settingName);

        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { [settingName] = value };

        return SetAsync(values, providerName, providerKey, forceToSet, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="Headless.ConflictException">A setting in <paramref name="values"/> is not defined, the provider named <paramref name="providerName"/> is not registered, or the resolved provider does not support write operations.</exception>
    public Task SetAsync(
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return _SetCoreAsync(
            unitOfWork: null,
            values,
            providerName,
            providerKey,
            forceToSet,
            expectedVersion: null,
            cancellationToken
        );
    }

    /// <inheritdoc/>
    public async Task<VersionedSettingValues> GetVersionedAsync(
        IReadOnlyCollection<string> settingNames,
        string providerName,
        string? providerKey = null,
        bool fallback = false,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(settingNames);
        Argument.IsNotNull(providerName);

        var settings = new List<SettingDefinition>(settingNames.Count);

        // Ordinal, like the version: a duplicate in any other comparison is a different setting.
        foreach (var settingName in settingNames.Distinct(StringComparer.Ordinal))
        {
            settings.Add(await _FindWritableAsync(settingName, providerName, cancellationToken).ConfigureAwait(false));
        }

        var providers = _ProvidersFrom(providerName);
        var provider = _VersionedProvider(providerName, _WriteProviders(providerName, providers));
        var stored = await provider
            .GetAllVersionedAsync(settings, providerKey, cancellationToken)
            .ConfigureAwait(false);
        var storedByName = new Dictionary<string, string?>(stored.Values.Count, StringComparer.Ordinal);

        foreach (var storedValue in stored.Values)
        {
            storedByName[storedValue.Name] = storedValue.Value;
        }

        var resolved = new Dictionary<string, SettingValue>(settings.Count, StringComparer.Ordinal);
        var inheriting = new List<SettingDefinition>();

        foreach (var setting in settings)
        {
            if (storedByName.GetValueOrDefault(setting.Name) is { } storedValue)
            {
                await _AddSettingValueAsync(resolved, setting, storedValue, provider, providerKey, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (fallback && setting.IsInherited)
            {
                inheriting.Add(setting);
            }
        }

        // An absent value shows what the scope inherits, the value it would get if it kept storing none. The version
        // covers only what this scope stores, the part a versioned write replaces.
        var nextProvider = providers.Find(p => !string.Equals(p.Name, providerName, StringComparison.Ordinal));

        if (inheriting.Count != 0 && nextProvider is not null)
        {
            var inherited = await _GetAllFromProviderAsync(
                    inheriting,
                    nextProvider.Name,
                    providerKey: null,
                    fallback: true,
                    cancellationToken
                )
                .ConfigureAwait(false);

            foreach (var value in inherited)
            {
                resolved[value.Name] = value;
            }
        }

        var values = settings.ConvertAll(setting =>
            resolved.TryGetValue(setting.Name, out var value) ? value : new SettingValue(setting.Name, Value: null)
        );

        return new VersionedSettingValues(values, stored.Version);
    }

    /// <inheritdoc/>
    public Task<string> SetVersionedAsync(
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        string version,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(values);
        Argument.IsNotNullOrWhiteSpace(version);

        return _SetCoreAsync(
            unitOfWork: null,
            values,
            providerName,
            providerKey,
            forceToSet,
            version,
            cancellationToken
        );
    }

    /// <summary>Returns the definition of <paramref name="settingName"/> when it may be written through the provider.</summary>
    private async Task<SettingDefinition> _FindWritableAsync(
        string settingName,
        string providerName,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(settingName);

        var setting =
            await definitionManager.FindAsync(settingName, cancellationToken).ConfigureAwait(false)
            ?? throw new ConflictException(errorsDescriptor.NotDefined(settingName));

        if (!_AllowsProvider(setting, providerName))
        {
            throw new ConflictException(errorsDescriptor.ProviderNotAllowed(settingName, providerName));
        }

        return setting;
    }

    /// <summary>Returns the provider chain from the first provider named <paramref name="providerName"/>.</summary>
    private List<ISettingValueReadProvider> _ProvidersFrom(string providerName)
    {
        var providers = valueProviderManager
            .Providers.SkipWhile(p => !string.Equals(p.Name, providerName, StringComparison.Ordinal))
            .ToList();

        return providers.Count == 0
            ? throw new ConflictException(errorsDescriptor.ProviderNotFound(providerName))
            : providers;
    }

    /// <summary>Returns every provider registered under <paramref name="providerName"/>, all of which must be writable.</summary>
    private List<ISettingValueProvider> _WriteProviders(string providerName, List<ISettingValueReadProvider> providers)
    {
        var writeProviders = new List<ISettingValueProvider>();

        foreach (
            var provider in providers.TakeWhile(p => string.Equals(p.Name, providerName, StringComparison.Ordinal))
        )
        {
            // Rejected before anything is written: a read-only provider found half-way through the write loop would
            // otherwise leave the providers ahead of it already written.
            writeProviders.Add(
                provider as ISettingValueProvider
                    ?? throw new ConflictException(errorsDescriptor.ProviderIsReadonly(providerName))
            );
        }

        return writeProviders;
    }

    /// <summary>The one provider a versioned group belongs to: a version covers one store, never several.</summary>
    private static ISettingValueProvider _VersionedProvider(
        string providerName,
        List<ISettingValueProvider> writeProviders
    )
    {
        return writeProviders is [var provider]
            ? provider
            : throw new NotSupportedException(
                $"{writeProviders.Count} setting value providers are registered under the name '{providerName}'. "
                    + "A versioned read or write needs exactly one."
            );
    }

    /// <inheritdoc/>
    public Task SetAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);

        return _SetCoreAsync(
            unitOfWork,
            values,
            providerName,
            providerKey,
            forceToSet,
            expectedVersion: null,
            cancellationToken
        );
    }

    /// <summary>
    /// Validates every setting and the provider, resolves the values to store, and writes them through each provider
    /// registered under <paramref name="providerName"/>. With a unit, the fallback comparison and the writes run inside
    /// its transaction and the change is announced after it commits. With <paramref name="expectedVersion"/>, the one
    /// provider writes only while the group still has that version.
    /// </summary>
    private async Task<string> _SetCoreAsync(
        IUnitOfWork? unitOfWork,
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        bool forceToSet,
        string? expectedVersion,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(values);
        Argument.IsNotNull(providerName);

        if (values.Count == 0)
        {
            return string.Empty;
        }

        var requested = new List<KeyValuePair<SettingDefinition, string?>>(values.Count);

        foreach (var (settingName, value) in values)
        {
            var setting = await _FindWritableAsync(settingName, providerName, cancellationToken).ConfigureAwait(false);
            requested.Add(new(setting, value));
        }

        var providers = _ProvidersFrom(providerName);

        // A list for the case of more than one provider registered under the same name.
        var writeProviders = _WriteProviders(providerName, providers);

        if (expectedVersion is not null)
        {
            writeProviders = [_VersionedProvider(providerName, writeProviders)];
        }

        var currentEncrypted = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (expectedVersion is not null && requested.Exists(item => item.Key.IsEncrypted))
        {
            var stored = await writeProviders[0]
                .GetAllVersionedAsync(requested.ConvertAll(item => item.Key), providerKey, cancellationToken)
                .ConfigureAwait(false);
            foreach (var item in stored.Values)
            {
                currentEncrypted[item.Name] = item.Value;
            }
        }

        var writes = new List<KeyValuePair<SettingDefinition, string?>>(requested.Count);

        foreach (var (setting, value) in requested)
        {
            var storedValue = await _ResolveStoredValueAsync(
                    setting,
                    value,
                    providerName,
                    providerKey,
                    providers,
                    forceToSet,
                    unitOfWork,
                    currentEncrypted.GetValueOrDefault(setting.Name),
                    cancellationToken
                )
                .ConfigureAwait(false);

            writes.Add(new(setting, storedValue));
        }

        var committedVersion = string.Empty;

        foreach (var provider in writeProviders)
        {
            if (expectedVersion is not null)
            {
                try
                {
                    committedVersion = await provider
                        .SetAllVersionedAsync(writes, providerKey, expectedVersion, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (DBConcurrencyException)
                {
                    throw new ConflictException(errorsDescriptor.VersionMismatch(providerName));
                }
            }
            else if (unitOfWork is null)
            {
                await provider.SetAllAsync(writes, providerKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await provider
                    .SetAllEnlistedAsync(unitOfWork, writes, providerKey, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (unitOfWork is null)
        {
            await _PublishChangedAsync([.. values.Keys], providerName, providerKey).ConfigureAwait(false);
        }
        else
        {
            _PublishChangedAfterCommit(unitOfWork, [.. values.Keys], providerName, providerKey);
        }

        return committedVersion;
    }

    /// <summary>
    /// Returns the value to store for <paramref name="setting"/>: encrypted when the definition asks for it, and
    /// <see langword="null"/> when an inherited value equals what the next provider would supply anyway.
    /// </summary>
    private async Task<string?> _ResolveStoredValueAsync(
        SettingDefinition setting,
        string? value,
        string providerName,
        string? providerKey,
        List<ISettingValueReadProvider> providers,
        bool forceToSet,
        IUnitOfWork? unitOfWork,
        string? existingCiphertext,
        CancellationToken cancellationToken
    )
    {
        if (
            providers.Count > 1
            && !forceToSet
            && setting.IsInherited
            && value is not null
            && !_IsFallbackTenantUnknown(providerName, providerKey, providers)
        )
        {
            var fallbackValue = await _CoreGetOrDefaultAsync(
                    setting,
                    providers[1].Name,
                    providerKey: null,
                    fallback: true,
                    unitOfWork,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (string.Equals(fallbackValue.Value, value, StringComparison.Ordinal))
            {
                // Clear the value if it is same as it's fallback value
                return null;
            }
        }

        // Encrypted after the fallback check: the fallback read returns plaintext, and each encryption draws a
        // fresh nonce, so comparing ciphertext would never match.
        if (setting.IsEncrypted)
        {
            if (existingCiphertext is not null)
            {
                var plaintext = await encryptionService
                    .DecryptAsync(setting, existingCiphertext, providerName, providerKey, cancellationToken)
                    .ConfigureAwait(false);
                if (string.Equals(plaintext, value, StringComparison.Ordinal))
                {
                    return existingCiphertext;
                }
            }

            value = await encryptionService
                .EncryptAsync(setting, value, providerName, providerKey, cancellationToken)
                .ConfigureAwait(false);
        }

        return value;
    }

    /// <summary>
    /// Whether the write names a user the ambient tenant may not hold. The Tenant provider below the User provider reads
    /// the ambient tenant, so from host scope (no tenant) the comparison would run against the host's chain and could
    /// drop a value that differs from what the user really inherits from their own tenant. Writing for the current user
    /// (no key), or from inside the user's tenant, compares against the right chain.
    /// </summary>
    private bool _IsFallbackTenantUnknown(
        string providerName,
        string? providerKey,
        List<ISettingValueReadProvider> providers
    )
    {
        return providerKey is not null
            && currentTenant.Id is null
            && string.Equals(providerName, SettingValueProviderNames.User, StringComparison.Ordinal)
            && providers.Exists(p => string.Equals(p.Name, SettingValueProviderNames.Tenant, StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var settings = await valueStore
            .GetAllProviderValuesAsync(providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        foreach (var setting in settings)
        {
            await valueStore
                .DeleteAsync(setting.Name, providerName, providerKey, cancellationToken)
                .ConfigureAwait(false);
        }

        if (settings.Count != 0)
        {
            // Every removed name rather than a wildcard: a receiver should be able to match on the names it
            // holds without knowing what else this provider scope contained.
            var removedNames = settings.Select(x => x.Name).ToArray();

            await _PublishChangedAsync(removedNames, providerName, providerKey).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        IUnitOfWork unitOfWork,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);

        var removedNames = await valueStore
            .DeleteEnlistedAsync(unitOfWork, providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        _PublishChangedAfterCommit(unitOfWork, [.. removedNames], providerName, providerKey);
    }

    /// <summary>
    /// Announces an enlisted write once its unit commits, so a peer that re-reads on the announcement loads the
    /// committed value; a rolled-back unit announces nothing. The callback restores the write's tenant, which the
    /// announcement carries and the code completing the unit may no longer hold.
    /// </summary>
    private void _PublishChangedAfterCommit(
        IUnitOfWork unitOfWork,
        string[] settingNames,
        string providerName,
        string? providerKey
    )
    {
        if (bus is null || settingNames.Length == 0)
        {
            return;
        }

        var tenantId = currentTenant.Id;
        var tenantName = currentTenant.Name;

        unitOfWork.OnCompleted(async () =>
        {
            using (currentTenant.Change(tenantId, tenantName))
            {
                await _PublishChangedAsync(settingNames, providerName, providerKey).ConfigureAwait(false);
            }
        });
    }

    /// <summary>
    /// Announces a completed write so every instance holding a copy of the value, this one included, can re-read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Published after the write, never before: a receiver that re-read on an announcement of a write that then
    /// failed would cache the old value and believe it fresh. An autonomous write has committed in the store's own
    /// context or connection when this runs; an enlisted write publishes from the unit's post-commit callback.
    /// </para>
    /// <para>
    /// Best-effort by design — messaging is optional, and a failed announcement must not fail the write that
    /// already succeeded, so publish failures are logged and swallowed. The cost of a lost message is that a
    /// peer keeps a stale copy until its own refresh, which is the behavior of a deployment with no bus at all.
    /// </para>
    /// </remarks>
    private async Task _PublishChangedAsync(string[] settingNames, string providerName, string? providerKey)
    {
        if (bus is null || settingNames.Length == 0)
        {
            return;
        }

        var message = new SettingChangedMessage
        {
            SettingNames = settingNames,
            ProviderName = providerName,
            ProviderKey = providerKey,
            OriginHostName = hostIdentity.HostName,
        };

        try
        {
            // The change is committed, so a caller that stops waiting must not keep peers serving the old value.
            await bus.PublishAsync(message, _ChangedPublishOptions, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The commit is durable; a lost announcement is logged, and peers refresh on expiry.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogFailedToPublishSettingChanged(ex, providerName, providerKey, settingNames.Length);
        }
    }

    /// <summary>Resolves a setting value by walking the provider chain, applying decryption when required, and attributing the resolving provider.</summary>
    private async Task<SettingValue> _CoreGetOrDefaultAsync(
        string settingName,
        string? providerName,
        string? providerKey,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(settingName);

        if (!fallback)
        {
            Argument.IsNotNull(providerName);
        }

        var definition =
            await definitionManager.FindAsync(settingName, cancellationToken).ConfigureAwait(false)
            ?? throw new ConflictException(errorsDescriptor.NotDefined(settingName));

        return await _CoreGetOrDefaultAsync(definition, providerName, providerKey, fallback, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Walks the provider chain for an already resolved <paramref name="definition"/>.</summary>
    private async Task<SettingValue> _CoreGetOrDefaultAsync(
        SettingDefinition definition,
        string? providerName,
        string? providerKey,
        bool fallback,
        CancellationToken cancellationToken
    )
    {
        return await _CoreGetOrDefaultAsync(
                definition,
                providerName,
                providerKey,
                fallback,
                unitOfWork: null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Walks the provider chain for <paramref name="definition"/>; with a unit, each provider reads inside its
    /// transaction (<see cref="ISettingValueReadProvider.GetOrDefaultEnlistedAsync"/>).
    /// </summary>
    private async Task<SettingValue> _CoreGetOrDefaultAsync(
        SettingDefinition definition,
        string? providerName,
        string? providerKey,
        bool fallback,
        IUnitOfWork? unitOfWork,
        CancellationToken cancellationToken
    )
    {
        var settingName = definition.Name;
        IEnumerable<ISettingValueReadProvider> providers = valueProviderManager.Providers;

        if (providerName is not null)
        {
            providers = providers.SkipWhile(c => !string.Equals(c.Name, providerName, StringComparison.Ordinal));
        }

        // A non-inherited setting does not fall back below a provider the caller names. With no provider named the
        // read resolves the whole chain, as the batch read does; narrowing to "providers named null" would always
        // read nothing.
        if (!fallback || (!definition.IsInherited && providerName is not null))
        {
            providers = providers.TakeWhile(c => string.Equals(c.Name, providerName, StringComparison.Ordinal));
        }

        var chain = providers.Where(provider => _AllowsProvider(definition, provider.Name)).ToArray();
        var cached = unitOfWork is null
            ? await _ReadCachedChainAsync(chain, settingName, providerName, providerKey, cancellationToken)
                .ConfigureAwait(false)
            : null;

        for (var i = 0; i < chain.Length; i++)
        {
            var provider = chain[i];
            var pk = string.Equals(provider.Name, providerName, StringComparison.Ordinal) ? providerKey : null;
            var value =
                cached?[i].HasValue == true ? cached[i].Value?.Value
                : unitOfWork is null
                    ? await provider.GetOrDefaultAsync(definition, pk, cancellationToken).ConfigureAwait(false)
                : await provider
                    .GetOrDefaultEnlistedAsync(unitOfWork, definition, pk, cancellationToken)
                    .ConfigureAwait(false);

            if (value is null)
            {
                continue;
            }

            if (definition.IsEncrypted && provider.StoresEncryptedValues)
            {
                value = await encryptionService
                    .DecryptAsync(definition, value, provider.Name, pk, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new SettingValue(settingName, value, new SettingValueProvider(provider.Name, pk));
        }

        return new SettingValue(settingName, Value: null, Provider: null);
    }

    /// <summary>
    /// Reads the cached entry of every store-backed provider in <paramref name="chain"/> in one cache call, indexed
    /// like the chain; <see langword="null"/> when fewer than two providers read the default store's cache.
    /// </summary>
    /// <remarks>
    /// With a distributed cache each provider read is a network round trip, and the walk would otherwise make one per
    /// provider (user, then tenant, then global) for a setting set at none of them. A hit is what the provider's own
    /// read returns, so the walk keeps its order and results and calls a provider only for a miss.
    /// </remarks>
    private static async Task<CacheValue<SettingValueCacheItem>[]?> _ReadCachedChainAsync(
        ISettingValueReadProvider[] chain,
        string settingName,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        SettingValueStore? sharedStore = null;
        string?[]? cacheKeys = null;
        var count = 0;

        for (var i = 0; i < chain.Length; i++)
        {
            if (chain[i] is not StoreSettingValueProvider { Store: SettingValueStore store } storeProvider)
            {
                continue;
            }

            if (sharedStore is not null && !ReferenceEquals(sharedStore, store))
            {
                return null;
            }

            sharedStore = store;
            var pk = string.Equals(storeProvider.Name, providerName, StringComparison.Ordinal) ? providerKey : null;
            cacheKeys ??= new string?[chain.Length];
            cacheKeys[i] = SettingValueCacheItem.CalculateCacheKey(
                settingName,
                storeProvider.Name,
                storeProvider.ResolveProviderKey(pk)
            );
            count++;
        }

        if (sharedStore is null || cacheKeys is null || count < 2)
        {
            return null;
        }

        var entries = await sharedStore
            .GetCachedAsync(cacheKeys.OfType<string>(), cancellationToken)
            .ConfigureAwait(false);

        var result = new CacheValue<SettingValueCacheItem>[chain.Length];

        for (var i = 0; i < cacheKeys.Length; i++)
        {
            if (cacheKeys[i] is { } cacheKey && entries.TryGetValue(cacheKey, out var entry))
            {
                result[i] = entry;
            }
        }

        return result;
    }
}

/// <summary>Structured log helpers for <see cref="SettingManager"/>.</summary>
internal static partial class SettingManagerLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToPublishSettingChanged",
        Level = LogLevel.Warning,
        Message = "Failed to announce a setting change for provider {ProviderName} (key={ProviderKey}, names={NameCount}); the write succeeded and peers keep their copies until they re-read"
    )]
    internal static partial void LogFailedToPublishSettingChanged(
        this ILogger logger,
        Exception exception,
        string providerName,
        string? providerKey,
        int nameCount
    );
}
