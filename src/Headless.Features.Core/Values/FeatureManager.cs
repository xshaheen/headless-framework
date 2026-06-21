// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Exceptions;
using Headless.Features.Definitions;
using Headless.Features.Models;
using Headless.Features.Resources;
using Headless.Features.ValueProviders;

namespace Headless.Features.Values;

/// <summary>Default implementation of <see cref="IFeatureManager"/> that walks the registered provider chain to resolve and mutate feature values.</summary>
public sealed class FeatureManager(
    IFeatureDefinitionManager definitionManager,
    IFeatureValueProviderManager valueProviderManager,
    IFeatureErrorsDescriptor errorsDescriptor
) : IFeatureManager
{
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
    /// <exception cref="ArgumentNullException"><paramref name="providerName"/> is <see langword="null"/>.</exception>
    public async Task<List<FeatureValue>> GetAllAsync(
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

        foreach (var definition in definitions)
        {
            foreach (var provider in providerList)
            {
                var pk = string.Equals(provider.Name, providerName, StringComparison.Ordinal) ? providerKey : null;
                var value = await provider.GetOrDefaultAsync(definition, pk, cancellationToken).ConfigureAwait(false);

                if (value is not null)
                {
                    featureValues[definition.Name] = new(definition.Name, value, new(provider.Name, pk));

                    break;
                }
            }
        }

        return [.. featureValues.Values];
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ConflictException">The feature named <paramref name="name"/> is not defined (<c>FeatureIsNotDefined</c>), the provider named <paramref name="providerName"/> is not registered (<c>FeatureProviderNotDefined</c>), or the provider is read-only (<c>ProviderIsReadonly</c>).</exception>
    public async Task SetAsync(
        string name,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(name);
        Argument.IsNotNull(providerName);

        var feature =
            await definitionManager.FindAsync(name, cancellationToken).ConfigureAwait(false)
            ?? throw new ConflictException(await errorsDescriptor.FeatureIsNotDefined(name).ConfigureAwait(false));

        var providers = valueProviderManager
            .ValueProviders.SkipWhile(p => !string.Equals(p.Name, providerName, StringComparison.Ordinal))
            .ToList();

        if (providers.Count == 0)
        {
            throw new ConflictException(
                await errorsDescriptor.FeatureProviderNotDefined(name, providerName).ConfigureAwait(false)
            );
        }

        if (providers.Count > 1 && !forceToSet && value is not null)
        {
            await using (
                await providers[0]
                    .HandleContextAsync(providerName, providerKey, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                var fallbackValue = await _CoreGetOrDefaultAsync(
                        name,
                        providers[1].Name,
                        providerKey: null,
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);

                if (string.Equals(fallbackValue.Value, value, StringComparison.Ordinal))
                {
                    // Clear the value if it is same as it's fallback value
                    value = null;
                }
            }
        }

        // Getting list for case of there are more than one provider with the same providerName
        providers = providers.TakeWhile(p => string.Equals(p.Name, providerName, StringComparison.Ordinal)).ToList();

        foreach (var provider in providers)
        {
            if (provider is not IFeatureValueProvider p)
            {
                throw new ConflictException(
                    await errorsDescriptor.ProviderIsReadonly(providerName).ConfigureAwait(false)
                );
            }

            if (value is null)
            {
                await p.ClearAsync(feature, providerKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await p.SetAsync(feature, value, providerKey, cancellationToken).ConfigureAwait(false);
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
        var featureNameValues = await GetAllAsync(providerName, providerKey, cancellationToken: cancellationToken)
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

        foreach (var featureNameValue in featureNameValues)
        {
            var feature =
                await definitionManager.FindAsync(featureNameValue.Name, cancellationToken).ConfigureAwait(false)
                ?? throw new ConflictException(
                    await errorsDescriptor.FeatureIsNotDefined(featureNameValue.Name).ConfigureAwait(false)
                );

            foreach (var provider in writableProviders)
            {
                await provider.ClearAsync(feature, providerKey, cancellationToken).ConfigureAwait(false);
            }
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
            ?? throw new ConflictException(await errorsDescriptor.FeatureIsNotDefined(name).ConfigureAwait(false));

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
