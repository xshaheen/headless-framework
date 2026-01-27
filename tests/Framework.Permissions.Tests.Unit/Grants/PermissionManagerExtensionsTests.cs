// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Grants;
using Framework.Testing.Tests;
using NSubstitute;

namespace Tests.Grants;

public sealed class PermissionManagerExtensionsTests : TestBase
{
    private readonly IPermissionManager _permissionManager = Substitute.For<IPermissionManager>();

    [Fact]
    public async Task should_grant_to_role()
    {
        // given
        const string permissionName = "TestPermission";
        const string roleName = "Admin";

        // when
        await _permissionManager.GrantToRoleAsync(permissionName, roleName, AbortToken);

        // then
        await _permissionManager
            .Received(1)
            .SetAsync(permissionName, PermissionGrantProviderNames.Role, roleName, isGranted: true, AbortToken);
    }

    [Fact]
    public async Task should_revoke_from_role()
    {
        // given
        const string permissionName = "TestPermission";
        const string roleName = "Admin";

        // when
        await _permissionManager.RevokeFromRoleAsync(permissionName, roleName, AbortToken);

        // then
        await _permissionManager
            .Received(1)
            .SetAsync(permissionName, PermissionGrantProviderNames.Role, roleName, isGranted: false, AbortToken);
    }

    [Fact]
    public async Task should_grant_to_user()
    {
        // given
        const string permissionName = "TestPermission";
        const string userId = "user-123";

        // when
        await _permissionManager.GrantToUserAsync(permissionName, userId, AbortToken);

        // then
        await _permissionManager
            .Received(1)
            .SetAsync(permissionName, PermissionGrantProviderNames.User, userId, isGranted: true, AbortToken);
    }

    [Fact]
    public async Task should_revoke_from_user()
    {
        // given
        const string permissionName = "TestPermission";
        const string userId = "user-123";

        // when
        await _permissionManager.RevokeFromUserAsync(permissionName, userId, AbortToken);

        // then
        await _permissionManager
            .Received(1)
            .SetAsync(permissionName, PermissionGrantProviderNames.User, userId, isGranted: false, AbortToken);
    }
}
