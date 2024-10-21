// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Models;
using Framework.Kernel.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Framework.Features.Definitions;

public interface IFeatureDefinitionManager
{
    Task<FeatureDefinition?> GetOrNullAsync(string name);

    Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync();

    Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync();
}

public sealed class FeatureDefinitionManager : IFeatureDefinitionManager
{
    private readonly Lazy<Dictionary<string, FeatureGroupDefinition>> _lazyFeatureGroupDefinitions;
    private readonly Lazy<Dictionary<string, FeatureDefinition>> _lazyFeatureDefinitions;
    private readonly IServiceProvider _serviceProvider;
    private readonly FeatureManagementProviderOptions _providerOptions;

    public FeatureDefinitionManager(
        IOptions<FeatureManagementProviderOptions> options,
        IServiceProvider serviceProvider
    )
    {
        _serviceProvider = serviceProvider;
        _providerOptions = options.Value;
        _lazyFeatureDefinitions = new(_CreateFeatureDefinitions, isThreadSafe: true);
        _lazyFeatureGroupDefinitions = new(_CreateFeatureGroupDefinitions, isThreadSafe: true);
    }

    public async Task<FeatureDefinition> GetAsync(string name)
    {
        Argument.IsNotNull(name);

        var feature = await GetOrNullAsync(name);

        return feature ?? throw new InvalidOperationException("Undefined feature: " + name);
    }

    public Task<FeatureDefinition?> GetOrNullAsync(string name)
    {
        return Task.FromResult(_lazyFeatureDefinitions.Value.GetOrDefault(name));
    }

    public Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync()
    {
        return Task.FromResult<IReadOnlyList<FeatureDefinition>>(_lazyFeatureDefinitions.Value.Values.ToList());
    }

    public Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync()
    {
        return Task.FromResult<IReadOnlyList<FeatureGroupDefinition>>(
            _lazyFeatureGroupDefinitions.Value.Values.ToList()
        );
    }

    #region Helpers

    private Dictionary<string, FeatureGroupDefinition> _CreateFeatureGroupDefinitions()
    {
        var context = new FeatureDefinitionContext();

        using var scope = _serviceProvider.CreateScope();

        var providers = _providerOptions
            .DefinitionProviders.Select(p => (IFeatureDefinitionProvider)scope.ServiceProvider.GetRequiredService(p))
            .ToList();

        foreach (var provider in providers)
        {
            provider.Define(context);
        }

        return context.Groups;
    }

    private Dictionary<string, FeatureDefinition> _CreateFeatureDefinitions()
    {
        var features = new Dictionary<string, FeatureDefinition>(StringComparer.Ordinal);

        foreach (
            var feature in _lazyFeatureGroupDefinitions.Value.Values.SelectMany(groupDefinition =>
                groupDefinition.Features
            )
        )
        {
            _AddFeatureToDictionaryRecursively(features, feature);
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

    #endregion
}
