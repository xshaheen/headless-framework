// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Globalization;
using Headless.Coordination;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests.Scheduling;

/// <summary>
/// The store-level chaos scenarios (cron race, claim race, fallback sweep at scale) run their Jobs hosts straight on the
/// database, with no network fault between them, so they need only a Jobs-and-Coordination fixture: the same shape the
/// relational conformance suite's leaves have, on a container no other run shares. The scenarios reset the database
/// between runs, so the collection runs them one at a time.
/// </summary>
public interface IStoreChaosFixture : IJobsCoordinationFixture
{
    /// <summary>The store's name in the chaos report.</summary>
    string Store { get; }
}

[UsedImplicitly]
public sealed class StoreChaosPostgreSqlFixture : HeadlessPostgreSqlFixture, IStoreChaosFixture
{
    // A chaos run loads its server for minutes and reads its log, so it keeps a server no other run shares.
    protected override bool ShareContainer => false;

    public string Store => "PostgreSQL";

    public StorageNamingStyle NamingStyle => StorageNamingStyle.SnakeCase;

    public string QualifiedTimeJobsTable => "headless.time_jobs";

    public string QualifiedCronJobsTable => "headless.cron_jobs";

    public string QualifiedCronJobOccurrencesTable => "headless.cron_job_occurrences";

    public string QualifyTable(string schema, string table) => $"{schema}.\"{table}\"";

    public string UtcNowSqlExpression => "now()";

    public string UtcNowOffsetSqlExpression(int seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"(now() + interval '{seconds} seconds')");

    public string EfTranslatedDatabaseClockSql => "now()";

    public string LeaseDatabaseClockSql => "clock_timestamp()";

    // Jobs and Coordination share the headless schema, so one drop resets both.
    public string ResetSql =>
        "DROP SCHEMA IF EXISTS headless CASCADE;"
        + "DROP TABLE IF EXISTS coordination_liveness, coordination_descriptor, coordination_node_generation CASCADE;";

    public string CreateProbeTableSql => throw new NotSupportedException("The chaos scenarios use no probe table.");

    public void ConfigureCoordination(HeadlessCoordinationSetupBuilder setup) => setup.UsePostgreSql(ConnectionString);

    public void ConfigureStore(DbContextOptionsBuilder db) => db.UseNpgsql(ConnectionString);

    public void ConfigureClaims(JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity> builder) =>
        builder.UsePostgreSqlClaims();

    public DbConnection CreateConnection() => new NpgsqlConnection(ConnectionString);

    public void ConfigureUnitOfWork(IServiceCollection services) =>
        throw new NotSupportedException("The chaos scenarios enlist no unit of work.");

    public void ConfigureMessagingStorage(MessagingSetupBuilder setup) =>
        throw new NotSupportedException("The chaos scenarios wire no messaging storage.");

    public Task RunCoordinatedTransactionAsync(
        IServiceProvider services,
        Func<IServiceProvider, IUnitOfWork, DbConnection, DbTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    ) => throw new NotSupportedException("The chaos scenarios enlist no unit of work.");
}

[UsedImplicitly]
public sealed class StoreChaosSqlServerFixture : HeadlessSqlServerFixture, IStoreChaosFixture
{
    // A chaos run loads its server for minutes and reads its plan cache, so it keeps a server no other run shares.
    protected override bool ShareContainer => false;

    public string Store => "SQL Server";

    public StorageNamingStyle NamingStyle => StorageNamingStyle.PascalCase;

    public string QualifiedTimeJobsTable => "[headless].[TimeJobs]";

    public string QualifiedCronJobsTable => "[headless].[CronJobs]";

    public string QualifiedCronJobOccurrencesTable => "[headless].[CronJobOccurrences]";

    public string QualifyTable(string schema, string table) => $"[{schema}].[{table}]";

    public string UtcNowSqlExpression => "SYSUTCDATETIME()";

    public string UtcNowOffsetSqlExpression(int seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"DATEADD(second, {seconds}, SYSUTCDATETIME())");

    public string EfTranslatedDatabaseClockSql => "GETUTCDATE()";

    public string LeaseDatabaseClockSql => "SYSUTCDATETIME()";

    // SQL Server has no DROP SCHEMA CASCADE: children before parents, and the schema history with the tables its
    // steps created so the next scenario's host recreates all of them.
    public string ResetSql =>
        "DROP TABLE IF EXISTS [headless].[TimeJobAttempts];"
        + "DROP TABLE IF EXISTS [headless].[CronJobOccurrenceAttempts];"
        + "DROP TABLE IF EXISTS [headless].[CronJobOccurrences];"
        + "DROP TABLE IF EXISTS [headless].[TimeJobIdempotencyReservations];"
        + "DROP TABLE IF EXISTS [headless].[TimeJobs];"
        + "DROP TABLE IF EXISTS [headless].[CronJobs];"
        + "DROP TABLE IF EXISTS [headless].[CoordinationLiveness];"
        + "DROP TABLE IF EXISTS [headless].[CoordinationDescriptor];"
        + "DROP TABLE IF EXISTS [headless].[CoordinationNodeGeneration];"
        + "DROP TABLE IF EXISTS [headless].[headless_schema_history];";

    public string CreateProbeTableSql => throw new NotSupportedException("The chaos scenarios use no probe table.");

    public void ConfigureCoordination(HeadlessCoordinationSetupBuilder setup) => setup.UseSqlServer(ConnectionString);

    public void ConfigureStore(DbContextOptionsBuilder db) => db.UseSqlServer(ConnectionString);

    public void ConfigureClaims(JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity> builder) =>
        builder.UseSqlServerClaims();

    public DbConnection CreateConnection() => new SqlConnection(ConnectionString);

    public void ConfigureUnitOfWork(IServiceCollection services) =>
        throw new NotSupportedException("The chaos scenarios enlist no unit of work.");

    public void ConfigureMessagingStorage(MessagingSetupBuilder setup) =>
        throw new NotSupportedException("The chaos scenarios wire no messaging storage.");

    public Task RunCoordinatedTransactionAsync(
        IServiceProvider services,
        Func<IServiceProvider, IUnitOfWork, DbConnection, DbTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    ) => throw new NotSupportedException("The chaos scenarios enlist no unit of work.");
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StoreChaosPostgreSqlCollection : ICollectionFixture<StoreChaosPostgreSqlFixture>
{
    public const string Name = "store-chaos-postgresql";
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StoreChaosSqlServerCollection : ICollectionFixture<StoreChaosSqlServerFixture>
{
    public const string Name = "store-chaos-sqlserver";
}
