// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Managers;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Progress reported from a running job, through the real executor, manager, and in-memory store: frequent reports
/// cost at most one write per interval, the last report always reaches the store, and progress survives a retry.
/// </summary>
public sealed class JobProgressExecutionTests : TestBase
{
    private const string _Function = "progress.job";

    [Fact]
    public async Task should_store_the_last_of_many_reports_without_writing_on_every_call()
    {
        await using var harness = await Harness.CreateAsync(retries: 0);

        await harness.ExecuteAsync(
            (context, attempt) =>
            {
                for (var item = 1; item <= 500; item++)
                {
                    context.ReportProgress(item / 5d, $"item {item} of 500");
                }

                return Task.CompletedTask;
            }
        );

        var stored = await harness.StoredAsync();
        stored.Status.Should().Be(JobStatus.Succeeded);
        stored.ProgressPercent.Should().Be(100);
        stored.ProgressMessage.Should().Be("item 500 of 500");
        stored.ProgressUpdatedAt.Should().NotBeNull();
        // Fake time never advances, so the throttle allows the leading write and nothing else until the run ends; the
        // final value rides on the terminal write.
        harness.ProgressWrites.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task should_keep_progress_from_a_failed_attempt_through_an_in_process_retry()
    {
        await using var harness = await Harness.CreateAsync(retries: 1);

        await harness.ExecuteAsync(
            (context, attempt) =>
            {
                if (attempt == 0)
                {
                    context.ReportProgress(40, "failed at 40%");
                    throw new InvalidOperationException("transient");
                }

                // The retry runs to completion without reporting, so the stored value is still the first attempt's.
                return Task.CompletedTask;
            }
        );

        var stored = await harness.StoredAsync();
        stored.Status.Should().Be(JobStatus.Succeeded);
        stored.RetryCount.Should().Be(1);
        stored.ProgressPercent.Should().Be(40);
        stored.ProgressMessage.Should().Be("failed at 40%");
    }

    [Fact]
    public async Task should_store_the_progress_a_failed_run_reached()
    {
        await using var harness = await Harness.CreateAsync(retries: 0);

        await harness.ExecuteAsync(
            (context, _) =>
            {
                context.ReportProgress(10);
                context.ReportProgress(65, "gave up here");
                throw new InvalidOperationException("permanent");
            }
        );

        var stored = await harness.StoredAsync();
        stored.Status.Should().Be(JobStatus.Failed);
        stored.ProgressPercent.Should().Be(65);
        stored.ProgressMessage.Should().Be("gave up here");
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly FakeTimeProvider _time;
        private readonly JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> _store;
        private readonly InternalJobsManager<TimeJobEntity, CronJobEntity> _manager;
        private readonly SchedulerOptionsBuilder _options;
        private readonly TimeJobEntity _job;
        private int _progressWrites;

        private Harness(
            ServiceProvider services,
            FakeTimeProvider time,
            JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> store,
            InternalJobsManager<TimeJobEntity, CronJobEntity> manager,
            SchedulerOptionsBuilder options,
            TimeJobEntity job,
            IJobsNotificationHubSender hub
        )
        {
            _services = services;
            _time = time;
            _store = store;
            _manager = manager;
            _options = options;
            _job = job;
            hub.When(x => x.UpdateJobProgress(Arg.Any<JobExecutionState>(), Arg.Any<JobProgress>()))
                .Do(_ => Interlocked.Increment(ref _progressWrites));
        }

        public int ProgressWrites => Volatile.Read(ref _progressWrites);

        public static async Task<Harness> CreateAsync(int retries)
        {
            var time = new FakeTimeProvider();
            var registrations = new ServiceCollection();
            registrations.AddSingleton<TimeProvider>(time);
            registrations.AddHeadlessGuidGenerator();
            var services = registrations.BuildServiceProvider();
            var store = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services);
            var options = new SchedulerOptionsBuilder();
            var hub = Substitute.For<IJobsNotificationHubSender>();
            var manager = new InternalJobsManager<TimeJobEntity, CronJobEntity>(
                store,
                time,
                hub,
                new CronScheduleCache(TimeZoneInfo.Utc),
                NullLogger<InternalJobsManager<TimeJobEntity, CronJobEntity>>.Instance,
                JobsRequestSerializationOptions.Default,
                services.GetRequiredService<IGuidGenerator>(),
                services,
                options
            );
            var job = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = _Function,
                ExecutionTime = time.GetUtcNow().UtcDateTime,
                Retries = retries,
                RetryIntervals = [0],
            };
            (await store.AddTimeJobsAsync([job], AbortToken)).Should().Be(1);
            return new Harness(services, time, store, manager, options, job, hub);
        }

        public async Task ExecuteAsync(Func<JobContext, int, Task> body)
        {
            var claimed = (await _store.AcquireImmediateTimeJobsAsync([_job.Id], AbortToken)).Single();
            var context = new JobExecutionState
            {
                JobId = claimed.Id,
                FunctionName = claimed.Function,
                ContractVersion = claimed.ContractVersion,
                Type = JobType.TimeJob,
                ExecutionTime = claimed.ExecutionTime!.Value,
                Status = claimed.Status,
                RetryCount = claimed.RetryCount,
                Retries = claimed.Retries,
                RetryIntervals = claimed.RetryIntervals,
            };
            var registry = JobFunctionRegistryBuilder.Build(
                [
                    new KeyValuePair<string, JobFunctionRegistration>(
                        _Function,
                        new()
                        {
                            CronExpression = "",
                            Priority = JobPriority.Normal,
                            MaxConcurrency = 0,
                            Delegate = (_, execution, _) => body(execution, execution.RetryCount),
                        }
                    ),
                ],
                [],
                [
                    new KeyValuePair<string, JobFunctionDescriptor>(
                        _Function,
                        new(_Function, null, "", JobPriority.Normal, 0, claimed.ContractVersion)
                    ),
                ]
            );
            JobsExecutionContext.CacheFunctionReferences(context, registry);
            var handler = new JobsExecutionTaskHandler(
                _services,
                _time,
                Substitute.For<IJobsInstrumentation>(),
                _manager,
                registry,
                new JobsExecutionCancellationRegistry(),
                _options,
                NullLogger<JobsExecutionTaskHandler>.Instance
            );

            await handler.ExecuteTaskAsync(context, isDue: false, cancellationToken: AbortToken);
            context.LeaseLost.Should().BeFalse();
        }

        public async Task<TimeJobEntity> StoredAsync()
        {
            return (await _store.GetTimeJobByIdAsync(_job.Id, AbortToken))!;
        }

        public ValueTask DisposeAsync() => _services.DisposeAsync();
    }
}
