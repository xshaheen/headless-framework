// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Coordination.PostgreSql;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.DependencyInjection;
using Headless.Testing.Testcontainers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
public sealed class JobsCoordinationFixture : HeadlessPostgreSqlFixture, ICollectionFixture<JobsCoordinationFixture>
{
    public const string ClusterName = "jobs-it";

    // Membership thresholds tuned low so a stopped host's liveness expires within seconds (real dead-node
    // recovery), while staying inside the CoordinationOptions validator envelope:
    // Heartbeat < Suspicion < Dead, and DeadRetention >= 2 * Heartbeat.
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan SuspicionThreshold = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan DeadThreshold = TimeSpan.FromMilliseconds(1200);
    public static readonly TimeSpan DeadRetentionWindow = TimeSpan.FromMilliseconds(1200);

    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure()
            .WithDatabase("jobs_coordination_test")
            .WithUsername("postgres")
            .WithPassword("postgres");
    }

    /// <summary>Drops every Jobs and Coordination table so each test starts from an empty database.</summary>
    public async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "DROP SCHEMA IF EXISTS jobs CASCADE;"
                + "DROP TABLE IF EXISTS coordination_liveness, coordination_descriptor, coordination_node_generation CASCADE;",
            connection
        );

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Builds (but does not start) a host wired the way a production Jobs node is: a Coordination Postgres
    /// provider registered <em>before</em> the durable Jobs store so the require-a-provider check is satisfied.
    /// </summary>
    public IHost BuildHost(
        string nodeId,
        MembershipLostBehavior lostBehavior = MembershipLostBehavior.StopMembershipOnly
    )
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddHeadlessCoordination(setup =>
        {
            setup.UsePostgreSql(ConnectionString);
            setup.Configure(options =>
            {
                options.ClusterName = ClusterName;
                options.ConfiguredNodeId = nodeId;
                options.HeartbeatInterval = HeartbeatInterval;
                options.SuspicionThreshold = SuspicionThreshold;
                options.DeadThreshold = DeadThreshold;
                options.DeadRetentionWindow = DeadRetentionWindow;
                options.MembershipLostBehavior = lostBehavior;
            });
        });

        builder.Services.AddHeadlessJobs(options =>
        {
            // The scheduler is disabled so it never races the tests' direct persistence calls. Dead-node recovery
            // is driven by the MembershipRecoveryBridge + coordination heartbeat, both of which still run.
            options.DisableBackgroundServices();
            options.AddOperationalStore(ef =>
                ef.UseJobsDbContext<JobsDbContext>(db => db.UseNpgsql(ConnectionString), schema: "jobs")
            );
        });

        return builder.Build();
    }

    /// <summary>
    /// Creates the Jobs tables in the (already-existing) container database. <c>EnsureCreated</c> is a no-op
    /// against an existing database, so we drive the relational creator directly to emit the schema + tables.
    /// </summary>
    public static async Task CreateJobsSchemaAsync(IHost host, CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        var creator = (RelationalDatabaseCreator)db.GetService<IDatabaseCreator>();
        await creator.CreateTablesAsync(cancellationToken);
    }

    /// <summary>Inserts a TimeJob row with an exact status/owner — bypasses the entity's internal setters.</summary>
    public async Task SeedTimeJobAsync(
        Guid id,
        string function,
        int status,
        string? lockHolder,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO jobs."TimeJobs"
                ("Id", "Function", "Description", "Status", "LockHolder",
                 "CreatedAt", "UpdatedAt", "ElapsedTime", "Retries", "RetryCount")
            VALUES (@id, @function, @function, @status, @lockHolder, now(), now(), 0, 0, 0);
            """,
            connection
        );
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("function", function);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("lockHolder", (object?)lockHolder ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Reads back a TimeJob's status + owner for assertions.</summary>
    public async Task<(int Status, string? LockHolder)> ReadTimeJobAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """SELECT "Status", "LockHolder" FROM jobs."TimeJobs" WHERE "Id" = @id;""",
            connection
        );
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"TimeJob {id} not found.");
        }

        var status = reader.GetInt32(0);
        var lockHolder = await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1);

        return (status, lockHolder);
    }
}
