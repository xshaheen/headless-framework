// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>
/// Fluent builder for configuring the Jobs subsystem, returned by the operational-store registration
/// extension (e.g., <c>UseEntityFramework</c>) and passed to optional add-ons such as
/// <c>AddDashboard</c>, <c>AddOpenTelemetryInstrumentation</c>, and <c>AddJobsDiscovery</c>.
/// </summary>
/// <typeparam name="TTimeJob">The application's concrete time job entity type.</typeparam>
/// <typeparam name="TCronJob">The application's concrete cron job entity type.</typeparam>
public sealed class JobsOptionsBuilder<TTimeJob, TCronJob> : IJobsOptionsSeeding
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly JobsExecutionContext _tickerExecutionContext;

    /// <summary>Scheduler options instance, exposed so Core-layer extensions can toggle internal flags.</summary>
    internal SchedulerOptionsBuilder SchedulerOptions { get; }

    internal JobsOptionsBuilder(JobsExecutionContext tickerExecutionContext, SchedulerOptionsBuilder schedulerOptions)
    {
        _tickerExecutionContext = tickerExecutionContext;
        SchedulerOptions = schedulerOptions;
        // Store this instance in the execution context for later retrieval
        tickerExecutionContext.OptionsSeeding = this;
    }

    /// <summary>
    /// Internal flag for request GZip compression.
    /// Defaults to false (plain JSON bytes).
    /// </summary>
    internal bool RequestGZipCompressionEnabled { get; set; }

    /// <summary>
    /// Controls whether code-defined cron jobs are seeded on startup.
    /// Defaults to true.
    /// </summary>
    internal bool SeedDefinedCronJobs { get; set; } = true;

    /// <summary>
    /// Controls whether background services (job processors) should be registered.
    /// Defaults to true. Set to false to only register managers for queuing jobs.
    /// </summary>
    internal bool RegisterBackgroundServices { get; set; } = true;

    /// <summary>
    /// Set by the durable operational-store provider to opt into coordinated membership: the node owner
    /// becomes <c>node@incarnation</c> and dead-node recovery flows through Headless.Coordination. The core
    /// pipeline reacts by requiring a coordination provider and wiring the recovery bridge + startup gate.
    /// </summary>
    internal bool RequiresCoordinatedMembership { get; set; }

    /// <summary>
    /// Seeding delegate for time jobs, executed with the application's service provider.
    /// </summary>
    internal Func<IServiceProvider, Task>? TimeSeederAction { get; set; }

    /// <summary>
    /// Seeding delegate for cron jobs, executed with the application's service provider.
    /// </summary>
    internal Func<IServiceProvider, Task>? CronSeederAction { get; set; }

    /// <summary>
    /// Deferred keyed-DI registration for the Jobs-scoped lock provider, set by the Core-layer
    /// <c>UseDistributedLock</c> extensions and replayed by <c>AddHeadlessJobs</c>. Kept as an untyped
    /// <see cref="Action{T}"/> over <see cref="IServiceCollection"/> so <c>Headless.Jobs.Abstractions</c> carries no
    /// dependency on any distributed-lock package. Last call wins: a second <c>UseDistributedLock</c> overwrites it.
    /// </summary>
    internal Action<IServiceCollection>? LockRegistrationAction { get; set; }

    // Explicit interface implementation for IJobsOptionsSeeding
    bool IJobsOptionsSeeding.SeedDefinedCronJobs => SeedDefinedCronJobs;
    Func<IServiceProvider, Task>? IJobsOptionsSeeding.TimeSeederAction => TimeSeederAction;
    Func<IServiceProvider, Task>? IJobsOptionsSeeding.CronSeederAction => CronSeederAction;

    internal Action<IServiceCollection>? ExternalProviderConfigServiceAction { get; set; }
    internal Action<IServiceCollection>? DashboardServiceAction { get; set; }
    internal Type? JobExceptionHandlerType { get; private set; }

    /// <summary>
    /// Configures the scheduler options (node identity, concurrency, lease duration, and timezone)
    /// by applying <paramref name="schedulerOptionsBuilder"/> to the shared <c>SchedulerOptionsBuilder</c>.
    /// </summary>
    /// <param name="schedulerOptionsBuilder">Action that mutates the scheduler options.</param>
    /// <returns>This builder for method chaining.</returns>
    public JobsOptionsBuilder<TTimeJob, TCronJob> ConfigureScheduler(
        Action<SchedulerOptionsBuilder>? schedulerOptionsBuilder
    )
    {
        schedulerOptionsBuilder?.Invoke(SchedulerOptions);
        return this;
    }

    /// <summary>
    /// JsonSerializerOptions specifically for serializing/deserializing job requests.
    /// If not set, default JsonSerializerOptions will be used.
    /// </summary>
    internal JsonSerializerOptions? RequestJsonSerializerOptions { get; set; }

    /// <summary>
    /// Configures the <c>JsonSerializerOptions</c> used to serialize and deserialize job request
    /// payloads. When not called, the default <c>JsonSerializerOptions</c> are used.
    /// </summary>
    /// <param name="configure">Action that mutates the serializer options.</param>
    /// <returns>This builder for method chaining.</returns>
    public JobsOptionsBuilder<TTimeJob, TCronJob> ConfigureRequestJsonOptions(Action<JsonSerializerOptions>? configure)
    {
        RequestJsonSerializerOptions ??= new JsonSerializerOptions();
        configure?.Invoke(RequestJsonSerializerOptions);
        return this;
    }

    /// <summary>
    /// Enables GZip compression for job request payloads stored in the persistence layer.
    /// When not called, requests are stored as plain UTF-8 JSON bytes.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public JobsOptionsBuilder<TTimeJob, TCronJob> UseGZipCompression()
    {
        RequestGZipCompressionEnabled = true;
        return this;
    }

    /// <summary>
    /// Disables automatic seeding of code-defined cron jobs on startup. Use when cron job definitions
    /// are managed entirely via <c>ICronJobManager</c> rather than seeded from code.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public JobsOptionsBuilder<TTimeJob, TCronJob> IgnoreSeedDefinedCronJobs()
    {
        SeedDefinedCronJobs = false;
        return this;
    }

    /// <summary>
    /// Disables the background processing services so this application instance only enqueues jobs
    /// rather than executing them. <c>ITimeJobManager</c> and <c>ICronJobManager</c> remain available
    /// for scheduling; all dispatching is handled by a separate worker process.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public JobsOptionsBuilder<TTimeJob, TCronJob> DisableBackgroundServices()
    {
        RegisterBackgroundServices = false;
        return this;
    }

    /// <summary>
    /// Registers a startup seeder delegate for time jobs. The delegate is invoked once during host
    /// startup via <c>IHostedService</c> initialization, after the operational store is ready.
    /// </summary>
    /// <param name="timeSeeder">
    /// Async factory that receives <c>ITimeJobManager</c> and enqueues initial time jobs.
    /// </param>
    /// <returns>This builder for method chaining.</returns>
    public JobsOptionsBuilder<TTimeJob, TCronJob> UseJobsSeeder(Func<ITimeJobManager<TTimeJob>, Task>? timeSeeder)
    {
        if (timeSeeder == null)
        {
            return this;
        }

        TimeSeederAction = async sp =>
        {
            var manager = sp.GetRequiredService<ITimeJobManager<TTimeJob>>();
            await timeSeeder(manager).ConfigureAwait(false);
        };

        return this;
    }

    /// <summary>
    /// Registers a startup seeder delegate for cron jobs. The delegate is invoked once during host
    /// startup via <c>IHostedService</c> initialization, after the operational store is ready.
    /// </summary>
    /// <param name="cronSeeder">
    /// Async factory that receives <c>ICronJobManager</c> and inserts or upserts initial cron job definitions.
    /// </param>
    /// <returns>This builder for method chaining.</returns>
    public JobsOptionsBuilder<TTimeJob, TCronJob> UseJobsSeeder(Func<ICronJobManager<TCronJob>, Task>? cronSeeder)
    {
        if (cronSeeder == null)
        {
            return this;
        }

        CronSeederAction = async sp =>
        {
            var manager = sp.GetRequiredService<ICronJobManager<TCronJob>>();
            await cronSeeder(manager).ConfigureAwait(false);
        };

        return this;
    }

    /// <summary>
    /// Registers startup seeder delegates for both time and cron jobs in a single call.
    /// </summary>
    /// <param name="timeSeeder">Seeder for time jobs.</param>
    /// <param name="cronSeeder">Seeder for cron job definitions.</param>
    /// <returns>This builder for method chaining.</returns>
    public JobsOptionsBuilder<TTimeJob, TCronJob> UseJobsSeeder(
        Func<ITimeJobManager<TTimeJob>, Task> timeSeeder,
        Func<ICronJobManager<TCronJob>, Task> cronSeeder
    )
    {
        UseJobsSeeder(timeSeeder);
        UseJobsSeeder(cronSeeder);
        return this;
    }

    /// <summary>
    /// Registers a custom exception handler that is called by the scheduler after a job function throws
    /// or is cancelled.
    /// </summary>
    /// <typeparam name="THandler">
    /// A type implementing <c>IJobExceptionHandler</c> that is registered in the DI container.
    /// </typeparam>
    /// <returns>This builder for method chaining.</returns>
    public JobsOptionsBuilder<TTimeJob, TCronJob> SetExceptionHandler<THandler>()
        where THandler : IJobExceptionHandler
    {
        JobExceptionHandlerType = typeof(THandler);
        return this;
    }

    internal void UseExternalProviderApplication(Action<IServiceProvider> action) =>
        _tickerExecutionContext.ExternalProviderApplicationAction = action;
}

