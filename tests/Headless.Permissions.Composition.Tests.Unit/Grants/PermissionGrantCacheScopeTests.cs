// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions;
using Headless.Testing;
using Headless.Testing.Tests;

namespace Tests.Grants;

public sealed class PermissionGrantCacheScopeTests : TestBase
{
    private readonly TestCurrentTenant _currentTenant = new();

    [Fact]
    public void should_build_the_tenant_scope_and_reuse_it_while_the_tenant_is_unchanged()
    {
        // given
        var sut = new PermissionGrantCacheScope(_currentTenant);
        using var tenant = _currentTenant.Change("tenant-1");

        // when
        var first = sut.Get();
        var second = sut.Get();

        // then
        first.Should().Be("t:tenant-1");
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void should_follow_the_ambient_tenant_and_use_an_empty_id_for_the_host()
    {
        // given
        var sut = new PermissionGrantCacheScope(_currentTenant);

        // when
        var host = sut.Get();
        string other;

        using (_currentTenant.Change("tenant-2"))
        {
            other = sut.Get();
        }

        // then
        host.Should().Be("t:");
        other.Should().Be("t:tenant-2");
        sut.Get().Should().Be("t:");
    }
}
