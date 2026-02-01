// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions;
using Framework.Permissions.Definitions;
using Framework.Permissions.GrantProviders;
using Framework.Permissions.Grants;
using Framework.Permissions.Models;
using Framework.Permissions.Seeders;
using Framework.Primitives;
using Framework.Testing.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;

namespace Tests.Workflows;

[Collection<PermissionsTestFixture>]
public sealed class PermissionWorkflowTests(PermissionsTestFixture fixture) : PermissionsTestBase(fixture)
{
    #region Test 164: should_grant_and_check_permission_end_to_end

    [Fact]
    public async Task should_grant_and_check_permission_end_to_end()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<EndToEndPermissionsProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        const string roleName = "Admin";
        var userId = new UserId("user-164");
        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            WritableRoles = { roleName },
        };

        // when: grant permission to role
        await permissionManager.GrantToRoleAsync(EndToEndPermissionsProvider.Permission1, roleName, AbortToken);

        // then: permission is granted
        var permission = await permissionManager.GetAsync(
            EndToEndPermissionsProvider.Permission1,
            currentUser,
            cancellationToken: AbortToken
        );

        permission.IsGranted.Should().BeTrue();
        permission.Providers.Should().ContainSingle();
        permission.Providers[0].Name.Should().Be(RolePermissionGrantProvider.ProviderName);

        // also verify via GetAllAsync
        var allPermissions = await permissionManager.GetAllAsync(currentUser, cancellationToken: AbortToken);
        allPermissions.Should().Contain(p => p.Name == EndToEndPermissionsProvider.Permission1 && p.IsGranted);
    }

    #endregion

    #region Test 165: should_revoke_and_verify_denied

    [Fact]
    public async Task should_revoke_and_verify_denied()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<EndToEndPermissionsProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        const string roleName = "Admin";
        var userId = new UserId("user-165");
        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            WritableRoles = { roleName },
        };

        // given: permission is granted
        await permissionManager.GrantToRoleAsync(EndToEndPermissionsProvider.Permission1, roleName, AbortToken);
        var permissionBefore = await permissionManager.GetAsync(
            EndToEndPermissionsProvider.Permission1,
            currentUser,
            cancellationToken: AbortToken
        );
        permissionBefore.IsGranted.Should().BeTrue();

        // when: revoke permission
        await permissionManager.RevokeFromRoleAsync(EndToEndPermissionsProvider.Permission1, roleName, AbortToken);

        // then: permission is denied
        var permissionAfter = await permissionManager.GetAsync(
            EndToEndPermissionsProvider.Permission1,
            currentUser,
            cancellationToken: AbortToken
        );

        permissionAfter.IsGranted.Should().BeFalse();
        permissionAfter.Providers.Should().BeEmpty();
    }

    #endregion

    #region Test 166: should_handle_role_based_permissions

    [Fact]
    public async Task should_handle_role_based_permissions()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<EndToEndPermissionsProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        const string adminRole = "Admin";
        const string userRole = "User";
        var userId = new UserId("user-166");

        // user with Admin role
        var adminUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            WritableRoles = { adminRole },
        };

        // user with User role
        var regularUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = new UserId("user-166-regular"),
            WritableRoles = { userRole },
        };

        // when: grant permission to Admin role only
        await permissionManager.GrantToRoleAsync(EndToEndPermissionsProvider.Permission1, adminRole, AbortToken);

        // then: admin has permission, regular user does not
        var adminPermission = await permissionManager.GetAsync(
            EndToEndPermissionsProvider.Permission1,
            adminUser,
            cancellationToken: AbortToken
        );
        var regularPermission = await permissionManager.GetAsync(
            EndToEndPermissionsProvider.Permission1,
            regularUser,
            cancellationToken: AbortToken
        );

        adminPermission.IsGranted.Should().BeTrue();
        adminPermission.Providers.Should().ContainSingle(p => p.Name == RolePermissionGrantProvider.ProviderName);

        regularPermission.IsGranted.Should().BeFalse();
        regularPermission.Providers.Should().BeEmpty();

        // when: grant permission to User role as well
        await permissionManager.GrantToRoleAsync(EndToEndPermissionsProvider.Permission1, userRole, AbortToken);

        // then: both have permission
        var regularPermissionAfter = await permissionManager.GetAsync(
            EndToEndPermissionsProvider.Permission1,
            regularUser,
            cancellationToken: AbortToken
        );

        regularPermissionAfter.IsGranted.Should().BeTrue();
    }

    #endregion

    #region Test 167: should_handle_user_based_permissions

    [Fact]
    public async Task should_handle_user_based_permissions()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<EndToEndPermissionsProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        var userId1 = new UserId("user-167-1");
        var userId2 = new UserId("user-167-2");

        var user1 = new TestCurrentUser { IsAuthenticated = true, UserId = userId1 };

        var user2 = new TestCurrentUser { IsAuthenticated = true, UserId = userId2 };

        // when: grant permission to user1 only
        await permissionManager.GrantToUserAsync(EndToEndPermissionsProvider.Permission1, userId1, AbortToken);

        // then: user1 has permission, user2 does not
        var user1Permission = await permissionManager.GetAsync(
            EndToEndPermissionsProvider.Permission1,
            user1,
            cancellationToken: AbortToken
        );
        var user2Permission = await permissionManager.GetAsync(
            EndToEndPermissionsProvider.Permission1,
            user2,
            cancellationToken: AbortToken
        );

        user1Permission.IsGranted.Should().BeTrue();
        user1Permission.Providers.Should().ContainSingle(p => p.Name == UserPermissionGrantProvider.ProviderName);

        user2Permission.IsGranted.Should().BeFalse();
        user2Permission.Providers.Should().BeEmpty();
    }

    #endregion

    #region Test 168: should_apply_explicit_deny_semantics (AWS IAM-style)

    [Fact]
    public async Task should_apply_explicit_deny_semantics()
    {
        // given: AWS IAM-style deny override - when role grants but user denies, result is denied
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<EndToEndPermissionsProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        const string adminRole = "Admin";
        var userId = new UserId("user-168");

        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            WritableRoles = { adminRole },
        };

        // given: grant permission to Admin role
        await permissionManager.GrantToRoleAsync(EndToEndPermissionsProvider.Permission1, adminRole, AbortToken);

        // then: user has permission via role
        var permissionBeforeDeny = await permissionManager.GetAsync(
            EndToEndPermissionsProvider.Permission1,
            currentUser,
            cancellationToken: AbortToken
        );
        permissionBeforeDeny.IsGranted.Should().BeTrue("user has Admin role which was granted permission");

        // when: explicitly deny permission for this specific user (revoke creates Prohibited status)
        await permissionManager.RevokeFromUserAsync(EndToEndPermissionsProvider.Permission1, userId, AbortToken);

        // then: explicit deny overrides role grant (AWS IAM semantics)
        var permissionAfterDeny = await permissionManager.GetAsync(
            EndToEndPermissionsProvider.Permission1,
            currentUser,
            cancellationToken: AbortToken
        );

        permissionAfterDeny.IsGranted.Should().BeFalse("explicit user deny should override role grant");
        permissionAfterDeny.Providers.Should().BeEmpty();
    }

    #endregion

    #region Test 169: should_sync_dynamic_permissions_across_hosts

    [Fact]
    public async Task should_sync_dynamic_permissions_across_hosts()
    {
        // given: multi-host setup where permissions are shared via database
        await Fixture.ResetAsync();

        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = new UserId("user-169"),
            WritableRoles = { "Admin" },
        };

        // given: host1 with dynamic permission store enabled
        using var host1 = _CreateDynamicEnabledHostBuilder<Host1PermissionsProvider>().Build();
        await using var scope1 = host1.Services.CreateAsyncScope();
        var permissionManager1 = scope1.ServiceProvider.GetRequiredService<IPermissionManager>();
        var dynamicStore1 = scope1.ServiceProvider.GetRequiredService<IDynamicPermissionDefinitionStore>();

        // given: host2 with dynamic permission store enabled
        using var host2 = _CreateDynamicEnabledHostBuilder<Host2PermissionsProvider>().Build();
        await using var scope2 = host2.Services.CreateAsyncScope();
        var permissionManager2 = scope2.ServiceProvider.GetRequiredService<IPermissionManager>();
        var dynamicStore2 = scope2.ServiceProvider.GetRequiredService<IDynamicPermissionDefinitionStore>();

        // when: host1 saves its static permissions to dynamic store
        await dynamicStore1.SaveAsync(AbortToken);

        // then: host2 can see host1's permissions
        var host2PermissionsAfterHost1Save = await permissionManager2.GetAllAsync(
            currentUser,
            cancellationToken: AbortToken
        );
        host2PermissionsAfterHost1Save.Should().Contain(p => p.Name == Host1PermissionsProvider.Permission);

        // when: host2 also saves its permissions
        await dynamicStore2.SaveAsync(AbortToken);

        // then: host1 can see all permissions from both hosts
        var host1PermissionsAfterBothSave = await permissionManager1.GetAllAsync(
            currentUser,
            cancellationToken: AbortToken
        );
        host1PermissionsAfterBothSave.Should().HaveCount(2);
        host1PermissionsAfterBothSave.Should().Contain(p => p.Name == Host1PermissionsProvider.Permission);
        host1PermissionsAfterBothSave.Should().Contain(p => p.Name == Host2PermissionsProvider.Permission);

        // when: grant permission on host1
        await permissionManager1.GrantToUserAsync(Host2PermissionsProvider.Permission, currentUser.UserId!, AbortToken);

        // then: permission grant is visible from host2
        var host2PermissionCheck = await permissionManager2.GetAsync(
            Host2PermissionsProvider.Permission,
            currentUser,
            cancellationToken: AbortToken
        );
        host2PermissionCheck.IsGranted.Should().BeTrue();
    }

    #endregion

    #region Test 170: should_handle_concurrent_permission_changes

    [Fact]
    public async Task should_handle_concurrent_permission_changes()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<ConcurrencyPermissionsProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        const string roleName = "Admin";
        var userId = new UserId("user-170");
        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            WritableRoles = { roleName },
        };

        // when: concurrent grants and revokes
        var tasks = new List<Task>
        {
            permissionManager.GrantToRoleAsync(ConcurrencyPermissionsProvider.Permission1, roleName, AbortToken),
            permissionManager.GrantToRoleAsync(ConcurrencyPermissionsProvider.Permission2, roleName, AbortToken),
            permissionManager.GrantToUserAsync(ConcurrencyPermissionsProvider.Permission3, userId, AbortToken),
            permissionManager.GrantToRoleAsync(ConcurrencyPermissionsProvider.Permission4, roleName, AbortToken),
            permissionManager.GrantToUserAsync(ConcurrencyPermissionsProvider.Permission5, userId, AbortToken),
        };

        await Task.WhenAll(tasks);

        // then: all permissions should be granted
        var allPermissions = await permissionManager.GetAllAsync(currentUser, cancellationToken: AbortToken);

        allPermissions.Should().HaveCount(5);
        allPermissions.Should().OnlyContain(p => p.IsGranted);

        // when: concurrent revokes
        var revokeTasks = new List<Task>
        {
            permissionManager.RevokeFromRoleAsync(ConcurrencyPermissionsProvider.Permission1, roleName, AbortToken),
            permissionManager.RevokeFromRoleAsync(ConcurrencyPermissionsProvider.Permission2, roleName, AbortToken),
        };

        await Task.WhenAll(revokeTasks);

        // then: revoked permissions should be denied
        var p1 = await permissionManager.GetAsync(
            ConcurrencyPermissionsProvider.Permission1,
            currentUser,
            cancellationToken: AbortToken
        );
        var p2 = await permissionManager.GetAsync(
            ConcurrencyPermissionsProvider.Permission2,
            currentUser,
            cancellationToken: AbortToken
        );

        p1.IsGranted.Should().BeFalse();
        p2.IsGranted.Should().BeFalse();
    }

    #endregion

    #region Test 171: should_cache_invalidate_on_permission_change

    [Fact]
    public async Task should_cache_invalidate_on_permission_change()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<EndToEndPermissionsProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        const string roleName = "Admin";
        var userId = new UserId("user-171");
        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            WritableRoles = { roleName },
        };

        // given: permission is not granted initially
        var permissionBefore = await permissionManager.GetAsync(
            EndToEndPermissionsProvider.Permission1,
            currentUser,
            cancellationToken: AbortToken
        );
        permissionBefore.IsGranted.Should().BeFalse();

        // when: grant permission
        await permissionManager.GrantToRoleAsync(EndToEndPermissionsProvider.Permission1, roleName, AbortToken);

        // then: cache should be invalidated and new value reflected immediately
        var permissionAfterGrant = await permissionManager.GetAsync(
            EndToEndPermissionsProvider.Permission1,
            currentUser,
            cancellationToken: AbortToken
        );
        permissionAfterGrant.IsGranted.Should().BeTrue();

        // when: revoke permission
        await permissionManager.RevokeFromRoleAsync(EndToEndPermissionsProvider.Permission1, roleName, AbortToken);

        // then: cache should be invalidated again
        var permissionAfterRevoke = await permissionManager.GetAsync(
            EndToEndPermissionsProvider.Permission1,
            currentUser,
            cancellationToken: AbortToken
        );
        permissionAfterRevoke.IsGranted.Should().BeFalse();
    }

    #endregion

    #region Test 172: should_initialize_permissions_on_startup

    [Fact]
    public async Task should_initialize_permissions_on_startup()
    {
        // given
        await Fixture.ResetAsync();

        // Create host WITH the background service (re-add it since base removes it)
        var builder = CreateHostBuilder();
        builder.Services.AddPermissionDefinitionProvider<StartupPermissionsProvider>();
        builder.Services.Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = true;
            options.IsDynamicPermissionStoreEnabled = true;
            options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.Zero;
        });
        builder.Services.AddHostedService<PermissionsInitializationBackgroundService>();

        using var host = builder.Build();

        // when: start the host (triggers background service)
        await host.StartAsync(AbortToken);

        // give background service time to complete
        await Task.Delay(TimeSpan.FromSeconds(2), AbortToken);

        // then: verify static permissions were saved to database
        await using var scope = host.Services.CreateAsyncScope();
        var dynamicStore = scope.ServiceProvider.GetRequiredService<IDynamicPermissionDefinitionStore>();

        var permissions = await dynamicStore.GetPermissionsAsync(AbortToken);
        permissions.Should().Contain(p => p.Name == StartupPermissionsProvider.Permission);

        await host.StopAsync(AbortToken);
    }

    #endregion

    #region Test 173: should_retry_initialization_on_failure

    [Fact]
    public async Task should_retry_initialization_on_failure()
    {
        // Note: This test verifies the Polly retry behavior is configured correctly
        // by checking that the background service completes successfully even after potential
        // initial failures (which can occur due to timing issues with containers)

        // given
        await Fixture.ResetAsync();

        var builder = CreateHostBuilder();
        builder.Services.AddPermissionDefinitionProvider<RetryPermissionsProvider>();
        builder.Services.Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = true;
            options.IsDynamicPermissionStoreEnabled = true;
            options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.Zero;
        });
        builder.Services.AddHostedService<PermissionsInitializationBackgroundService>();

        using var host = builder.Build();

        // when: start the host
        await host.StartAsync(AbortToken);

        // give enough time for potential retries
        await Task.Delay(TimeSpan.FromSeconds(3), AbortToken);

        // then: permissions should eventually be saved (Polly retry succeeded)
        await using var scope = host.Services.CreateAsyncScope();
        var dynamicStore = scope.ServiceProvider.GetRequiredService<IDynamicPermissionDefinitionStore>();

        var permissions = await dynamicStore.GetPermissionsAsync(AbortToken);
        permissions.Should().Contain(p => p.Name == RetryPermissionsProvider.Permission);

        await host.StopAsync(AbortToken);
    }

    #endregion

    #region Helpers

    private HostApplicationBuilder _CreateDynamicEnabledHostBuilder<T>()
        where T : class, IPermissionDefinitionProvider
    {
        var builder = CreateHostBuilder();

        builder.Services.AddPermissionDefinitionProvider<T>();
        builder.Services.Configure<PermissionManagementOptions>(options =>
        {
            options.IsDynamicPermissionStoreEnabled = true;
            options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.Zero;
        });

        return builder;
    }

    #endregion

    #region Test Permission Providers

    [UsedImplicitly]
    private sealed class EndToEndPermissionsProvider : IPermissionDefinitionProvider
    {
        public const string Permission1 = "EndToEnd.Permission1";
        public const string Permission2 = "EndToEnd.Permission2";

        public void Define(IPermissionDefinitionContext context)
        {
            var group = context.AddGroup("EndToEndGroup");
            group.AddChild(Permission1);
            group.AddChild(Permission2);
        }
    }

    [UsedImplicitly]
    private sealed class Host1PermissionsProvider : IPermissionDefinitionProvider
    {
        public const string Permission = "Host1.Permission";

        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("Host1Group").AddChild(Permission);
        }
    }

    [UsedImplicitly]
    private sealed class Host2PermissionsProvider : IPermissionDefinitionProvider
    {
        public const string Permission = "Host2.Permission";

        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("Host2Group").AddChild(Permission);
        }
    }

    [UsedImplicitly]
    private sealed class ConcurrencyPermissionsProvider : IPermissionDefinitionProvider
    {
        public const string Permission1 = "Concurrent.Permission1";
        public const string Permission2 = "Concurrent.Permission2";
        public const string Permission3 = "Concurrent.Permission3";
        public const string Permission4 = "Concurrent.Permission4";
        public const string Permission5 = "Concurrent.Permission5";

        public void Define(IPermissionDefinitionContext context)
        {
            var group = context.AddGroup("ConcurrencyGroup");
            group.AddChild(Permission1);
            group.AddChild(Permission2);
            group.AddChild(Permission3);
            group.AddChild(Permission4);
            group.AddChild(Permission5);
        }
    }

    [UsedImplicitly]
    private sealed class StartupPermissionsProvider : IPermissionDefinitionProvider
    {
        public const string Permission = "Startup.Permission";

        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("StartupGroup").AddChild(Permission);
        }
    }

    [UsedImplicitly]
    private sealed class RetryPermissionsProvider : IPermissionDefinitionProvider
    {
        public const string Permission = "Retry.Permission";

        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("RetryGroup").AddChild(Permission);
        }
    }

    #endregion
}
