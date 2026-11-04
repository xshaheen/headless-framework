// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;

namespace Tests;

public sealed class CurrentTenantEnterOrVerifyTests
{
    [Fact]
    public void should_enter_the_named_tenant_and_restore_none_when_no_tenant_is_set()
    {
        // given
        var currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);

        // when
        string? inside;
        using (currentTenant.EnterOrVerify("charity-a"))
        {
            inside = currentTenant.Id;
        }

        // then
        inside.Should().Be("charity-a");
        currentTenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void should_keep_the_ambient_tenant_when_it_is_the_named_one()
    {
        // given
        var currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
        using var ambient = currentTenant.Change("charity-a", "Charity A");

        // when
        using (currentTenant.EnterOrVerify("charity-a"))
        {
            currentTenant.Name.Should().Be("Charity A");
        }

        // then - disposing the verified scope leaves the caller's tenant in place
        currentTenant.Id.Should().Be("charity-a");
    }

    [Fact]
    public void should_refuse_a_different_tenant_and_leave_the_ambient_one_in_place()
    {
        // given
        var currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
        using var ambient = currentTenant.Change("charity-a");

        // when
        var act = () => currentTenant.EnterOrVerify("charity-b");

        // then
        act.Should()
            .Throw<TenantMismatchException>()
            .Which.Should()
            .BeEquivalentTo(new { RequestedTenantId = "charity-b", CurrentTenantId = "charity-a" });
        currentTenant.Id.Should().Be("charity-a");
    }

    [Fact]
    public void should_compare_tenant_ids_ordinally()
    {
        // given
        var currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
        using var ambient = currentTenant.Change("charity-a");

        // when
        var act = () => currentTenant.EnterOrVerify("CHARITY-A");

        // then
        act.Should().Throw<TenantMismatchException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void should_reject_an_empty_tenant_id(string tenantId)
    {
        // given
        var currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);

        // when
        var act = () => currentTenant.EnterOrVerify(tenantId);

        // then
        act.Should().Throw<ArgumentException>();
    }
}
