// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Models;
using Framework.Kernel.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Framework.Features.Definitions;

public interface IFeatureDefinitionManager
{
    Task<FeatureDefinition?> GetOrNullAsync(string name);

    Task<IReadOnlyList<FeatureDefinition>> GetAllAsync();

    Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync();
}

public sealed class FeatureDefinitionManager(
    IStaticFeatureDefinitionStore staticStore,
    IDynamicFeatureDefinitionStore dynamicStore
) : IFeatureDefinitionManager
{
    public async Task<FeatureDefinition?> GetOrNullAsync(string name)
    {
        Argument.IsNotNull(name);

        return await staticStore.GetOrNullAsync(name) ?? await dynamicStore.GetOrNullAsync(name);
    }

    public async Task<IReadOnlyList<FeatureDefinition>> GetAllAsync()
    {
        var staticFeatures = await staticStore.GetFeaturesAsync();
        var staticFeatureNames = staticFeatures.Select(p => p.Name).ToImmutableHashSet();

        var dynamicFeatures = await dynamicStore.GetFeaturesAsync();

        // We prefer static features over dynamics
        return staticFeatures
            .Concat(dynamicFeatures.Where(d => !staticFeatureNames.Contains(d.Name)))
            .ToImmutableList();
    }

    public async Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync()
    {
        var staticGroups = await staticStore.GetGroupsAsync();
        var staticGroupNames = staticGroups.Select(p => p.Name).ToImmutableHashSet();

        var dynamicGroups = await dynamicStore.GetGroupsAsync();

        // We prefer static groups over dynamics
        return staticGroups.Concat(dynamicGroups.Where(d => !staticGroupNames.Contains(d.Name))).ToImmutableList();
    }
}
