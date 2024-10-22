// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Framework.Features.Definitions;

public interface IStaticFeatureDefinitionStore
{
    Task<FeatureDefinition?> GetOrNullAsync(string name);

    Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync();

    Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync();
}

public sealed class StaticFeatureDefinitionStore : IStaticFeatureDefinitionStore
{
    private Dictionary<string, FeatureGroupDefinition> FeatureGroupDefinitions => _lazyFeatureGroupDefinitions.Value;
    private readonly Lazy<Dictionary<string, FeatureGroupDefinition>> _lazyFeatureGroupDefinitions;

    private Dictionary<string, FeatureDefinition> FeatureDefinitions => _lazyFeatureDefinitions.Value;
    private readonly Lazy<Dictionary<string, FeatureDefinition>> _lazyFeatureDefinitions;

    private AbpFeatureOptions Options { get; }

    private readonly IServiceProvider _serviceProvider;

    public StaticFeatureDefinitionStore(IOptions<AbpFeatureOptions> options, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        Options = options.Value;

        _lazyFeatureDefinitions = new Lazy<Dictionary<string, FeatureDefinition>>(
            _CreateFeatureDefinitions,
            isThreadSafe: true
        );

        _lazyFeatureGroupDefinitions = new Lazy<Dictionary<string, FeatureGroupDefinition>>(
            _CreateFeatureGroupDefinitions,
            isThreadSafe: true
        );
    }

    public Task<FeatureDefinition?> GetOrNullAsync(string name)
    {
        return Task.FromResult(FeatureDefinitions.GetOrDefault(name));
    }

    public Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync()
    {
        return Task.FromResult<IReadOnlyList<FeatureDefinition>>(FeatureDefinitions.Values.ToList());
    }

    public Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync()
    {
        return Task.FromResult<IReadOnlyList<FeatureGroupDefinition>>(FeatureGroupDefinitions.Values.ToList());
    }

    #region Helpers


    private Dictionary<string, FeatureDefinition> _CreateFeatureDefinitions()
    {
        var features = new Dictionary<string, FeatureDefinition>(StringComparer.Ordinal);

        foreach (var featureGroupDefinition in FeatureGroupDefinitions.Values)
        {
            foreach (var feature in featureGroupDefinition.Features)
            {
                _AddFeatureToDictionaryRecursively(features, feature);
            }
        }

        return features;
    }

    private static void _AddFeatureToDictionaryRecursively(
        Dictionary<string, FeatureDefinition> features,
        FeatureDefinition feature
    )
    {
        if (!features.TryAdd(feature.Name, feature))
        {
            throw new InvalidOperationException("Duplicate feature name: " + feature.Name);
        }

        foreach (var child in feature.Children)
        {
            _AddFeatureToDictionaryRecursively(features, child);
        }
    }

    private Dictionary<string, FeatureGroupDefinition> _CreateFeatureGroupDefinitions()
    {
        var context = new FeatureDefinitionContext();

        using (var scope = _serviceProvider.CreateScope())
        {
            var providers = Options
                .DefinitionProviders.Select(p =>
                    (scope.ServiceProvider.GetRequiredService(p) as IFeatureDefinitionProvider)!
                )
                .ToList();

            foreach (var provider in providers)
            {
                provider.Define(context);
            }
        }

        return context.Groups;
    }

    #endregion
}
