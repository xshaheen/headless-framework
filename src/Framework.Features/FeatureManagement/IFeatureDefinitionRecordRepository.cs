// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Features.FeatureManagement;

public interface IFeatureDefinitionRecordRepository
{
    Task<FeatureDefinitionRecord> FindByNameAsync(string name, CancellationToken cancellationToken = default);
}
