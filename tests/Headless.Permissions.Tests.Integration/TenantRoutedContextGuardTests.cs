// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Permissions;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class TenantRoutedContextGuardTests : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_refuse_only_a_tenant_routed_context(bool routed)
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        if (routed)
        {
            services.AddSingleton(new TenantDataRoutedContextRegistration(typeof(GuardedDbContext)));
        }

        // when
        services.AddHeadlessPermissions(setup => setup.UseEntityFramework<GuardedDbContext>());
        await using var provider = services.BuildServiceProvider();
        var diagnostics = provider
            .GetServices<IHeadlessTenancyValidator>()
            .SelectMany(validator => validator.Validate(new(provider, new TenantPostureManifest())))
            .Where(diagnostic => diagnostic.Code == "HEADLESS_TENANCY_ROUTED_CONTEXT_NOT_ALLOWED")
            .ToList();

        // then
        diagnostics.Should().HaveCount(routed ? 1 : 0);
    }

    private sealed class GuardedDbContext(DbContextOptions<GuardedDbContext> options) : DbContext(options);
}
