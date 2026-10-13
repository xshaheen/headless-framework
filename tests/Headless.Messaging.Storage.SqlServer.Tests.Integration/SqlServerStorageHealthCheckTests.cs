// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerStorageHealthCheckTests(SqlServerTestFixture fixture) : TestBase
{
    private const string _PrimaryCheckName = "messaging-storage-sqlserver-default";
    private const string _OutboxCheckName = "messaging-storage-sqlserver-outbox-1";

    // Each outbox check follows whether its own database accepts connections. The outbox database is taken
    // offline rather than the server stopped, because the collection's server serves every other test in the project.
    [Fact]
    public async Task should_report_only_the_outbox_whose_database_stopped_accepting_connections_unhealthy()
    {
        // given
        var databaseName = "health_" + Guid.NewGuid().ToString("N");
        await using var admin = new SqlConnection(fixture.ConnectionString);
        await admin.ExecuteAsync($"CREATE DATABASE [{databaseName}];");

        try
        {
            var outboxConnectionString = new SqlConnectionStringBuilder(fixture.ConnectionString)
            {
                InitialCatalog = databaseName,
                ConnectTimeout = 5,
            }.ConnectionString;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHeadlessMessaging(setup =>
            {
                setup.UseSqlServer(fixture.ConnectionString);
                setup.AddOutbox().UseSqlServer(outboxConnectionString);
            });
            await using var provider = services.BuildServiceProvider();
            var health = provider.GetRequiredService<HealthCheckService>();

            // when every database accepts connections
            var running = await _CheckAsync(health);

            // then
            running[_PrimaryCheckName].Status.Should().Be(HealthStatus.Healthy);
            running[_OutboxCheckName].Status.Should().Be(HealthStatus.Healthy);

            // when the outbox database stops accepting connections, with the connection the first probe returned
            // still in the pool
            await admin.ExecuteAsync($"ALTER DATABASE [{databaseName}] SET OFFLINE WITH ROLLBACK IMMEDIATE;");
            var offline = await _CheckAsync(health);

            // then
            offline[_PrimaryCheckName].Status.Should().Be(HealthStatus.Healthy);
            offline[_OutboxCheckName].Status.Should().Be(HealthStatus.Unhealthy);
            offline[_OutboxCheckName]
                .Description.Should()
                .NotContain(outboxConnectionString)
                .And.NotContain("Password");
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await admin.ExecuteAsync($"ALTER DATABASE [{databaseName}] SET ONLINE;");
            await admin.ExecuteAsync(
                $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];"
            );
        }
    }

    private static async Task<IReadOnlyDictionary<string, HealthReportEntry>> _CheckAsync(HealthCheckService health)
    {
        var report = await health.CheckHealthAsync(
            registration => registration.Name is _PrimaryCheckName or _OutboxCheckName,
            AbortToken
        );

        return report.Entries;
    }
}
