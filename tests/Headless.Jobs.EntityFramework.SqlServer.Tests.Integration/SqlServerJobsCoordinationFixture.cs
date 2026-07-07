// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.CommitCoordination;
using Headless.Coordination;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// One Testcontainers SQL Server instance shared by every test, backing both the Jobs operational store (schema
/// <c>jobs</c> in <c>master</c>) and the Coordination SQL Server provider (its own <c>coordination_*</c> tables).
/// Serialized at the collection level because tests reset the whole database between runs.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerJobsCoordinationFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerJobsCoordinationFixture>,
        IJobsCoordinationFixture
{
    public string QualifiedTimeJobsTable => "[jobs].[TimeJobs]";

    public string QualifiedCronJobsTable => "[jobs].[CronJobs]";

    public string QualifiedCronJobOccurrencesTable => "[jobs].[CronJobOccurrences]";

    public string UtcNowSqlExpression => "SYSUTCDATETIME()";

    // SQL Server has no DROP SCHEMA CASCADE. Drop child tables before parents (CronJobOccurrences -> CronJobs),
    // then the schema, then the Coordination tables. DROP TABLE IF EXISTS is a no-op when the table is absent.
    public string ResetSql =>
        "DROP TABLE IF EXISTS [jobs].[CronJobOccurrences];"
        + "DROP TABLE IF EXISTS [jobs].[TimeJobs];"
        + "DROP TABLE IF EXISTS [jobs].[CronJobs];"
        + "IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'jobs') DROP SCHEMA [jobs];"
        + "DROP TABLE IF EXISTS [coordination_liveness];"
        + "DROP TABLE IF EXISTS [coordination_descriptor];"
        + "DROP TABLE IF EXISTS [coordination_node_generation];";

    public string CreateProbeTableSql =>
        "IF OBJECT_ID(N'jobs_probe', N'U') IS NULL CREATE TABLE jobs_probe (id int); DELETE FROM jobs_probe;";

    public void ConfigureCoordination(HeadlessCoordinationSetupBuilder setup) => setup.UseSqlServer(ConnectionString);

    public void ConfigureStore(DbContextOptionsBuilder db) => db.UseSqlServer(ConnectionString);

    public DbConnection CreateConnection() => new SqlConnection(ConnectionString);

    public void ConfigureCommitCoordination(IServiceCollection services) => services.AddSqlServerCommitCoordination();

    public async Task RunCoordinatedTransactionAsync(
        IServiceProvider services,
        Func<DbConnection, DbTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(ConnectionString);

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
