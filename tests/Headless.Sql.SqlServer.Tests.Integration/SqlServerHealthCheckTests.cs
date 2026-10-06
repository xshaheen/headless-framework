// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tests.TestSetup;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerHealthCheckTests(SqlServerTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_report_healthy_when_the_server_answers()
    {
        // given
        await using var provider = _CreateProvider(configure: null);

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        var entry = report.Entries["sql-sqlserver"];
        entry.Status.Should().Be(HealthStatus.Healthy);
        entry.Tags.Should().Contain([HeadlessHealthCheckTags.Ready, HeadlessHealthCheckTags.Database]);
    }

    [Fact]
    public async Task should_run_the_configured_test_command_and_hide_the_driver_message()
    {
        // given
        await using var provider = _CreateProvider(options =>
        {
            options.TestCommand = "SELECT COUNT(*) FROM headless_missing_table";
            options.FailureStatus = HealthStatus.Degraded;
        });

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        var entry = report.Entries["sql-sqlserver"];
        entry.Status.Should().Be(HealthStatus.Degraded);
        entry.Description.Should().Be("The 'sql-sqlserver' dependency probe failed.");
        entry.Exception.Should().NotBeNull();
    }

    private ServiceProvider _CreateProvider(Action<HeadlessHealthCheckOptions>? configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqlServerSql(_ => fixture.ConnectionString);

        if (configure is not null)
        {
            services.ConfigureHeadlessHealthCheck("sql-sqlserver", configure);
        }

        return services.BuildServiceProvider();
    }
}
