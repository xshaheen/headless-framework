// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.MultiTenancy;
using Headless.Testing.Tests;

namespace Tests;

public sealed class NullCurrentTenantInfoTests
{
    [Fact]
    public async Task should_always_return_null()
    {
        // given
        var sut = new NullCurrentTenantInfo();

        // when
        var result = await sut.GetAsync();

        // then
        result.Should().BeNull();
    }
}

public sealed class TenantCatalogCurrentTenantInfoTests : TestBase
{
    [Fact]
    public async Task should_return_null_when_no_tenant_context_is_ambient()
    {
        // given
        var currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
        var catalogService = Substitute.For<ITenantCatalogService>();
        var sut = new TenantCatalogCurrentTenantInfo(currentTenant, catalogService);

        // when
        var result = await sut.GetAsync(AbortToken);

        // then
        result.Should().BeNull();
        await catalogService.DidNotReceive().FindByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_load_tenant_info_for_the_ambient_id()
    {
        // given
        var currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
        var catalogService = Substitute.For<ITenantCatalogService>();
        var tenant = new TenantInfo("ten_A", "acme", "Acme", isEnabled: true);
        catalogService.FindByIdAsync("ten_A", AbortToken).Returns(tenant);
        var sut = new TenantCatalogCurrentTenantInfo(currentTenant, catalogService);

        // when
        using (currentTenant.Change("ten_A"))
        {
            var result = await sut.GetAsync(AbortToken);

            // then
            result.Should().BeSameAs(tenant);
        }
    }

    [Fact]
    public async Task should_observe_the_inner_tenant_inside_a_nested_change_scope_and_the_outer_tenant_after_dispose()
    {
        // given — AE12: nested ICurrentTenant.Change reads the inner tenant while active, the outer
        // tenant again once the inner scope disposes. KTD3: no per-scope memoization.
        var currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
        var catalogService = Substitute.For<ITenantCatalogService>();
        var tenantA = new TenantInfo("ten_A", "acme", "Acme", isEnabled: true);
        var tenantB = new TenantInfo("ten_B", "globex", "Globex", isEnabled: true);
        catalogService.FindByIdAsync("ten_A", AbortToken).Returns(tenantA);
        catalogService.FindByIdAsync("ten_B", AbortToken).Returns(tenantB);
        var sut = new TenantCatalogCurrentTenantInfo(currentTenant, catalogService);

        // when / then
        using (currentTenant.Change("ten_A"))
        {
            (await sut.GetAsync(AbortToken)).Should().BeSameAs(tenantA);

            using (currentTenant.Change("ten_B"))
            {
                (await sut.GetAsync(AbortToken)).Should().BeSameAs(tenantB);
            }

            (await sut.GetAsync(AbortToken)).Should().BeSameAs(tenantA);
        }
    }
}
