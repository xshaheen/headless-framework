// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.CommitCoordination;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Testing.Testcontainers;
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
public sealed class PostgreSqlJobsCoordinationFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlJobsCoordinationFixture>,
        IJobsCoordinationFixture
{
    public string ConnectionString => Container.GetConnectionString();

    public string QualifiedTimeJobsTable => "jobs.\"TimeJobs\"";

    public string QualifiedCronJobsTable => "jobs.\"CronJobs\"";

    public string QualifiedCronJobOccurrencesTable => "jobs.\"CronJobOccurrences\"";

    public string UtcNowSqlExpression => "now()";

    public string ResetSql =>
        "DROP SCHEMA IF EXISTS jobs CASCADE;"
        + "DROP SCHEMA IF EXISTS messaging CASCADE;"
        + "DROP TABLE IF EXISTS coordination_liveness, coordination_descriptor, coordination_node_generation CASCADE;";

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure()
            .WithDatabase("jobs_coordination_test")
            .WithUsername("postgres")
            .WithPassword("postgres");
    }

    public string CreateProbeTableSql => "CREATE TABLE IF NOT EXISTS jobs_probe (id integer); DELETE FROM jobs_probe;";

    public void ConfigureCoordination(HeadlessCoordinationSetupBuilder setup) => setup.UsePostgreSql(ConnectionString);

    public void ConfigureStore(DbContextOptionsBuilder db) => db.UseNpgsql(ConnectionString);

    public DbConnection CreateConnection() => new NpgsqlConnection(ConnectionString);

    public void ConfigureCommitCoordination(IServiceCollection services) => services.AddPostgreSqlCommitCoordination();

    public void ConfigureMessagingStorage(MessagingSetupBuilder setup) => setup.UsePostgreSql(ConnectionString);

    public async Task RunCoordinatedTransactionAsync(
        IServiceProvider services,
        Func<DbConnection, DbTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(ConnectionString);

        await connection.ExecuteCoordinatedTransactionAsync(
            async (conn, ct) =>
            {
                // Reach the live transaction through the same relational capability production participants use.
                var coordinator =
                    services.GetRequiredService<ICurrentCommitCoordinator>().Current
                    ?? throw new InvalidOperationException("No ambient coordinator — the helper did not enlist.");

                if (
                    !coordinator.TryGetCapability<IRelationalCommitContext>(out var relational)
                    || relational.Transaction is null
                )
                {
                    throw new InvalidOperationException(
                        "The coordinated scope exposed no live relational transaction."
                    );
                }

                await operation(conn, relational.Transaction, ct);
            },
            services,
            cancellationToken: cancellationToken
        );
    }
}
