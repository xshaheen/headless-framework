// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Domain;
using Headless.Permissions.Entities;
using Headless.Permissions.Grants;
using Headless.Testing.Tests;

namespace Tests.Grants;

public sealed class PermissionGrantCacheItemInvalidatorTests : TestBase
{
    private readonly ICache<PermissionGrantCacheItem> _cache = Substitute.For<ICache<PermissionGrantCacheItem>>();
    private readonly ICurrentTenant _currentTenant = Substitute.For<ICurrentTenant>();
    private readonly PermissionGrantCacheItemInvalidator _sut;

    public PermissionGrantCacheItemInvalidatorTests()
    {
        _sut = new PermissionGrantCacheItemInvalidator(_cache, _currentTenant);
    }

    [Fact]
    public async Task should_invalidate_cache_on_entity_changed()
    {
        // given
        var entity = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Create",
            "RoleProvider",
            "admin",
            isGranted: true,
            tenantId: "tenant-1"
        );

        var eventData = new EntityChangedEventData<PermissionGrantRecord>(entity);
        var expectedCacheKey = PermissionGrantCacheItem.CalculateCacheKey(
            entity.Name,
            entity.ProviderName,
            entity.ProviderKey
        );

        _currentTenant.Change(entity.TenantId).Returns(Substitute.For<IDisposable>());

        // when
        await _sut.HandleAsync(eventData, AbortToken);

        // then
        _currentTenant.Received(1).Change(entity.TenantId);
        await _cache.Received(1).RemoveAsync(expectedCacheKey, AbortToken);
    }

    [Fact]
    public async Task should_handle_entity_without_tenant()
    {
        // given
        var entity = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.View",
            "UserProvider",
            "user-123",
            isGranted: false,
            tenantId: null
        );

        var eventData = new EntityChangedEventData<PermissionGrantRecord>(entity);
        var expectedCacheKey = PermissionGrantCacheItem.CalculateCacheKey(
            entity.Name,
            entity.ProviderName,
            entity.ProviderKey
        );

        _currentTenant.Change(null).Returns(Substitute.For<IDisposable>());

        // when
        await _sut.HandleAsync(eventData, AbortToken);

        // then
        _currentTenant.Received(1).Change(null);
        await _cache.Received(1).RemoveAsync(expectedCacheKey, AbortToken);
    }
}
