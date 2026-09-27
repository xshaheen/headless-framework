// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Hosting;
using Tests.Fixture;

namespace Tests.Tenancy;

public sealed class TenantDataRoutingRegistrationTests : TestBase
{
    [Fact]
    public void should_refuse_routing_one_context_twice()
    {
        var builder = Host.CreateApplicationBuilder();

        var act = () =>
            builder.AddHeadlessTenancy(tenancy =>
                tenancy.EntityFramework(ef =>
                    ef.RouteTenantData<PlacementDbContext>().RouteTenantData<PlacementDbContext>()
                )
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*'PlacementDbContext' is already tenant-routed*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_refuse_a_non_positive_schema_cache_bound(int maxCachedSchemas)
    {
        var builder = Host.CreateApplicationBuilder();

        var act = () =>
            builder.AddHeadlessTenancy(tenancy =>
                tenancy.EntityFramework(ef =>
                    ef.RouteTenantData<PlacementDbContext>(o => o.MaxCachedSchemas = maxCachedSchemas)
                )
            );

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
