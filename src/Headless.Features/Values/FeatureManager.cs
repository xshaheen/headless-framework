// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Caching;
using Headless.Checks;
using Headless.Context;
using Headless.Features.Resources;
using Headless.Messaging;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Features;

/// <summary>
/// Default implementation of <see cref="IFeatureManager"/> that walks the registered provider chain to resolve and
/// mutate feature values. Also backs <c>unit.Features</c> (<see cref="IUnitOfWorkFeatures"/>), whose writes run inside
/// the caller's unit of work.
/// </summary>
public sealed class FeatureManager(
    IFeatureDefinitionManager definitionManager,
    IFeatureValueProviderManager valueProviderManager,
    IFeatureErrorsDescriptor errorsDescriptor,
    IHostIdentityAccessor hostIdentity,
    ICurrentTenant currentTenant,
    IBus? bus = null,
    ILogger<FeatureManager>? logger = null
) : IFeatureManager, IUnitOfWorkFeatures
{
    // A system message: a change made from host scope carries no tenant, and its announcement must still reach peers
    // when the host requires a tenant on every publish.
    private static readonly PublishOptions _ChangedPublishOptions = new() { IsSystemMessage = true };

    private readonly ILogger _logger = logger ?? NullLogger<FeatureManager>.Instance;

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>. Also thrown for <paramref name="providerName"/> when <paramref name="fallback"/> is <see langword="false"/>.</exception>
    /// <exception cref="ConflictException">The feature named <paramref name="name"/> is not defined.</exception>
    public async Task<FeatureValue> GetAsync(
        string name,
        string? providerName = null,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        if (!fallback)
        {
            Argument.IsNotNull(providerName);
        }

        await using (
            await _EnterProviderContextAsync(providerName, providerKey, fallback, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            return await _CoreGetOrDefaultAsync(name, providerName, providerKey, fallback, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, FeatureValue>> GetAllAsync(
        IReadOnlyCollection<string> featureNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(featureNames);

        if (featureNames.Count == 0)
        {
            return new Dictionary<string, FeatureValue>(StringComparer.Ordinal);
        }

        // Matching against an ordinal set keeps the definition filter linear and folds duplicate names.
        var requestedNames = featureNames.ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, FeatureValue>(requestedNames.Count, StringComparer.Ordinal);

        // One definition read for the whole set instead of a FindAsync per name.
        var definitions = await definitionManager.GetFeaturesAsync(cancellationToken).ConfigureAwait(false);
        var providers = valueProviderManager.ValueProviders;

        // Each provider is asked once with the whole pending batch instead of once per definition: every
        // built-in provider resolves a batch read in one cache round-trip, so the previous per-definition
        // walk cost one round-trip per feature per provider. Providers are ordered highest priority first,
        // so the first non-null value wins and lower providers are asked only about the still-unresolved names.
        var pending = definitions.Where(d => requestedNames.Contains(d.Name)).ToList();
        var processedNames = new HashSet<string>(StringComparer.Ordinal);
        var chain = providers.ToArray();
        var cached = await _ReadCachedChainAsync(
                chain,
                pending,
                providerName: null,
                providerKey: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        for (var i = 0; i < chain.Length; i++)
        {
            var provider = chain[i];

            if (processedNames.Count >= pending.Count)
            {
                break;
            }

            // A feature restricted to other providers never reads a value this provider holds.
            var pendingForProvider = pending
                .Where(d => !processedNames.Contains(d.Name) && _AllowsProvider(d, provider.Name))
                .ToArray();

            if (pendingForProvider.Length == 0)
            {
                continue;
            }

            var values =
                _TryAnswerFromCache(cached?[i]?.Entries, pendingForProvider)
                ?? await provider
                    .GetAllAsync(pendingForProvider, cached?[i]?.Key, cancellationToken)
                    .ConfigureAwait(false);

            foreach (var value in values)
            {
                // The first non-null value wins; a later value for an already-resolved name is ignored.
                if (value.Value is null || !processedNames.Add(value.Name))
                {
                    continue;
                }

                result[value.Name] = new(value.Name, value.Value, new(provider.Name, Key: null));
            }
        }

        // Every requested name that no provider resolved still appears, with a null value — the same
        // shape GetAsync(name) returns for a feature with no value anywhere in the chain.
        foreach (var definition in pending)
        {
            if (!processedNames.Contains(definition.Name))
            {
                result[definition.Name] = new(definition.Name, Value: null, Provider: null);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="providerName"/> is <see langword="null"/>.</exception>
    public async Task<IReadOnlyList<FeatureValue>> GetAllAsync(
        string providerName,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(providerName);

        var definitions = await definitionManager.GetFeaturesAsync(cancellationToken).ConfigureAwait(false);

        var providers = valueProviderManager.ValueProviders.SkipWhile(c =>
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

        var featureValues = new Dictionary<string, FeatureValue>(StringComparer.Ordinal);

        await using var context = await _EnterProviderContextAsync(
                providerName,
                providerKey,
                fallback,
                cancellationToken
            )
            .ConfigureAwait(false);

        // One batch read per provider instead of one read per definition per provider; the head of the
        // chain takes providerKey (SkipWhile guarantees it is the requested provider) and the rest read
        // the provider's default scope, exactly as the per-definition walk did.
        var pending = definitions.ToList();

        foreach (var provider in providerList)
        {
            if (pending.Count == 0)
            {
                break;
            }

            var pk = string.Equals(provider.Name, providerName, StringComparison.Ordinal) ? providerKey : null;
            var allowed = pending.FindAll(definition => _AllowsProvider(definition, provider.Name));

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

            var stillPending = new List<FeatureDefinition>(pending.Count);

            foreach (var definition in pending)
            {
                if (_AllowsProvider(definition, provider.Name) && resolved.TryGetValue(definition.Name, out var value))
                {
                    featureValues[definition.Name] = new(definition.Name, value, new(provider.Name, pk));
                }
                else
                {
                    stillPending.Add(definition);
                }
            }

            pending = stillPending;
        }

        // Emit in definition order: the previous per-definition loop populated the dictionary that way.
        var orderedValues = new List<FeatureValue>(featureValues.Count);

        foreach (var definition in definitions)
        {
            if (featureValues.TryGetValue(definition.Name, out var featureValue))
            {
                orderedValues.Add(featureValue);
            }
        }

        return [.. orderedValues];
    }

    /// <summary>Whether <paramref name="feature"/> may be read or written through the provider named <paramref name="providerName"/>; an empty list allows every provider.</summary>
    private static bool _AllowsProvider(FeatureDefinition feature, string providerName)
    {
        return feature.Providers.Count == 0 || feature.Providers.Contains(providerName, StringComparer.Ordinal);
    }

    /// <summary>Indexes the values a provider actually stored; a <see langword="null"/> value means "not set here".</summary>
    private static Dictionary<string, string> _ToResolvedValues(List<FeatureValue> providerValues)
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

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ConflictException">The feature named <paramref name="name"/> is not defined (<c>FeatureIsNotDefined</c>), the provider named <paramref name="providerName"/> is not registered (<c>ProviderNotFound</c>), the provider is read-only (<c>ProviderIsReadonly</c>), or the feature's <c>Providers</c> list excludes the provider (<c>features:provider-not-allowed</c>).</exception>
    public Task SetAsync(
        string name,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(name);

        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { [name] = value };

        return SetAsync(values, providerName, providerKey, forceToSet, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ConflictException">A feature in <paramref name="values"/> is not defined (<c>FeatureIsNotDefined</c>), the provider named <paramref name="providerName"/> is not registered (<c>ProviderNotFound</c>), the provider is read-only (<c>ProviderIsReadonly</c>), or the feature's <c>Providers</c> list excludes the provider (<c>features:provider-not-allowed</c>).</exception>
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
    public async Task<VersionedFeatureValues> GetVersionedAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string? providerKey = null,
        bool fallback = false,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(names);
        Argument.IsNotNull(providerName);

        var features = new List<FeatureDefinition>(names.Count);

        // Ordinal, like the version: a duplicate in any other comparison is a different feature.
        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            features.Add(await _FindWritableAsync(name, providerName, cancellationToken).ConfigureAwait(false));
        }

        var providers = _ProvidersFrom(providerName);
        var provider = _VersionedProvider(providerName, _WriteProviders(providerName, providers));
        var stored = await provider
            .GetAllVersionedAsync(features, providerKey, cancellationToken)
            .ConfigureAwait(false);
        var storedByName = new Dictionary<string, string?>(stored.Values.Count, StringComparer.Ordinal);

        foreach (var storedValue in stored.Values)
        {
            storedByName[storedValue.Name] = storedValue.Value;
        }

        var values = new List<FeatureValue>(features.Count);
        var nextProvider = providers.Find(p => !string.Equals(p.Name, providerName, StringComparison.Ordinal));

        // An absent value shows what the scope inherits, the value it would get if it kept storing none, read in the
        // scope's own context (for a tenant, its edition). The version covers only what this scope stores, the part a
        // versioned write replaces.
        await using (
            await providers[0].HandleContextAsync(providerName, providerKey, cancellationToken).ConfigureAwait(false)
        )
        {
            foreach (var feature in features)
            {
                if (storedByName.GetValueOrDefault(feature.Name) is { } storedValue)
                {
                    values.Add(new(feature.Name, storedValue, new(providerName, providerKey)));
                }
                else if (fallback && nextProvider is not null)
                {
                    var inherited = await _CoreGetOrDefaultAsync(
                            feature,
                            nextProvider.Name,
                            providerKey: null,
                            fallback: true,
                            cancellationToken
                        )
                        .ConfigureAwait(false);

                    values.Add(inherited);
                }
                else
                {
                    values.Add(new(feature.Name, Value: null));
                }
            }
        }

        return new VersionedFeatureValues(values, stored.Version);
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

    /// <summary>Returns the definition of <paramref name="name"/> when it may be written through the provider.</summary>
    private async Task<FeatureDefinition> _FindWritableAsync(
        string name,
        string providerName,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(name);

        var feature =
            await definitionManager.FindAsync(name, cancellationToken).ConfigureAwait(false)
            ?? throw new ConflictException(errorsDescriptor.FeatureIsNotDefined(name));

        // A feature that names its providers (for example only "Edition") must not take a value from another one: a
        // tenant value would otherwise override what the plan grants.
        if (!_AllowsProvider(feature, providerName))
        {
            throw new ConflictException(errorsDescriptor.ProviderNotAllowed(name, providerName));
        }

        return feature;
    }

    /// <summary>Returns the provider chain from the first provider named <paramref name="providerName"/>.</summary>
    private List<IFeatureValueReadProvider> _ProvidersFrom(string providerName)
    {
        var providers = valueProviderManager
            .ValueProviders.SkipWhile(p => !string.Equals(p.Name, providerName, StringComparison.Ordinal))
            .ToList();

        return providers.Count == 0
            ? throw new ConflictException(errorsDescriptor.ProviderNotFound(providerName))
            : providers;
    }

    /// <summary>Returns every provider registered under <paramref name="providerName"/>, all of which must be writable.</summary>
    private List<IFeatureValueProvider> _WriteProviders(string providerName, List<IFeatureValueReadProvider> providers)
    {
        var writeProviders = new List<IFeatureValueProvider>();

        foreach (
            var provider in providers.TakeWhile(p => string.Equals(p.Name, providerName, StringComparison.Ordinal))
        )
        {
            // Rejected before anything is written: a read-only provider found half-way through the write loop would
            // otherwise leave the providers ahead of it already written.
            writeProviders.Add(
                provider as IFeatureValueProvider
                    ?? throw new ConflictException(errorsDescriptor.ProviderIsReadonly(providerName))
            );
        }

        return writeProviders;
    }

    /// <summary>The one provider a versioned group belongs to: a version covers one store, never several.</summary>
    private static IFeatureValueProvider _VersionedProvider(
        string providerName,
        List<IFeatureValueProvider> writeProviders
    )
    {
        return writeProviders is [var provider]
            ? provider
            : throw new NotSupportedException(
                $"{writeProviders.Count} feature value providers are registered under the name '{providerName}'. "
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
    /// Validates every feature and the provider, clears values equal to their fallback, and writes them through each
    /// provider registered under <paramref name="providerName"/>. With a unit, the fallback comparison and the writes
    /// run inside its transaction and the change is announced after it commits. With <paramref name="expectedVersion"/>,
    /// the one provider writes only while the group still has that version.
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

        var writes = new List<KeyValuePair<FeatureDefinition, string?>>(values.Count);

        foreach (var (name, value) in values)
        {
            var feature = await _FindWritableAsync(name, providerName, cancellationToken).ConfigureAwait(false);
            writes.Add(new(feature, value));
        }

        var providers = _ProvidersFrom(providerName);

        // A list for the case of more than one provider registered under the same name.
        var writeProviders = _WriteProviders(providerName, providers);

        if (expectedVersion is not null)
        {
            writeProviders = [_VersionedProvider(providerName, writeProviders)];
        }

        // A batch of clears has nothing to compare, so it skips opening the provider context.
        if (providers.Count > 1 && !forceToSet && writes.Exists(write => write.Value is not null))
        {
            await _ClearFallbackEqualValuesAsync(
                    writes,
                    providers,
                    providerName,
                    providerKey,
                    unitOfWork,
                    cancellationToken
                )
                .ConfigureAwait(false);
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
                catch (DBConcurrencyException exception)
                {
                    throw new ConflictException(errorsDescriptor.VersionMismatch(providerName), exception);
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
    /// Replaces with <see langword="null"/> every value that equals what the next provider would supply, so the
    /// feature keeps inheriting it instead of pinning a copy.
    /// </summary>
    private async Task _ClearFallbackEqualValuesAsync(
        List<KeyValuePair<FeatureDefinition, string?>> writes,
        List<IFeatureValueReadProvider> providers,
        string providerName,
        string? providerKey,
        IUnitOfWork? unitOfWork,
        CancellationToken cancellationToken
    )
    {
        await using (
            await providers[0].HandleContextAsync(providerName, providerKey, cancellationToken).ConfigureAwait(false)
        )
        {
            // Without the edition of the tenant being written, the comparison would run against the wrong fallback
            // (the default, or the caller's own edition) and could drop a value that differs from what the tenant
            // really inherits. Keeping the value as written is the safe answer.
            if (!await _CanResolveFallbackEditionAsync(providers, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            for (var i = 0; i < writes.Count; i++)
            {
                var (feature, value) = writes[i];

                if (value is null)
                {
                    continue;
                }

                var fallbackValue = await _CoreGetOrDefaultAsync(
                        feature,
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
                    writes[i] = new(feature, value: null);
                }
            }
        }
    }

    private static async ValueTask<bool> _CanResolveFallbackEditionAsync(
        List<IFeatureValueReadProvider> providers,
        CancellationToken cancellationToken
    )
    {
        for (var i = 1; i < providers.Count; i++)
        {
            if (providers[i] is EditionFeatureValueProvider edition)
            {
                var resolution = await edition.ResolveCurrentEditionAsync(cancellationToken).ConfigureAwait(false);

                return resolution.IsKnown;
            }
        }

        return true;
    }

    /// <summary>
    /// Enters the context of the provider a read names with an explicit key (for the Tenant provider, that tenant),
    /// so the fallback providers below it resolve against that scope rather than the caller's.
    /// </summary>
    /// <remarks>
    /// Not an <see langword="async"/> method: a provider switches ambient (async-local) state synchronously, and an
    /// async method would discard that switch when it returns.
    /// </remarks>
    private Task<IAsyncDisposable> _EnterProviderContextAsync(
        string? providerName,
        string? providerKey,
        bool fallback,
        CancellationToken cancellationToken
    )
    {
        if (!fallback || providerName is null || providerKey is null)
        {
            return Task.FromResult(DisposableFactory.EmptyAsync);
        }

        var provider = valueProviderManager.ValueProviders.FirstOrDefault(p =>
            string.Equals(p.Name, providerName, StringComparison.Ordinal)
        );

        return provider is null
            ? Task.FromResult(DisposableFactory.EmptyAsync)
            : provider.HandleContextAsync(providerName, providerKey, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ConflictException">A feature record exists whose definition is not found (<c>FeatureIsNotDefined</c>).</exception>
    public async Task DeleteAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        // Only the values this scope stores. With fallback, a feature whose value lives in a lower provider
        // would be listed too, cleared as a no-op, and announced as changed when nothing changed for it.
        var featureNameValues = await GetAllAsync(
                providerName,
                providerKey,
                fallback: false,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        var providers = valueProviderManager.ValueProviders.SkipWhile(p =>
            !string.Equals(p.Name, providerName, StringComparison.Ordinal)
        );

        // Getting list for case of there are more than one provider with the same providerName
        providers = providers.TakeWhile(p => string.Equals(p.Name, providerName, StringComparison.Ordinal));

        var writableProviders = providers.OfType<IFeatureValueProvider>().ToList();

        if (writableProviders.Count == 0)
        {
            return;
        }

        var removedNames = new List<string>(featureNameValues.Count);

        foreach (var featureNameValue in featureNameValues)
        {
            var feature =
                await definitionManager.FindAsync(featureNameValue.Name, cancellationToken).ConfigureAwait(false)
                ?? throw new ConflictException(errorsDescriptor.FeatureIsNotDefined(featureNameValue.Name));

            foreach (var provider in writableProviders)
            {
                await provider.ClearAsync(feature, providerKey, cancellationToken).ConfigureAwait(false);
            }

            removedNames.Add(featureNameValue.Name);
        }

        if (removedNames.Count != 0)
        {
            await _PublishChangedAsync([.. removedNames], providerName, providerKey).ConfigureAwait(false);
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

        var writableProviders = valueProviderManager
            .ValueProviders.SkipWhile(p => !string.Equals(p.Name, providerName, StringComparison.Ordinal))
            .TakeWhile(p => string.Equals(p.Name, providerName, StringComparison.Ordinal))
            .OfType<IFeatureValueProvider>()
            .ToList();

        var removedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in writableProviders)
        {
            var names = await provider
                .DeleteAllEnlistedAsync(unitOfWork, providerKey, cancellationToken)
                .ConfigureAwait(false);

            removedNames.UnionWith(names);
        }

        _PublishChangedAfterCommit(unitOfWork, [.. removedNames], providerName, providerKey);
    }

    /// <summary>
    /// Announces an enlisted write once its unit commits, so a peer that re-reads on the announcement loads the
    /// committed value; a rolled-back unit announces nothing. The callback restores the write's tenant, which the
    /// announcement carries and the code completing the unit may no longer hold.
    /// </summary>
    private void _PublishChangedAfterCommit(
        IUnitOfWork unitOfWork,
        string[] featureNames,
        string providerName,
        string? providerKey
    )
    {
        if (bus is null || featureNames.Length == 0)
        {
            return;
        }

        var tenantId = currentTenant.Id;
        var tenantName = currentTenant.Name;

        unitOfWork.OnCompleted(async () =>
        {
            using (currentTenant.Change(tenantId, tenantName))
            {
                await _PublishChangedAsync(featureNames, providerName, providerKey).ConfigureAwait(false);
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
    private async Task _PublishChangedAsync(string[] featureNames, string providerName, string? providerKey)
    {
        if (bus is null || featureNames.Length == 0)
        {
            return;
        }

        var message = new FeatureChangedMessage
        {
            FeatureNames = featureNames,
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
            _logger.LogFailedToPublishFeatureChanged(ex, providerName, providerKey, featureNames.Length);
        }
    }

    private async Task<FeatureValue> _CoreGetOrDefaultAsync(
        string name,
        string? providerName,
        string? providerKey,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(name);

        if (!fallback)
        {
            Argument.IsNotNull(providerName);
        }

        var definition =
            await definitionManager.FindAsync(name, cancellationToken).ConfigureAwait(false)
            ?? throw new ConflictException(errorsDescriptor.FeatureIsNotDefined(name));

        return await _CoreGetOrDefaultAsync(definition, providerName, providerKey, fallback, cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<FeatureValue> _CoreGetOrDefaultAsync(
        FeatureDefinition definition,
        string? providerName,
        string? providerKey,
        bool fallback,
        CancellationToken cancellationToken
    )
    {
        return _CoreGetOrDefaultAsync(
            definition,
            providerName,
            providerKey,
            fallback,
            unitOfWork: null,
            cancellationToken
        );
    }

    /// <summary>
    /// Walks the provider chain for <paramref name="definition"/>; with a unit, each provider reads inside its
    /// transaction (<see cref="IFeatureValueReadProvider.GetOrDefaultEnlistedAsync"/>).
    /// </summary>
    private async Task<FeatureValue> _CoreGetOrDefaultAsync(
        FeatureDefinition definition,
        string? providerName,
        string? providerKey,
        bool fallback,
        IUnitOfWork? unitOfWork,
        CancellationToken cancellationToken
    )
    {
        var name = definition.Name;
        IEnumerable<IFeatureValueReadProvider> providers = valueProviderManager.ValueProviders;

        if (providerName is not null)
        {
            providers = providers.SkipWhile(c => !string.Equals(c.Name, providerName, StringComparison.Ordinal));
        }

        if (!fallback)
        {
            providers = providers.TakeWhile(c => string.Equals(c.Name, providerName, StringComparison.Ordinal));
        }

        var chain = providers.Where(provider => _AllowsProvider(definition, provider.Name)).ToArray();
        var cached = unitOfWork is null
            ? await _ReadCachedChainAsync(chain, [definition], providerName, providerKey, cancellationToken)
                .ConfigureAwait(false)
            : null;

        for (var i = 0; i < chain.Length; i++)
        {
            var provider = chain[i];
            var pk = string.Equals(provider.Name, providerName, StringComparison.Ordinal) ? providerKey : null;
            var value =
                cached?[i] is { } prefetched && prefetched.Entries.TryGetValue(name, out var hit) && hit.HasValue
                    ? hit.Value?.Value
                : unitOfWork is null
                    ? await provider
                        .GetOrDefaultAsync(definition, cached?[i]?.Key ?? pk, cancellationToken)
                        .ConfigureAwait(false)
                : await provider
                    .GetOrDefaultEnlistedAsync(unitOfWork, definition, pk, cancellationToken)
                    .ConfigureAwait(false);

            if (value is not null)
            {
                return new(name, value, new(provider.Name, pk));
            }
        }

        return new(name, Value: null, Provider: null);
    }

    /// <summary>
    /// Reads the cached entries of every built-in store provider in <paramref name="chain"/> for
    /// <paramref name="features"/> in one cache call, indexed like the chain and keyed by feature name;
    /// <see langword="null"/> when fewer than two providers read the default store's cache.
    /// </summary>
    /// <remarks>
    /// With a distributed cache each provider read is a network round trip, and the walk would otherwise make one per
    /// provider (tenant, then edition) for a feature set at neither. Only the sealed built-in providers qualify,
    /// because their reads are the store's reads unchanged; a hit is what that read returns, so the walk keeps its
    /// order and results and calls a provider only for a miss.
    /// </remarks>
    private static async Task<PrefetchedProvider?[]?> _ReadCachedChainAsync(
        IFeatureValueReadProvider[] chain,
        List<FeatureDefinition> features,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        if (features.Count == 0)
        {
            return null;
        }

        FeatureValueStore? sharedStore = null;
        string?[]? providerKeys = null;
        bool[]? prefetched = null;
        var count = 0;

        for (var i = 0; i < chain.Length; i++)
        {
            if (
                chain[i] is not (TenantFeatureValueProvider or EditionFeatureValueProvider)
                || chain[i] is not StoreFeatureValueProvider { Store: FeatureValueStore store } storeProvider
            )
            {
                continue;
            }

            if (sharedStore is not null && !ReferenceEquals(sharedStore, store))
            {
                return null;
            }

            var pk = string.Equals(storeProvider.Name, providerName, StringComparison.Ordinal) ? providerKey : null;
            string? resolvedKey;

            if (storeProvider is EditionFeatureValueProvider edition)
            {
                // Resolving an unknown edition can fail or perform I/O even when the tenant value is cached.
                if (!edition.TryResolveLocalKey(pk, out resolvedKey))
                {
                    continue;
                }
            }
            else
            {
                resolvedKey = await storeProvider.ResolveProviderKeyAsync(pk, cancellationToken).ConfigureAwait(false);
            }

            sharedStore = store;
            providerKeys ??= new string?[chain.Length];
            prefetched ??= new bool[chain.Length];
            providerKeys[i] = resolvedKey;
            prefetched[i] = true;
            count++;
        }

        if (sharedStore is null || providerKeys is null || prefetched is null || count < 2)
        {
            return null;
        }

        // Flat, indexed (provider index * feature count + feature index).
        var cacheKeys = new string[chain.Length * features.Count];
        var allKeys = new List<string>(count * features.Count);

        for (var i = 0; i < chain.Length; i++)
        {
            if (!prefetched[i])
            {
                continue;
            }

            for (var f = 0; f < features.Count; f++)
            {
                var cacheKey = FeatureValueCacheItem.CalculateCacheKey(
                    features[f].Name,
                    chain[i].Name,
                    providerKeys[i]
                );
                cacheKeys[(i * features.Count) + f] = cacheKey;
                allKeys.Add(cacheKey);
            }
        }

        var entries = await sharedStore.GetCachedAsync(allKeys, cancellationToken).ConfigureAwait(false);
        var result = new PrefetchedProvider?[chain.Length];

        for (var i = 0; i < chain.Length; i++)
        {
            if (!prefetched[i])
            {
                continue;
            }

            var byName = new Dictionary<string, CacheValue<FeatureValueCacheItem>>(
                features.Count,
                StringComparer.Ordinal
            );

            for (var f = 0; f < features.Count; f++)
            {
                if (entries.TryGetValue(cacheKeys[(i * features.Count) + f], out var entry))
                {
                    byName[features[f].Name] = entry;
                }
            }

            result[i] = new(providerKeys[i], byName);
        }

        return result;
    }

    private sealed record PrefetchedProvider(
        string? Key,
        Dictionary<string, CacheValue<FeatureValueCacheItem>> Entries
    );

    /// <summary>
    /// Answers a provider's batch read from its prefetched entries when every name is a hit, else returns
    /// <see langword="null"/> so the provider reads (and loads its misses) itself.
    /// </summary>
    private static List<FeatureValue>? _TryAnswerFromCache(
        Dictionary<string, CacheValue<FeatureValueCacheItem>>? entries,
        FeatureDefinition[] features
    )
    {
        if (entries is null)
        {
            return null;
        }

        var values = new List<FeatureValue>(features.Length);

        foreach (var feature in features)
        {
            if (!entries.TryGetValue(feature.Name, out var entry) || !entry.HasValue)
            {
                return null;
            }

            values.Add(new FeatureValue(feature.Name, entry.Value?.Value));
        }

        return values;
    }
}

/// <summary>Structured log helpers for <see cref="FeatureManager"/>.</summary>
internal static partial class FeatureManagerLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToPublishFeatureChanged",
        Level = LogLevel.Warning,
        Message = "Failed to announce a feature change for provider {ProviderName} (key={ProviderKey}, names={NameCount}); the write succeeded and peers keep their copies until they re-read"
    )]
    internal static partial void LogFailedToPublishFeatureChanged(
        this ILogger logger,
        Exception exception,
        string providerName,
        string? providerKey,
        int nameCount
    );
}
