// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    /// <summary>Enables this backend's native claim strategy through the public Jobs EF builder.</summary>
    void ConfigureClaims(JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity> builder);

    /// <summary>Creates a new, unopened provider-specific connection (Npgsql / SqlClient).</summary>
    DbConnection CreateConnection();

    /// <summary>Fully-qualified, provider-quoted TimeJobs table (Postgres: <c>jobs."TimeJobs"</c>; SqlServer: <c>[jobs].[TimeJobs]</c>).</summary>
    string QualifiedTimeJobsTable { get; }

    /// <summary>Fully-qualified, provider-quoted CronJobs table (Postgres: <c>jobs."CronJobs"</c>; SqlServer: <c>[jobs].[CronJobs]</c>).</summary>
    string QualifiedCronJobsTable { get; }

    /// <summary>Fully-qualified, provider-quoted CronJobOccurrences table (Postgres: <c>jobs."CronJobOccurrences"</c>; SqlServer: <c>[jobs].[CronJobOccurrences]</c>).</summary>
    string QualifiedCronJobOccurrencesTable { get; }

    /// <summary>Provider SQL expression for "now in UTC" (Postgres: <c>now()</c>; SqlServer: <c>SYSUTCDATETIME()</c>).</summary>
    string UtcNowSqlExpression { get; }

    /// <summary>
    /// The server-clock function EF Core emits when it translates a bare <c>DateTime.UtcNow</c> inside an
    /// expression tree (Postgres: <c>now()</c>; SqlServer: <c>GETUTCDATE()</c>). This is NOT
    /// <see cref="UtcNowSqlExpression" />: that one is what the harness itself writes in seed SQL, this one is
    /// what the EF provider chooses. <see cref="JobsDatabaseClockConformanceTests{TFixture}" /> asserts it against
    /// the SQL the real Jobs lease paths put on the wire.
    /// </summary>
    string EfTranslatedDatabaseClockSql { get; }

    /// <summary>Provider DDL that drops every Jobs and Coordination table so each test starts empty.</summary>
    string ResetSql { get; }

    /// <summary>Provider DDL that creates the atomicity probe table if absent and clears its rows.</summary>
    string CreateProbeTableSql { get; }

    /// <summary>Registers this backend's commit-coordination provider (e.g. <c>services.AddPostgreSqlCommitCoordination()</c>).</summary>
    void ConfigureCommitCoordination(IServiceCollection services);

    /// <summary>Wires this backend's relational messaging storage against <see cref="ConnectionString" />.</summary>
    void ConfigureMessagingStorage(MessagingSetupBuilder setup);

    /// <summary>
    /// Opens a provider connection, begins a commit-coordinated transaction, enlists it, and runs
    /// <paramref name="operation" /> with the live connection + ambient transaction. The helper owns enlist/commit;
    /// an operation exception propagates and rolls the transaction back — the AsyncLocal-capture regression net
    /// (a stranded capture would take the direct path and leave a row after rollback) relies on this.
    /// </summary>
    Task RunCoordinatedTransactionAsync(
        IServiceProvider services,
        Func<DbConnection, DbTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    );
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

    /// <summary>Registers the generated-equivalent job functions used by the relational conformance suite.</summary>
    public static void RegisterJobFunctions() => CoordinatedEnqueueJobsRegistration.Initialize();

    /// <summary>
    /// Builds (but does not start) a host wired the way a production Jobs node is: a Coordination provider
    /// registered <em>before</em> the durable Jobs store so the require-a-provider check is satisfied.
    /// </summary>
    public static IHost BuildHost(
        this IJobsCoordinationFixture fixture,
        string nodeId,
        MembershipLostBehavior lostBehavior = MembershipLostBehavior.StopMembershipOnly,
        TimeProvider? timeProvider = null,
        TimeSpan? leaseDuration = null,
        bool useNativeClaims = true
    )
    {
        return _BuildHost<JobsDbContext>(
            fixture,
            nodeId,
            "jobs",
            lostBehavior,
            timeProvider,
            leaseDuration,
            useNativeClaims
        );
    }

    /// <summary>Builds a host with a custom Jobs DbContext so provider SQL can be verified against renamed mappings.</summary>
    public static IHost BuildMappedHost<TDbContext>(this IJobsCoordinationFixture fixture, string nodeId, string schema)
        where TDbContext : JobsDbContext<TimeJobEntity, CronJobEntity>
    {
        return _BuildHost<TDbContext>(fixture, nodeId, schema, MembershipLostBehavior.StopMembershipOnly, null);
    }

    /// <summary>
    /// Builds a host whose <em>real</em> Jobs DbContext carries <paramref name="interceptor" />, so a test can read
    /// the SQL the production lease paths actually put on the wire. Native claiming is off: the native strategies
    /// build raw ADO commands off the underlying connection (bypassing EF's interception pipeline) and spell
    /// <c>clock_timestamp()</c> / <c>SYSUTCDATETIME()</c> literally in their SQL, so they are not the subject here.
    /// The EF-translated clock — the one a client-evaluated regression could silently replace — is.
    /// </summary>
    public static IHost BuildInterceptedHost(
        this IJobsCoordinationFixture fixture,
        string nodeId,
        IInterceptor interceptor,
        TimeSpan? leaseDuration = null
    )
    {
        return _BuildHost<JobsDbContext>(
            fixture,
            nodeId,
            "jobs",
            MembershipLostBehavior.StopMembershipOnly,
            timeProvider: null,
            leaseDuration,
            useNativeClaims: false,
            interceptor
        );
    }

    private static IHost _BuildHost<TDbContext>(
        IJobsCoordinationFixture fixture,
        string nodeId,
        string schema,
        MembershipLostBehavior lostBehavior,
        TimeProvider? timeProvider,
        TimeSpan? leaseDuration = null,
        bool useNativeClaims = true,
        IInterceptor? interceptor = null
    )
        where TDbContext : JobsDbContext<TimeJobEntity, CronJobEntity>
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
            if (leaseDuration is not null)
            {
                options.ConfigureScheduler(scheduler => scheduler.LeaseDuration = leaseDuration.Value);
            }
            options.UseEntityFramework(ef =>
            {
                ef.UseJobsDbContext<TDbContext>(
                    db =>
                    {
                        fixture.ConfigureStore(db);
                        if (interceptor is not null)
                        {
                            db.AddInterceptors(interceptor);
                        }
                    },
                    schema
                );
                if (useNativeClaims)
                {
                    fixture.ConfigureClaims(ef);
                }
            });
        });

        // Lets a test inject a deliberately skewed clock to prove the EF lease-expiry path reads the DB clock, not
        // this node's TimeProvider (#316 clock-skew). Registered last so it wins over the framework's default.
        if (timeProvider is not null)
        {
            builder.Services.AddSingleton(timeProvider);
        }

        return builder.Build();
    }

    /// <summary>The time-job function the coordinated-enqueue conformance scenarios enqueue against.</summary>
    public const string CoordinatedFunctionName = "Coordinated_Enqueue_Sample";

    /// <summary>The typed function scheduled through <see cref="IJobScheduler" /> by facade conformance scenarios.</summary>
    public const string CoordinatedFacadeFunctionName = "Coordinated_Facade_Enqueue_Sample";

    /// <summary>
    /// Builds (but does not start) a host wired like <see cref="BuildHost" /> plus commit coordination, so the
    /// <c>JobsManager</c> coordinated-enqueue path is active and <c>ExecuteCoordinatedTransactionAsync</c> can enlist.
    /// A test time-job function is registered before the host's startup <c>Build()</c> so <c>AddAsync</c> validation
    /// passes (empty cron expression so the startup seeder ignores it).
    /// </summary>
    internal static IHost BuildCoordinatedEnqueueHost(
        this IJobsCoordinationFixture fixture,
        string nodeId,
        bool includeMessaging = false,
        JobsSideEffectsProbe? sideEffectsProbe = null
    )
    {
        return _BuildCoordinatedEnqueueHost<JobsDbContext>(
            fixture,
            nodeId,
            includeMessaging: includeMessaging,
            sideEffectsProbe: sideEffectsProbe
        );
    }

    /// <summary>
    /// Builds the coordinated-enqueue host with a custom Jobs context and optional options instrumentation.
    /// </summary>
    public static IHost BuildCoordinatedEnqueueHost<TDbContext>(
        this IJobsCoordinationFixture fixture,
        string nodeId,
        Action<DbContextOptionsBuilder>? configureOptions = null,
        bool includeMessaging = false
    )
        where TDbContext : JobsDbContext<TimeJobEntity, CronJobEntity>
    {
        return _BuildCoordinatedEnqueueHost<TDbContext>(fixture, nodeId, configureOptions, includeMessaging);
    }

    /// <summary>
    /// Builds the coordinated-enqueue host with the Jobs tenancy seam enabled (<c>PropagateTenant</c>) plus a
    /// consumer-supplied ambient <see cref="ICurrentTenant" /> so an integration test can prove schedule-time ambient
    /// capture flows through the middleware into the persisted row. The consumer-supplied tenant also satisfies the
    /// Jobs propagation startup validator's "a real tenant source is present" check.
    /// </summary>
    public static IHost BuildTenantPropagationEnqueueHost(this IJobsCoordinationFixture fixture, string nodeId)
    {
        return _BuildCoordinatedEnqueueHost<JobsDbContext>(fixture, nodeId, enableTenantPropagation: true);
    }

    private static IHost _BuildCoordinatedEnqueueHost<TDbContext>(
        IJobsCoordinationFixture fixture,
        string nodeId,
        Action<DbContextOptionsBuilder>? configureOptions = null,
        bool includeMessaging = false,
        JobsSideEffectsProbe? sideEffectsProbe = null,
        bool enableTenantPropagation = false
    )
        where TDbContext : JobsDbContext<TimeJobEntity, CronJobEntity>
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

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
            });
        });

        builder.Services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            options.UseEntityFramework(ef =>
                ef.UseJobsDbContext<TDbContext>(
                    db =>
                    {
                        fixture.ConfigureStore(db);
                        configureOptions?.Invoke(db);
                    },
                    schema: "jobs"
                )
            );
        });

        if (sideEffectsProbe is not null)
        {
            builder.Services.AddSingleton<IJobsHostScheduler>(sideEffectsProbe);
            builder.Services.AddSingleton<IJobsNotificationHubSender>(sideEffectsProbe);
        }

        if (includeMessaging)
        {
            builder.Services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemory();
                fixture.ConfigureMessagingStorage(setup);
            });
        }

        // AddCommitCoordination wins over the Jobs null-coordinator fallback (AddSingleton over TryAddSingleton),
        // so ICurrentCommitCoordinator resolves to the real scope stack that EnlistCommitCoordination pushes onto.
        fixture.ConfigureCommitCoordination(builder.Services);

        if (enableTenantPropagation)
        {
            // Registered after AddHeadlessJobs (last-wins over the framework CurrentTenant fallback) so both the schedule
            // middleware and the test resolve THIS ambient tenant. Being neither CurrentTenant nor NullCurrentTenant, it
            // is the "real tenant source" the propagation startup validator requires, so StartAsync does not error.
            builder.Services.AddSingleton<ICurrentTenant, HarnessAmbientCurrentTenant>();
            builder.AddHeadlessTenancy(tenancy => tenancy.Jobs(jobs => jobs.PropagateTenant()));
        }

        return builder.Build();
    }

    /// <summary>Creates the atomicity probe table (outside any coordinated transaction) and clears its rows.</summary>
    public static async Task CreateProbeTableAsync(
        this IJobsCoordinationFixture fixture,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.CreateProbeTableSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Inserts one probe row inside the supplied coordinated transaction (a stand-in domain write).</summary>
    public static async Task InsertProbeRowAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO jobs_probe (id) VALUES (1);";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Counts probe rows on an independent connection (observes committed state only).</summary>
    public static Task<int> CountProbeRowsAsync(
        this IJobsCoordinationFixture fixture,
        CancellationToken cancellationToken
    )
    {
        return _CountAsync(fixture, "SELECT COUNT(*) FROM jobs_probe;", cancellationToken);
    }

    /// <summary>Counts TimeJob rows on an independent connection (observes committed state only).</summary>
    public static Task<int> CountTimeJobsAsync(
        this IJobsCoordinationFixture fixture,
        CancellationToken cancellationToken
    )
    {
        return _CountAsync(fixture, $"SELECT COUNT(*) FROM {fixture.QualifiedTimeJobsTable};", cancellationToken);
    }

    /// <summary>Counts CronJob rows on an independent connection (observes committed state only).</summary>
    public static Task<int> CountCronJobsAsync(
        this IJobsCoordinationFixture fixture,
        CancellationToken cancellationToken
    )
    {
        return _CountAsync(fixture, $"SELECT COUNT(*) FROM {fixture.QualifiedCronJobsTable};", cancellationToken);
    }

    /// <summary>Counts CronJobOccurrence rows on an independent connection.</summary>
    public static Task<int> CountCronOccurrencesAsync(
        this IJobsCoordinationFixture fixture,
        CancellationToken cancellationToken
    )
    {
        return _CountAsync(
            fixture,
            $"SELECT COUNT(*) FROM {fixture.QualifiedCronJobOccurrencesTable};",
            cancellationToken
        );
    }

    /// <summary>Counts published outbox rows on an independent connection (observes committed state only).</summary>
    public static Task<int> CountPublishedMessagesAsync(
        this IJobsCoordinationFixture fixture,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var table = services.GetRequiredService<IStorageInitializer>().GetPublishedTableName();
        return _CountAsync(fixture, $"SELECT COUNT(*) FROM {table};", cancellationToken);
    }

    private static async Task<int> _CountAsync(
        IJobsCoordinationFixture fixture,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var scalar = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Creates the Jobs tables in the (already-existing) container database. <c>EnsureCreated</c> is a no-op
    /// against an existing database, so we drive the relational creator directly to emit the schema + tables.
    /// </summary>
    public static async Task CreateJobsSchemaAsync(IHost host, CancellationToken cancellationToken)
    {
        await CreateJobsSchemaAsync<JobsDbContext>(host, cancellationToken);
    }

    /// <summary>Creates tables for a custom mapped Jobs DbContext.</summary>
    public static async Task CreateJobsSchemaAsync<TDbContext>(IHost host, CancellationToken cancellationToken)
        where TDbContext : DbContext
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
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
            + "VALUES (@id, @function, @function, @status, @ownerId, "
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
            + $"VALUES (@id, @function, @function, @expression, @timeZoneId, @isPaused, @scheduleRevision, 0, {fixture.UtcNowSqlExpression}, {fixture.UtcNowSqlExpression}, @onNodeDeath);";

        _AddParameter(command, "@id", id);
        _AddParameter(command, "@function", function);
        _AddParameter(command, "@expression", expression);
        _AddParameter(command, "@timeZoneId", DBNull.Value);
        _AddParameter(command, "@isPaused", false);
        _AddParameter(command, "@scheduleRevision", 0L);
        _AddParameter(command, "@onNodeDeath", onNodeDeath.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts a CronJobOccurrence row with an exact status/owner/lease — bypasses the entity's internal setters.
    /// Requires a parent CronJob (FK <c>CronJobId</c>) to already exist (seed it via <see cref="SeedCronJobAsync" />).
    /// </summary>
    public static async Task SeedCronOccurrenceAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        Guid cronJobId,
        int status,
        string? ownerId,
        NodeDeathPolicy onNodeDeath,
        DateTime? lockedUntil,
        DateTime executionTime,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // ExecutionTime is an explicit parameter, not now(): the (CronJobId, ExecutionTime) unique index requires
        // distinct execution times when several occurrences of the same cron are seeded together.
        command.CommandText =
            $"INSERT INTO {fixture.QualifiedCronJobOccurrencesTable} ({_CronOccurrenceInsertColumns}) "
            + "VALUES (@id, @cronJobId, @status, @ownerId, @executionTime, "
            + $"{fixture.UtcNowSqlExpression}, {fixture.UtcNowSqlExpression}, 0, 0, @onNodeDeath, @lockedUntil);";

        _AddParameter(command, "@id", id);
        _AddParameter(command, "@cronJobId", cronJobId);
        _AddParameter(command, "@status", ((JobStatus)status).ToString());
        _AddParameter(command, "@ownerId", (object?)ownerId ?? DBNull.Value);
        _AddParameter(command, "@executionTime", executionTime);
        _AddParameter(command, "@onNodeDeath", onNodeDeath.ToString());
        _AddParameter(command, "@lockedUntil", (object?)lockedUntil ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Reads back a CronJobOccurrence's status + owner for assertions.</summary>
    public static async Task<(int Status, string? OwnerId)> ReadCronOccurrenceAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT \"Status\", \"OwnerId\" FROM {fixture.QualifiedCronJobOccurrencesTable} WHERE \"Id\" = @id;";
        _AddParameter(command, "@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"CronJobOccurrence {id} not found.");
        }

        var status = (int)Enum.Parse<JobStatus>(reader.GetString(0));
        var ownerId = await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1);

        return (status, ownerId);
    }

    /// <summary>Reads a CronJobOccurrence's durable owner and lease.</summary>
    public static async Task<(string? OwnerId, DateTime? LockedUntil)> ReadCronOccurrenceClaimAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT \"OwnerId\", \"LockedUntil\" FROM {fixture.QualifiedCronJobOccurrencesTable} WHERE \"Id\" = @id;";
        _AddParameter(command, "@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"CronJobOccurrence {id} not found.");
        }

        var ownerId = await reader.IsDBNullAsync(0, cancellationToken) ? null : reader.GetString(0);
        var lockedUntil = await reader.IsDBNullAsync(1, cancellationToken) ? (DateTime?)null : reader.GetDateTime(1);
        return (ownerId, lockedUntil);
    }

    /// <summary>Reads a CronJobOccurrence's database-stamped lease timestamps.</summary>
    public static async Task<(DateTime? LockedUntil, DateTime UpdatedAt)> ReadCronOccurrenceClaimTimestampsAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT \"LockedUntil\", \"UpdatedAt\" FROM {fixture.QualifiedCronJobOccurrencesTable} WHERE \"Id\" = @id;";
        _AddParameter(command, "@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"CronJobOccurrence {id} not found.");
        }

        var lockedUntil = await reader.IsDBNullAsync(0, cancellationToken) ? (DateTime?)null : reader.GetDateTime(0);
        return (lockedUntil, reader.GetDateTime(1));
    }

    /// <summary>Reads back a TimeJob's status + owner for assertions.</summary>
    public static async Task<(int Status, string? OwnerId)> ReadTimeJobAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var (status, ownerId, _, _, _) = await fixture.ReadTimeJobDetailAsync(id, cancellationToken);
        return (status, ownerId);
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
            "SELECT \"Status\", \"OwnerId\", \"LockedUntil\", \"ExceptionMessage\", \"SkippedReason\" "
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

    /// <summary>Reads a TimeJob's database-stamped lease timestamps.</summary>
    public static async Task<(DateTime? LockedUntil, DateTime UpdatedAt)> ReadTimeJobClaimTimestampsAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT \"LockedUntil\", \"UpdatedAt\" FROM {fixture.QualifiedTimeJobsTable} WHERE \"Id\" = @id;";
        _AddParameter(command, "@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"TimeJob {id} not found.");
        }

        var lockedUntil = await reader.IsDBNullAsync(0, cancellationToken) ? (DateTime?)null : reader.GetDateTime(0);
        return (lockedUntil, reader.GetDateTime(1));
    }

    /// <summary>Reads a TimeJob's persisted <c>TenantId</c> (system scope reads back as <see langword="null"/>).</summary>
    public static async Task<string?> ReadTimeJobTenantAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"TenantId\" FROM {fixture.QualifiedTimeJobsTable} WHERE \"Id\" = @id;";
        _AddParameter(command, "@id", id);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);

        return scalar is null or DBNull ? null : (string)scalar;
    }

    // Column identifiers are double-quoted: SQL Server accepts ANSI double quotes for delimited identifiers
    // (QUOTED_IDENTIFIER is ON by default for SqlClient), and Postgres requires them for the PascalCase columns.
    private const string _InsertColumns =
        "\"Id\", \"Function\", \"Description\", \"Status\", \"OwnerId\", "
        + "\"CreatedAt\", \"UpdatedAt\", \"ElapsedTime\", \"Retries\", \"RetryCount\", \"OnNodeDeath\", \"LockedUntil\"";

    private const string _CronInsertColumns =
        "\"Id\", \"Function\", \"Description\", \"Expression\", \"TimeZoneId\", \"IsPaused\", \"ScheduleRevision\", "
        + "\"Retries\", \"CreatedAt\", \"UpdatedAt\", \"OnNodeDeath\"";

    private const string _CronOccurrenceInsertColumns =
        "\"Id\", \"CronJobId\", \"Status\", \"OwnerId\", \"ExecutionTime\", "
        + "\"CreatedAt\", \"UpdatedAt\", \"ElapsedTime\", \"RetryCount\", \"OnNodeDeath\", \"LockedUntil\"";

    // Both Npgsql and SqlClient accept the "@name" parameter form.
    private static void _AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        if (value is DateTime dateTime)
        {
            parameter.DbType = DbType.DateTime2;
            parameter.Value = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
        }
        else
        {
            parameter.Value = value;
        }
        command.Parameters.Add(parameter);
    }
}