/// <summary>
/// Fine-grained configuration for the Jobs scheduler: node identity, thread pool, lease duration,
/// renewal cadence, and start mode.
/// </summary>
public sealed class SchedulerOptionsBuilder
{
    /// <summary>
    /// Identifies this node on the in-memory single-process path; defaults to <see cref="Environment.MachineName"/>.
    /// The durable (Coordination) path does NOT use this value — it stamps rows with the membership
    /// <c>node@incarnation</c> owner, and node identity there (including K8s pod-collision handling via
    /// <c>POD_NAME</c>) is owned by <c>Headless.Coordination</c>'s node-id provider, not this option. This value
    /// only serves as the durable path's pre-registration display fallback.
    /// </summary>
    public string NodeId { get; set; } = Environment.MachineName;

    /// <summary>
    /// Maximum number of jobs that may execute concurrently across all priorities. Defaults to
    /// <see cref="Environment.ProcessorCount"/>.
    /// </summary>
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// How long an idle worker thread waits before terminating. Defaults to one minute.
    /// </summary>
    public TimeSpan IdleWorkerTimeOut { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long a per-row pickup lease is held before it expires and the row becomes re-claimable. Stamped as
    /// <c>LockedUntil = now + LeaseDuration</c> on every claim using the injected <see cref="TimeProvider"/>
    /// (application clock, not the DB server clock — matches Headless.Messaging for InMemory↔SQL parity).
    /// <para>
    /// Running jobs slide this lease forward on the <see cref="LeaseRenewalInterval"/> cadence (#316), so
    /// <c>LeaseDuration</c> no longer needs to exceed the longest job runtime — a healthy long job keeps renewing.
    /// It now sizes two things: the <c>Idle</c>/<c>Queued</c> claim→start window (a row claimed but not yet started
    /// can lapse and be re-claimed — keep it ≥ <see cref="FallbackIntervalChecker"/>), and the recovery latency for
    /// a stalled running job (a job that stops renewing is reclaimed within ≈ one <c>LeaseDuration</c>). Defaults to
    /// five minutes.
    /// </para>
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often a running job's lease is renewed (slides <c>LockedUntil</c> forward) while it executes, so a
    /// healthy long-running job is never falsely reclaimed (#316). The owning worker's execution loop extends the
    /// lease on this cadence; a job that stops renewing (crashed or wedged) has its lease lapse and is reclaimed
    /// per its <c>OnNodeDeath</c> policy. <see langword="null"/> (the default) derives ≈ <see cref="LeaseDuration"/> / 3 so a
    /// single missed renewal cannot lapse the lease. An explicit value must be positive and strictly less than
    /// <see cref="LeaseDuration"/>; see <see cref="ResolveLeaseRenewalInterval"/>.
    /// </summary>
    public TimeSpan? LeaseRenewalInterval { get; set; }

    /// <summary>
    /// How often the fallback background service wakes up to poll for due jobs and reclaim stalled
    /// leases when no scheduler event triggered an earlier wake-up. Defaults to 30 seconds. Should be
    /// less than or equal to <see cref="LeaseDuration"/> so a stalled job is reclaimed within one
    /// lease TTL.
    /// </summary>
    public TimeSpan FallbackIntervalChecker { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The timezone used when evaluating cron expressions. Defaults to the local machine timezone.
    /// Set this to a consistent value (e.g., a fixed <c>TimeZoneInfo</c>) in server environments
    /// where local timezone may differ across nodes.
    /// </summary>
    public TimeZoneInfo SchedulerTimeZone { get; set; } = TimeZoneInfo.Local;

    /// <summary>
    /// How often the durable path reconciles dead nodes from the membership liveness snapshot to reclaim
    /// any <c>NodeLeft</c> signal missed while not subscribed. Membership events accelerate recovery; this
    /// periodic reconcile is the backstop (origin §4b invariant). Defaults to one minute.
    /// </summary>
    public TimeSpan DeadNodeReconcileInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Controls how job processing starts. Defaults to <see cref="JobsStartMode.Immediate"/>.
    /// </summary>
    public JobsStartMode StartMode { get; set; } = JobsStartMode.Immediate;

    /// <summary>
    /// Whether the Jobs-scoped distributed lock coarse-gates startup cron-seed migration. Enabled only via the
    /// <c>UseDistributedLock(...)</c> builder extension (the setter is internal so the flag can never be
    /// <see langword="true"/> while the keyed slot still holds the <c>NullDistributedLock</c> fallback — that would
    /// silently no-op the guard on every node with no diagnostic). Defaults to <see langword="false"/> (no lock —
    /// every node runs the seed independently; seeded rows carry a deterministic primary key, so simultaneous
    /// first-boot still converges on a single row without this gate). This is an optimization flag, never the
    /// job-execution correctness boundary.
    /// </summary>
    public bool UseStorageLock { get; internal set; }

    /// <summary>
    /// Resolves the effective lease-renewal cadence (#316): an explicit <see cref="LeaseRenewalInterval"/> when
    /// set, otherwise the derived ≈ <see cref="LeaseDuration"/> / 3. Validates an explicit value — it must be
    /// positive and strictly less than <see cref="LeaseDuration"/>, so a renewal always lands before the lease
    /// deadline. Throws <see cref="InvalidOperationException"/> on a misconfigured explicit value; the startup
    /// initialization service calls this once (ValidateOnStart-equivalent), and the execution handler calls it to
    /// read the cadence.
    /// </summary>
    internal TimeSpan ResolveLeaseRenewalInterval()
    {
        if (LeaseRenewalInterval is not { } interval)
        {
            return TimeSpan.FromTicks(LeaseDuration.Ticks / 3);
        }

        if (interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"SchedulerOptionsBuilder.LeaseRenewalInterval ({interval}) must be positive."
            );
        }

        if (interval >= LeaseDuration)
        {
            throw new InvalidOperationException(
                $"SchedulerOptionsBuilder.LeaseRenewalInterval ({interval}) must be strictly less than "
                    + $"LeaseDuration ({LeaseDuration}) so a renewal lands before the lease deadline."
            );
        }

        return interval;
    }
}
