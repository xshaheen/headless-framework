// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class TenantRoutedContextGuardTests : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_refuse_only_a_tenant_routed_context(bool routed)
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        if (routed)
        {
            builder.Services.AddSingleton(new TenantDataRoutedContextRegistration(typeof(GuardedDbContext)));
        }

        // when
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Catalog(catalog => catalog.UseEntityFramework<GuardedDbContext>())
        );
        await using var provider = builder.Services.BuildServiceProvider();
        var diagnostics = provider
            .GetServices<IHeadlessTenancyValidator>()
            .SelectMany(validator => validator.Validate(new(provider, new TenantPostureManifest())))
            .Where(diagnostic =>
                string.Equals(diagnostic.Code, "HEADLESS_TENANCY_ROUTED_CONTEXT_NOT_ALLOWED", StringComparison.Ordinal)
            )
            .ToList();

        // then
        diagnostics.Should().HaveCount(routed ? 1 : 0);
    }

    private sealed class GuardedDbContext(DbContextOptions<GuardedDbContext> options) : DbContext(options);
}
