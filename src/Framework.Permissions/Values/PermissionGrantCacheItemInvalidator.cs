using Framework.Caching;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Domains;
using Framework.Permissions.Entities;
using Framework.Permissions.PermissionManagement;

namespace Framework.Permissions.Values;

public sealed class PermissionGrantCacheItemInvalidator(
    ICache<PermissionGrantCacheItem> cache,
    ICurrentTenant currentTenant
) : ILocalMessageHandler<EntityChangedEventData<PermissionGrant>>
{
    public async Task HandleAsync(
        EntityChangedEventData<PermissionGrant> message,
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
