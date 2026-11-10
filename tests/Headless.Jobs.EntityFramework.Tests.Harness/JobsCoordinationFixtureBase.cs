// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Headless.Context;
using Headless.Coordination;
using Headless.Fencing;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Adds the application-context setup path to the provider-neutral Jobs fixture.
/// </summary>
public interface IJobsApplicationConfigurationFixture : IJobsCoordinationFixture
{
    /// <summary>Configures jobs through the application DbContext convenience API.</summary>
    void ConfigureApplicationJobs<TContext>(
        JobsOptionsBuilder<TimeJobEntity, CronJobEntity> builder,
        Action<CoordinationOptions> configureCoordination
    )
        where TContext : DbContext;

    /// <summary>Wires the EF Core provider for an application context with its retrying execution strategy enabled.</summary>
    void ConfigureRetryingStore(DbContextOptionsBuilder db);
}

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

    /// <summary>
    /// The casing the Jobs model uses on this backend: <see cref="StorageNamingStyle.SnakeCase" /> on PostgreSQL,
    /// <see cref="StorageNamingStyle.PascalCase" /> on SQL Server.
    /// </summary>
    StorageNamingStyle NamingStyle { get; }

    /// <summary>Fully-qualified, provider-quoted time-job table (Postgres: <c>headless.time_jobs</c>; SqlServer: <c>[headless].[TimeJobs]</c>).</summary>
    string QualifiedTimeJobsTable { get; }

    /// <summary>Fully-qualified, provider-quoted cron-job table (Postgres: <c>headless.cron_jobs</c>; SqlServer: <c>[headless].[CronJobs]</c>).</summary>
    string QualifiedCronJobsTable { get; }

    /// <summary>Fully-qualified, provider-quoted cron-occurrence table (Postgres: <c>headless.cron_job_occurrences</c>; SqlServer: <c>[headless].[CronJobOccurrences]</c>).</summary>
    string QualifiedCronJobOccurrencesTable { get; }

    /// <summary>
    /// Quotes <paramref name="table" /> in <paramref name="schema" /> the way this backend expects. The
    /// <c>Qualified*Table</c> members above are fixed to the default schema; this one lets a scenario that moves the
    /// schema read the tables wherever they actually landed.
    /// </summary>
    string QualifyTable(string schema, string table);

    /// <summary>Provider SQL expression for "now in UTC" (Postgres: <c>now()</c>; SqlServer: <c>SYSUTCDATETIME()</c>).</summary>
    string UtcNowSqlExpression { get; }

    /// <summary>
    /// Provider SQL expression for the store's UTC instant shifted by <paramref name="seconds"/>, which may be
    /// negative. Lets a seed express "already due" or "not yet due" against the STORE's clock rather than the test
    /// process's, so a seeded position never starts a skew away from what the advance itself would compare against.
    /// </summary>
    string UtcNowOffsetSqlExpression(int seconds);

    /// <summary>
    /// The server-clock function EF Core emits when it translates a bare <c>DateTime.UtcNow</c> inside an
    /// expression tree (Postgres: <c>now()</c>; SqlServer: <c>GETUTCDATE()</c>). This is NOT
    /// <see cref="UtcNowSqlExpression" />: that one is what the harness itself writes in seed SQL, this one is
    /// what the EF provider chooses. <see cref="JobsDatabaseClockConformanceTests{TFixture}" /> asserts it against
    /// the SQL the real Jobs lease paths put on the wire.
    /// </summary>
    string EfTranslatedDatabaseClockSql { get; }

    /// <summary>
    /// The per-statement server clock the lease SQL compares and stamps with (Postgres: <c>clock_timestamp()</c>;
    /// SqlServer: <c>SYSUTCDATETIME()</c>). A lease reads the clock at each statement rather than at transaction
    /// start, so it is not <see cref="EfTranslatedDatabaseClockSql" />.
    /// </summary>
    string LeaseDatabaseClockSql { get; }

    /// <summary>Provider DDL that drops every Jobs and Coordination table so each test starts empty.</summary>
    string ResetSql { get; }

    /// <summary>Provider DDL that creates the atomicity probe table if absent and clears its rows.</summary>
    string CreateProbeTableSql { get; }

    /// <summary>Registers the unit-of-work services this backend enlists through (e.g. <c>services.AddUnitOfWork()</c>).</summary>
    void ConfigureUnitOfWork(IServiceCollection services);

    /// <summary>Wires this backend's relational messaging storage against <see cref="ConnectionString" />.</summary>
    void ConfigureMessagingStorage(MessagingSetupBuilder setup);

    /// <summary>
    /// Opens a fresh DI scope on <paramref name="services" />, begins an owned unit of work on a provider connection
    /// through the host's <c>IUnitOfWorkFactory</c>, and runs <paramref name="operation" /> with the scope's
    /// <see cref="IServiceProvider" />, that unit, and the live connection/transaction. Jobs writes that must
    /// enlist go through the unit's receivers (<c>unit.Jobs</c> / <c>unit.TimeJobs&lt;&gt;()</c> /
    /// <c>unit.CronJobs&lt;&gt;()</c>), which write in the SAME transaction the raw SQL below runs in; an injected
    /// manager resolved from the scope writes autonomously. The helper owns commit/rollback: an operation exception
    /// propagates and rolls the unit's transaction back — the regression net (an autonomous write would leave a row
    /// after rollback) relies on this.
    /// </summary>
    Task RunCoordinatedTransactionAsync(
        IServiceProvider services,
        Func<IServiceProvider, IUnitOfWork, DbConnection, DbTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Shared, provider-neutral behavior over <see cref="IJobsCoordinationFixture" />: host bootstrap (Coordination
/// before Jobs), Jobs schema creation, database reset, and raw-SQL seed/read of TimeJob rows (raw SQL because
/// <c>TimeJobEntity.Status/Lease</c> have internal setters). Mirrors the Coordination harness's
/// <c>CoordinationFixtureExtensions</c> shape.
/// </summary>
public static partial class JobsCoordinationFixtureExtensions
{
    /// <summary>
    /// Adapts harness SQL to this backend's naming: every double-quoted PascalCase identifier (<c>"LeaseExpiresAt"</c>)
    /// becomes <c>"lease_expires_at"</c> on a snake_case backend and stays as written otherwise. Shared scenarios write
    /// their seed and probe SQL once, in the PascalCase the property names use, and run it on both backends.
    /// </summary>
    public static string Sql(this IJobsCoordinationFixture fixture, string sql)
    {
        return fixture.NamingStyle is StorageNamingStyle.PascalCase
            ? sql
            : _QuotedPascalIdentifier.Replace(
                sql,
                match => "\"" + HeadlessStorageNaming.Apply(fixture.NamingStyle, match.Groups["name"].Value) + "\""
            );
    }

    /// <summary>The name of a Jobs table on this backend, from its PascalCase default (<c>TimeJobs</c>).</summary>
    public static string JobsTable(this IJobsCoordinationFixture fixture, string pascalTable)
    {
        return HeadlessStorageNaming.Apply(fixture.NamingStyle, pascalTable);
    }

    [GeneratedRegex(
        "\"(?<name>[A-Z][A-Za-z0-9]*)\"",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _QuotedPascalIdentifier { get; }

    public const string ClusterName = "jobs-it";

    // Membership thresholds tuned low so a stopped host's liveness expires within seconds (real dead-node
    // recovery), while staying inside the CoordinationOptions validator envelope:
    // Heartbeat < Suspicion < Dead, and DeadRetention >= 2 * Heartbeat.
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan SuspicionThreshold = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan DeadThreshold = TimeSpan.FromMilliseconds(1200);
    public static readonly TimeSpan DeadRetentionWindow = TimeSpan.FromMilliseconds(1200);

    // Production-scale thresholds for a host whose scenario never waits for a node to die. On the second-scale ones
    // above, one heartbeat stalled past 1.2 s by a loaded run declares the live node Dead: every later acquire returns
    // nothing because the node has no owner identity, and the dead-owner reclaim hands the node's own Queued rows back
    // to Idle with no owner, so a scenario that reads its claims back sees them undone.
    public static readonly TimeSpan SteadyHeartbeatInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan _SteadySuspicionThreshold = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _SteadyDeadThreshold = TimeSpan.FromSeconds(30);

    private static readonly (
        TimeSpan Heartbeat,
        TimeSpan Suspicion,
        TimeSpan Dead,
        TimeSpan DeadRetention
    ) _SteadyMembershipTimings = (
        SteadyHeartbeatInterval,
        _SteadySuspicionThreshold,
        _SteadyDeadThreshold,
        _SteadyDeadThreshold
    );

    /// <summary>
    /// Builds (but does not start) a host wired the way a production Jobs node is: a Coordination provider
    /// registered <em>before</em> the durable Jobs store so the require-a-provider check is satisfied. The host runs
    /// on the steady membership profile; pass <paramref name="fastMembership" /> only for a scenario that waits for a
    /// node to die. A crossed self-fence is terminal, so on the second-scale profile one heartbeat a loaded database
    /// stalls past the threshold leaves the host without an owner identity for the rest of the test.
    /// </summary>
    public static IHost BuildHost(
        this IJobsCoordinationFixture fixture,
        string nodeId,
        MembershipLostBehavior lostBehavior = MembershipLostBehavior.StopMembershipOnly,
        TimeProvider? timeProvider = null,
        TimeSpan? leaseDuration = null,
        bool useNativeClaims = true,
        IInterceptor? interceptor = null,
        Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>>? configureJobs = null,
        bool runBackgroundServices = false,
        bool fastMembership = false,
        Action<IServiceCollection>? configureServices = null
    )
    {
        return _BuildHost<JobsDbContext>(
            fixture,
            nodeId,
            HeadlessStorageDefaults.Schema,
            lostBehavior,
            timeProvider,
            leaseDuration,
            useNativeClaims,
            interceptor,
            configureJobs,
            membershipTimings: fastMembership ? null : _SteadyMembershipTimings,
            runBackgroundServices: runBackgroundServices,
            configureServices: configureServices
        );
    }

    /// <summary>
    /// Builds a host with a custom Jobs DbContext so provider SQL can be verified against renamed mappings. Mapping
    /// scenarios never wait for a node to die, so the host runs on the steady membership profile.
    /// </summary>
    public static IHost BuildMappedHost<TDbContext>(this IJobsCoordinationFixture fixture, string nodeId, string schema)
        where TDbContext : JobsDbContext<TimeJobEntity, CronJobEntity>
    {
        return _BuildHost<TDbContext>(
            fixture,
            nodeId,
            schema,
            MembershipLostBehavior.StopMembershipOnly,
            null,
            membershipTimings: _SteadyMembershipTimings
        );
    }

    /// <summary>
    /// Builds a host whose <em>real</em> Jobs DbContext carries <paramref name="interceptor" />, so a test can read
    /// the SQL the production paths actually put on the wire. It sees only the SQL EF runs: the claims, the lease
    /// transitions, and every fenced write build raw commands off the underlying connection, which bypass EF's
    /// interception pipeline. Native claiming is off, so a test that also drives a claim stays on the generic path.
    /// </summary>
    public static IHost BuildInterceptedHost(
        this IJobsCoordinationFixture fixture,
        string nodeId,
        IInterceptor interceptor,
        TimeSpan? leaseDuration = null,
        TimeProvider? timeProvider = null
    )
    {
        return _BuildHost<JobsDbContext>(
            fixture,
            nodeId,
            HeadlessStorageDefaults.Schema,
            MembershipLostBehavior.StopMembershipOnly,
            timeProvider,
            leaseDuration,
            useNativeClaims: false,
            interceptor,
            membershipTimings: _SteadyMembershipTimings
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
        IInterceptor? interceptor = null,
        Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>>? configureJobs = null,
        (TimeSpan Heartbeat, TimeSpan Suspicion, TimeSpan Dead, TimeSpan DeadRetention)? membershipTimings = null,
        bool runBackgroundServices = false,
        Action<IServiceCollection>? configureServices = null
    )
        where TDbContext : JobsDbContext<TimeJobEntity, CronJobEntity>
    {
        var (heartbeat, suspicion, dead, deadRetention) =
            membershipTimings ?? (HeartbeatInterval, SuspicionThreshold, DeadThreshold, DeadRetentionWindow);

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // IMPORTANT: Coordination must be registered before Jobs so the durable store's require-a-provider
        // check is satisfied at registration time.
        builder.Services.AddHeadlessHostIdentity(options => options.HostName = nodeId);
        builder.Services.AddHeadlessCoordination(setup =>
        {
            fixture.ConfigureCoordination(setup);
            setup.Configure(options =>
            {
                options.ClusterName = ClusterName;
                options.HeartbeatInterval = heartbeat;
                options.SuspicionThreshold = suspicion;
                options.DeadThreshold = dead;
                options.DeadRetentionWindow = deadRetention;
                // Fence at DeadThreshold, where these hosts fenced before the margin existed: their 200ms beats against
                // a loaded database container can stall past the derived 1s fence and stop stamping mid-test, and
                // these hosts exercise claims and stamping, not the margin.
                options.SelfFenceMargin = TimeSpan.Zero;
                options.MembershipLostBehavior = lostBehavior;
            });
        });

        builder.Services.AddHeadlessJobs(options =>
        {
            // The scheduler is disabled so it never races the tests' direct persistence calls. Dead-node recovery
            // is driven by the MembershipRecoveryBridge + coordination heartbeat, both of which still run. A scenario
            // that needs a job to actually execute opts in with runBackgroundServices.
            if (!runBackgroundServices)
            {
                options.DisableBackgroundServices();
            }

            options.AddModule<CoordinatedJobsModule>();
            configureJobs?.Invoke(options);
            options.ConfigureStorage(storage => storage.Schema = schema);
            if (leaseDuration is not null)
            {
                options.ConfigureScheduler(scheduler => scheduler.LeaseDuration = leaseDuration.Value);
            }

            options.UseEntityFramework(ef =>
            {
                ef.UseJobsDbContext<TDbContext>(db =>
                {
                    fixture.ConfigureStore(db);
                    if (interceptor is not null)
                    {
                        db.AddInterceptors(interceptor);
                    }
                });
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

        configureServices?.Invoke(builder.Services);
        builder.Services.AddDeployStepsOnStartup();
        return builder.Build();
    }

    /// <summary>The time-job function the coordinated-enqueue conformance scenarios enqueue against.</summary>
    public const string CoordinatedFunctionName = "Coordinated_Enqueue_Sample";

    /// <summary>The typed function scheduled through <see cref="IJobScheduler" /> by facade conformance scenarios.</summary>
    public const string CoordinatedFacadeFunctionName = "Coordinated_Facade_Enqueue_Sample";

    /// <summary>A function that reports progress <see cref="ProgressReports" /> times in a tight loop, then succeeds.</summary>
    public const string CoordinatedProgressFunctionName = "Coordinated_Progress_Sample";

    /// <summary>How many progress reports <see cref="CoordinatedProgressFunctionName" /> makes.</summary>
    public const int ProgressReports = 200;

    /// <summary>
    /// Builds (but does not start) a host wired like <see cref="BuildHost" /> plus the unit-of-work provider, so the
    /// <c>JobsManagerFacade</c> coordinated-enqueue path is active and <c>RunCoordinatedTransactionAsync</c> can
    /// enlist. A test time-job function is registered before the host's startup <c>Build()</c> so <c>AddAsync</c>
    /// validation passes (empty cron expression so the startup seeder ignores it).
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
        bool includeMessaging = false,
        TimeProvider? timeProvider = null,
        Action<IServiceCollection>? configureServices = null
    )
        where TDbContext : JobsDbContext<TimeJobEntity, CronJobEntity>
    {
        return _BuildCoordinatedEnqueueHost<TDbContext>(
            fixture,
            nodeId,
            configureOptions,
            includeMessaging,
            timeProvider: timeProvider,
            configureServices: configureServices
        );
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

    /// <summary>
    /// Builds the coordinated-enqueue host with every Jobs table mapped into <paramref name="schema" /> through the
    /// feature-owned storage option, so a scenario can assert where the tables actually landed.
    /// </summary>
    public static IHost BuildCustomSchemaEnqueueHost(
        this IJobsCoordinationFixture fixture,
        string nodeId,
        string schema
    )
    {
        return _BuildCoordinatedEnqueueHost<CustomSchemaJobsDbContext>(fixture, nodeId, schema: schema);
    }

    private static IHost _BuildCoordinatedEnqueueHost<TDbContext>(
        IJobsCoordinationFixture fixture,
        string nodeId,
        Action<DbContextOptionsBuilder>? configureOptions = null,
        bool includeMessaging = false,
        JobsSideEffectsProbe? sideEffectsProbe = null,
        bool enableTenantPropagation = false,
        TimeProvider? timeProvider = null,
        Action<IServiceCollection>? configureServices = null,
        string schema = HeadlessStorageDefaults.Schema
    )
        where TDbContext : JobsDbContext<TimeJobEntity, CronJobEntity>
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddHeadlessHostIdentity(options => options.HostName = nodeId);
        builder.Services.AddHeadlessCoordination(setup =>
        {
            fixture.ConfigureCoordination(setup);
            setup.Configure(options =>
            {
                // No enqueue scenario waits for a node to die, so these hosts run the steady profile: on the
                // second-scale one, a heartbeat stalled past 1.2 s by a loaded run declares the live node Dead, the
                // store rejects its identity, and the node self-fences and stops the host while it is still starting.
                options.ClusterName = ClusterName;
                options.HeartbeatInterval = _SteadyMembershipTimings.Heartbeat;
                options.SuspicionThreshold = _SteadyMembershipTimings.Suspicion;
                options.DeadThreshold = _SteadyMembershipTimings.Dead;
                options.DeadRetentionWindow = _SteadyMembershipTimings.DeadRetention;
                // Fence at DeadThreshold, where these hosts fenced before the margin existed: these hosts exercise
                // claims and stamping, not the margin.
                options.SelfFenceMargin = TimeSpan.Zero;
            });
        });

        builder.Services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            options.AddModule<CoordinatedJobsModule>();
            options.ConfigureStorage(storage => storage.Schema = schema);
            options.UseEntityFramework(ef =>
                ef.UseJobsDbContext<TDbContext>(db =>
                {
                    fixture.ConfigureStore(db);
                    configureOptions?.Invoke(db);
                })
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

        // AddUnitOfWork() is idempotent: this registers the scoped IUnitOfWorkFactory the JobsManagerFacade
        // resolves .Current from, and RunCoordinatedTransactionAsync begins the unit through the same manager type.
        fixture.ConfigureUnitOfWork(builder.Services);

        // Same purpose as on BuildHost: a deliberately skewed node clock, so a write that is supposed to be anchored
        // on the STORE's instant cannot pass by accidentally agreeing with this process's clock.
        if (timeProvider is not null)
        {
            builder.Services.AddSingleton(timeProvider);
        }

        if (enableTenantPropagation)
        {
            // Registered after AddHeadlessJobs (last-wins over the framework CurrentTenant fallback) so both the schedule
            // middleware and the test resolve THIS ambient tenant. Being neither CurrentTenant nor NullCurrentTenant, it
            // is the "real tenant source" the propagation startup validator requires, so StartAsync does not error.
            builder.Services.AddSingleton<ICurrentTenant, HarnessAmbientCurrentTenant>();
            builder.AddHeadlessTenancy(tenancy => tenancy.Jobs(jobs => jobs.PropagateTenant()));
        }

        configureServices?.Invoke(builder.Services);
        builder.Services.AddDeployStepsOnStartup();
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
        var table = services.GetRequiredService<IStorageTableNames>().GetPublishedTableName();
        return _CountAsync(fixture, $"SELECT COUNT(*) FROM {table};", cancellationToken);
    }

    /// <summary>The non-default schema the storage-option conformance scenario maps every Jobs table into.</summary>
    public const string CustomSchemaName = "jobs_custom";

    /// <summary>
    /// Whether the Jobs table <paramref name="table" /> (its PascalCase default name, converted to this backend's
    /// naming) exists in <paramref name="schema" />. Asks <c>information_schema</c>, which
    /// both backends implement, so the scenario can assert where a table landed without provider-specific catalog SQL.
    /// </summary>
    public static async Task<bool> TableExistsAsync(
        this IJobsCoordinationFixture fixture,
        string schema,
        string table,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table;";
        AddParameter(command, "@schema", schema);
        AddParameter(command, "@table", fixture.JobsTable(table));
        var scalar = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(scalar, CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>
    /// Counts rows in a Jobs table, named by its PascalCase default, in <paramref name="schema" /> on an independent
    /// connection.
    /// </summary>
    public static Task<int> CountRowsAsync(
        this IJobsCoordinationFixture fixture,
        string schema,
        string table,
        CancellationToken cancellationToken
    )
    {
        return _CountAsync(
            fixture,
            $"SELECT COUNT(*) FROM {fixture.QualifyTable(schema, fixture.JobsTable(table))};",
            cancellationToken
        );
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
    /// <remarks>
    /// <paramref name="ownerId"/> and <paramref name="lockedUntil"/> map onto the lease as <see cref="SeedLease"/>
    /// describes; every held row is seeded at generation 1.
    /// </remarks>
    public static async Task SeedTimeJobAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        string function,
        int status,
        string? ownerId,
        CancellationToken cancellationToken,
        NodeDeathPolicy onNodeDeath = NodeDeathPolicy.Retry,
        DateTime? lockedUntil = null,
        DateTime? executionTime = null
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(
            $"INSERT INTO {fixture.QualifiedTimeJobsTable} ({_InsertColumns}, \"ContractVersion\", \"ExecutionTime\") "
                + "VALUES (@id, @function, @function, @status, "
                + $"{fixture.UtcNowSqlExpression}, {fixture.UtcNowSqlExpression}, 0, 0, 0, @onNodeDeath, {_LeaseValues}, '1', "
                + "@executionTime);"
        );

        // Status and OnNodeDeath persist as enum names (HasConversion<string>), so seed the names, not ordinals.
        AddParameter(command, "@id", id);
        AddParameter(command, "@function", function);
        AddParameter(command, "@status", ((JobStatus)status).ToString());
        AddParameter(command, "@onNodeDeath", onNodeDeath.ToString());
        _AddLeaseParameters(command, SeedLease((JobStatus)status, ownerId, lockedUntil));
        AddParameter(command, "@executionTime", (object?)executionTime ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Inserts a CronJob row with an explicit node-death policy (FK target for seeded occurrences).</summary>
    /// <param name="isPaused">Whether the definition is seeded paused — the advance fence rejects a paused row.</param>
    /// <param name="scheduleRevision">The definition version the advance fence must be given to succeed.</param>
    /// <param name="reconciledThroughOffsetSeconds">
    /// The schedule watermark, as an offset from the STORE's current instant (negative = in the past). Defaults to the
    /// store's instant, matching the rule that every definition carries a position from the moment it is created —
    /// never a zero watermark, which would read as "infinitely behind" and manufacture a backlog nothing ever missed.
    /// </param>
    /// <param name="nextDueOffsetSeconds">
    /// The dispatch projection, as an offset from the store's instant. Defaults to one minute out, so a definition
    /// seeded by an unrelated test is not incidentally due. Pass a negative value to seed an already-due definition.
    /// </param>
    /// <param name="initIdentifier">
    /// The seed marker, or <see langword="null"/> for a runtime row. Pass a seed owner's marker to stand in for a row
    /// that owner seeded.
    /// </param>
    /// <remarks>
    /// The position, grace, and policy columns are non-nullable and the harness builds its schema from the EF model
    /// (<c>CreateTablesAsync</c>). There is no column default to fall back on, so this explicit column list must carry them. Grace and policy are seeded at the entity's own
    /// CLR defaults so a seeded row matches what inserting a fresh entity would persist.
    /// <para>
    /// The position is expressed as an offset evaluated by the STORE rather than as a bound instant: that keeps the
    /// seed on the same clock the advance compares against, and sidesteps binding a <c>Kind</c>-bearing instant to a
    /// <c>timestamptz</c> column. Read the resulting values back with <see cref="ReadCronSchedulePositionAsync"/>.
    /// </para>
    /// </remarks>
    public static async Task SeedCronJobAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        string function,
        string expression,
        NodeDeathPolicy onNodeDeath,
        CancellationToken cancellationToken,
        bool isPaused = false,
        long scheduleRevision = 0L,
        int reconciledThroughOffsetSeconds = 0,
        int nextDueOffsetSeconds = 60,
        CronOverlapPolicy onOverlap = CronOverlapPolicy.Allow,
        string? initIdentifier = null
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = fixture.Sql(
            $"INSERT INTO {fixture.QualifiedCronJobsTable} ({_CronInsertColumns}, \"ContractVersion\", \"OnOverlap\", \"InitIdentifier\") "
                + "VALUES (@id, @function, @function, @expression, @timeZoneId, @isPaused, @scheduleRevision, 0, "
                + $"{fixture.UtcNowSqlExpression}, {fixture.UtcNowSqlExpression}, @onNodeDeath, "
                + $"{fixture.UtcNowOffsetSqlExpression(reconciledThroughOffsetSeconds)}, "
                + $"{fixture.UtcNowOffsetSqlExpression(nextDueOffsetSeconds)}, "
                + "@missedRunGraceSeconds, @onMissedRun, '1', @onOverlap, @initIdentifier);"
        );
        AddParameter(command, "@initIdentifier", (object?)initIdentifier ?? DBNull.Value);

        AddParameter(command, "@id", id);
        AddParameter(command, "@function", function);
        AddParameter(command, "@expression", expression);
        AddParameter(command, "@timeZoneId", DBNull.Value);
        AddParameter(command, "@isPaused", isPaused);
        AddParameter(command, "@scheduleRevision", scheduleRevision);
        AddParameter(command, "@onNodeDeath", onNodeDeath.ToString());
        AddParameter(command, "@missedRunGraceSeconds", 0);
        AddParameter(command, "@onMissedRun", nameof(MissedRunPolicy.Coalesce));
        AddParameter(command, "@onOverlap", onOverlap.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Reads back a definition's persisted schedule position, bypassing the entity's internal setters.</summary>
    public static async Task<CronSchedulePosition> ReadCronSchedulePositionAsync(
        this IJobsCoordinationFixture fixture,
        Guid cronJobId,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(
            $"SELECT \"ReconciledThroughUtc\", \"NextDueUtc\" FROM {fixture.QualifiedCronJobsTable} WHERE \"Id\" = @id;"
        );
        AddParameter(command, "@id", cronJobId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).Should().BeTrue("the seeded cron definition must exist");

        return new CronSchedulePosition(
            DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc),
            DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc)
        );
    }

    /// <summary>
    /// Inserts a CronJobOccurrence row with an exact status/owner/lease — bypasses the entity's internal setters.
    /// <paramref name="ownerId"/> and <paramref name="lockedUntil"/> map onto the lease as <see cref="SeedLease"/>
    /// describes. Requires a parent CronJob (FK <c>CronJobId</c>) to already exist (seed it via <see cref="SeedCronJobAsync" />).
    /// </summary>
    /// <param name="skippedReason">
    /// The retirement reason a <see cref="JobStatus.Skipped" /> row carries. Display text only: two producers write
    /// the identical string "Cron definition updated" yet owe opposite accounting answers, which is why the rule
    /// reads <paramref name="disposition" /> instead. Seeded so a row still LOOKS like the production one.
    /// </param>
    /// <param name="disposition">
    /// The typed accounting disposition — the sole input to the occupied-instant rule. Defaults to
    /// <see cref="CronOccurrenceDisposition.Accounted" />, matching every ordinary producer.
    /// </param>
    /// <param name="rawStatus">
    /// Writes this string into the Status column verbatim instead of <paramref name="status" />'s enum name. The
    /// only way to seed a value a newer binary could have written, which the rule must fail closed on rather than
    /// throw over — an enum parameter cannot express it.
    /// </param>
    /// <param name="createdAtOffsetSeconds">
    /// Stamps <c>CreatedAt</c>/<c>UpdatedAt</c> at the store clock shifted by this many seconds instead of at the
    /// store clock itself. Required whenever a test depends on the RELATIVE creation order of rows sharing an
    /// instant: <c>SYSUTCDATETIME()</c> can return the same value for two consecutive inserts, and the tie then falls
    /// to a random <c>Id</c>, which makes the read a coin flip rather than an assertion.
    /// </param>
    public static async Task SeedCronOccurrenceAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        Guid cronJobId,
        int status,
        string? ownerId,
        NodeDeathPolicy onNodeDeath,
        DateTime? lockedUntil,
        DateTime executionTime,
        CancellationToken cancellationToken,
        string? skippedReason = null,
        CronOccurrenceDisposition disposition = CronOccurrenceDisposition.Accounted,
        string? rawStatus = null,
        int? createdAtOffsetSeconds = null
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var createdAtSql = createdAtOffsetSeconds is { } offset
            ? fixture.UtcNowOffsetSqlExpression(offset)
            : fixture.UtcNowSqlExpression;
        // ExecutionTime is an explicit parameter, not now(): the (CronJobId, ExecutionTime) unique index requires
        // distinct execution times when several occurrences of the same cron are seeded together.
        command.CommandText = fixture.Sql(
            $"INSERT INTO {fixture.QualifiedCronJobOccurrencesTable} ({_CronOccurrenceInsertColumns}, "
                + "\"SkippedReason\", \"Disposition\", \"Function\", \"ContractVersion\", \"Request\") "
                + "SELECT @id, @cronJobId, @status, @executionTime, "
                + $"{createdAtSql}, {createdAtSql}, 0, 0, @onNodeDeath, {_LeaseValues}, "
                + $"@skippedReason, @disposition, \"Function\", \"ContractVersion\", \"Request\" FROM {fixture.QualifiedCronJobsTable} WHERE \"Id\" = @cronJobId;"
        );

        AddParameter(command, "@id", id);
        AddParameter(command, "@cronJobId", cronJobId);
        AddParameter(command, "@status", rawStatus ?? ((JobStatus)status).ToString());
        AddParameter(command, "@executionTime", executionTime);
        AddParameter(command, "@onNodeDeath", onNodeDeath.ToString());
        _AddLeaseParameters(command, SeedLease((JobStatus)status, ownerId, lockedUntil));
        AddParameter(command, "@skippedReason", (object?)skippedReason ?? DBNull.Value);
        AddParameter(command, "@disposition", disposition.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Reads back a CronJobOccurrence's raw status string and typed disposition for assertions.</summary>
    public static async Task<(string Status, string Disposition)> ReadCronOccurrenceDispositionAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(
            $"SELECT \"Status\", \"Disposition\" FROM {fixture.QualifiedCronJobOccurrencesTable} WHERE \"Id\" = @id;"
        );
        AddParameter(command, "@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).Should().BeTrue("the seeded cron occurrence must exist");

        return (reader.GetString(0), reader.GetString(1));
    }

    /// <summary>
    /// Reads back a CronJobOccurrence's status and the holder of its lease while the lease is Active, for asserting
    /// who holds the row. An ended lease keeps its holder as a record, so the holder alone no longer says the row is
    /// held; read <see cref="ReadCronOccurrenceClaimAsync"/> to assert on the ended lease itself.
    /// </summary>
    public static async Task<(int Status, string? ActiveHolder)> ReadCronOccurrenceAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var (status, lease) = await fixture._ReadRowAsync(
            fixture.QualifiedCronJobOccurrencesTable,
            id,
            cancellationToken
        );

        return (status, ActiveHolder(lease));
    }

    /// <summary>Reads a CronJobOccurrence's lease.</summary>
    public static async Task<RowLeaseSnapshot> ReadCronOccurrenceClaimAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var (_, lease) = await fixture._ReadRowAsync(fixture.QualifiedCronJobOccurrencesTable, id, cancellationToken);

        return lease;
    }

    /// <summary>Reads a CronJobOccurrence's lease deadline and database-stamped <c>UpdatedAt</c>.</summary>
    public static Task<(DateTimeOffset? ExpiresAt, DateTimeOffset UpdatedAt)> ReadCronOccurrenceClaimTimestampsAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        return fixture._ReadClaimTimestampsAsync(fixture.QualifiedCronJobOccurrencesTable, id, cancellationToken);
    }

    /// <summary>
    /// Reads back a TimeJob's status and the holder of its lease while the lease is Active (see
    /// <see cref="ReadCronOccurrenceAsync"/>).
    /// </summary>
    public static async Task<(int Status, string? ActiveHolder)> ReadTimeJobAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var (status, lease, _, _) = await fixture.ReadTimeJobDetailAsync(id, cancellationToken);
        return (status, ActiveHolder(lease));
    }

    /// <summary>
    /// Reads a TimeJob's status, lease, and failure/skip reasons — for asserting dead-node sweep hygiene (terminal
    /// rows end the lease but keep its holder; MarkFailed sets <c>ExceptionMessage</c>, Skip sets
    /// <c>SkippedReason</c>).
    /// </summary>
    public static async Task<(
        int Status,
        RowLeaseSnapshot Lease,
        string? ExceptionMessage,
        string? SkippedReason
    )> ReadTimeJobDetailAsync(this IJobsCoordinationFixture fixture, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(
            $"SELECT \"Status\", {LeaseColumnList}, \"ExceptionMessage\", \"SkippedReason\" "
                + $"FROM {fixture.QualifiedTimeJobsTable} WHERE \"Id\" = @id;"
        );
        AddParameter(command, "@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"TimeJob {id} not found.");
        }

        // Status persists as its enum name; parse back to the ordinal the callers assert against.
        var status = (int)Enum.Parse<JobStatus>(reader.GetString(0));
        var lease = await ReadLeaseAsync(reader, 1, cancellationToken);
        var exceptionMessage = await reader.IsDBNullAsync(6, cancellationToken) ? null : reader.GetString(6);
        var skippedReason = await reader.IsDBNullAsync(7, cancellationToken) ? null : reader.GetString(7);

        return (status, lease, exceptionMessage, skippedReason);
    }

    /// <summary>Reads a TimeJob's persisted RetryCount — for asserting crash-durable budget consumption.</summary>
    public static async Task<int> ReadTimeJobRetryCountAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(
            $"SELECT \"RetryCount\" FROM {fixture.QualifiedTimeJobsTable} WHERE \"Id\" = @id;"
        );
        AddParameter(command, "@id", id);
        var result =
            await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException($"TimeJob {id} not found.");
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <summary>Reads a TimeJob's lease deadline and database-stamped <c>UpdatedAt</c>.</summary>
    public static Task<(DateTimeOffset? ExpiresAt, DateTimeOffset UpdatedAt)> ReadTimeJobClaimTimestampsAsync(
        this IJobsCoordinationFixture fixture,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        return fixture._ReadClaimTimestampsAsync(fixture.QualifiedTimeJobsTable, id, cancellationToken);
    }

    /// <summary>The holder of <paramref name="lease"/> while it is Active, else <see langword="null"/>.</summary>
    public static string? ActiveHolder(RowLeaseSnapshot lease)
    {
        return lease.State == RowLeaseState.Active ? lease.Holder : null;
    }

    /// <summary>
    /// Overwrites a row's lease columns, so a scenario can stage a takeover (a peer at a later generation) or a lapsed
    /// lease without going through a claim.
    /// </summary>
    public static async Task OverwriteLeaseAsync(
        this IJobsCoordinationFixture fixture,
        string table,
        Guid id,
        RowLeaseSnapshot lease,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(
            $"UPDATE {table} SET \"LeaseGeneration\" = @leaseGeneration, \"LeaseState\" = @leaseState, "
                + "\"LeaseExpiresAt\" = @leaseExpiresAt, \"LeaseHolder\" = @leaseHolder, "
                + "\"LeaseTakeoverCount\" = @leaseTakeoverCount WHERE \"Id\" = @id;"
        );
        _AddLeaseParameters(command, lease);
        AddParameter(command, "@id", id);

        (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1, $"row {id} exists in {table}");
    }

    /// <summary>
    /// The lease a seeded row starts with. An owner with a deadline holds the row Active at generation 1. An owner
    /// without one stood for a row whose lease had lapsed: an in-flight or Idle row is seeded Active with an expiry
    /// far in the past, so the sweeps and claims still see a held-but-dead lease, and a terminal row is seeded
    /// Settled, keeping its holder as the record. No owner seeds a never-granted lease.
    /// </summary>
    public static RowLeaseSnapshot SeedLease(JobStatus status, string? ownerId, DateTime? lockedUntil)
    {
        if (ownerId is null)
        {
            return lockedUntil is { } deadline
                ? new RowLeaseSnapshot(0, RowLeaseState.Idle, _AsUtcOffset(deadline), null, 0)
                : default;
        }

        if (lockedUntil is { } until)
        {
            return new RowLeaseSnapshot(1, RowLeaseState.Active, _AsUtcOffset(until), ownerId, 0);
        }

        return status is JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress
            ? new RowLeaseSnapshot(1, RowLeaseState.Active, _LapsedLeaseExpiry, ownerId, 0)
            : new RowLeaseSnapshot(1, RowLeaseState.Settled, null, ownerId, 0);
    }

    private static readonly DateTimeOffset _LapsedLeaseExpiry = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset _AsUtcOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static async Task<(int Status, RowLeaseSnapshot Lease)> _ReadRowAsync(
        this IJobsCoordinationFixture fixture,
        string table,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql($"SELECT \"Status\", {LeaseColumnList} FROM {table} WHERE \"Id\" = @id;");
        AddParameter(command, "@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Row {id} not found in {table}.");
        }

        var status = (int)Enum.Parse<JobStatus>(reader.GetString(0));

        return (status, await ReadLeaseAsync(reader, 1, cancellationToken));
    }

    private static async Task<(DateTimeOffset? ExpiresAt, DateTimeOffset UpdatedAt)> _ReadClaimTimestampsAsync(
        this IJobsCoordinationFixture fixture,
        string table,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql($"SELECT \"LeaseExpiresAt\", \"UpdatedAt\" FROM {table} WHERE \"Id\" = @id;");
        AddParameter(command, "@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Row {id} not found in {table}.");
        }

        var expiresAt = await reader.IsDBNullAsync(0, cancellationToken)
            ? (DateTimeOffset?)null
            : await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken);
        var updatedAt = await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken);
        return (expiresAt, updatedAt);
    }

    /// <summary>Reads the five lease columns, in <see cref="LeaseColumnList"/> order, starting at <paramref name="ordinal"/>.</summary>
    internal static async Task<RowLeaseSnapshot> ReadLeaseAsync(
        DbDataReader reader,
        int ordinal,
        CancellationToken cancellationToken
    )
    {
        var generation = Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        var state = (RowLeaseState)Convert.ToInt16(reader.GetValue(ordinal + 1), CultureInfo.InvariantCulture);
        var expiresAt = await reader.IsDBNullAsync(ordinal + 2, cancellationToken)
            ? (DateTimeOffset?)null
            : await reader.GetFieldValueAsync<DateTimeOffset>(ordinal + 2, cancellationToken);
        var holder = await reader.IsDBNullAsync(ordinal + 3, cancellationToken) ? null : reader.GetString(ordinal + 3);
        var takeovers = Convert.ToInt32(reader.GetValue(ordinal + 4), CultureInfo.InvariantCulture);

        return new RowLeaseSnapshot(generation, state, expiresAt, holder, takeovers);
    }

    private static void _AddLeaseParameters(DbCommand command, RowLeaseSnapshot lease)
    {
        AddParameter(command, "@leaseGeneration", lease.Generation);
        AddParameter(command, "@leaseState", (short)lease.State);
        AddParameter(command, "@leaseExpiresAt", (object?)lease.ExpiresAt ?? DBNull.Value);
        AddParameter(command, "@leaseHolder", (object?)lease.Holder ?? DBNull.Value);
        AddParameter(command, "@leaseTakeoverCount", lease.TakeoverCount);
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
        command.CommandText = fixture.Sql(
            $"SELECT \"TenantId\" FROM {fixture.QualifiedTimeJobsTable} WHERE \"Id\" = @id;"
        );
        AddParameter(command, "@id", id);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);

        return scalar is null or DBNull ? null : (string)scalar;
    }

    // Column identifiers are double-quoted: SQL Server accepts ANSI double quotes for delimited identifiers
    // (QUOTED_IDENTIFIER is ON by default for SqlClient). Statements pass through Sql(), which converts them to the
    // snake_case columns on Postgres.
    // The lease columns, in the order ReadLeaseAsync reads and _LeaseValues binds them.
    internal const string LeaseColumnList =
        "\"LeaseGeneration\", \"LeaseState\", \"LeaseExpiresAt\", \"LeaseHolder\", \"LeaseTakeoverCount\"";

    private const string _LeaseValues =
        "@leaseGeneration, @leaseState, @leaseExpiresAt, @leaseHolder, @leaseTakeoverCount";

    private const string _InsertColumns =
        "\"Id\", \"Function\", \"Description\", \"Status\", "
        + "\"CreatedAt\", \"UpdatedAt\", \"ElapsedTime\", \"Retries\", \"RetryCount\", \"OnNodeDeath\", "
        + LeaseColumnList;

    private const string _CronInsertColumns =
        "\"Id\", \"Function\", \"Description\", \"Expression\", \"TimeZoneId\", \"IsPaused\", \"ScheduleRevision\", "
        + "\"Retries\", \"CreatedAt\", \"UpdatedAt\", \"OnNodeDeath\", "
        + "\"ReconciledThroughUtc\", \"NextDueUtc\", \"MissedRunGraceSeconds\", \"OnMissedRun\"";

    private const string _CronOccurrenceInsertColumns =
        "\"Id\", \"CronJobId\", \"Status\", \"ExecutionTime\", "
        + "\"CreatedAt\", \"UpdatedAt\", \"ElapsedTime\", \"RetryCount\", \"OnNodeDeath\", "
        + LeaseColumnList;

    // Both Npgsql and SqlClient accept the "@name" parameter form. Internal so the chain-conformance tests reuse it.
    internal static void AddParameter(DbCommand command, string name, object value)
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

/// <summary>
/// The context the custom-schema scenario runs on. EF caches one model per context type for the life of the process,
/// so reusing <see cref="JobsDbContext" /> here would assert against whichever schema an earlier test in the same run
/// built its model with — the scenario passes alone and fails in a full suite. A private type gives it a private model.
/// </summary>
public sealed class CustomSchemaJobsDbContext(DbContextOptions<CustomSchemaJobsDbContext> options)
    : JobsDbContext<TimeJobEntity, CronJobEntity>(options);

/// <summary>Typed payload registered as a generated-equivalent job-function request by the relational harness.</summary>
public sealed record CoordinatedFacadeRequest(Guid Id, string Value);

/// <summary>
/// The class the plain coordinated registration names, so scheduling code can address that job without arguments by
/// its type.
/// </summary>
internal sealed class CoordinatedJob : IJob
{
    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal static class CoordinatedEnqueueJobs
{
#pragma warning disable IDE0060 // These methods are registered as job-function delegates, whose context and token are part of the required signature.
    public static Task RunAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public static Task RunAsync(JobContext<CoordinatedFacadeRequest> context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
#pragma warning restore IDE0060
}

internal sealed class JobsScheduleMiddlewareProbe
{
    public int Calls { get; private set; }

    public void Record() => Calls++;
}

/// <summary>
/// The hand-written equivalent of a generated <c>JobsModule</c> for the relational conformance suite. Every harness host
/// adds it, because each host builds its own job catalog.
/// </summary>
public sealed class CoordinatedJobsModule : IJobsModule
{
    private CoordinatedJobsModule() { }

    static void IJobsModule.Register(JobsCatalogBuilder catalog)
    {
        catalog.AddScheduleMiddleware(
            "Tests:CoordinatedScheduleProbe",
            JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            JobMiddlewarePriority.Default,
            static (context, next, cancellationToken) =>
            {
                context.Services.GetService<JobsScheduleMiddlewareProbe>()?.Record();
                return next(cancellationToken);
            }
        );
        catalog.AddFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [JobsCoordinationFixtureExtensions.CoordinatedFunctionName] = new JobFunctionRegistration
                {
                    CronExpression = string.Empty,
                    Priority = JobPriority.LongRunning,
                    Delegate = (_, context, cancellationToken) =>
                        CoordinatedEnqueueJobs.RunAsync(context, cancellationToken),
                    MaxConcurrency = 1,
                    JobType = typeof(CoordinatedJob),
                },
                [JobsCoordinationFixtureExtensions.CoordinatedProgressFunctionName] = new JobFunctionRegistration
                {
                    CronExpression = string.Empty,
                    Priority = JobPriority.LongRunning,
                    Delegate = async (_, context, cancellationToken) =>
                    {
                        // Far faster than the progress interval, so the throttle has to coalesce almost every report.
                        for (var report = 1; report <= JobsCoordinationFixtureExtensions.ProgressReports; report++)
                        {
                            context.ReportProgress(
                                100d * report / JobsCoordinationFixtureExtensions.ProgressReports,
                                string.Create(CultureInfo.InvariantCulture, $"report {report}")
                            );
                            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                        }
                    },
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
                            .RunAsync(new JobContext<CoordinatedFacadeRequest>(context, request), cancellationToken)
                            .ConfigureAwait(false);
                    },
                    MaxConcurrency = 1,
                },
            }
        );
        catalog.AddRequestTypes(
            new Dictionary<string, (string, Type)>(StringComparer.Ordinal)
            {
                [JobsCoordinationFixtureExtensions.CoordinatedFacadeFunctionName] = (
                    typeof(CoordinatedFacadeRequest).FullName!,
                    typeof(CoordinatedFacadeRequest)
                ),
            }
        );
        catalog.AddDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                [JobsCoordinationFixtureExtensions.CoordinatedFunctionName] = new(
                    JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
                    null,
                    string.Empty,
                    JobPriority.LongRunning,
                    1
                ),
                [JobsCoordinationFixtureExtensions.CoordinatedProgressFunctionName] = new(
                    JobsCoordinationFixtureExtensions.CoordinatedProgressFunctionName,
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

    /// <summary>
    /// Waits until the post-commit worker has delivered at least <paramref name="restarts" /> scheduler wake-ups and
    /// <paramref name="notifications" /> notifications. A commit only enqueues its signal; the worker delivers it
    /// asynchronously, so reading the counts right after the commit races it.
    /// </summary>
    public async Task WaitForPostCommitSignalsAsync(
        int restarts,
        int notifications,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            while (RestartCount < restarts || _notificationIds.Count < notifications)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Fall through: the caller's assertions then report the counts that did arrive.
        }
    }

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

    void IJobsNotificationHubSender.UpdateJobProgress(JobExecutionState executionState, JobProgress progress) { }
}

/// <summary>
/// Wraps the live connection/transaction <see cref="IJobsCoordinationFixture.RunCoordinatedTransactionAsync" />
/// hands to a test body as an <see cref="IRelationalUnitOfWorkResource" />, for scenarios that call an
/// <c>ICoordinatedJobWriter</c> directly rather than through a manager resolved from the scope.
/// </summary>
public sealed class FixedRelationalResource(DbConnection connection, DbTransaction transaction)
    : IRelationalUnitOfWorkResource
{
    public DbConnection Connection { get; } = connection;

    public DbTransaction Transaction { get; } = transaction;

    public bool IsOwned => false;

    public bool IsTransactionCompleted => false;

    public ValueTask CommitAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
