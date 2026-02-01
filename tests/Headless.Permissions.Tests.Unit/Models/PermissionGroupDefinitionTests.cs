// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Models;
using Headless.Testing.Tests;

namespace Tests.Models;

public sealed class PermissionGroupDefinitionTests : TestBase
{
    [Fact]
    public void should_create_group_with_name()
    {
        // when
        var group = new PermissionGroupDefinition("TestGroup");

        // then
        group.Name.Should().Be("TestGroup");
        group.DisplayName.Should().Be("TestGroup");
        group.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void should_add_permission_to_group()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");

        // when
        var permission = group.AddChild("TestPermission", "Test Permission Display");

        // then
        group.Permissions.Should().HaveCount(1);
        group.Permissions.Should().Contain(permission);
        permission.Name.Should().Be("TestPermission");
    }

    [Fact]
    public void should_return_all_flat_permissions()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");
        var parent1 = group.AddChild("Parent1");
        var child1 = parent1.AddChild("Child1");
        var child2 = parent1.AddChild("Child2");
        var grandChild = child1.AddChild("GrandChild");
        var parent2 = group.AddChild("Parent2");

        // when
        var flatPermissions = group.GetFlatPermissions();

        // then
        flatPermissions.Should().HaveCount(5);
        flatPermissions.Should().Contain(parent1);
        flatPermissions.Should().Contain(child1);
        flatPermissions.Should().Contain(child2);
        flatPermissions.Should().Contain(grandChild);
        flatPermissions.Should().Contain(parent2);
    }

    [Fact]
    public void should_set_display_name()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");

        // when
        group.DisplayName = "Updated Display Name";

        // then
        group.DisplayName.Should().Be("Updated Display Name");
    }
}
