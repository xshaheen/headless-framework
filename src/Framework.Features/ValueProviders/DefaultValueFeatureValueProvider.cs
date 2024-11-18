// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Helpers.System;
using Framework.Features.Models;

namespace Framework.Features.ValueProviders;

[PublicAPI]
public sealed class DefaultValueFeatureValueProvider : IFeatureValueReadProvider
{
    public const string ProviderName = "DefaultValue";

    public string Name => ProviderName;

    public Task<IAsyncDisposable> HandleContextAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IAsyncDisposable>(NullAsyncDisposable.Instance);
    }

    public Task<string?> GetOrDefaultAsync(
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(feature.DefaultValue);
    }
}