/// <summary>Typed payload registered as a generated-equivalent job-function request by the relational harness.</summary>
public sealed record CoordinatedFacadeRequest(Guid Id, string Value);

internal static class CoordinatedEnqueueJobs
{
    public static Task RunAsync(JobFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public static Task RunAsync(
        JobFunctionContext<CoordinatedFacadeRequest> context,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;
}

internal static class CoordinatedEnqueueJobsRegistration
{
    internal static void Initialize()
    {
        JobFunctionProvider.RegisterFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [JobsCoordinationFixtureExtensions.CoordinatedFunctionName] = new JobFunctionRegistration
                {
                    CronExpression = string.Empty,
                    Priority = JobPriority.LongRunning,
                    Delegate = (_, context, cancellationToken) =>
                        CoordinatedEnqueueJobs.RunAsync(context, cancellationToken),
                    MaxConcurrency = 1,
                },
                [JobsCoordinationFixtureExtensions.CoordinatedFacadeFunctionName] = new JobFunctionRegistration
                {
                    CronExpression = string.Empty,
                    Priority = JobPriority.LongRunning,
                    Delegate = async (_, context, cancellationToken) =>
                    {
                        var request = await JobsRequestProvider
                            .GetRequestAsync<CoordinatedFacadeRequest>(context, cancellationToken)
                            .ConfigureAwait(false);
                        await CoordinatedEnqueueJobs
                            .RunAsync(
                                new JobFunctionContext<CoordinatedFacadeRequest>(context, request),
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                    },
                    MaxConcurrency = 1,
                },
            }
        );
        JobFunctionProvider.RegisterRequestType(
            new Dictionary<string, (string, Type)>(StringComparer.Ordinal)
            {
                [JobsCoordinationFixtureExtensions.CoordinatedFacadeFunctionName] = (
                    typeof(CoordinatedFacadeRequest).FullName!,
                    typeof(CoordinatedFacadeRequest)
                ),
            }
        );
        JobFunctionProvider.RegisterDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                [JobsCoordinationFixtureExtensions.CoordinatedFunctionName] = new(
                    JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
                    null,
                    string.Empty,
                    JobPriority.LongRunning,
                    1
                ),
                [JobsCoordinationFixtureExtensions.CoordinatedFacadeFunctionName] = new(
                    JobsCoordinationFixtureExtensions.CoordinatedFacadeFunctionName,
                    typeof(CoordinatedFacadeRequest),
                    string.Empty,
                    JobPriority.LongRunning,
                    1
                ),
            }
        );
    }
}

