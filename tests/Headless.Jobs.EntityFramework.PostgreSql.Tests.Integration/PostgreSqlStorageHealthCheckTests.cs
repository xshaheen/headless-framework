// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlStorageHealthCheckTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsStorageHealthCheckConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task storage_check_reports_healthy_when_the_store_can_connect() =>
        base.storage_check_reports_healthy_when_the_store_can_connect();

    [Fact]
    public override Task storage_check_reports_unhealthy_when_the_database_cannot_be_opened() =>
        base.storage_check_reports_unhealthy_when_the_database_cannot_be_opened();

    // Runs against a PostgreSQL of its own, because the test stops it: the collection container serves every other
    // test in the project. Npgsql hands an idle pooled connection back without contacting the server, so this proves
    // the probe reaches the server rather than only opening a connection.
    [Fact]
    public async Task storage_check_reports_unhealthy_once_the_server_stops()
    {
        // given
        await using var container = new PostgreSqlBuilder(TestImages.PostgreSql).Build();
        await container.StartAsync(AbortToken);
        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Timeout = 5,
        }.ConnectionString;
        using var host = BuildHost(connectionString);

        // when the server is running
        var running = await CheckAsync(host);

        // then
        running.Status.Should().Be(HealthStatus.Healthy);

        // when the server stops, with the connection the first probe returned still in the pool
        await container.StopAsync(AbortToken);
        var stopped = await CheckAsync(host);

        // then
        stopped.Status.Should().Be(HealthStatus.Unhealthy);
        stopped.Description.Should().Be($"The '{CheckName}' dependency probe failed.");
    }
}
