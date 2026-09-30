// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>
/// Evicts a tenant's cached data placements on this node, so a moved tenant routes to its new schema or database on
/// the next resolution instead of after <see cref="TenantDataPlacementOptions.CacheExpiration"/>. Call it on every
/// node after changing a tenant's placement; the placement cache is in-process only. Contexts already pinned to
/// the old placement keep it until they are disposed.
/// </summary>
[PublicAPI]
public interface ITenantDataPlacementCacheInvalidator
{
    /// <summary>Evicts every cached placement of the tenant whose canonical id is <paramref name="tenantId"/>.</summary>
    /// <param name="tenantId">The canonical tenant id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="tenantId"/> is <see langword="null"/>.</exception>
    Task InvalidateTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}

/// <summary>The invalidator for <c>UseResolver&lt;T&gt;()</c>, which caches placements in the in-memory tier.</summary>
internal sealed class TenantDataPlacementCacheInvalidator(IInMemoryCache cache) : ITenantDataPlacementCacheInvalidator
{
    private readonly Cache<TenantDataPlacementCacheItem> _cache = new(cache);

    public async Task InvalidateTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(tenantId);

        await _cache
            .RemoveByPrefixAsync(TenantDataPlacementCacheItem.CalculateTenantCachePrefix(tenantId), cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// The invalidator for <c>UseConfiguration(...)</c>, whose placements are bound once at startup and never cached:
/// there is nothing to evict, and a placement change there needs a restart.
/// </summary>
internal sealed class NullTenantDataPlacementCacheInvalidator : ITenantDataPlacementCacheInvalidator
{
    public Task InvalidateTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }
}