/// <summary>
/// Consumer-supplied ambient <see cref="ICurrentTenant" /> for the tenancy conformance host: an AsyncLocal-backed
/// tenant a test drives with <see cref="Change" /> to prove schedule-time ambient capture end-to-end. Being neither
/// the framework <c>CurrentTenant</c> nor <c>NullCurrentTenant</c>, it registers as a real tenant source that satisfies
/// the Jobs propagation startup validator.
/// </summary>
internal sealed class HarnessAmbientCurrentTenant : ICurrentTenant
{
    private readonly AsyncLocal<string?> _current = new();

    public bool IsAvailable => _current.Value is not null;

    public string? Id => _current.Value;

    public string? Name => null;

    public IDisposable Change(string? id, string? name = null)
    {
        var previous = _current.Value;
        _current.Value = id;

        return new TenantScope(() => _current.Value = previous);
    }

    private sealed class TenantScope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}

/// <summary>Observes scheduler wake-ups so conformance tests can distinguish pre-commit from post-commit effects.</summary>
internal sealed class JobsSideEffectsProbe : IJobsHostScheduler, IJobsNotificationHubSender
{
    private readonly ConcurrentQueue<Guid> _notificationIds = new();
    private int _restartCount;

    bool IJobsHostScheduler.IsRunning => false;

