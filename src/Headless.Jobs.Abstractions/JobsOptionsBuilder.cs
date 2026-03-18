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
    public string NodeIdentifier { get; set; } = Environment.MachineName;
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
    public TimeSpan IdleWorkerTimeOut { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan FallbackIntervalChecker { get; set; } = TimeSpan.FromSeconds(30);
    public TimeZoneInfo SchedulerTimeZone { get; set; } = TimeZoneInfo.Local;

    /// <summary>
    /// Controls how job processing starts. Defaults to <see cref="JobsStartMode.Immediate"/>.
    /// </summary>
    public JobsStartMode StartMode { get; set; } = JobsStartMode.Immediate;
}
