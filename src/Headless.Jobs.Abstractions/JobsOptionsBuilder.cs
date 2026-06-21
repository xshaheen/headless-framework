// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

public sealed class JobsOptionsBuilder<TTimeJob, TCronJob> : IJobsOptionsSeeding
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly JobsExecutionContext _tickerExecutionContext;
    private readonly SchedulerOptionsBuilder _schedulerOptions;

    internal JobsOptionsBuilder(JobsExecutionContext tickerExecutionContext, SchedulerOptionsBuilder schedulerOptions)
    {
        _tickerExecutionContext = tickerExecutionContext;
        _schedulerOptions = schedulerOptions;
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

    // Explicit interface implementation for IJobsOptionsSeeding
    bool IJobsOptionsSeeding.SeedDefinedCronJobs => SeedDefinedCronJobs;
    Func<IServiceProvider, Task>? IJobsOptionsSeeding.TimeSeederAction => TimeSeederAction;
    Func<IServiceProvider, Task>? IJobsOptionsSeeding.CronSeederAction => CronSeederAction;

    internal Action<IServiceCollection>? ExternalProviderConfigServiceAction { get; set; }
    internal Action<IServiceCollection>? DashboardServiceAction { get; set; }
    internal Type? JobExceptionHandlerType { get; private set; }

    public JobsOptionsBuilder<TTimeJob, TCronJob> ConfigureScheduler(
        Action<SchedulerOptionsBuilder> schedulerOptionsBuilder
    )
    {
        schedulerOptionsBuilder?.Invoke(_schedulerOptions);
        return this;
    }

    /// <summary>
    /// JsonSerializerOptions specifically for serializing/deserializing job requests.
    /// If not set, default JsonSerializerOptions will be used.
    /// </summary>
    internal JsonSerializerOptions? RequestJsonSerializerOptions { get; set; }

    /// <summary>
    /// Configures the JSON serialization options specifically for job request serialization/deserialization.
    /// </summary>
    /// <param name="configure">Action to configure JsonSerializerOptions for job requests</param>
    /// <returns>The JobsOptionsBuilder for method chaining</returns>
    public JobsOptionsBuilder<TTimeJob, TCronJob> ConfigureRequestJsonOptions(Action<JsonSerializerOptions> configure)
    {
        RequestJsonSerializerOptions ??= new JsonSerializerOptions();
        configure?.Invoke(RequestJsonSerializerOptions);
        return this;
    }

    /// <summary>
    /// Enables GZip compression for job request payloads.
    /// When not called, requests are stored as plain UTF-8 JSON bytes.
    /// </summary>
    /// <returns>The JobsOptionsBuilder for method chaining</returns>
    public JobsOptionsBuilder<TTimeJob, TCronJob> UseGZipCompression()
    {
        RequestGZipCompressionEnabled = true;
        return this;
    }

    /// <summary>
    /// Disable automatic seeding of code-defined cron jobs on startup.
    /// </summary>
    public JobsOptionsBuilder<TTimeJob, TCronJob> IgnoreSeedDefinedCronJobs()
    {
        SeedDefinedCronJobs = false;
        return this;
    }

    /// <summary>
    /// Disables background services registration.
    /// Use this when you only want to queue jobs without processing them in this application.
    /// Only the managers (ITimeJobManager, ICronJobManager) will be available for queuing jobs.
    /// </summary>
    public JobsOptionsBuilder<TTimeJob, TCronJob> DisableBackgroundServices()
    {
        RegisterBackgroundServices = false;
        return this;
    }

    /// <summary>
    /// Configure a custom seeder for time jobs, executed on application startup.
    /// </summary>
    public JobsOptionsBuilder<TTimeJob, TCronJob> UseJobsSeeder(Func<ITimeJobManager<TTimeJob>, Task> timeSeeder)
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
    /// Configure a custom seeder for cron jobs, executed on application startup.
    /// </summary>
    public JobsOptionsBuilder<TTimeJob, TCronJob> UseJobsSeeder(Func<ICronJobManager<TCronJob>, Task> cronSeeder)
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
    /// Configure custom seeders for both time and cron jobs, executed on application startup.
    /// </summary>
    public JobsOptionsBuilder<TTimeJob, TCronJob> UseJobsSeeder(
        Func<ITimeJobManager<TTimeJob>, Task> timeSeeder,
        Func<ICronJobManager<TCronJob>, Task> cronSeeder
    )
    {
        UseJobsSeeder(timeSeeder);
        UseJobsSeeder(cronSeeder);
        return this;
    }

    public JobsOptionsBuilder<TTimeJob, TCronJob> SetExceptionHandler<THandler>()
        where THandler : IJobExceptionHandler
    {
        JobExceptionHandlerType = typeof(THandler);
        return this;
    }

    internal void UseExternalProviderApplication(Action<IServiceProvider> action) =>
        _tickerExecutionContext.ExternalProviderApplicationAction = action;
}

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

    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
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
    /// per its <c>OnNodeDeath</c> policy. <c>null</c> (the default) derives ≈ <see cref="LeaseDuration"/> / 3 so a
    /// single missed renewal cannot lapse the lease. An explicit value must be positive and strictly less than
    /// <see cref="LeaseDuration"/>; see <see cref="ResolveLeaseRenewalInterval"/>.
    /// </summary>
    public TimeSpan? LeaseRenewalInterval { get; set; }

    public TimeSpan FallbackIntervalChecker { get; set; } = TimeSpan.FromSeconds(30);
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
