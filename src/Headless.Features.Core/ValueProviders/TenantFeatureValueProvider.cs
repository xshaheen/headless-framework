// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;
using Headless.Features.Models;
using Headless.Features.Values;

namespace Headless.Features.ValueProviders;

/// <summary>
/// Feature value provider that resolves values scoped to the current tenant.
/// <see cref="HandleContextAsync"/> temporarily switches the active tenant when a specific
/// <c>providerKey</c> is supplied, allowing cross-tenant feature reads.
/// </summary>
[PublicAPI]
public sealed class TenantFeatureValueProvider(IFeatureValueStore store, ICurrentTenant currentTenant)
    : StoreFeatureValueProvider(store)
{
    /// <summary>The well-known name used to identify this provider in the provider chain.</summary>
    public const string ProviderName = FeatureValueProviderNames.Tenant;

    /// <inheritdoc/>
    public override string Name => ProviderName;

    /// <summary>
    /// Switches the active tenant to <paramref name="providerKey"/> for the duration of the returned
    /// <see cref="IAsyncDisposable"/> when <paramref name="providerName"/> matches this provider and
    /// <paramref name="providerKey"/> is non-null/non-whitespace. Otherwise returns a no-op disposable.
    /// </summary>
    /// <param name="providerName">The name of the provider requesting the context switch.</param>
    /// <param name="providerKey">The tenant identifier to switch to, or <see langword="null"/> to skip the switch.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An <see cref="IAsyncDisposable"/> that restores the original tenant context on disposal.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled (delegated to base).</exception>
    public override Task<IAsyncDisposable> HandleContextAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.Equals(providerName, Name, StringComparison.Ordinal) || providerKey.IsNullOrWhiteSpace())
        {
            return base.HandleContextAsync(providerName, providerKey, cancellationToken);
        }

        var disposable = currentTenant.Change(providerKey);

        var asyncDisposable = DisposableFactory.Create(() =>
        {
            disposable.Dispose();

            return ValueTask.CompletedTask;
        });

        return Task.FromResult(asyncDisposable);
    }

    /// <summary>Returns the feature value for the current tenant, ignoring <paramref name="providerKey"/> in favour of the active tenant identifier.</summary>
    /// <param name="feature">The feature definition to look up.</param>
    /// <param name="providerKey">Unused; the active tenant ID from <see cref="ICurrentTenant"/> is always used.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The stored string value for the current tenant, or <see langword="null"/> if not set.</returns>
    public override Task<string?> GetOrDefaultAsync(
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        return Store.GetOrDefaultAsync(feature.Name, Name, currentTenant.Id, cancellationToken);
    }
}
