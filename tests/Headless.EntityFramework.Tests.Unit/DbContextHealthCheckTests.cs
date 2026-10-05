// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tests;

public sealed class DbContextHealthCheckTests : TestBase
{
    private static readonly string _Name = "dbcontext-" + typeof(ProbeDbContext).FullName;

    [Fact]
    public async Task should_report_healthy_when_the_context_can_connect()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContext<ProbeDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        var entry = report.Entries[_Name];
        entry.Status.Should().Be(HealthStatus.Healthy);
        entry.Tags.Should().Contain([HeadlessHealthCheckTags.Ready, HeadlessHealthCheckTags.Database]);
    }

    [Fact]
    public async Task should_report_unhealthy_when_the_context_cannot_connect()
    {
        // given - Mode=ReadOnly refuses to create the missing file, so the connection cannot open
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContextPool<ProbeDbContext>(options =>
            options.UseSqlite($"Data Source={path};Mode=ReadOnly")
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        report.Entries[_Name].Status.Should().Be(HealthStatus.Unhealthy);
    }

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : HeadlessDbContext(options)
    {
        public override string DefaultSchema => "";
    }
}