    public int RestartCount => Volatile.Read(ref _restartCount);

    public Guid[] NotificationIds => [.. _notificationIds];

    Task IJobsHostScheduler.StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    Task IJobsHostScheduler.StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    void IJobsHostScheduler.RestartIfNeeded(DateTime? dateTime)
    {
        if (dateTime is not null)
        {
            Interlocked.Increment(ref _restartCount);
        }
    }

    void IJobsHostScheduler.Restart()
    {
        Interlocked.Increment(ref _restartCount);
    }

    Task IJobsNotificationHubSender.AddTimeJobNotifyAsync(Guid id)
    {
        _notificationIds.Enqueue(id);
        return Task.CompletedTask;
    }

    Task IJobsNotificationHubSender.AddCronJobNotifyAsync(object cronJob)
    {
        return Task.CompletedTask;
    }

    Task IJobsNotificationHubSender.UpdateCronJobNotifyAsync(object cronJob)
    {
        return Task.CompletedTask;
    }

    Task IJobsNotificationHubSender.RemoveCronJobNotifyAsync(Guid id)
    {
        return Task.CompletedTask;
    }

    Task IJobsNotificationHubSender.AddTimeJobsBatchNotifyAsync()
    {
        return Task.CompletedTask;
    }

    Task IJobsNotificationHubSender.UpdateTimeJobNotifyAsync(object timeJob)
    {
        return Task.CompletedTask;
    }

