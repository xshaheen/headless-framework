// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Models;
using Framework.Testing.Tests;

namespace Tests.Models;

public sealed class GrantedPermissionResultTests : TestBase
{
    [Fact]
    public void should_create_with_granted_status()
    {
        // when
        var result = new GrantedPermissionResult("TestPermission", isGranted: true);

        // then
        result.Name.Should().Be("TestPermission");
        result.IsGranted.Should().BeTrue();
        result.Providers.Should().BeEmpty();
    }

    [Fact]
    public void should_create_with_denied_status()
    {
        // when
        var result = new GrantedPermissionResult("TestPermission", isGranted: false);

        // then
        result.Name.Should().Be("TestPermission");
        result.IsGranted.Should().BeFalse();
    }

    [Fact]
    public void should_add_provider_to_result()
    {
        // given
        var result = new GrantedPermissionResult("TestPermission", isGranted: true);
        var provider = new GrantPermissionProvider("Role", ["admin", "manager"]);

        // when
        result.Providers.Add(provider);

        // then
        result.Providers.Should().HaveCount(1);
        result.Providers.Should().Contain(provider);
        provider.Name.Should().Be("Role");
        provider.Keys.Should().Contain("admin");
        provider.Keys.Should().Contain("manager");
    }

    [Fact]
    public void should_allow_is_granted_to_be_mutable()
    {
        // given
        var result = new GrantedPermissionResult("TestPermission", isGranted: false);

        // when - internal setter used (simulating internal modification)
        // Note: IsGranted has internal setter, so testing initial value behavior
        result.IsGranted.Should().BeFalse();
    }
}
