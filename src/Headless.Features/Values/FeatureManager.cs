// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Context;
using Headless.Features.Resources;
using Headless.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Features;

/// <summary>Default implementation of <see cref="IFeatureManager"/> that walks the registered provider chain to resolve and mutate feature values.</summary>
public sealed class FeatureManager(
    IFeatureDefinitionManager definitionManager,
    IFeatureValueProviderManager valueProviderManager,
    IFeatureErrorsDescriptor errorsDescriptor,
    IHostIdentityAccessor hostIdentity,
    IBus? bus = null,
    ILogger<FeatureManager>? logger = null
) : IFeatureManager
{
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

        return await _CoreGetOrDefaultAsync(name, providerName, providerKey, fallback, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, FeatureValue>> GetAllAsync(
        IReadOnlySet<string> featureNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(featureNames);

        var result = new Dictionary<string, FeatureValue>(featureNames.Count, StringComparer.Ordinal);

        if (featureNames.Count == 0)
        {
            return result;
        }

        // One definition read for the whole set instead of a FindAsync per name.
        var definitions = await definitionManager.GetFeaturesAsync(cancellationToken).ConfigureAwait(false);
        var providers = valueProviderManager.ValueProviders;

        // Each provider is asked once with the whole pending batch instead of once per definition: every
        // built-in provider resolves a batch read in one cache round-trip, so the previous per-definition
        // walk cost one round-trip per feature per provider. Providers are ordered highest priority first,
        // so the first non-null value wins and lower providers are asked only about the still-unresolved names.
        var pending = definitions.Where(d => featureNames.Contains(d.Name)).ToList();
        var processedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providers)
        {
            if (processedNames.Count >= pending.Count)
            {
                break;
            }

            var pendingForProvider = pending.Where(d => !processedNames.Contains(d.Name)).ToArray();
            var values = await provider
                .GetAllAsync(pendingForProvider, providerKey: null, cancellationToken)
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
            var values = await provider.GetAllAsync([.. pending], pk, cancellationToken).ConfigureAwait(false);
            var resolved = _ToResolvedValues(values);

            if (resolved.Count == 0)
            {
                continue;
            }

            var stillPending = new List<FeatureDefinition>(pending.Count);

            foreach (var definition in pending)
            {
                if (resolved.TryGetValue(definition.Name, out var value))
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
    /// <exception cref="ConflictException">The feature named <paramref name="name"/> is not defined (<c>FeatureIsNotDefined</c>), the provider named <paramref name="providerName"/> is not registered (<c>ProviderNotFound</c>), or the provider is read-only (<c>ProviderIsReadonly</c>).</exception>
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
    /// <exception cref="ConflictException">A feature in <paramref name="values"/> is not defined (<c>FeatureIsNotDefined</c>), the provider named <paramref name="providerName"/> is not registered (<c>ProviderNotFound</c>), or the provider is read-only (<c>ProviderIsReadonly</c>).</exception>
    public async Task SetAsync(
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(values);
        Argument.IsNotNull(providerName);

        if (values.Count == 0)
        {
            return;
        }

        var writes = new List<KeyValuePair<FeatureDefinition, string?>>(values.Count);

        foreach (var (name, value) in values)
        {
            Argument.IsNotNull(name);

            var feature =
                await definitionManager.FindAsync(name, cancellationToken).ConfigureAwait(false)
                ?? throw new ConflictException(errorsDescriptor.FeatureIsNotDefined(name));

            writes.Add(new(feature, value));
        }

        var providers = valueProviderManager
            .ValueProviders.SkipWhile(p => !string.Equals(p.Name, providerName, StringComparison.Ordinal))
            .ToList();

        if (providers.Count == 0)
        {
            throw new ConflictException(errorsDescriptor.ProviderNotFound(providerName));
        }

        // Getting list for case of there are more than one provider with the same providerName
        var writeProviders = new List<IFeatureValueProvider>();

        foreach (
            var provider in providers.TakeWhile(p => string.Equals(p.Name, providerName, StringComparison.Ordinal))
        )
        {
            // Rejected before anything is written: a read-only provider found half-way through the loop below
            // would otherwise leave the providers ahead of it already written.
            writeProviders.Add(
                provider as IFeatureValueProvider
                    ?? throw new ConflictException(errorsDescriptor.ProviderIsReadonly(providerName))
            );
        }

        // A batch of clears has nothing to compare, so it skips opening the provider context.
        if (providers.Count > 1 && !forceToSet && writes.Exists(write => write.Value is not null))
        {
            await _ClearFallbackEqualValuesAsync(writes, providers, providerName, providerKey, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var provider in writeProviders)
        {
            await provider.SetAllAsync(writes, providerKey, cancellationToken).ConfigureAwait(false);
        }

        await _PublishChangedAsync([.. values.Keys], providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);
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
        CancellationToken cancellationToken
    )
    {
        await using (
            await providers[0].HandleContextAsync(providerName, providerKey, cancellationToken).ConfigureAwait(false)
        )
        {
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
            await _PublishChangedAsync([.. removedNames], providerName, providerKey, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Announces a completed write so every instance holding a copy of the value, this one included, can re-read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Published after the write, never before: a receiver that re-read on an announcement of a write that then
    /// failed would cache the old value and believe it fresh. The write has already committed when this runs: the
    /// stores save through their own context or connection and never join a unit of work the caller has open, so a peer
    /// that re-reads on the announcement loads the new value. A <c>SetAsync</c> inside a unit that later rolls back
    /// therefore leaves the value changed; the consumer contract in <c>docs/llms/features.md</c> states this.
    /// </para>
    /// <para>
    /// Best-effort by design — messaging is optional, and a failed announcement must not fail the write that
    /// already succeeded, so publish failures are logged and swallowed. The cost of a lost message is that a
    /// peer keeps a stale copy until its own refresh, which is the behavior of a deployment with no bus at all.
    /// </para>
    /// </remarks>
    private async Task _PublishChangedAsync(
        string[] featureNames,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken
    )
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
            await bus.PublishAsync(message, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
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

    private async Task<FeatureValue> _CoreGetOrDefaultAsync(
        FeatureDefinition definition,
        string? providerName,
        string? providerKey,
        bool fallback,
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

        foreach (var provider in providers)
        {
            var pk = string.Equals(provider.Name, providerName, StringComparison.Ordinal) ? providerKey : null;
            var value = await provider.GetOrDefaultAsync(definition, pk, cancellationToken).ConfigureAwait(false);

            if (value is not null)
            {
                return new(name, value, new(provider.Name, pk));
            }
        }

        return new(name, Value: null, Provider: null);
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
    public static partial void LogFailedToPublishFeatureChanged(
        this ILogger logger,
        Exception exception,
        string providerName,
        string? providerKey,
        int nameCount
    );
}
