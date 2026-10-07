// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlCoordinationConnectionTests(PostgreSqlJobsCoordinationFixture fixture) : TestBase
{
    // When EF builds an Npgsql data source (a plugin such as NetTopologySuite, ConfigureDataSource, or a data source
    // passed in), the context's connection string omits the password, so coordination must connect through the data
    // source rather than through that string.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task coordination_registers_through_the_application_context_data_source(bool passDataSource)
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddDbContext<DataSourceApplicationContext>(db =>
        {
            if (passDataSource)
            {
                db.UseNpgsql(dataSource);
            }
            else
            {
                db.UseNpgsql(fixture.ConnectionString, npgsql => npgsql.ConfigureDataSource(_ => { }));
            }
        });
        builder.Services.AddHeadlessJobs(jobs =>
        {
            jobs.DisableBackgroundServices();
            jobs.AddModule<CoordinatedJobsModule>();
            jobs.UsePostgreSql<DataSourceApplicationContext>(coordination =>
                coordination.Configure(options => options.ClusterName = "application-data-source")
            );
        });

        using var host = builder.Build();
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<DataSourceApplicationContext>(host, AbortToken);
        await host.StartAsync(AbortToken);

        try
        {
            host.Services.GetRequiredService<INodeMembership>().Identity.Should().NotBeNull();
        }
        finally
        {
            await host.StopAsync(AbortToken);
        }
    }

    private sealed class DataSourceApplicationContext(DbContextOptions<DataSourceApplicationContext> options)
        : DbContext(options);
}
