// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Entities;

namespace Framework.Features.Values;

public interface IFeatureValueRepository
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
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(string? providerName, string? providerKey, CancellationToken cancellationToken = default);

    Task UpdateAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default);

    Task DeleteAsync(FeatureValueRecord featureValue, CancellationToken cancellationToken = default);
}
