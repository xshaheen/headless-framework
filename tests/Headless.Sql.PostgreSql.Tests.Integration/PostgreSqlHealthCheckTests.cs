// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tests.TestSetup;

namespace Tests;

[Collection<NpgsqlTestFixture>]
public sealed class PostgreSqlHealthCheckTests(NpgsqlTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_report_healthy_when_the_server_answers()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlSql(fixture.Container.GetConnectionString());
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        var entry = report.Entries["sql-postgresql"];
        entry.Status.Should().Be(HealthStatus.Healthy);
        entry.Tags.Should().Contain([HeadlessHealthCheckTags.Ready, HeadlessHealthCheckTags.Database]);
    }

    [Fact]
    public async Task should_report_unhealthy_when_the_server_is_unreachable()
    {
        // given - nothing listens on port 1, so the connection is refused at once
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlSql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2");
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        var entry = report.Entries["sql-postgresql"];
        entry.Status.Should().Be(HealthStatus.Unhealthy);
        entry.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task should_run_the_configured_test_command()
    {
        // given - a query against a table that does not exist proves the probe runs the command, not SELECT 1
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlSql(fixture.Container.GetConnectionString());
        services.ConfigureHeadlessHealthCheck(
            "sql-postgresql",
            options =>
            {
                options.TestCommand = "SELECT count(*) FROM headless_missing_table";
                options.FailureStatus = HealthStatus.Degraded;
            }
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then - the driver message names the table; the description must not
        var entry = report.Entries["sql-postgresql"];
        entry.Status.Should().Be(HealthStatus.Degraded);
        entry.Description.Should().Be("The 'sql-postgresql' dependency probe failed.");
        entry.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task should_report_healthy_when_the_configured_test_command_succeeds()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlSql(fixture.Container.GetConnectionString());
        services.ConfigureHeadlessHealthCheck("sql-postgresql", options => options.TestCommand = "SELECT version()");
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        report.Entries["sql-postgresql"].Status.Should().Be(HealthStatus.Healthy);
    }
}
