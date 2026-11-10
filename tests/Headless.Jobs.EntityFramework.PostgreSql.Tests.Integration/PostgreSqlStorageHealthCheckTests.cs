// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

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
        await using var server = new StoppablePostgreSqlFixture();
        await server.StartAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(server.ConnectionString)
        {
            Timeout = 5,
        }.ConnectionString;
        using var host = BuildHost(connectionString);

        // when the server is running
        var running = await CheckAsync(host);

        // then
        running.Status.Should().Be(HealthStatus.Healthy);

        // when the server stops, with the connection the first probe returned still in the pool
        await server.Container.StopAsync(AbortToken);
        var stopped = await CheckAsync(host);

        // then
        stopped.Status.Should().Be(HealthStatus.Unhealthy);
        stopped.Description.Should().Be($"The '{CheckName}' dependency probe failed.");
    }

    private sealed class StoppablePostgreSqlFixture : HeadlessPostgreSqlFixture
    {
        // Its test stops the server, which would cut every other fixture on a shared one.
        protected override bool ShareContainer => false;

        public ValueTask StartAsync() => InitializeAsync();

        protected override async ValueTask DisposeAsyncCore()
        {
            try
            {
                await base.DisposeAsyncCore();
            }
            catch (NpgsqlException)
            {
                // The test left the server stopped, so its database cannot be dropped; removing the container does it.
                await Container.DisposeAsync();
            }
        }
    }
}
