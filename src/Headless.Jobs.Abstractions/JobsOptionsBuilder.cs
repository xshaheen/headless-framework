// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.DistributedLocks;
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

    /// <summary>
    /// Lock provider instance supplied via <see cref="UseDistributedLock(IDistributedLock)"/>. Mutually exclusive
    /// with <see cref="LockProviderFactory"/> — setting either clears the other so the last call wins. Consumed by
    /// <c>AddHeadlessJobs</c>, which registers it under the Jobs-scoped keyed-DI slot.
    /// </summary>
    internal IDistributedLock? LockProviderInstance { get; private set; }

    /// <summary>
    /// Factory supplied via <see cref="UseDistributedLock(Func{IServiceProvider, IDistributedLock})"/> for a lock
    /// provider that itself depends on DI-registered services. Mutually exclusive with <see cref="LockProviderInstance"/>.
    /// </summary>
    internal Func<IServiceProvider, IDistributedLock>? LockProviderFactory { get; private set; }

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
    /// Registers an <see cref="IDistributedLock"/> for the Jobs-scoped lock slot and enables
    /// <see cref="SchedulerOptionsBuilder.UseStorageLock"/>. The lock coarse-gates startup cron-seed migration and
    /// dead-node resource reclaim so N booting/surviving nodes do not redundantly re-run the same work.
    /// </summary>
    /// <param name="provider">The lock provider instance to use for Jobs coordination.</param>
    /// <remarks>
    /// This is an optimization, not a correctness boundary: per-row predicates, <c>node@incarnation</c> ownership,
    /// and per-job leases stay the correctness boundary, so behavior is unchanged when no provider is registered.
    /// Jobs keeps the provider under a Jobs-private keyed-DI key so it never conflicts with any other
    /// <see cref="IDistributedLock"/> registered at the application level. Calling this method implicitly sets
    /// <c>UseStorageLock = true</c>. Last-wins: calling this method (or its factory overload) more than once replaces
    /// any prior Jobs lock provider registration.
    /// </remarks>
    public JobsOptionsBuilder<TTimeJob, TCronJob> UseDistributedLock(IDistributedLock provider)
    {
        Argument.IsNotNull(provider);

        LockProviderInstance = provider;
        LockProviderFactory = null;
        _schedulerOptions.UseStorageLock = true;
        return this;
    }

    /// <summary>
    /// Registers a factory-resolved <see cref="IDistributedLock"/> for the Jobs-scoped lock slot and enables
    /// <see cref="SchedulerOptionsBuilder.UseStorageLock"/>. Use this overload when the provider itself depends on
    /// other DI-registered services.
    /// </summary>
    /// <param name="factory">A factory delegate that receives the <see cref="IServiceProvider"/> and returns the lock provider.</param>
    /// <remarks>See <see cref="UseDistributedLock(IDistributedLock)"/> for the correctness/optimization contract.</remarks>
    public JobsOptionsBuilder<TTimeJob, TCronJob> UseDistributedLock(Func<IServiceProvider, IDistributedLock> factory)
    {
        Argument.IsNotNull(factory);

        LockProviderFactory = factory;
        LockProviderInstance = null;
        _schedulerOptions.UseStorageLock = true;
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
    /// How long a per-row pickup lease is held before it expires and the row becomes re-claimable by the
    /// lease-expiry self-heal arm. Stamped as <c>LockedUntil = now + LeaseDuration</c> on every claim using the
    /// injected <see cref="TimeProvider"/> (application clock, not the DB server clock — matches Headless.Messaging
    /// for InMemory↔SQL parity). The lease is a duplicate-suppression floor, NOT the liveness authority: a dead
    /// node's rows are recovered by Coordination's incarnation + heartbeat sweep, not by lease expiry. Must exceed
    /// the longest expected job runtime, or a still-running job's lease can expire and (for OnNodeDeath = Retry jobs)
    /// be speculatively re-claimed. Defaults to five minutes.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

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
    /// Whether the Jobs-scoped distributed lock coarse-gates startup cron-seed migration and dead-node resource
    /// reclaim. Enabled only via <c>JobsOptionsBuilder.UseDistributedLock(...)</c> (the setter is internal so the
    /// flag can never be <see langword="true"/> while the keyed slot still holds the <c>NullDistributedLock</c>
    /// fallback — that would silently no-op the guard on every node with no diagnostic). Defaults to
    /// <see langword="false"/> (no lock — every node runs both operations independently, which stays correct via
    /// per-row predicates and per-job leases). This is an optimization flag, never a correctness gate.
    /// </summary>
    public bool UseStorageLock { get; internal set; }
}
