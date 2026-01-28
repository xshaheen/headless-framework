// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Features.Definitions;

public interface IStaticFeatureDefinitionStore
{
    Task<FeatureDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);
}

public sealed class StaticFeatureDefinitionStore : IStaticFeatureDefinitionStore
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FeatureManagementProvidersOptions _options;
    private readonly Lazy<Dictionary<string, FeatureGroupDefinition>> _lazyFeatureGroupDefinitions;
    private readonly Lazy<Dictionary<string, FeatureDefinition>> _lazyFeatureDefinitions;
    private Dictionary<string, FeatureGroupDefinition> FeatureGroupDefinitions => _lazyFeatureGroupDefinitions.Value;
    private Dictionary<string, FeatureDefinition> FeatureDefinitions => _lazyFeatureDefinitions.Value;

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

    public Task<FeatureDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FeatureDefinitions.GetOrDefault(name));
    }

    public Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<FeatureDefinition>>(FeatureDefinitions.Values.ToList());
    }

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
