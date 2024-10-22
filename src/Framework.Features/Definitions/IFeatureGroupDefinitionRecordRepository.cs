// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Entities;

namespace Framework.Features.Definitions;

public interface IFeatureGroupDefinitionRecordRepository
{
    Task<List<FeatureGroupDefinitionRecord>> GetListAsync(CancellationToken cancellationToken = default);
}
