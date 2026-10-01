// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Globalization;
using Headless.Coordination;
using Headless.Hosting.Initialization;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// One Testcontainers SQL Server instance shared by every test, backing the Jobs operational store, the Coordination
/// SQL Server provider, and Messaging storage, all in the shared <c>headless</c> schema in <c>master</c>.
/// Serialized at the collection level because tests reset the whole database between runs.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerJobsCoordinationFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerJobsCoordinationFixture>,
        IJobsApplicationConfigurationFixture
{
    public StorageNamingStyle NamingStyle => StorageNamingStyle.PascalCase;

    public string QualifiedTimeJobsTable => "[headless].[TimeJobs]";

    public string QualifiedCronJobsTable => "[headless].[CronJobs]";

    public string QualifiedCronJobOccurrencesTable => "[headless].[CronJobOccurrences]";

    public string QualifyTable(string schema, string table) => $"[{schema}].[{table}]";

    public string UtcNowSqlExpression => "SYSUTCDATETIME()";

    public string UtcNowOffsetSqlExpression(int seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"DATEADD(second, {seconds}, SYSUTCDATETIME())");

    // The SQL Server EF provider translates a bare DateTime.UtcNow inside an expression tree to GETUTCDATE(), not
    // SYSUTCDATETIME(). Its datetime precision (~3.33 ms) is immaterial against minute-scale leases, and unlike
    // PostgreSQL's now() it is evaluated per statement, so it carries no transaction-anchoring hazard.
    public string EfTranslatedDatabaseClockSql => "GETUTCDATE()";

    // SQL Server has no DROP SCHEMA CASCADE. Drop child tables before parents (CronJobOccurrences -> CronJobs).
    // The shared headless schema itself stays: the Coordination tables live in it too, and every initializer
    // creates the schema when missing anyway. DROP TABLE IF EXISTS is a no-op when the table is absent.
    public string ResetSql =>
        "DROP TABLE IF EXISTS [headless].[CronJobOccurrences];"
        + "DROP TABLE IF EXISTS [consumer_jobs].[consumer_time_jobs];"
        + "IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'consumer_jobs') DROP SCHEMA [consumer_jobs];"
        + "DROP TABLE IF EXISTS [headless].[TimeJobIdempotencyReservations];"
        + "DROP TABLE IF EXISTS [headless].[TimeJobs];"
        + "DROP TABLE IF EXISTS [headless].[CronJobs];"
        + "DROP TABLE IF EXISTS [headless].[ApplicationProbe];"
        // The custom-schema conformance scenario maps the whole store into its own schema. SQL Server refuses to drop
        // a schema that still owns objects, so the tables go first, children before parents, exactly as above.
        + _CustomSchemaResetSql
        + _MappedSchemaResetSql
        + "DROP TABLE IF EXISTS [headless].[MessagingInboxAudit];"
        + "DROP TABLE IF EXISTS [headless].[MessagingInboxOperationReceipts];"
        + "DROP TABLE IF EXISTS [headless].[MessagingSchemaState];"
        + "DROP TABLE IF EXISTS [headless].[MessagingPublished];"
        + "DROP TABLE IF EXISTS [headless].[MessagingReceived];"
        + "IF TYPE_ID(N'headless.HeadlessMessagingIdList') IS NOT NULL DROP TYPE [headless].[HeadlessMessagingIdList];"
        + "IF TYPE_ID(N'headless.HeadlessMessagingOwnerList') IS NOT NULL DROP TYPE [headless].[HeadlessMessagingOwnerList];"
        + "IF TYPE_ID(N'headless.HeadlessMessagingPoisonMessageList') IS NOT NULL DROP TYPE [headless].[HeadlessMessagingPoisonMessageList];"
        + "IF TYPE_ID(N'headless.HeadlessMessagingConsumerIdentityList') IS NOT NULL DROP TYPE [headless].[HeadlessMessagingConsumerIdentityList];"
        + "DROP TABLE IF EXISTS [jobs_probe];"
        + "DROP TABLE IF EXISTS [coordination_liveness];"
        + "DROP TABLE IF EXISTS [coordination_descriptor];"
        + "DROP TABLE IF EXISTS [coordination_node_generation];";

    private static string _CustomSchemaResetSql
    {
        get
        {
            const string schema = JobsCoordinationFixtureExtensions.CustomSchemaName;

            return $"DROP TABLE IF EXISTS [{schema}].[CronJobOccurrences];"
                + $"DROP TABLE IF EXISTS [{schema}].[TimeJobIdempotencyReservations];"
                + $"DROP TABLE IF EXISTS [{schema}].[TimeJobs];"
                + $"DROP TABLE IF EXISTS [{schema}].[CronJobs];"
                + $"IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = '{schema}') DROP SCHEMA [{schema}];";
        }
    }

    // The renamed-mapping claim test owns its own mapped_jobs cleanup, but this fixture reuses its container: if that
    // test's teardown is cut short, the leftovers wedge every later run at CREATE TABLE. Drop them defensively here
    // for the same reason the stale jobs leftovers above are dropped.
    private const string _MappedSchemaResetSql =
        "DROP TABLE IF EXISTS [mapped_jobs].[native_cron_occurrences];"
        + "DROP TABLE IF EXISTS [mapped_jobs].[native_time_jobs];"
        + "DROP TABLE IF EXISTS [mapped_jobs].[TimeJobIdempotencyReservations];"
        + "DROP TABLE IF EXISTS [mapped_jobs].[CronJobs];"
        + "IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'mapped_jobs') DROP SCHEMA [mapped_jobs];";

    public string CreateProbeTableSql => "DROP TABLE IF EXISTS jobs_probe; CREATE TABLE jobs_probe (id int);";

    public void ConfigureCoordination(HeadlessCoordinationSetupBuilder setup)
    {
        setup.UseSqlServer(ConnectionString);
    }

    public void ConfigureStore(DbContextOptionsBuilder db)
    {
        db.UseSqlServer(ConnectionString);
    }

    public void ConfigureClaims(JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity> builder)
    {
        builder.UseSqlServerClaims();
    }

    public void ConfigureApplicationJobs<TContext>(
        JobsOptionsBuilder<TimeJobEntity, CronJobEntity> builder,
        Action<CoordinationOptions> configureCoordination
    )
        where TContext : DbContext
    {
        builder.UseSqlServer<TContext>(configureCoordination);
    }

    public DbConnection CreateConnection()
    {
        return new SqlConnection(ConnectionString);
    }

    public void ConfigureUnitOfWork(IServiceCollection services)
    {
        services.AddSqlServerUnitOfWork();
    }

    public void ConfigureMessagingStorage(MessagingSetupBuilder setup)
    {
        setup.UseSqlServer(ConnectionString);
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
        await using var connection = new SqlConnection(ConnectionString);

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
