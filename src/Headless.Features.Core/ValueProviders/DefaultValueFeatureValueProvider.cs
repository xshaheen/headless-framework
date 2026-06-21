// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;
using Headless.Features.Models;
using Headless.Features.Values;

namespace Headless.Features.ValueProviders;

/// <summary>
/// Read-only provider that returns a feature's statically defined <see cref="FeatureDefinition.DefaultValue"/>.
/// Registered last so it acts as the lowest-priority fallback in the provider chain.
/// </summary>
[PublicAPI]
public sealed class DefaultValueFeatureValueProvider : IFeatureValueReadProvider
{
    /// <summary>The well-known name used to identify this provider in the provider chain.</summary>
    public const string ProviderName = FeatureValueProviderNames.DefaultValue;

    /// <inheritdoc/>
    public string Name => ProviderName;

    /// <inheritdoc/>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    public Task<IAsyncDisposable> HandleContextAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(DisposableFactory.EmptyAsync);
    }

    /// <inheritdoc/>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
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
