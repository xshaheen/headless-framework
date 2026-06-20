// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Domain;
using Headless.Permissions.Entities;

namespace Headless.Permissions.Grants;

/// <summary>
/// Domain event handler that removes the cached grant status entry whenever a
/// <see cref="PermissionGrantRecord"/> is created, updated, or deleted. Switches to the record's
/// tenant context before evicting so the scoped cache key matches the one used during reads.
/// </summary>
public sealed class PermissionGrantCacheItemInvalidator(
    ICache<PermissionGrantCacheItem> cache,
    ICurrentTenant currentTenant
) : IDomainEventHandler<EntityChangedEventData<PermissionGrantRecord>>
{
    public async ValueTask HandleAsync(
        EntityChangedEventData<PermissionGrantRecord> message,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(
            message.Entity.Name,
            message.Entity.ProviderName,
            message.Entity.ProviderKey
        );

        using (currentTenant.Change(message.Entity.TenantId))
        {
            await cache.RemoveAsync(cacheKey, cancellationToken);
        }
    }
}
