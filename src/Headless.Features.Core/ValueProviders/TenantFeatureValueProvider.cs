// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;
using Headless.Features.Values;
using Headless.MultiTenancy;

namespace Headless.Features.ValueProviders;

/// <summary>
/// Feature value provider that resolves values scoped to a tenant: an explicit <c>providerKey</c> when the
/// caller supplies one, otherwise the ambient tenant from <see cref="ICurrentTenant"/>.
/// <see cref="HandleContextAsync"/> additionally switches the active tenant so code that reads the ambient
/// tenant (rather than the key) observes the requested one.
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

        var asyncDisposable = DisposableFactory.Create(
            disposable,
            static tenantScope =>
            {
                tenantScope.Dispose();

                return ValueTask.CompletedTask;
            }
        );

        return Task.FromResult(asyncDisposable);
    }

    /// <summary>Returns <paramref name="providerKey"/> when explicitly supplied, otherwise falls back to the current tenant identifier.</summary>
    /// <param name="providerKey">An explicit tenant key, or <see langword="null"/> to use the ambient tenant.</param>
    /// <returns>The resolved tenant key, or <see langword="null"/> when no tenant is active.</returns>
    protected override string? NormalizeProviderKey(string? providerKey)
    {
        return providerKey ?? currentTenant.Id;
    }
}
