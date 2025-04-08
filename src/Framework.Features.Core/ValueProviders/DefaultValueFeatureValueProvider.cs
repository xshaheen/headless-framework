// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Core;
using Framework.Features.Models;
using Framework.Features.Values;

namespace Framework.Features.ValueProviders;

[PublicAPI]
public sealed class DefaultValueFeatureValueProvider : IFeatureValueReadProvider
{
    public const string ProviderName = FeatureValueProviderNames.DefaultValue;

    public string Name => ProviderName;

    public Task<IAsyncDisposable> HandleContextAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(DisposableFactory.EmptyAsync);
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
