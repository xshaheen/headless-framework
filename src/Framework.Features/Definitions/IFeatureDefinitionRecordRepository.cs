// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Entities;

namespace Framework.Features.Definitions;

public interface IFeatureDefinitionRecordRepository
{
    Task<List<FeatureGroupDefinitionRecord>> GetGroupsListAsync(CancellationToken cancellationToken = default);

    Task<List<FeatureDefinitionRecord>> GetFeaturesListAsync(CancellationToken cancellationToken = default);

    Task<FeatureDefinitionRecord> FindFeatureByNameAsync(string name, CancellationToken cancellationToken = default);
}