    Task IJobsNotificationHubSender.RemoveTimeJobNotifyAsync(Guid id)
    {
        return Task.CompletedTask;
    }

    void IJobsNotificationHubSender.UpdateActiveThreads(object activeThreads) { }

    void IJobsNotificationHubSender.UpdateNextOccurrence(object nextOccurrence) { }

    void IJobsNotificationHubSender.UpdateHostStatus(object active) { }

    void IJobsNotificationHubSender.UpdateHostException(object exceptionMessage) { }

    Task IJobsNotificationHubSender.UpdateNodesAsync(object nodes)
    {
        return Task.CompletedTask;
    }

    Task IJobsNotificationHubSender.AddCronOccurrenceAsync(Guid groupId, object occurrence)
    {
        return Task.CompletedTask;
    }

    Task IJobsNotificationHubSender.UpdateCronOccurrenceAsync(Guid groupId, object occurrence)
    {
        return Task.CompletedTask;
    }

    Task IJobsNotificationHubSender.UpdateTimeJobFromExecutionState<TTimeJobEntity>(JobExecutionState executionState)
    {
        return Task.CompletedTask;
    }

    Task IJobsNotificationHubSender.UpdateCronOccurrenceFromExecutionState<TCronJobEntity>(
        JobExecutionState executionState
    )
    {
        return Task.CompletedTask;
    }

    Task IJobsNotificationHubSender.CanceledJobNotifyAsync(Guid id)
    {
        return Task.CompletedTask;
    }
}
