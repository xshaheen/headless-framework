// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Models;

namespace Framework.Features.Definitions;

public interface IFeatureDefinitionManager
{
    Task<FeatureDefinition?> FindAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);
}
