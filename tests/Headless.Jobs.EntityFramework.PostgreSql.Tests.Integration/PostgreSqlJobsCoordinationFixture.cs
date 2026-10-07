// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Globalization;
using Headless.Coordination;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// One Testcontainers Postgres instance shared by every test, backing both the Jobs operational store (schema
/// <c>jobs</c>) and the Coordination Postgres provider (its own <c>coordination_*</c> tables in <see langword="public"/>).
/// Serialized at the collection level because tests reset the whole database between runs.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public class PostgreSqlJobsCoordinationFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlJobsCoordinationFixture>,
        IJobsApplicationConfigurationFixture
{
    public string ConnectionString => Container.GetConnectionString();

    public StorageNamingStyle NamingStyle => StorageNamingStyle.SnakeCase;

    public string QualifiedTimeJobsTable => "headless.time_jobs";

    public string QualifiedCronJobsTable => "headless.cron_jobs";

    public string QualifiedCronJobOccurrencesTable => "headless.cron_job_occurrences";

    public string QualifyTable(string schema, string table) => $"{schema}.\"{table}\"";

    public string UtcNowSqlExpression => "now()";

    public string UtcNowOffsetSqlExpression(int seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"(now() + interval '{seconds} seconds')");

    // Npgsql translates a bare DateTime.UtcNow inside an expression tree to the server's now().
    public string EfTranslatedDatabaseClockSql => "now()";

    public string ResetSql =>
        // Jobs, Messaging, and Coordination share this schema, so one drop resets all three.
        "DROP SCHEMA IF EXISTS headless CASCADE;"
        // The custom-schema conformance scenario maps the whole store here, so it must be dropped like any other.
        + $"DROP SCHEMA IF EXISTS {JobsCoordinationFixtureExtensions.CustomSchemaName} CASCADE;"
        + "DROP SCHEMA IF EXISTS consumer_jobs CASCADE;"
        + "DROP TABLE IF EXISTS jobs_probe;"
        + "DROP TABLE IF EXISTS coordination_liveness, coordination_descriptor, coordination_node_generation CASCADE;";

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure()
            .WithDatabase("jobs_coordination_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            // The process-boundary smoke test starts a second host while the suite fixture still owns pooled
            // connections. Leave headroom so provider validation cannot fail before the child reaches Jobs code.
            .WithCommand("-c", "max_connections=300");
    }

    public string CreateProbeTableSql => "DROP TABLE IF EXISTS jobs_probe; CREATE TABLE jobs_probe (id integer);";

    public void ConfigureCoordination(HeadlessCoordinationSetupBuilder setup)
    {
        setup.UsePostgreSql(ConnectionString);
    }

    public virtual void ConfigureStore(DbContextOptionsBuilder db)
    {
        db.UseNpgsql(ConnectionString);
    }

    public void ConfigureRetryingStore(DbContextOptionsBuilder db)
    {
        db.UseNpgsql(ConnectionString, provider => provider.EnableRetryOnFailure(1, TimeSpan.Zero, null));
    }

    public void ConfigureClaims(JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity> builder)
    {
        builder.UsePostgreSqlClaims();
    }

    public void ConfigureApplicationJobs<TContext>(
        JobsOptionsBuilder<TimeJobEntity, CronJobEntity> builder,
        Action<CoordinationOptions> configureCoordination
    )
        where TContext : DbContext
    {
        builder.UsePostgreSql<TContext>(coordination => coordination.Configure(configureCoordination));
    }

    public DbConnection CreateConnection()
    {
        return new NpgsqlConnection(ConnectionString);
    }

    public void ConfigureUnitOfWork(IServiceCollection services)
    {
        services.AddPostgreSqlUnitOfWork();
    }

    public void ConfigureMessagingStorage(MessagingSetupBuilder setup)
    {
        setup.UsePostgreSql(ConnectionString);
    }

    public async Task RunCoordinatedTransactionAsync(
        IServiceProvider services,
        Func<IServiceProvider, IUnitOfWork, DbConnection, DbTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        // A fresh scope for the scoped services the operation may resolve; the unit itself comes from the
        // singleton factory and is handed to the operation, which enlists through its receivers.
        await using var scope = services.CreateAsyncScope();
        var factory = services.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new NpgsqlConnection(ConnectionString);

        await factory.RunAsync(
            connection,
            async (unitOfWork, ct) =>
            {
                var resource =
                    unitOfWork.Resource as IRelationalUnitOfWorkResource
                    ?? throw new InvalidOperationException("The begun unit of work exposed no relational resource.");

                await operation(scope.ServiceProvider, unitOfWork, resource.Connection, resource.Transaction, ct);
            },
            cancellationToken: cancellationToken
        );
    }
}
