// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Models;

namespace Framework.Features.FeatureManagement;

public interface IFeatureManagementProvider
{
    string Name { get; }

    //TODO: Other better method name.
    bool Compatible(string providerName);

    //TODO: Other better method name.
    Task<IAsyncDisposable> HandleContextAsync(string providerName, string providerKey);

    Task<string?> GetOrNullAsync(FeatureDefinition feature, string? providerKey);

    Task SetAsync(FeatureDefinition feature, string value, string? providerKey);

    Task ClearAsync(FeatureDefinition feature, string? providerKey);
}
