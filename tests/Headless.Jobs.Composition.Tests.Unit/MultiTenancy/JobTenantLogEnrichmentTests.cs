// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless;
using Headless.Jobs;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.MultiTenancy;

/// <summary>
/// A job attempt's log entries carry the job's tenant once on a host whose only provider is the console, through the
/// real scheduler, in-memory store, execute middleware, and execution handler.
/// </summary>
public sealed class JobTenantLogEnrichmentTests : TestBase
{
    private const string _Function = "tenant.log";
    private const string _JobLogMessage = "the tenant job ran";
    private const string _TenantId = "TenantId";

    [Fact]
    public async Task should_log_the_job_tenant_once_on_the_attempt_entries_of_a_console_only_host()
    {
        // given: the Headless logging profile with no OpenTelemetry
        using var logs = new KeyCapture();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.AddHeadlessLogging();
        builder.Logging.AddProvider(logs).SetMinimumLevel(LogLevel.Debug);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            options.AddModule<TenantLogJobsModule>();
        });
        using var host = builder.Build();

        // when: a job scheduled for a tenant runs
        await host
            .Services.GetRequiredService<IJobScheduler>()
            .ScheduleAsync<TenantLogJob>(
                time.GetUtcNow().AddMinutes(1),
                new JobOptions { TenantId = "acme" },
                AbortToken
            );
        time.Advance(TimeSpan.FromMinutes(1));
        await _RunDueAsync(host.Services);

        // then: the job's own entry gets the tenant from the enrichment
        var ran = logs
            .Entries.Should()
            .ContainSingle(x => string.Equals(x.Message, _JobLogMessage, StringComparison.Ordinal))
            .Subject;
        ran.Keys.Count(k => string.Equals(k, _TenantId, StringComparison.Ordinal)).Should().Be(1);
        ran.Values[_TenantId].Should().Be("acme");

        // and the framework's restore entry, written under the restored tenant with {TenantId} in its template, keeps
        // one key
        var restored = logs
            .Entries.Should()
            .ContainSingle(x => x.Message.StartsWith("Restoring ICurrentTenant to tenant", StringComparison.Ordinal))
            .Subject;
        restored.Keys.Count(k => string.Equals(k, _TenantId, StringComparison.Ordinal)).Should().Be(1);
        restored.Values[_TenantId].Should().Be("acme");
    }

    private static async Task _RunDueAsync(IServiceProvider services)
    {
        // The claim, hydration, and admission steps the dispatcher performs before a run starts.
        var manager = services.GetRequiredService<IInternalJobManager>();
        var registry = services.GetRequiredService<JobFunctionRegistry>();
        var (_, claimed) = await manager.GetNextJobs(AbortToken);
        claimed.Should().ContainSingle("the run is due");

        foreach (var state in claimed)
        {
            JobsExecutionContext.CacheFunctionReferences(state, registry);
        }

        var admitted = await manager.SetTickersInProgress([.. claimed], AbortToken);
        var handler = ActivatorUtilities.CreateInstance<JobsExecutionTaskHandler>(
            services,
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder()
        );

        foreach (var state in admitted)
        {
            await handler.ExecuteTaskAsync(state, isDue: false, AbortToken);
        }
    }

    public sealed class TenantLogJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class TenantLogJobsModule : IJobsModule
    {
        private TenantLogJobsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog)
        {
            catalog.AddFunctions(
                new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
                {
                    [_Function] = new()
                    {
                        CronExpression = "",
                        Priority = JobPriority.Normal,
                        Delegate = static (services, _, _) =>
                        {
                            services
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("Tests.TenantLogJob")
                                .LogInformation(_JobLogMessage);
                            return Task.CompletedTask;
                        },
                        MaxConcurrency = 0,
                        JobType = typeof(TenantLogJob),
                    },
                }
            );
            catalog.AddDescriptors(
                new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
                {
                    [_Function] = new(_Function, null, "", JobPriority.Normal, 0),
                }
            );
        }
    }

    /// <summary>Records each entry's keys with repeats, as a provider would export them.</summary>
    private sealed class KeyCapture : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<Entry> _entries = new();

        public IReadOnlyList<Entry> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var keys = new List<string>();
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);

            if (state is IReadOnlyList<KeyValuePair<string, object?>> tags)
            {
                foreach (var (key, value) in tags)
                {
                    keys.Add(key);
                    values.TryAdd(key, value?.ToString());
                }
            }

            _entries.Enqueue(new(formatter(state, exception), keys, values));
        }

        public void Dispose() { }
    }

    private sealed record Entry(
        string Message,
        IReadOnlyList<string> Keys,
        IReadOnlyDictionary<string, string?> Values
    );
}
