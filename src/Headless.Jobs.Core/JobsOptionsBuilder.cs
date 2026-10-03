// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Reliability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>
/// Fluent builder for configuring the Jobs subsystem, returned by the operational-store registration
/// extension (e.g., <c>UseEntityFramework</c>) and passed to optional add-ons such as <c>AddDashboard</c>.
/// Generated job modules are added with <see cref="AddModule{TModule}"/>.
/// </summary>
/// <typeparam name="TTimeJob">The application's concrete time job entity type.</typeparam>
/// <typeparam name="TCronJob">The application's concrete cron job entity type.</typeparam>
public sealed class JobsOptionsBuilder<TTimeJob, TCronJob> : IJobsOptionsSeeding
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly JobsExecutionContext _tickerExecutionContext;
    private readonly List<string> _runOnly = [];
    private JobOptions _jobDefaults = new();
    private readonly Dictionary<Type, JobOptions> _jobOptionsByRequest = [];
    private FailurePolicyDefinition? _defaultFailurePolicy;

    /// <summary>
    /// Adds one assembly's generated job functions and middleware, for example
    /// <c>AddModule&lt;Billing.JobsModule&gt;()</c>. Add every assembly the host runs jobs or middleware from, including
    /// the host's own assembly. Adding a module more than once is harmless.
    /// </summary>
    /// <remarks>
    /// Modules register into this host's catalog when its job registry is built, so a module can also arrive through
    /// <c>services.ConfigureJobs(...)</c> before or after this call. Each host builds its own catalog, so hosts in one
    /// process may add different modules.
    /// </remarks>
    /// <typeparam name="TModule">The generated <see cref="IJobsModule"/> of the assembly.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public JobsOptionsBuilder<TTimeJob, TCronJob> AddModule<TModule>()
        where TModule : IJobsModule
    {
        Services.AddJobsModuleContribution<TModule>();
        return this;
    }

    /// <summary>
    /// Tunes the deployment settings of one declared job on this host, for example
    /// <c>Tune("billing.close-day", job =&gt; job.Concurrency(2))</c>. Equivalent to the same call on
    /// <c>services.ConfigureJobs(...)</c>.
    /// </summary>
    /// <remarks>
    /// The identity is checked when the host's job registry is built: an identity no registered module declares fails
    /// startup. <c>Headless:Jobs:Jobs:{identity}</c> configuration (<c>Concurrency</c>, <c>Priority</c>,
    /// <c>FailurePolicy</c>) applies after every <c>Tune</c> call. <paramref name="configure"/> runs once, synchronously, during this call.
    /// </remarks>
    /// <param name="identity">The job's <c>[Job]</c> identity.</param>
    /// <param name="configure">Changes the job's deployment settings.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="identity"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public JobsOptionsBuilder<TTimeJob, TCronJob> Tune(
        string identity,
        [InstantHandle] Action<JobTuningBuilder> configure
    )
    {
        Services.AddJobTuning(identity, configure);
        return this;
    }

    /// <summary>
    /// Limits which registered jobs this host claims and executes. Each entry is an exact job identity, such as
    /// <c>billing.close-day</c>, or an <c>owner.*</c> pattern, such as <c>orders.*</c>, that matches every job whose
    /// identity starts with that owner segment. Calls accumulate.
    /// </summary>
    /// <remarks>
    /// Jobs outside the filter stay registered: this host can still schedule them, seeds their cron definitions, and
    /// shows them in the dashboard, and another host without the filter runs them. Without any <c>RunOnly</c> call the
    /// host runs every job. An entry that matches no registered job fails startup.
    /// </remarks>
    /// <param name="identities">Exact job identities or <c>owner.*</c> patterns.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="identities"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="identities"/> is empty, or an entry is blank or misplaces <c>*</c>.
    /// </exception>
    public JobsOptionsBuilder<TTimeJob, TCronJob> RunOnly(params string[] identities)
    {
        Argument.IsNotNullOrEmpty(identities);
        var validated = identities.Select(JobsRunFilter.ValidateEntry).ToArray();
        _runOnly.AddRange(validated);
        return this;
    }

    /// <summary>
    /// Sets the failure policy of every job on this host that neither declares one on its attribute nor has one tuned.
    /// Without this call such a job does not retry.
    /// </summary>
    /// <remarks>
    /// <c>Headless:Jobs:Jobs:{identity}:FailurePolicy</c> configuration still adjusts the retry counts and delays of a
    /// job that uses the default. A later call replaces an earlier one; the value is captured when
    /// <c>AddHeadlessJobs</c> returns.
    /// </remarks>
    /// <typeparam name="TPolicy">The policy type; it is built once, during this call.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException">The policy's retry counts or delays are out of range.</exception>
    public JobsOptionsBuilder<TTimeJob, TCronJob> DefaultFailurePolicy<TPolicy>()
        where TPolicy : FailurePolicy, new()
    {
        _defaultFailurePolicy = FailurePolicyDefinition.Create<TPolicy>();
        return this;
    }

    /// <summary>
    /// Sets the host's default job failure policy inline, for example
    /// <c>DefaultFailurePolicy(p =&gt; p.Delayed(3, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)))</c>.
    /// See <see cref="DefaultFailurePolicy{TPolicy}"/>.
    /// </summary>
    /// <param name="configure">Describes the policy; it runs once, synchronously, during this call.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The policy's retry counts or delays are out of range.</exception>
    public JobsOptionsBuilder<TTimeJob, TCronJob> DefaultFailurePolicy(
        [InstantHandle] Action<FailurePolicyBuilder> configure
    )
    {
        _defaultFailurePolicy = FailurePolicyDefinition.Create(configure);
        return this;
    }

    /// <summary>
    /// Sets the node-death default for this host. Retries are not accepted: a job's failure policy owns them, so use
    /// <see cref="DefaultFailurePolicy{TPolicy}"/> instead. Invocation metadata is not accepted.
    /// </summary>
    /// <param name="options">Startup policy settings.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The options set retries or retry intervals, contain invalid settings, or carry invocation metadata.
    /// </exception>
    public JobsOptionsBuilder<TTimeJob, TCronJob> ConfigureDefaults(JobOptions options)
    {
        _jobDefaults = JobSchedulingPolicies.Snapshot(options);
        return this;
    }

    /// <summary>Authors the node-death default for this host. See <see cref="ConfigureDefaults(JobOptions)"/>.</summary>
    /// <remarks>Invokes the callback once synchronously with a fresh builder, then validates and snapshots its options. Asynchronous callbacks are not supported.</remarks>
    /// <param name="configure">Authors startup policy settings; retries and invocation metadata are not accepted.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The authored policy sets retries or retry intervals, contains invalid settings, or carries invocation metadata.
    /// </exception>
    public JobsOptionsBuilder<TTimeJob, TCronJob> ConfigureDefaults(Action<JobOptionsBuilder> configure)
    {
        Argument.IsNotNull(configure);
        var builder = new JobOptionsBuilder();
        configure(builder);
        return ConfigureDefaults(builder.Build());
    }

    /// <summary>
    /// Overrides the host's node-death default for the generated handler accepting this request type. Retries are not
    /// accepted: the job's failure policy owns them.
    /// </summary>
    /// <typeparam name="TRequest">The request type accepted by the generated handler.</typeparam>
    /// <param name="options">Startup policy settings.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The options set retries or retry intervals, contain invalid settings, or carry invocation metadata.
    /// </exception>
    public JobsOptionsBuilder<TTimeJob, TCronJob> ConfigureJob<TRequest>(JobOptions options)
    {
        _jobOptionsByRequest[typeof(TRequest)] = JobSchedulingPolicies.Snapshot(options);
        return this;
    }

    /// <summary>
    /// Authors the node-death override for the generated handler accepting this request type. See
    /// <see cref="ConfigureJob{TRequest}(JobOptions)"/>.
    /// </summary>
    /// <remarks>Invokes the callback once synchronously with a fresh builder, then validates and snapshots its options. Asynchronous callbacks are not supported.</remarks>
    /// <typeparam name="TRequest">The request type accepted by the generated handler.</typeparam>
    /// <param name="configure">Authors startup policy settings; retries and invocation metadata are not accepted.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The authored policy sets retries or retry intervals, contains invalid settings, or carries invocation metadata.
    /// </exception>
    public JobsOptionsBuilder<TTimeJob, TCronJob> ConfigureJob<TRequest>(Action<JobOptionsBuilder> configure)
    {
        Argument.IsNotNull(configure);
        var builder = new JobOptionsBuilder();
        configure(builder);
        return ConfigureJob<TRequest>(builder.Build());
    }

    internal JobSchedulingPolicies FreezeSchedulingPolicies() => new(_jobDefaults, _jobOptionsByRequest, []);

    /// <summary>The <c>DefaultFailurePolicy</c> authored so far, captured when <c>AddHeadlessJobs</c> returns.</summary>
    internal FailurePolicyDefinition? FreezeDefaultFailurePolicy() => _defaultFailurePolicy;

    /// <summary>The <c>RunOnly</c> entries authored so far, snapshotted when <c>AddHeadlessJobs</c> returns.</summary>
    internal string[] FreezeRunOnly() => [.. _runOnly];

    /// <summary>Scheduler options instance, exposed so Core-layer extensions can toggle internal flags.</summary>
    internal SchedulerOptionsBuilder SchedulerOptions { get; }

    /// <summary>
    /// The collection <c>AddHeadlessJobs</c> is registering into. Storage naming is registered against it the moment
    /// it is authored, rather than snapshotted on this builder, so the schema lives in one place and the two
    /// <c>ConfigureStorage</c> overloads order against each other by call order alone.
    /// </summary>
    internal IServiceCollection Services { get; }

    /// <summary>
    /// Applies <paramref name="configure"/> to <see cref="JobsStorageOptions"/>, which names the database schema
    /// holding every Jobs table.
    /// </summary>
    /// <remarks>Composes with the configuration overload as last call wins.</remarks>
    /// <param name="configure">A delegate that mutates the storage options.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    public JobsOptionsBuilder<TTimeJob, TCronJob> ConfigureStorage(Action<JobsStorageOptions> configure)
    {
        Argument.IsNotNull(configure);
        Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Binds <paramref name="configuration"/> to <see cref="JobsStorageOptions"/>. Pass the
    /// <c>Headless:Jobs:Storage</c> section itself — the section is bound directly, so its keys are the option's
    /// property names (<c>Schema</c>), not a further nested path.
    /// </summary>
    /// <remarks>Composes with the callback overload as last call wins.</remarks>
    /// <param name="configuration">The <c>Headless:Jobs:Storage</c> configuration section.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    public JobsOptionsBuilder<TTimeJob, TCronJob> ConfigureStorage(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);
        Services.Configure<JobsStorageOptions>(configuration);
        return this;
    }

    internal JobsOptionsBuilder(
        JobsExecutionContext tickerExecutionContext,
        SchedulerOptionsBuilder schedulerOptions,
        IServiceCollection services
    )
    {
        _tickerExecutionContext = tickerExecutionContext;
        SchedulerOptions = schedulerOptions;
        Services = services;
        // Store this instance in the execution context for later retrieval
        tickerExecutionContext.OptionsSeeding = this;
    }

    /// <summary>
    /// Internal flag for request GZip compression.
    /// Defaults to false (plain JSON bytes).
    /// </summary>
    internal bool RequestGZipCompressionEnabled { get; set; }

    internal int RequestGZipMaxDecompressedBytes { get; set; } =
        JobsRequestSerializationOptions.DefaultMaxDecompressedRequestBytes;

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

    /// <summary>
    /// Deferred Dashboard registration set by the Dashboard package's <c>AddDashboard</c> extension and
    /// replayed by <c>AddHeadlessJobs</c> with the composed per-host
    /// <see cref="JobsRequestSerializationOptions"/> so Dashboard components share the host's request
    /// serialization settings.
    /// </summary>
    internal Action<IServiceCollection, JobsRequestSerializationOptions>? DashboardServiceAction { get; set; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    [field: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    internal Type? JobExceptionHandlerType { get; private set; }

    internal JobsRetryOptions RetryOptions { get; } = new();

    /// <summary>
    /// Configures the notification raised when a job run fails terminally. Retries are declared per job through its
    /// failure policy, not here.
    /// </summary>
    /// <param name="configure">Action that mutates the retry options.</param>
    /// <returns>This builder for method chaining.</returns>
    public JobsOptionsBuilder<TTimeJob, TCronJob> ConfigureRetries(Action<JobsRetryOptions> configure)
    {
        Argument.IsNotNull(configure);
        configure(RetryOptions);
        return this;
    }

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
    /// <remarks>
    /// Payload metadata comes from the options' <c>TypeInfoResolver</c>. If none is set, reflection-based metadata is
    /// used when the app allows it. A trimmed or native AOT app must add a <c>JsonSerializerContext</c> covering every
    /// request type, for example <c>json.TypeInfoResolverChain.Insert(0, AppJsonContext.Default)</c>.
    /// </remarks>
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
        return UseGZipCompression(JobsRequestSerializationOptions.DefaultMaxDecompressedRequestBytes);
    }

    /// <summary>
    /// Enables GZip compression for stored requests and limits the expanded payload accepted during reads.
    /// </summary>
    /// <param name="maxDecompressedBytes">Maximum expanded request size in bytes.</param>
    public JobsOptionsBuilder<TTimeJob, TCronJob> UseGZipCompression(int maxDecompressedBytes)
    {
        RequestGZipCompressionEnabled = true;
        RequestGZipMaxDecompressedBytes = Argument.IsPositive(maxDecompressedBytes);
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
    public JobsOptionsBuilder<TTimeJob, TCronJob> SetExceptionHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler
    >()
        where THandler : IJobExceptionHandler
    {
        JobExceptionHandlerType = typeof(THandler);
        return this;
    }

    internal void UseExternalProviderApplication(Action<IServiceProvider> action)
    {
        _tickerExecutionContext.ExternalProviderApplicationAction = action;
    }
}
