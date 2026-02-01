// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Models;
using Headless.Testing.Tests;

namespace Tests.Models;

public sealed class PermissionDefinitionTests : TestBase
{
    [Fact]
    public void should_create_definition_with_name_only()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");

        // when
        var permission = group.AddChild("TestPermission");

        // then
        permission.Name.Should().Be("TestPermission");
        permission.DisplayName.Should().Be("TestPermission");
        permission.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void should_create_definition_with_custom_display_name()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");

        // when
        var permission = group.AddChild("TestPermission", "Test Permission Display");

        // then
        permission.Name.Should().Be("TestPermission");
        permission.DisplayName.Should().Be("Test Permission Display");
    }

    [Fact]
    public void should_default_is_enabled_to_true()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");

        // when
        var permission = group.AddChild("TestPermission");

        // then
        permission.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void should_create_definition_with_is_enabled_false()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");

        // when
        var permission = group.AddChild("TestPermission", isEnabled: false);

        // then
        permission.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void should_throw_when_name_is_null()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");

        // when
        var action = () => group.AddChild(null!);

        // then
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_throw_when_display_name_setter_is_null()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");
        var permission = group.AddChild("TestPermission");

        // when
        var action = () => permission.DisplayName = null!;

        // then
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_add_child_permission()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");
        var parent = group.AddChild("ParentPermission");

        // when
        var child = parent.AddChild("ChildPermission");

        // then
        parent.Children.Should().HaveCount(1);
        parent.Children.Should().Contain(child);
    }

    [Fact]
    public void should_add_multiple_children()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");
        var parent = group.AddChild("ParentPermission");

        // when
        var child1 = parent.AddChild("Child1");
        var child2 = parent.AddChild("Child2");
        var child3 = parent.AddChild("Child3");

        // then
        parent.Children.Should().HaveCount(3);
        parent.Children.Should().ContainInOrder(child1, child2, child3);
    }

    [Fact]
    public void should_set_parent_reference_on_child()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");
        var parent = group.AddChild("ParentPermission");

        // when
        var child = parent.AddChild("ChildPermission");

        // then
        child.Parent.Should().Be(parent);
        parent.Parent.Should().BeNull();
    }

    [Fact]
    public void should_get_and_set_properties_via_indexer()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");
        var permission = group.AddChild("TestPermission");

        // when
        permission["CustomKey"] = "CustomValue";
        permission["IntKey"] = 42;

        // then
        permission["CustomKey"].Should().Be("CustomValue");
        permission["IntKey"].Should().Be(42);
        permission["NonExistent"].Should().BeNull();
    }

    [Fact]
    public void should_allow_adding_providers()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");
        var permission = group.AddChild("TestPermission");

        // when
        permission.Providers.Add("Provider1");
        permission.Providers.Add("Provider2");

        // then
        permission.Providers.Should().HaveCount(2);
        permission.Providers.Should().Contain("Provider1");
        permission.Providers.Should().Contain("Provider2");
    }

    [Fact]
    public void should_format_to_string_correctly()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");
        var permission = group.AddChild("TestPermission");

        // when
        var result = permission.ToString();

        // then
        result.Should().Be("[PermissionDefinition TestPermission]");
    }
}
