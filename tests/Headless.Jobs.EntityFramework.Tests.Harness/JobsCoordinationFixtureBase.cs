// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Coordination;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.DependencyInjection;
using Headless.Jobs.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Provider-neutral contract for a Jobs+Coordination integration fixture. Each leaf fixture owns its own
/// Testcontainers instance (Postgres or SQL Server) and implements these members; all shared host wiring,
/// schema creation, reset, and raw-SQL seeding live in <see cref="JobsCoordinationFixtureExtensions" />.
/// The single container backs both the Jobs operational store (schema <c>jobs</c>) and the Coordination
/// provider (its own <c>coordination_*</c> tables).
/// </summary>
public interface IJobsCoordinationFixture
{
    /// <summary>Connection string to the shared container database.</summary>
    string ConnectionString { get; }

    /// <summary>Wires the Coordination provider for this backend (e.g. <c>setup.UsePostgreSql(ConnectionString)</c>).</summary>
    void ConfigureCoordination(HeadlessCoordinationSetupBuilder setup);

    /// <summary>Wires the EF Core provider for the Jobs store (e.g. <c>db.UseNpgsql(ConnectionString)</c>).</summary>
    void ConfigureStore(DbContextOptionsBuilder db);

    /// <summary>Creates a new, unopened provider-specific connection (Npgsql / SqlClient).</summary>
    DbConnection CreateConnection();

    /// <summary>Fully-qualified, provider-quoted TimeJobs table (Postgres: <c>jobs."TimeJobs"</c>; SqlServer: <c>[jobs].[TimeJobs]</c>).</summary>
    string QualifiedTimeJobsTable { get; }

    /// <summary>Fully-qualified, provider-quoted CronJobs table (Postgres: <c>jobs."CronJobs"</c>; SqlServer: <c>[jobs].[CronJobs]</c>).</summary>
    string QualifiedCronJobsTable { get; }

    /// <summary>Provider SQL expression for "now in UTC" (Postgres: <c>now()</c>; SqlServer: <c>SYSUTCDATETIME()</c>).</summary>
    string UtcNowSqlExpression { get; }

    /// <summary>Provider DDL that drops every Jobs and Coordination table so each test starts empty.</summary>
    string ResetSql { get; }
}

/// <summary>
/// Shared, provider-neutral behavior over <see cref="IJobsCoordinationFixture" />: host bootstrap (Coordination
/// before Jobs), Jobs schema creation, database reset, and raw-SQL seed/read of TimeJob rows (raw SQL because
/// <c>TimeJobEntity.Status/OwnerId</c> have internal setters). Mirrors the Coordination harness's
/// <c>CoordinationFixtureExtensions</c> shape.
/// </summary>
public static class JobsCoordinationFixtureExtensions
{
    public const string ClusterName = "jobs-it";

