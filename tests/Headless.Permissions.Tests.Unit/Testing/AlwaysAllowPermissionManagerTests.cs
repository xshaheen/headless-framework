// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Permissions.Definitions;
using Headless.Permissions.Models;
using Headless.Permissions.Testing;
using Headless.Testing.Tests;

namespace Tests.Testing;

public sealed class AlwaysAllowPermissionManagerTests : TestBase
{
    private readonly IPermissionDefinitionManager _definitionManager = Substitute.For<IPermissionDefinitionManager>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly AlwaysAllowPermissionManager _sut;

    public AlwaysAllowPermissionManagerTests()
    {
        _sut = new AlwaysAllowPermissionManager(_definitionManager);
    }

    [Fact]
    public async Task should_always_return_granted()
    {
        // given
        const string permissionName = "Any.Permission";

        // when
        var result = await _sut.GetAsync(permissionName, _currentUser, cancellationToken: AbortToken);

        // then
        result.Name.Should().Be(permissionName);
        result.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_all_permissions_granted()
    {
        // given
        var group = new PermissionGroupDefinition("TestGroup");
        var permission1 = group.AddChild("Permission1");
        var permission2 = group.AddChild("Permission2");
        var permission3 = group.AddChild("Permission3");

        _definitionManager.GetPermissionsAsync(AbortToken).Returns([permission1, permission2, permission3]);

        // when
        var result = await _sut.GetAllAsync(_currentUser, cancellationToken: AbortToken);

        // then
        result.Should().HaveCount(3);
        result.Should().OnlyContain(r => r.IsGranted);
        result.Select(r => r.Name).Should().BeEquivalentTo(["Permission1", "Permission2", "Permission3"]);
    }
}
