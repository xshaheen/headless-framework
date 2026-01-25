// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Permissions.Definitions;
using Framework.Permissions.Models;
using Framework.Testing.Tests;
using NSubstitute;

namespace Tests.Serialization;

public sealed class PermissionDefinitionSerializerTests : TestBase
{
    private readonly IGuidGenerator _guidGenerator;
    private readonly PermissionDefinitionSerializer _sut;

    public PermissionDefinitionSerializerTests()
    {
        _guidGenerator = Substitute.For<IGuidGenerator>();
        _guidGenerator.Create().Returns(_ => Guid.NewGuid());
        _sut = new PermissionDefinitionSerializer(_guidGenerator);
    }

    #region Test 112: should_serialize_permission_to_record

    [Fact]
    public void should_serialize_permission_to_record()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup", "Test Group Display");
        var permission = group.AddChild("TestPermission", "Test Permission Display", isEnabled: true);

        // when
        var record = _sut.Serialize(permission, group);

        // then
        record.Name.Should().Be("TestPermission");
        record.DisplayName.Should().Be("Test Permission Display");
        record.GroupName.Should().Be("TestGroup");
        record.IsEnabled.Should().BeTrue();
        record.ParentName.Should().BeNull();
    }

    #endregion

    #region Test 113: should_serialize_group_to_record

    [Fact]
    public void should_serialize_group_to_record()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup", "Test Group Display");

        // when
        var record = _sut.Serialize(group);

        // then
        record.Name.Should().Be("TestGroup");
        record.DisplayName.Should().Be("Test Group Display");
    }

    #endregion

    #region Test 114: should_serialize_properties_as_json

    [Fact]
    public void should_serialize_properties_to_extra_properties()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup", "Test Group Display");
        group["CustomKey"] = "CustomValue";
        group["NumberKey"] = 42;

        var permission = group.AddChild("TestPermission", "Test Permission Display");
        permission["PermissionKey"] = "PermissionValue";

        // when
        var groupRecord = _sut.Serialize(group);
        var permissionRecord = _sut.Serialize(permission, group);

        // then - group properties transferred to extra properties
        groupRecord.ExtraProperties.Should().ContainKey("CustomKey");
        groupRecord.ExtraProperties["CustomKey"].Should().Be("CustomValue");
        groupRecord.ExtraProperties.Should().ContainKey("NumberKey");
        groupRecord.ExtraProperties["NumberKey"].Should().Be(42);

        // then - permission properties transferred to extra properties
        permissionRecord.ExtraProperties.Should().ContainKey("PermissionKey");
        permissionRecord.ExtraProperties["PermissionKey"].Should().Be("PermissionValue");
    }

    #endregion

    #region Test 115: should_serialize_providers_list

    [Fact]
    public void should_serialize_providers_list_as_comma_separated()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");
        var permission = group.AddChild("TestPermission");
        permission.Providers.Add("Provider1");
        permission.Providers.Add("Provider2");
        permission.Providers.Add("Provider3");

        // when
        var record = _sut.Serialize(permission, group);

        // then
        record.Providers.Should().Be("Provider1,Provider2,Provider3");
    }

    #endregion

    #region Test 116: should_deserialize_record_to_permission

    [Fact]
    public void should_serialize_permission_with_parent_name()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");
        var parent = group.AddChild("ParentPermission", "Parent Display");
        var child = parent.AddChild("ChildPermission", "Child Display");

        // when
        var record = _sut.Serialize(child, group);

        // then - record contains parent reference for later reconstruction
        record.Name.Should().Be("ChildPermission");
        record.DisplayName.Should().Be("Child Display");
        record.ParentName.Should().Be("ParentPermission");
        record.GroupName.Should().Be("TestGroup");
    }

    #endregion

    #region Test 117: should_deserialize_record_to_group

    [Fact]
    public void should_serialize_all_groups_and_permissions()
    {
        // given
        var group1 = new PermissionGroupDefinition("Group1", "Group 1 Display");
        group1.AddChild("Permission1", "Permission 1 Display");
        group1.AddChild("Permission2", "Permission 2 Display");

        var group2 = new PermissionGroupDefinition("Group2", "Group 2 Display");
        group2.AddChild("Permission3", "Permission 3 Display");

        // when
        var (groupRecords, permissionRecords) = _sut.Serialize([group1, group2]);

        // then - all groups serialized
        groupRecords.Should().HaveCount(2);
        groupRecords.Select(g => g.Name).Should().Contain(["Group1", "Group2"]);

        // then - all permissions serialized
        permissionRecords.Should().HaveCount(3);
        permissionRecords.Select(p => p.Name).Should().Contain(["Permission1", "Permission2", "Permission3"]);
    }

    #endregion

    #region Test 118: should_handle_null_properties

    [Fact]
    public void should_handle_null_properties()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");
        var permission = group.AddChild("TestPermission");
        // No properties set - Properties dictionary is empty

        // when
        var groupRecord = _sut.Serialize(group);
        var permissionRecord = _sut.Serialize(permission, group);

        // then - empty properties result in empty extra properties
        groupRecord.ExtraProperties.Should().BeEmpty();
        permissionRecord.ExtraProperties.Should().BeEmpty();
    }

    #endregion

    #region Test 119: should_handle_empty_providers

    [Fact]
    public void should_handle_empty_providers()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");
        var permission = group.AddChild("TestPermission");
        // No providers added - Providers list is empty

        // when
        var record = _sut.Serialize(permission, group);

        // then - empty providers list results in null
        record.Providers.Should().BeNull();
    }

    #endregion
}
