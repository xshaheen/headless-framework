// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Coordination;
using Headless.Coordination.PostgreSql;
using Headless.Testing.Testcontainers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// One Testcontainers Postgres instance shared by every test, backing both the Jobs operational store (schema
/// <c>jobs</c>) and the Coordination Postgres provider (its own <c>coordination_*</c> tables in <c>public</c>).
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

    public string UtcNowSqlExpression => "now()";

    public string ResetSql =>
        "DROP SCHEMA IF EXISTS jobs CASCADE;"
        + "DROP TABLE IF EXISTS coordination_liveness, coordination_descriptor, coordination_node_generation CASCADE;";

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure()
            .WithDatabase("jobs_coordination_test")
            .WithUsername("postgres")
            .WithPassword("postgres");
    }

    public void ConfigureCoordination(HeadlessCoordinationSetupBuilder setup) => setup.UsePostgreSql(ConnectionString);

    public void ConfigureStore(DbContextOptionsBuilder db) => db.UseNpgsql(ConnectionString);

    public DbConnection CreateConnection() => new NpgsqlConnection(ConnectionString);
}
