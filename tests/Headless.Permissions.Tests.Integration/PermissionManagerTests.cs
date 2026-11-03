using Headless;
using Headless.Permissions;
using Headless.Primitives;
using Headless.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;

namespace Tests;

public sealed class PermissionManagerTests(PermissionsTestFixture fixture) : PermissionsTestBase(fixture)
{
    private static readonly PermissionGroupDefinition[] _GroupDefinitions =
    [
        TestData.CreateGroupDefinition(4),
        TestData.CreateGroupDefinition(5),
        TestData.CreateGroupDefinition(7),
    ];

    [Fact]
    public async Task should_get_empty_when_no_permissions()
    {
        // given
        await Fixture.ResetAsync();
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
        var permissions = await permissionManager.GetAllAsync(currentUser, cancellationToken: AbortToken);

        var permission = await permissionManager.GetAsync(
            somePermission.Name,
            currentUser,
            cancellationToken: AbortToken
        );

        // then
        permissions.Should().HaveCount(16);
        permissions.Should().AllSatisfy(x => x.IsGranted.Should().BeFalse());
        permission.Should().NotBeNull();
        permission.IsGranted.Should().BeFalse();
        permission.Name.Should().Be(somePermission.Name);
        permission.Providers.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Role1", "Role2")]
    [InlineData("Role2", "Role1")]
    public async Task should_deny_whatever_the_role_order_when_one_role_prohibits_and_another_grants(
        string firstRole,
        string secondRole
    )
    {
        // given — Role1 grants and Role2 prohibits the same permission
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();
        var somePermission = _GroupDefinitions[0].Permissions[0];

        await permissionManager.GrantToRoleAsync(somePermission.Name, "Role1", AbortToken);
        await permissionManager.RevokeFromRoleAsync(somePermission.Name, "Role2", AbortToken);

        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = new UserId("123"),
            WritableRoles = { firstRole, secondRole },
        };

        // when
        var permission = await permissionManager.GetAsync(
            somePermission.Name,
            currentUser,
            cancellationToken: AbortToken
        );

        // then — a prohibition from any role denies, as a prohibition from any provider does
        permission.IsGranted.Should().BeFalse();
    }

    [Fact]
    public async Task should_get_not_granted()
    {
        // given
        await Fixture.ResetAsync();
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
        var permission = await permissionManager.GetAsync("NotDefined", currentUser, cancellationToken: AbortToken);

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
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        const string roleName = "Role1";
        var somePermission = _GroupDefinitions[0].Permissions[0];

        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = new UserId("123"),
            WritableRoles = { roleName },
        };

        // when: grant
        await permissionManager.GrantToRoleAsync(somePermission.Name, roleName: roleName, AbortToken);

        // then: granted
        var permission = await permissionManager.GetAsync(
            somePermission.Name,
            currentUser,
            cancellationToken: AbortToken
        );

        permission.Should().NotBeNull();
        permission.IsGranted.Should().BeTrue();
        permission.Name.Should().Be(somePermission.Name);
        permission.Providers.Should().ContainSingle();
        permission.Providers[0].Name.Should().Be(RolePermissionGrantProvider.ProviderName);

        var permissions = await permissionManager.GetAllAsync(currentUser, cancellationToken: AbortToken);
        permissions.Should().HaveCount(16);
        var granted = permissions.Where(x => x.IsGranted).ToList();
        var notGranted = permissions.Where(x => !x.IsGranted).ToList();
        var grantedPermission = granted[0];
        grantedPermission.Name.Should().Be(somePermission.Name);
        notGranted.Should().HaveCount(15);

        // when: revoke
        await permissionManager.RevokeFromRoleAsync(somePermission.Name, roleName, cancellationToken: AbortToken);

