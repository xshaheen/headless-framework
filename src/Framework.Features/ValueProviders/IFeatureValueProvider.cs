// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Models;

namespace Framework.Features.ValueProviders;

public interface IFeatureValueReadProvider
{
    string Name { get; }

    Task<IAsyncDisposable> HandleContextAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<string?> GetOrDefaultAsync(
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken = default
    );
}

public interface IFeatureValueProvider : IFeatureValueReadProvider
{
    Task SetAsync(
        FeatureDefinition feature,
        string value,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task ClearAsync(FeatureDefinition feature, string? providerKey, CancellationToken cancellationToken = default);
}
