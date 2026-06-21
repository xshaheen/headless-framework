// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Features.Definitions;

/// <summary>Store for feature definitions that are registered statically at application startup via <see cref="IFeatureDefinitionProvider"/> implementations.</summary>
public interface IStaticFeatureDefinitionStore
{
    /// <summary>Returns the statically-registered feature definition with the given <paramref name="name"/>, or <see langword="null"/> if not found.</summary>
    /// <param name="name">The unique feature name to look up.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The matching <see cref="FeatureDefinition"/>, or <see langword="null"/> when absent.</returns>
    Task<FeatureDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns all statically-registered feature definitions.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A read-only list of all <see cref="FeatureDefinition"/> instances.</returns>
    Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all statically-registered feature group definitions.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A read-only list of all <see cref="FeatureGroupDefinition"/> instances.</returns>
    Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IStaticFeatureDefinitionStore"/> implementation that builds its catalog once (lazily, thread-safely)
/// by invoking all registered <see cref="IFeatureDefinitionProvider"/> instances and caching the result in memory.
/// </summary>
public sealed class StaticFeatureDefinitionStore : IStaticFeatureDefinitionStore
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FeatureManagementProvidersOptions _options;
    private readonly Lazy<Dictionary<string, FeatureGroupDefinition>> _lazyFeatureGroupDefinitions;
    private readonly Lazy<Dictionary<string, FeatureDefinition>> _lazyFeatureDefinitions;
    private Dictionary<string, FeatureGroupDefinition> FeatureGroupDefinitions => _lazyFeatureGroupDefinitions.Value;
    private Dictionary<string, FeatureDefinition> FeatureDefinitions => _lazyFeatureDefinitions.Value;

    /// <summary>Initializes the store and configures lazy initialization of the feature and group catalogs.</summary>
    /// <param name="serviceProvider">Used to create a scoped container when invoking definition providers.</param>
    /// <param name="optionsAccessor">Provides the list of registered <see cref="IFeatureDefinitionProvider"/> types.</param>
    public StaticFeatureDefinitionStore(
        IServiceProvider serviceProvider,
        IOptions<FeatureManagementProvidersOptions> optionsAccessor
    )
    {
        _serviceProvider = serviceProvider;
        _options = optionsAccessor.Value;
        _lazyFeatureDefinitions = new(_CreateFeatureDefinitions, isThreadSafe: true);
        _lazyFeatureGroupDefinitions = new(_CreateFeatureGroupDefinitions, isThreadSafe: true);
    }

    /// <inheritdoc/>
    public Task<FeatureDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FeatureDefinitions.GetOrDefault(name));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<FeatureDefinition>>(FeatureDefinitions.Values.ToList());
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<FeatureGroupDefinition>>(FeatureGroupDefinitions.Values.ToList());
    }

    #region Helpers

    private Dictionary<string, FeatureGroupDefinition> _CreateFeatureGroupDefinitions()
    {
        var context = new FeatureDefinitionContext();

#pragma warning disable MA0045 // Do not use blocking calls in a sync method (need to make calling method async)
        using var scope = _serviceProvider.CreateScope();
#pragma warning restore MA0045

        foreach (var type in _options.DefinitionProviders)
        {
            var provider = (IFeatureDefinitionProvider)scope.ServiceProvider.GetRequiredService(type);
            provider.Define(context);
        }

        return context.Groups;
    }

    /// <summary>
    /// Flattens all features from <see cref="FeatureGroupDefinitions"/> into a single dictionary keyed by name.
    /// </summary>
    /// <exception cref="InvalidOperationException">Two or more definition providers registered a feature with the same name.</exception>
    private Dictionary<string, FeatureDefinition> _CreateFeatureDefinitions()
    {
        var features = new Dictionary<string, FeatureDefinition>(StringComparer.Ordinal);

        foreach (var feature in FeatureGroupDefinitions.Values.SelectMany(x => x.Features))
        {
            addFeatureToDictionaryRecursively(features, feature);
        }

        return features;

        static void addFeatureToDictionaryRecursively(
            Dictionary<string, FeatureDefinition> features,
            FeatureDefinition feature
        )
        {
            if (!features.TryAdd(feature.Name, feature))
            {
                throw new InvalidOperationException($"Duplicate feature name: {feature.Name}");
            }

            foreach (var child in feature.Children)
            {
                addFeatureToDictionaryRecursively(features, child);
            }
        }
    }

    #endregion
}
