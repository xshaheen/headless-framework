// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Coordination;
using Headless.Coordination.SqlServer;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

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

    public void ConfigureCoordination(HeadlessCoordinationSetupBuilder setup) => setup.UseSqlServer(ConnectionString);

    public void ConfigureStore(DbContextOptionsBuilder db) => db.UseSqlServer(ConnectionString);

    public DbConnection CreateConnection() => new SqlConnection(ConnectionString);
}
