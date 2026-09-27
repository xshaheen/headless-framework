// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.Customizer;
using Headless.Jobs.Entities;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Transactions;

/// <summary>
/// The Jobs store reads one fixed database, so its application context must never be tenant-routed. A Headless
/// routed context is already refused by the options-constructor requirement (it has no single-options constructor);
/// this pins the explicit tenancy guard that also covers any other routed context type.
/// </summary>
public sealed class TenantRoutedContextGuardTests : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_refuse_only_a_tenant_routed_application_context(bool routed)
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<INodeMembership>());

        if (routed)
        {
            services.AddSingleton(new TenantDataRoutedContextRegistration(typeof(ApplicationContext)));
        }

        // when
        services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(jobs =>
            jobs.UseEntityFramework(ef =>
                ef.UseApplicationDbContext<ApplicationContext>(ConfigurationType.IgnoreModelCustomizer)
            )
        );
        await using var provider = services.BuildServiceProvider();
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

    private sealed class ApplicationContext(DbContextOptions<ApplicationContext> options) : DbContext(options);
}
