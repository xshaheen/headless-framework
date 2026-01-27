// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Entities;
using Framework.Permissions.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests.Repository;

[Collection<PermissionsTestFixture>]
public sealed class EfPermissionDefinitionRecordRepositoryTests(PermissionsTestFixture fixture)
    : PermissionsTestBase(fixture)
{
    [Fact]
    public async Task should_get_all_permission_definitions()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionRecordRepository>();

        var permission1 = _CreatePermissionRecord("group1", "perm1");
        var permission2 = _CreatePermissionRecord("group1", "perm2");

        await repository.SaveAsync(
            newGroups: [],
            updatedGroups: [],
            deletedGroups: [],
            newPermissions: [permission1, permission2],
            updatedPermissions: [],
            deletedPermissions: [],
            AbortToken
        );

        // when
        var permissions = await repository.GetPermissionsListAsync(AbortToken);

        // then
        permissions.Should().HaveCount(2);
        permissions.Should().Contain(p => p.Name == "perm1");
        permissions.Should().Contain(p => p.Name == "perm2");
    }

    [Fact]
    public async Task should_get_all_group_definitions()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionRecordRepository>();

        var group1 = _CreateGroupRecord("group1");
        var group2 = _CreateGroupRecord("group2");

        await repository.SaveAsync(
            newGroups: [group1, group2],
            updatedGroups: [],
            deletedGroups: [],
            newPermissions: [],
            updatedPermissions: [],
            deletedPermissions: [],
            AbortToken
        );

        // when
        var groups = await repository.GetGroupsListAsync(AbortToken);

        // then
        groups.Should().HaveCount(2);
        groups.Should().Contain(g => g.Name == "group1");
        groups.Should().Contain(g => g.Name == "group2");
    }

    [Fact]
    public async Task should_insert_permission_definition()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionRecordRepository>();

        var permission = _CreatePermissionRecord("testGroup", "testPermission");

        // when
        await repository.SaveAsync(
            newGroups: [],
            updatedGroups: [],
            deletedGroups: [],
            newPermissions: [permission],
            updatedPermissions: [],
            deletedPermissions: [],
            AbortToken
        );

        // then
        var permissions = await repository.GetPermissionsListAsync(AbortToken);
        permissions.Should().ContainSingle();
        var saved = permissions[0];
        saved.Name.Should().Be("testPermission");
        saved.GroupName.Should().Be("testGroup");
        saved.DisplayName.Should().Be("Test Permission");
        saved.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task should_insert_group_definition()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionRecordRepository>();

        var group = _CreateGroupRecord("testGroup");

        // when
        await repository.SaveAsync(
            newGroups: [group],
            updatedGroups: [],
            deletedGroups: [],
            newPermissions: [],
            updatedPermissions: [],
            deletedPermissions: [],
            AbortToken
        );

        // then
        var groups = await repository.GetGroupsListAsync(AbortToken);
        groups.Should().ContainSingle();
        var saved = groups[0];
        saved.Name.Should().Be("testGroup");
        saved.DisplayName.Should().Be("Test Group");
    }

    [Fact]
    public async Task should_update_permission_definition()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionRecordRepository>();

        var permission = _CreatePermissionRecord("group1", "perm1");

        await repository.SaveAsync(
            newGroups: [],
            updatedGroups: [],
            deletedGroups: [],
            newPermissions: [permission],
            updatedPermissions: [],
            deletedPermissions: [],
            AbortToken
        );

        // when
        permission.DisplayName = "Updated Display Name";
        permission.IsEnabled = false;

        await repository.SaveAsync(
            newGroups: [],
            updatedGroups: [],
            deletedGroups: [],
            newPermissions: [],
            updatedPermissions: [permission],
            deletedPermissions: [],
            AbortToken
        );

        // then
        var permissions = await repository.GetPermissionsListAsync(AbortToken);
        permissions.Should().ContainSingle();
        var updated = permissions[0];
        updated.DisplayName.Should().Be("Updated Display Name");
        updated.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task should_delete_permission_definition()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionRecordRepository>();

        var permission = _CreatePermissionRecord("group1", "permToDelete");

        await repository.SaveAsync(
            newGroups: [],
            updatedGroups: [],
            deletedGroups: [],
            newPermissions: [permission],
            updatedPermissions: [],
            deletedPermissions: [],
            AbortToken
        );

        var beforeDelete = await repository.GetPermissionsListAsync(AbortToken);
        beforeDelete.Should().ContainSingle();

        // when
        await repository.SaveAsync(
            newGroups: [],
            updatedGroups: [],
            deletedGroups: [],
            newPermissions: [],
            updatedPermissions: [],
            deletedPermissions: [permission],
            AbortToken
        );

        // then
        var afterDelete = await repository.GetPermissionsListAsync(AbortToken);
        afterDelete.Should().BeEmpty();
    }

    private static PermissionDefinitionRecord _CreatePermissionRecord(string groupName, string name)
    {
        return new PermissionDefinitionRecord(
            id: Guid.NewGuid(),
            groupName: groupName,
            name: name,
            parentName: null,
            displayName: "Test Permission",
            isEnabled: true,
            providers: null
        );
    }

    private static PermissionGroupDefinitionRecord _CreateGroupRecord(string name)
    {
        return new PermissionGroupDefinitionRecord(id: Guid.NewGuid(), name: name, displayName: "Test Group");
    }
}
