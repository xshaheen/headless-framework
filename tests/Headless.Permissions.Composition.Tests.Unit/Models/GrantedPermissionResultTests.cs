// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions;
using Headless.Testing.Tests;

namespace Tests.Models;

public sealed class GrantedPermissionResultTests : TestBase
{
    [Fact]
    public void should_add_provider_to_result()
    {
        // given
        var result = new GrantedPermissionResult("TestPermission", isGranted: true);
        var provider = new GrantPermissionProvider("Role", ["admin", "manager"]);

        // when
        result.AddProvider(provider);

        // then
        result.Providers.Should().ContainSingle();
        result.Providers.Should().Contain(provider);
        provider.Name.Should().Be("Role");
        provider.Keys.Should().Contain("admin");
        provider.Keys.Should().Contain("manager");
    }
}
