// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Definitions;
using Headless.Permissions.Models;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.Definitions;

public sealed class PermissionDefinitionManagerTests : TestBase
{
    private readonly IStaticPermissionDefinitionStore _staticStore;
    private readonly IDynamicPermissionDefinitionStore _dynamicStore;
    private readonly PermissionDefinitionManager _sut;

    public PermissionDefinitionManagerTests()
    {
        _staticStore = Substitute.For<IStaticPermissionDefinitionStore>();
        _dynamicStore = Substitute.For<IDynamicPermissionDefinitionStore>();
        _sut = new PermissionDefinitionManager(_staticStore, _dynamicStore);
    }

    #region FindAsync

    [Fact]
    public async Task should_find_permission_in_static_store()
    {
        // given
        const string permissionName = "Users.Create";
        var permission = _CreatePermission(permissionName);
        _staticStore.GetOrDefaultPermissionAsync(permissionName, AbortToken).Returns(permission);

        // when
        var result = await _sut.FindAsync(permissionName, AbortToken);

        // then
        result.Should().BeSameAs(permission);
        await _dynamicStore.DidNotReceive().GetOrDefaultAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_fallback_to_dynamic_store_when_not_in_static()
    {
        // given
        const string permissionName = "Dynamic.Permission";
        var dynamicPermission = _CreatePermission(permissionName);
        _staticStore.GetOrDefaultPermissionAsync(permissionName, AbortToken).Returns((PermissionDefinition?)null);
        _dynamicStore.GetOrDefaultAsync(permissionName, AbortToken).Returns(dynamicPermission);

        // when
        var result = await _sut.FindAsync(permissionName, AbortToken);

        // then
        result.Should().BeSameAs(dynamicPermission);
    }

    [Fact]
    public async Task should_return_null_for_unknown_permission()
    {
        // given
        const string permissionName = "Unknown.Permission";
        _staticStore.GetOrDefaultPermissionAsync(permissionName, AbortToken).Returns((PermissionDefinition?)null);
        _dynamicStore.GetOrDefaultAsync(permissionName, AbortToken).Returns((PermissionDefinition?)null);

        // when
        var result = await _sut.FindAsync(permissionName, AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_throw_for_null_name()
    {
        // given/when
        var act = () => _sut.FindAsync(null!, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region GetPermissionsAsync

    [Fact]
    public async Task should_merge_static_and_dynamic_permissions()
    {
        // given
        var staticPermission1 = _CreatePermission("Static.Permission1");
        var staticPermission2 = _CreatePermission("Static.Permission2");
        var dynamicPermission1 = _CreatePermission("Dynamic.Permission1");
        var dynamicPermission2 = _CreatePermission("Dynamic.Permission2");

        _staticStore.GetAllPermissionsAsync(AbortToken).Returns([staticPermission1, staticPermission2]);
        _dynamicStore.GetPermissionsAsync(AbortToken).Returns([dynamicPermission1, dynamicPermission2]);

        // when
        var result = await _sut.GetPermissionsAsync(AbortToken);

        // then
        result.Should().HaveCount(4);
        result.Should().Contain(staticPermission1);
        result.Should().Contain(staticPermission2);
        result.Should().Contain(dynamicPermission1);
        result.Should().Contain(dynamicPermission2);
    }

    [Fact]
    public async Task should_prefer_static_permission_on_duplicate_name()
    {
        // given
        const string duplicateName = "Shared.Permission";
        var staticPermission = _CreatePermission(duplicateName);
        var dynamicPermission = _CreatePermission(duplicateName);

        _staticStore.GetAllPermissionsAsync(AbortToken).Returns([staticPermission]);
        _dynamicStore.GetPermissionsAsync(AbortToken).Returns([dynamicPermission]);

        // when
        var result = await _sut.GetPermissionsAsync(AbortToken);

        // then
        result.Should().ContainSingle();
        result.Should().Contain(staticPermission);
        result.Should().NotContain(dynamicPermission);
    }

    #endregion

    #region GetGroupsAsync

    [Fact]
    public async Task should_merge_static_and_dynamic_groups()
    {
        // given
        var staticGroup1 = new PermissionGroupDefinition("Static.Group1");
        var staticGroup2 = new PermissionGroupDefinition("Static.Group2");
        var dynamicGroup1 = new PermissionGroupDefinition("Dynamic.Group1");
        var dynamicGroup2 = new PermissionGroupDefinition("Dynamic.Group2");

        _staticStore.GetGroupsAsync(AbortToken).Returns([staticGroup1, staticGroup2]);
        _dynamicStore.GetGroupsAsync(AbortToken).Returns([dynamicGroup1, dynamicGroup2]);

        // when
        var result = await _sut.GetGroupsAsync(AbortToken);

        // then
        result.Should().HaveCount(4);
        result.Should().Contain(staticGroup1);
        result.Should().Contain(staticGroup2);
        result.Should().Contain(dynamicGroup1);
        result.Should().Contain(dynamicGroup2);
    }

    [Fact]
    public async Task should_prefer_static_group_on_duplicate_name()
    {
        // given
        const string duplicateName = "Shared.Group";
        var staticGroup = new PermissionGroupDefinition(duplicateName);
        var dynamicGroup = new PermissionGroupDefinition(duplicateName);

        _staticStore.GetGroupsAsync(AbortToken).Returns([staticGroup]);
        _dynamicStore.GetGroupsAsync(AbortToken).Returns([dynamicGroup]);

        // when
        var result = await _sut.GetGroupsAsync(AbortToken);

        // then
        result.Should().ContainSingle();
        result.Should().Contain(staticGroup);
        result.Should().NotContain(dynamicGroup);
    }

    #endregion

    #region Helpers

    private static PermissionDefinition _CreatePermission(string name)
    {
        var group = new PermissionGroupDefinition("TestGroup");
        return group.AddChild(name);
    }

    #endregion
}
