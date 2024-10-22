// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Entities;

namespace Framework.Features.Definitions;

public interface IFeatureDefinitionRecordRepository
{
    Task<FeatureDefinitionRecord> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<List<FeatureDefinitionRecord>> GetListAsync(CancellationToken cancellationToken = default);
}
