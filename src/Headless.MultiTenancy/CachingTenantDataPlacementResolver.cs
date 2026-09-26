// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.MultiTenancy;

/// <summary>Cached entry for a resolved tenant data placement. There is no negative variant.</summary>
internal sealed class TenantDataPlacementCacheItem(TenantDataPlacement placement)
{
    public TenantDataPlacement Placement { get; } = placement;

    public static string CalculateCacheKey(string tenantId)
    {
        return $"tenancy:placement:{tenantId}";
    }
}

/// <summary>
/// Read-through cache in front of an app-supplied <see cref="ITenantDataPlacementResolver"/>, with the catalog's
/// fault rules (<see cref="TenantCacheOperations"/>): a cache fault degrades to a miss, a resolver fault propagates
/// unwrapped, and cancellation always propagates.
/// </summary>
/// <remarks>
/// The cache is the in-process tier only, never a distributed one: placements carry connection strings, which
/// usually hold credentials. A tenant with no placement is not cached, so a newly provisioned tenant routes as soon
/// as its placement exists instead of after an expiry window.
/// </remarks>
internal sealed class CachingTenantDataPlacementResolver<TResolver>(
    TResolver inner,
    IInMemoryCache cache,
    IOptions<TenantDataPlacementOptions> options,
    ILogger<CachingTenantDataPlacementResolver<TResolver>> logger
) : ITenantDataPlacementResolver
    where TResolver : class, ITenantDataPlacementResolver
{
    private readonly ICache<TenantDataPlacementCacheItem> _cache = new Cache<TenantDataPlacementCacheItem>(cache);

    public async Task<TenantDataPlacement?> ResolveAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(tenantId);

        var cacheKey = TenantDataPlacementCacheItem.CalculateCacheKey(tenantId);
        var cached = await TenantCacheOperations
            .TryGetAsync(logger, _cache, cacheKey, cancellationToken)
            .ConfigureAwait(false);

        if (cached is { HasValue: true, Value: not null })
        {
            return cached.Value.Placement;
        }

        var placement = await inner.ResolveAsync(tenantId, cancellationToken).ConfigureAwait(false);

        if (placement is not null)
        {
            await TenantCacheOperations
                .TryUpsertAsync(
                    logger,
                    _cache,
                    cacheKey,
                    new TenantDataPlacementCacheItem(placement),
                    options.Value.CacheExpiration,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return placement;
    }
}
