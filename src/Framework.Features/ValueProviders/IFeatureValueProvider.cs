// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Models;

namespace Framework.Features.ValueProviders;

public interface IFeatureValueReadProvider
{
    string Name { get; }

    bool Compatible(string providerName);

    Task<IAsyncDisposable> HandleContextAsync(string providerName, string providerKey);

    Task<string?> GetOrDefaultAsync(FeatureDefinition feature, string? providerKey);
}

public interface IFeatureValueProvider : IFeatureValueReadProvider
{
    Task SetAsync(FeatureDefinition feature, string value, string? providerKey);

    Task ClearAsync(FeatureDefinition feature, string? providerKey);
}
