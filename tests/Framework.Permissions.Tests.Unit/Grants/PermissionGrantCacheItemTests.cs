// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Grants;
using Framework.Testing.Tests;

namespace Tests.Grants;

public sealed class PermissionGrantCacheItemTests : TestBase
{
    [Fact]
    public void should_calculate_cache_key_format()
    {
        // given
        const string name = "Users.Create";
        const string providerName = "RoleProvider";
        const string providerKey = "admin";

        // when
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey);

        // then
        cacheKey.Should().Be("permissions:provider:RoleProvider:admin,name:Users.Create");
    }

    [Fact]
    public void should_extract_permission_name_from_key()
    {
        // given
        const string cacheKey = "permissions:provider:RoleProvider:admin,name:Users.Create";

        // when
        var permissionName = PermissionGrantCacheItem.GetPermissionNameFormCacheKeyOrDefault(cacheKey);

        // then
        permissionName.Should().Be("Users.Create");
    }

    [Fact]
    public void should_return_null_for_invalid_key()
    {
        // given
        const string invalidKey = "invalid:cache:key:format";

        // when
        var permissionName = PermissionGrantCacheItem.GetPermissionNameFormCacheKeyOrDefault(invalidKey);

        // then
        permissionName.Should().BeNull();
    }

    [Fact]
    public void should_store_is_granted_value()
    {
        // given/when
        var grantedItem = new PermissionGrantCacheItem(isGranted: true);
        var deniedItem = new PermissionGrantCacheItem(isGranted: false);
        var undefinedItem = new PermissionGrantCacheItem(isGranted: null);

        // then
        grantedItem.IsGranted.Should().BeTrue();
        deniedItem.IsGranted.Should().BeFalse();
        undefinedItem.IsGranted.Should().BeNull();
    }
}