        // then: revoked
        permission = await permissionManager.GetAsync(somePermission.Name, currentUser, cancellationToken: AbortToken);
        permission.Should().NotBeNull();
        permission.IsGranted.Should().BeFalse();
        permission.Name.Should().Be(somePermission.Name);
        permission.Providers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_deny_permission_when_any_provider_prohibits()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        const string roleName = "Role1";
        var userId = new UserId("123");
        var somePermission = _GroupDefinitions[0].Permissions[0];

        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            WritableRoles = { roleName },
        };

        // when: grant to user
        await permissionManager.GrantToUserAsync(somePermission.Name, userId, AbortToken);

        // then: user has permission
        var permission = await permissionManager.GetAsync(
            somePermission.Name,
            currentUser,
            cancellationToken: AbortToken
        );
        permission.IsGranted.Should().BeTrue();

        // when: deny at role level (explicit deny via revoke = Prohibited status)
        await permissionManager.RevokeFromRoleAsync(somePermission.Name, roleName, AbortToken);

        // then: explicit deny overrides user grant (AWS IAM-style)
        permission = await permissionManager.GetAsync(somePermission.Name, currentUser, cancellationToken: AbortToken);
        permission.IsGranted.Should().BeFalse("explicit deny should override all grants");
        permission.Providers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_grant_when_no_provider_prohibits()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        const string roleName = "Role1";
        var userId = new UserId("123");
        var somePermission = _GroupDefinitions[0].Permissions[0];

        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            WritableRoles = { roleName },
        };

        // when: grant to role
        await permissionManager.GrantToRoleAsync(somePermission.Name, roleName, AbortToken);

        // then: permission granted via role
        var permission = await permissionManager.GetAsync(
            somePermission.Name,
            currentUser,
            cancellationToken: AbortToken
        );
        permission.IsGranted.Should().BeTrue();
        permission.Providers.Should().ContainSingle();

        // when: also grant to user
        await permissionManager.GrantToUserAsync(somePermission.Name, userId, AbortToken);

        // then: permission still granted, multiple providers
        permission = await permissionManager.GetAsync(somePermission.Name, currentUser, cancellationToken: AbortToken);
        permission.IsGranted.Should().BeTrue();
        permission.Providers.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_invalidate_cached_permission_end_to_end_when_repository_deletes_grant()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();
        var grantRepository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        const string roleName = "Role1";
        var permissionName = _GroupDefinitions[0].Permissions[0].Name;

        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = new UserId("123"),
            WritableRoles = { roleName },
        };

        await permissionManager.GrantToRoleAsync(permissionName, roleName, AbortToken);

        (await permissionManager.GetAsync(permissionName, currentUser, cancellationToken: AbortToken))
            .IsGranted.Should()
            .BeTrue();

        var record = await grantRepository.FindAsync(
            permissionName,
            RolePermissionGrantProvider.ProviderName,
            roleName,
            AbortToken
        );
        record.Should().NotBeNull();

        // when
        await grantRepository.DeleteAsync(record!, AbortToken);

        // then
        (await permissionManager.GetAsync(permissionName, currentUser, cancellationToken: AbortToken))
            .IsGranted.Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_get_dynamic_permissions()
    {
        var currentUser = new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = new UserId("123"),
            WritableRoles = { "Role1" },
        };

        // given: host1 with dynamic permission store enabled
        await Fixture.ResetAsync();
        using var host1 = _CreateDynamicEnabledHostBuilder<Host1PermissionsDefinitionProvider>().Build();
        await using var scope1 = host1.Services.CreateAsyncScope();
        var permissionManager1 = scope1.ServiceProvider.GetRequiredService<IPermissionManager>();
        var dynamicStore1 = scope1.ServiceProvider.GetRequiredService<IDynamicPermissionDefinitionStore>();
        const string host1Permission = "Permission1";

        // given: host2 with dynamic permission store enabled
        using var host2 = _CreateDynamicEnabledHostBuilder<Host2PermissionsDefinitionProvider>().Build();
        await using var scope2 = host2.Services.CreateAsyncScope();
        var permissionManager2 = scope2.ServiceProvider.GetRequiredService<IPermissionManager>();
        var dynamicStore2 = scope2.ServiceProvider.GetRequiredService<IDynamicPermissionDefinitionStore>();
        const string host2Permission = "Permission2";

        // given: host2 saved its local permissions to dynamic store
        await dynamicStore2.SaveAsync(AbortToken);

        // when: get dynamic permissions from host1
        var host1Permissions = await permissionManager1.GetAllAsync(currentUser, cancellationToken: AbortToken);

        // then: dynamic permissions should be returned
        host1Permissions.Should().HaveCount(2);
        host1Permissions.Should().ContainSingle(x => x.Name == host1Permission);
        host1Permissions.Should().ContainSingle(x => x.Name == host2Permission);

        // given: host1 saved its local permissions to dynamic store
        await dynamicStore1.SaveAsync(AbortToken);

        // when: get dynamic permissions from host1
        var host2Permissions = await permissionManager2.GetAllAsync(currentUser, cancellationToken: AbortToken);

        // then: dynamic permissions should be returned
        host2Permissions.Should().HaveCount(2);
        host2Permissions.Should().ContainSingle(x => x.Name == host1Permission);
        host2Permissions.Should().ContainSingle(x => x.Name == host2Permission);

        // when: change dynamic permission value in host1
        await permissionManager1.GrantToUserAsync(host2Permission, currentUser.UserId, cancellationToken: AbortToken);

        // then: dynamic permission value should be available in both hosts
        (await permissionManager1.GetAsync(host2Permission, currentUser, cancellationToken: AbortToken))
            .IsGranted.Should()
            .BeTrue();
        (await permissionManager2.GetAsync(host2Permission, currentUser, cancellationToken: AbortToken))
            .IsGranted.Should()
            .BeTrue();

        // when: change dynamic permission value in host2
        await permissionManager2.RevokeFromUserAsync(
            host2Permission,
            currentUser.UserId,
            cancellationToken: AbortToken
        );

        // then: dynamic permission value should be changed
        (await permissionManager1.GetAsync(host2Permission, currentUser, cancellationToken: AbortToken))
            .IsGranted.Should()
            .BeFalse();
        (await permissionManager2.GetAsync(host2Permission, currentUser, cancellationToken: AbortToken))
            .IsGranted.Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_keep_the_explicit_state_when_flipping_it_fails_midway(bool storedIsGranted)
    {
        // given — a role holds an explicit grant (or denial) on one permission and a grant on another
        await Fixture.ResetAsync();
        var guidGenerator = new ScriptedGuidGenerator();
        using var host = CreateHost(b =>
        {
            b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>();
            b.Services.AddSingleton<IGuidGenerator>(guidGenerator);
        });
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        const string roleName = "Role1";
        var flipped = _GroupDefinitions[0].Permissions[0].Name;
        var other = _GroupDefinitions[0].Permissions[1].Name;

        await _SetAsync(store, flipped, roleName, storedIsGranted);
        await store.GrantAsync(other, RolePermissionGrantProvider.ProviderName, roleName, AbortToken);
        var otherRecord = await repository.FindAsync(
            other,
            RolePermissionGrantProvider.ProviderName,
            roleName,
            AbortToken
        );

        // when — the replacement row cannot be written (its id collides with the other grant), so the flip fails after
        // the old row is removed
        guidGenerator.Next = otherRecord!.Id;
        var act = () => _SetAsync(store, flipped, roleName, !storedIsGranted);

        // then — the flip is all-or-nothing: the explicit state survives instead of falling back to undefined
        await act.Should().ThrowAsync<Exception>();
        var stored = await repository.FindAsync(
            flipped,
            RolePermissionGrantProvider.ProviderName,
            roleName,
            AbortToken
        );
        stored.Should().NotBeNull();
        stored!.IsGranted.Should().Be(storedIsGranted);
    }

    private static Task _SetAsync(IPermissionGrantStore store, string name, string roleName, bool isGranted)
    {
        return isGranted
            ? store.GrantAsync(name, RolePermissionGrantProvider.ProviderName, roleName, AbortToken)
            : store.RevokeAsync(name, RolePermissionGrantProvider.ProviderName, roleName, AbortToken);
    }

    private HostApplicationBuilder _CreateDynamicEnabledHostBuilder<T>()
        where T : class, IPermissionDefinitionProvider
    {
        var builder = CreateHostBuilder();

        builder.Services.AddPermissionDefinitionProvider<T>();
        builder.Services.Configure<PermissionManagementOptions>(options =>
            options.IsDynamicPermissionStoreEnabled = true
        );

        return builder;
    }

    [UsedImplicitly]
    private sealed class Host1PermissionsDefinitionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("Group1").AddChild("Permission1");
        }
    }

    [UsedImplicitly]
    private sealed class Host2PermissionsDefinitionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("Group2").AddChild("Permission2");
        }
    }

    private sealed class ScriptedGuidGenerator : IGuidGenerator
    {
        public Guid? Next { get; set; }

        public Guid Create()
        {
            var next = Next ?? Guid.CreateVersion7();
            Next = null;

            return next;
        }
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
