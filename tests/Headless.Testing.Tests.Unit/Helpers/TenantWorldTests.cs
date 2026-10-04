// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Security;
using Headless.Testing;

namespace Tests.Helpers;

public sealed class TenantWorldTests
{
    [Fact]
    public void should_default_to_two_distinct_tenants_over_a_fresh_test_tenant()
    {
        var world = TenantWorld.Create();

        world.TenantA.Should().Be(TenantWorld.DefaultTenantA);
        world.TenantB.Should().Be(TenantWorld.DefaultTenantB);
        world.CurrentTenant.Should().BeOfType<TestCurrentTenant>();
        world.CurrentTenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void should_scope_over_the_supplied_current_tenant()
    {
        var current = new TestCurrentTenant();
        var world = new TenantWorld(current, "acme", "globex");

        using (world.AsTenantA())
        {
            current.Id.Should().Be("acme");
        }

        using (world.AsTenantB())
        {
            current.Id.Should().Be("globex");
        }

        using (world.AsTenant("initech"))
        {
            current.Id.Should().Be("initech");
        }

        current.Id.Should().BeNull();
    }

    [Fact]
    public void should_restore_the_outer_scope_when_nested_scopes_end()
    {
        var world = TenantWorld.Create();

        using (world.AsTenantA())
        {
            using (world.AsTenantB())
            {
                world.CurrentTenant.Id.Should().Be(world.TenantB);

                using (world.AsHost())
                {
                    world.CurrentTenant.IsAvailable.Should().BeFalse();
                }

                world.CurrentTenant.Id.Should().Be(world.TenantB);
            }

            world.CurrentTenant.Id.Should().Be(world.TenantA);
        }

        world.CurrentTenant.Id.Should().BeNull();
    }

    [Theory]
    [InlineData("same", "same")]
    [InlineData("", "tenant-b")]
    [InlineData("tenant-a", " ")]
    public void should_reject_blank_or_equal_tenant_ids(string tenantA, string tenantB)
    {
        var act = () => new TenantWorld(new TestCurrentTenant(), tenantA, tenantB);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_create_an_authenticated_principal_carrying_the_tenant_claim()
    {
        var world = TenantWorld.Create();

        var principal = TenantWorld.CreatePrincipal(world.TenantB, userId: "user-7");

        principal.Identity!.IsAuthenticated.Should().BeTrue();
        principal.FindFirst(UserClaimTypes.TenantId)!.Value.Should().Be(world.TenantB);
        principal.FindFirst(UserClaimTypes.UserId)!.Value.Should().Be("user-7");
    }

    [Fact]
    public void should_give_each_tenant_principal_a_distinct_default_user()
    {
        var world = TenantWorld.Create();

        var a = TenantWorld.CreatePrincipal(world.TenantA);
        var b = TenantWorld.CreatePrincipal(world.TenantB);

        a.FindFirst(UserClaimTypes.UserId)!.Value.Should().NotBe(b.FindFirst(UserClaimTypes.UserId)!.Value);
    }
}
