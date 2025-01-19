// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Caching;
using Framework.Domains;
using Framework.Permissions.Entities;

namespace Framework.Permissions.Grants;

public sealed class PermissionGrantCacheItemInvalidator(
    ICache<PermissionGrantCacheItem> cache,
    ICurrentTenant currentTenant
) : ILocalMessageHandler<EntityChangedEventData<PermissionGrantRecord>>
{
    public async Task HandleAsync(
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
