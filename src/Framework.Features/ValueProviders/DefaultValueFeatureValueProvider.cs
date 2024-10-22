// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Models;
using Framework.Kernel.BuildingBlocks.Helpers.System;

namespace Framework.Features.ValueProviders;

[PublicAPI]
public sealed class DefaultValueFeatureValueProvider : IFeatureValueReadProvider
{
    public const string ProviderName = "DefaultValue";

    public string Name => ProviderName;

    public bool Compatible(string providerName)
    {
        return string.Equals(providerName, Name, StringComparison.Ordinal);
    }

    public Task<IAsyncDisposable> HandleContextAsync(string providerName, string? providerKey)
    {
        return Task.FromResult<IAsyncDisposable>(NullAsyncDisposable.Instance);
    }

    public Task<string?> GetOrDefaultAsync(FeatureDefinition feature, string? providerKey)
    {
        return Task.FromResult(feature.DefaultValue);
    }
}
