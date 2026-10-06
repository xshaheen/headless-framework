// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

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

    [Fact]
    public async Task should_report_fixed_text_instead_of_the_driver_message_when_the_check_throws()
    {
        // given - a failure the check does not catch would put its message in the description; the wrapper must not
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContext<ProbeDbContext>(
            (_, _) => throw new InvalidOperationException("host=secret-db"),
            configureHeadlessOptions: null
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        var entry = report.Entries[_Name];
        entry.Status.Should().Be(HealthStatus.Unhealthy);
        entry.Description.Should().Be($"The '{_Name}' dependency probe failed.");
        entry.Description.Should().NotContain("secret-db");
        entry.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task should_contribute_one_ready_check_that_is_never_live_and_can_be_removed()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContext<ProbeDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        services.AddHeadlessDbContext<ProbeDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        await using var provider = services.BuildServiceProvider();

        // when
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        // then
        var registration = registrations.Should().ContainSingle(r => r.Name == _Name).Subject;
        registration
            .Tags.Should()
            .BeEquivalentTo([
                HeadlessHealthCheckTags.Ready,
                HeadlessHealthCheckTags.Headless,
                HeadlessHealthCheckTags.Database,
            ]);
        registration.Tags.Should().NotContain("live");
    }

    [Fact]
    public async Task should_drop_the_check_through_remove_health_checks()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.RemoveHealthChecks(registration => registration.Tags.Contains(HeadlessHealthCheckTags.Headless));
        services.AddHeadlessDbContext<ProbeDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        await using var provider = services.BuildServiceProvider();

        // when
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        // then
        registrations.Should().NotContain(r => r.Name == _Name);
    }

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : HeadlessDbContext(options)
    {
        public override string DefaultSchema => "";
    }
}
