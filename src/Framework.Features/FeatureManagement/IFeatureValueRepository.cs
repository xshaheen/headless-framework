// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Features.FeatureManagement;

public interface IFeatureValueRepository
{
    Task<FeatureValue?> FindAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<FeatureValue>> FindAllAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<FeatureValue>> GetListAsync(
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(string? providerName, string? providerKey, CancellationToken cancellationToken = default);

    Task UpdateAsync(FeatureValue featureValue);

    Task DeleteAsync(FeatureValue featureValue);
}
