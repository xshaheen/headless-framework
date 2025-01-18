using Framework.Permissions;
using Framework.Permissions.Definitions;
using Framework.Permissions.GrantProviders;
using Framework.Permissions.Grants;
using Framework.Permissions.Models;
using Framework.Primitives;
using Framework.Testing.Helpers;
using Microsoft.Extensions.DependencyInjection;
using MoreLinq.Extensions;
using Tests.TestSetup;

namespace Tests;

public sealed class PermissionManagerTests(PermissionsTestFixture fixture, ITestOutputHelper output)
    : PermissionsTestBase(fixture, output)
{
    private static readonly PermissionGroupDefinition[] _GroupDefinitions =
    [
        TestData.CreateGroupDefinition(4),
        TestData.CreateGroupDefinition(5),
        TestData.CreateGroupDefinition(7),
    ];

    [Fact]
    public async Task should_to_get_empty_when_no_permissions()
    {
        // given
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = new UserId("123"),
            WritableRoles = { "Role1" },
        };

        var somePermission = _GroupDefinitions[0].Permissions[0];

        // when
        var permissions = await permissionManager.GetAllAsync(currentUser);
        var permission = await permissionManager.GetAsync(somePermission.Name, currentUser);

        // then
        permissions.Should().HaveCount(16);
        var allNotGranted = permissions.TrueForAll(x => !x.IsGranted);
        allNotGranted.Should().BeTrue();
        permission.Should().NotBeNull();
        permission.IsGranted.Should().BeFalse();
        permission.Name.Should().Be(somePermission.Name);
        permission.Providers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_get_not_granted()
    {
        // given
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = new UserId("123"),
            WritableRoles = { "Role1" },
        };

        // when
        var permission = await permissionManager.GetAsync("NotDefined", currentUser);

        // then
        permission.Should().NotBeNull();
        permission.IsGranted.Should().BeFalse();
        permission.Name.Should().Be("NotDefined");
        permission.Providers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_be_able_to_grant_and_revoke_permissions()
    {
        // given
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = new UserId("123"),
            WritableRoles = { "Role1" },
        };

        var somePermission = _GroupDefinitions[0].Permissions[0];

        // when grant
        await permissionManager.GrantToRoleAsync(somePermission.Name, "Role1");

        // then granted
        var permission = await permissionManager.GetAsync(somePermission.Name, currentUser);
        permission.Should().NotBeNull();
        permission.IsGranted.Should().BeTrue();
        permission.Name.Should().Be(somePermission.Name);
        permission.Providers.Should().ContainSingle();
        permission.Providers[0].Name.Should().Be(RolePermissionGrantProvider.ProviderName);
        var permissions = await permissionManager.GetAllAsync(currentUser);
        permissions.Should().HaveCount(16);
        var (granted, notGranted) = permissions.Partition(x => x.IsGranted);
        var grantedPermission = granted.First();
        grantedPermission.Name.Should().Be(somePermission.Name);
        notGranted.Should().HaveCount(15);

        // when revoke
        await permissionManager.RevokeFromRoleAsync(somePermission.Name, "Role1");
        // then revoked
        permission = await permissionManager.GetAsync(somePermission.Name, currentUser);
        permission.Should().NotBeNull();
        permission.IsGranted.Should().BeFalse();
        permission.Name.Should().Be(somePermission.Name);
        permission.Providers.Should().BeEmpty();
    }

    [UsedImplicitly]
    private sealed class PermissionsDefinitionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            foreach (var item in _GroupDefinitions)
            {
                context.AddGroup(item);
            }
        }
    }
}
