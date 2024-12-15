// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Entities;

namespace Framework.Features.Values;

public interface IFeatureValueRecordRepository
{
    Task<FeatureValueRecord?> FindAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<FeatureValueRecord>> FindAllAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<FeatureValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task InsertAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default);

    Task UpdateAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default);

    Task DeleteAsync(IEnumerable<FeatureValueRecord> featureValues, CancellationToken cancellationToken = default);
}