    // Membership thresholds tuned low so a stopped host's liveness expires within seconds (real dead-node
    // recovery), while staying inside the CoordinationOptions validator envelope:
    // Heartbeat < Suspicion < Dead, and DeadRetention >= 2 * Heartbeat.
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan SuspicionThreshold = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan DeadThreshold = TimeSpan.FromMilliseconds(1200);
    public static readonly TimeSpan DeadRetentionWindow = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// Builds (but does not start) a host wired the way a production Jobs node is: a Coordination provider
    /// registered <em>before</em> the durable Jobs store so the require-a-provider check is satisfied.
    /// </summary>
    public static IHost BuildHost(
        this IJobsCoordinationFixture fixture,
        string nodeId,
        MembershipLostBehavior lostBehavior = MembershipLostBehavior.StopMembershipOnly,
        TimeProvider? timeProvider = null
    )
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // IMPORTANT: Coordination must be registered before Jobs so the durable store's require-a-provider
        // check (R5) is satisfied at registration time.
        builder.Services.AddHeadlessCoordination(setup =>
        {
            fixture.ConfigureCoordination(setup);
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
                ef.UseJobsDbContext<JobsDbContext>(fixture.ConfigureStore, schema: "jobs")
            );
        });

        // Lets a test inject a deliberately skewed clock to prove the EF lease-expiry path reads the DB clock, not
        // this node's TimeProvider (#316 clock-skew). Registered last so it wins over the framework's default.
        if (timeProvider is not null)
        {
            builder.Services.AddSingleton(timeProvider);
        }

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

    /// <summary>Drops every Jobs and Coordination table so each test starts from an empty database.</summary>
    public static async Task ResetDatabaseAsync(
        this IJobsCoordinationFixture fixture,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.ResetSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Inserts a TimeJob row with an exact status/owner — bypasses the entity's internal setters.</summary>
    public static async Task SeedTimeJobAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        string function,
        int status,
        string? ownerId,
        CancellationToken cancellationToken,
        NodeDeathPolicy onNodeDeath = NodeDeathPolicy.Retry,
        DateTime? lockedUntil = null
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO {fixture.QualifiedTimeJobsTable} ({_InsertColumns}) "
            + $"VALUES (@id, @function, @function, @status, @ownerId, "
            + $"{fixture.UtcNowSqlExpression}, {fixture.UtcNowSqlExpression}, 0, 0, 0, @onNodeDeath, @lockedUntil);";

        // Status and OnNodeDeath persist as enum names (HasConversion<string>), so seed the names, not ordinals.
        _AddParameter(command, "@id", id);
        _AddParameter(command, "@function", function);
        _AddParameter(command, "@status", ((JobStatus)status).ToString());
        _AddParameter(command, "@ownerId", (object?)ownerId ?? DBNull.Value);
        _AddParameter(command, "@onNodeDeath", onNodeDeath.ToString());
        _AddParameter(command, "@lockedUntil", (object?)lockedUntil ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Inserts a CronJob row with an explicit node-death policy (FK target for seeded occurrences).</summary>
    public static async Task SeedCronJobAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        string function,
        string expression,
        NodeDeathPolicy onNodeDeath,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO {fixture.QualifiedCronJobsTable} ({_CronInsertColumns}) "
            + $"VALUES (@id, @function, @function, @expression, 0, {fixture.UtcNowSqlExpression}, {fixture.UtcNowSqlExpression}, @onNodeDeath);";

        _AddParameter(command, "@id", id);
        _AddParameter(command, "@function", function);
        _AddParameter(command, "@expression", expression);
        _AddParameter(command, "@onNodeDeath", onNodeDeath.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Reads back a TimeJob's status + owner for assertions.</summary>
    public static async Task<(int Status, string? OwnerId)> ReadTimeJobAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var detail = await fixture.ReadTimeJobDetailAsync(id, cancellationToken);
        return (detail.Status, detail.OwnerId);
    }

    /// <summary>
    /// Reads a TimeJob's status, owner, lease deadline, and failure/skip reasons — for asserting dead-node
    /// sweep hygiene (terminal rows clear <c>LockedUntil</c> but retain <c>OwnerId</c>; MarkFailed sets
    /// <c>ExceptionMessage</c>, Skip sets <c>SkippedReason</c>).
    /// </summary>
    public static async Task<(
        int Status,
        string? OwnerId,
        DateTime? LockedUntil,
        string? ExceptionMessage,
        string? SkippedReason
    )> ReadTimeJobDetailAsync(this IJobsCoordinationFixture fixture, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT \"Status\", \"OwnerId\", \"LockedUntil\", \"ExceptionMessage\", \"SkippedReason\" "
            + $"FROM {fixture.QualifiedTimeJobsTable} WHERE \"Id\" = @id;";
        _AddParameter(command, "@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"TimeJob {id} not found.");
        }

        // Status persists as its enum name; parse back to the ordinal the callers assert against.
        var status = (int)Enum.Parse<JobStatus>(reader.GetString(0));
        var ownerId = await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1);
        var lockedUntil = await reader.IsDBNullAsync(2, cancellationToken) ? (DateTime?)null : reader.GetDateTime(2);
        var exceptionMessage = await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetString(3);
        var skippedReason = await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4);

        return (status, ownerId, lockedUntil, exceptionMessage, skippedReason);
    }

    // Column identifiers are double-quoted: SQL Server accepts ANSI double quotes for delimited identifiers
    // (QUOTED_IDENTIFIER is ON by default for SqlClient), and Postgres requires them for the PascalCase columns.
    private const string _InsertColumns =
        "\"Id\", \"Function\", \"Description\", \"Status\", \"OwnerId\", "
        + "\"CreatedAt\", \"UpdatedAt\", \"ElapsedTime\", \"Retries\", \"RetryCount\", \"OnNodeDeath\", \"LockedUntil\"";

    private const string _CronInsertColumns =
        "\"Id\", \"Function\", \"Description\", \"Expression\", \"Retries\", \"CreatedAt\", \"UpdatedAt\", \"OnNodeDeath\"";

    // Both Npgsql and SqlClient accept the "@name" parameter form.
    private static void _AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
