using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

public class JobsOptionsBuilder<TTimeTicker, TCronTicker> : IJobsOptionsSeeding
    where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
    where TCronTicker : CronJobEntity, new()
{
    private readonly JobsExecutionContext _tickerExecutionContext;
    private readonly SchedulerOptionsBuilder _schedulerOptions;

    internal JobsOptionsBuilder(
        JobsExecutionContext tickerExecutionContext,
        SchedulerOptionsBuilder schedulerOptions
    )
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
    /// Controls whether code-defined cron tickers are seeded on startup.
    /// Defaults to true.
    /// </summary>
    internal bool SeedDefinedCronTickers { get; set; } = true;

    /// <summary>
    /// Controls whether background services (job processors) should be registered.
    /// Defaults to true. Set to false to only register managers for queuing jobs.
    /// </summary>
    internal bool RegisterBackgroundServices { get; set; } = true;

    /// <summary>
    /// Seeding delegate for time tickers, executed with the application's service provider.
    /// </summary>
    internal Func<IServiceProvider, Task>? TimeSeederAction { get; set; }

    /// <summary>
    /// Seeding delegate for cron tickers, executed with the application's service provider.
    /// </summary>
    internal Func<IServiceProvider, Task>? CronSeederAction { get; set; }

    // Explicit interface implementation for IJobsOptionsSeeding
    bool IJobsOptionsSeeding.SeedDefinedCronTickers => SeedDefinedCronTickers;
    Func<IServiceProvider, Task>? IJobsOptionsSeeding.TimeSeederAction => TimeSeederAction;
    Func<IServiceProvider, Task>? IJobsOptionsSeeding.CronSeederAction => CronSeederAction;

    internal Action<IServiceCollection>? ExternalProviderConfigServiceAction { get; set; }
    internal Action<IServiceCollection>? DashboardServiceAction { get; set; }
    internal Type? JobExceptionHandlerType { get; private set; }

    public JobsOptionsBuilder<TTimeTicker, TCronTicker> ConfigureScheduler(
        Action<SchedulerOptionsBuilder> schedulerOptionsBuilder
    )
    {
        schedulerOptionsBuilder?.Invoke(_schedulerOptions);
        return this;
    }

    /// <summary>
    /// JsonSerializerOptions specifically for serializing/deserializing ticker requests.
    /// If not set, default JsonSerializerOptions will be used.
    /// </summary>
    internal JsonSerializerOptions? RequestJsonSerializerOptions { get; set; }

    /// <summary>
    /// Configures the JSON serialization options specifically for ticker request serialization/deserialization.
    /// </summary>
    /// <param name="configure">Action to configure JsonSerializerOptions for ticker requests</param>
    /// <returns>The JobsOptionsBuilder for method chaining</returns>
    public JobsOptionsBuilder<TTimeTicker, TCronTicker> ConfigureRequestJsonOptions(
        Action<JsonSerializerOptions> configure
    )
    {
        RequestJsonSerializerOptions ??= new JsonSerializerOptions();
        configure?.Invoke(RequestJsonSerializerOptions);
        return this;
    }

    /// <summary>
    /// Enables GZip compression for ticker request payloads.
    /// When not called, requests are stored as plain UTF-8 JSON bytes.
    /// </summary>
    /// <returns>The JobsOptionsBuilder for method chaining</returns>
    public JobsOptionsBuilder<TTimeTicker, TCronTicker> UseGZipCompression()
    {
        RequestGZipCompressionEnabled = true;
        return this;
    }

    /// <summary>
    /// Disable automatic seeding of code-defined cron tickers on startup.
    /// </summary>
    public JobsOptionsBuilder<TTimeTicker, TCronTicker> IgnoreSeedDefinedCronTickers()
    {
        SeedDefinedCronTickers = false;
        return this;
    }

    /// <summary>
    /// Disables background services registration.
    /// Use this when you only want to queue jobs without processing them in this application.
    /// Only the managers (ITimeJobManager, ICronJobManager) will be available for queuing jobs.
    /// </summary>
    public JobsOptionsBuilder<TTimeTicker, TCronTicker> DisableBackgroundServices()
    {
        RegisterBackgroundServices = false;
        return this;
    }

    /// <summary>
    /// Configure a custom seeder for time tickers, executed on application startup.
    /// </summary>
    public JobsOptionsBuilder<TTimeTicker, TCronTicker> UseJobsSeeder(
        Func<ITimeJobManager<TTimeTicker>, Task> timeSeeder
    )
    {
        if (timeSeeder == null)
        {
            return this;
        }

        TimeSeederAction = async sp =>
        {
            var manager = sp.GetRequiredService<ITimeJobManager<TTimeTicker>>();
            await timeSeeder(manager).ConfigureAwait(false);
        };

        return this;
    }

    /// <summary>
    /// Configure a custom seeder for cron tickers, executed on application startup.
    /// </summary>
    public JobsOptionsBuilder<TTimeTicker, TCronTicker> UseJobsSeeder(
        Func<ICronJobManager<TCronTicker>, Task> cronSeeder
    )
    {
        if (cronSeeder == null)
        {
            return this;
        }

        CronSeederAction = async sp =>
        {
            var manager = sp.GetRequiredService<ICronJobManager<TCronTicker>>();
            await cronSeeder(manager).ConfigureAwait(false);
        };

        return this;
    }

    /// <summary>
    /// Configure custom seeders for both time and cron tickers, executed on application startup.
    /// </summary>
    public JobsOptionsBuilder<TTimeTicker, TCronTicker> UseJobsSeeder(
        Func<ITimeJobManager<TTimeTicker>, Task> timeSeeder,
        Func<ICronJobManager<TCronTicker>, Task> cronSeeder
    )
    {
        UseJobsSeeder(timeSeeder);
        UseJobsSeeder(cronSeeder);
        return this;
    }

    public JobsOptionsBuilder<TTimeTicker, TCronTicker> SetExceptionHandler<THandler>()
        where THandler : IJobExceptionHandler
    {
        JobExceptionHandlerType = typeof(THandler);
        return this;
    }

    internal void UseExternalProviderApplication(Action<IServiceProvider> action) =>
        _tickerExecutionContext.ExternalProviderApplicationAction = action;

    internal void UseDashboardApplication(Action<object> action) =>
        _tickerExecutionContext.DashboardApplicationAction = action;
}

public class SchedulerOptionsBuilder
{
    public string NodeIdentifier { get; set; } = Environment.MachineName;
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
    public TimeSpan IdleWorkerTimeOut { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan FallbackIntervalChecker { get; set; } = TimeSpan.FromSeconds(30);
    public TimeZoneInfo SchedulerTimeZone { get; set; } = TimeZoneInfo.Local;
}
