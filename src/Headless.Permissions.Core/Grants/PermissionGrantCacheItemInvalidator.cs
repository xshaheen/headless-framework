// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Domain;
using Headless.Permissions.Entities;

namespace Headless.Permissions.Grants;

public sealed class PermissionGrantCacheItemInvalidator(
    ICache<PermissionGrantCacheItem> cache,
    ICurrentTenant currentTenant
) : ILocalMessageHandler<EntityChangedEventData<PermissionGrantRecord>>
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
