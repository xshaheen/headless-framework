// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// Runs against a PostgreSQL of its own, because the test stops it: the shared collection container serves every other
/// test in the project.
/// </summary>
public sealed class PostgreSqlStorageHealthCheckTests : TestBase
{
    private const string _CheckName = "messaging-storage-postgresql-default";

    // Covers AE1: the outbox check follows whether its database accepts connections.
    [Fact]
    public async Task should_report_the_outbox_store_unhealthy_once_its_database_stops()
    {
        // given
        await using var container = new PostgreSqlBuilder(TestImages.PostgreSql).Build();
        await container.StartAsync(AbortToken);
        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Timeout = 5,
        }.ConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UsePostgreSql(connectionString);
        });
        await using var provider = services.BuildServiceProvider();
        var health = provider.GetRequiredService<HealthCheckService>();

        // when the database is running
        var running = await _CheckAsync(health);

        // then
        running.Status.Should().Be(HealthStatus.Healthy);

        // when the database stops, with the connection the first probe returned still in the pool
        await container.StopAsync(AbortToken);
        var stopped = await _CheckAsync(health);

        // then
        stopped.Status.Should().Be(HealthStatus.Unhealthy);
        stopped.Description.Should().NotContain(connectionString).And.NotContain("Password");
    }

    private static async Task<HealthReportEntry> _CheckAsync(HealthCheckService health)
    {
        var report = await health.CheckHealthAsync(
            registration => string.Equals(registration.Name, _CheckName, StringComparison.Ordinal),
            AbortToken
        );

        return report.Entries.Should().ContainKey(_CheckName).WhoseValue;
    }
}
