// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// A graceful host stop is not a crash: work that has not started stays unstarted, a job the stop interrupts is
/// settled by its node-death policy without spending a retry, and every release lands before the stop returns.
/// Each test runs the real scheduler, dispatcher, manager, and in-memory store, and reads the stored row back.
/// </summary>
public sealed class JobsGracefulStopReleaseTests : TestBase
{
    private const string _Function = "graceful.stop";
    private const string _ShutdownReason = "interrupted by host shutdown";
    private static readonly TimeSpan _WaitBudget = TimeSpan.FromSeconds(20);

    // Short enough to keep the suite fast: a job that waits for cancellation is cancelled when this runs out.
    private static readonly TimeSpan _ShutdownTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task should_release_a_zero_retry_job_uncharged_and_let_the_next_host_run_it_once()
    {
        // given
        var probe = new StopProbe(StopProbeMode.BlockUntilCancelled);
        using var first = _BuildHost(probe);
        await first.StartAsync(AbortToken);
        var id = await _Scheduler(first).EnqueueAsync<StopJob>(o => o.WithRetries(0), AbortToken);
        await probe.Started.WaitAsync(_WaitBudget, AbortToken);

        // when
        await first.StopAsync(AbortToken);

        // then
        var store = _Store(first);
        var released = await store.GetTimeJobByIdAsync(id, AbortToken);
        released!.Status.Should().Be(JobStatus.Idle);
        released.RetryCount.Should().Be(0, "a graceful stop is not an attempt the job used up");
        released.OwnerId.Should().BeNull();
        released.LockedUntil.Should().BeNull();
        probe.Invocations.Should().Be(1);

        // and the next host runs it once to completion
        var next = new StopProbe(StopProbeMode.Succeed);
        using var second = _BuildHost(next, store);
        await second.StartAsync(AbortToken);
        try
        {
            await next.Started.WaitAsync(_WaitBudget, AbortToken);
            var done = await _WaitForAsync(store, id, row => row.Status is JobStatus.Succeeded or JobStatus.DueDone);
            done.RetryCount.Should().Be(0);
            next.Invocations.Should().Be(1);
        }
        finally
        {
            await second.StopAsync(AbortToken);
        }
    }

    [Theory]
    [InlineData(NodeDeathPolicy.MarkFailed, JobStatus.Failed)]
    [InlineData(NodeDeathPolicy.Skip, JobStatus.Skipped)]
    public async Task should_settle_an_interrupted_job_by_its_terminal_node_death_policy(
        NodeDeathPolicy policy,
        JobStatus expected
    )
    {
        // given
        var probe = new StopProbe(StopProbeMode.BlockUntilCancelled);
        using var host = _BuildHost(probe);
        await host.StartAsync(AbortToken);
        var id = await _Scheduler(host)
            .EnqueueAsync<StopJob>(o => o.WithRetries(3).WithNodeDeathPolicy(policy), AbortToken);
        await probe.Started.WaitAsync(_WaitBudget, AbortToken);

        // when
        await host.StopAsync(AbortToken);

        // then
        var stored = await _Store(host).GetTimeJobByIdAsync(id, AbortToken);
        stored!.Status.Should().Be(expected);
        stored.RetryCount.Should().Be(0);
        stored.LockedUntil.Should().BeNull();
        (expected == JobStatus.Failed ? stored.ExceptionMessage : stored.SkippedReason).Should().Be(_ShutdownReason);
    }

    [Fact]
    public async Task should_park_a_job_stopped_in_its_retry_wait_at_its_planned_retry_instant()
    {
        // given: the first attempt fails, and the next one is planned a minute out
        var probe = new StopProbe(StopProbeMode.Fail);
        using var host = _BuildHost(probe);
        await host.StartAsync(AbortToken);
        var beforeFailure = DateTime.UtcNow;
        var id = await _Scheduler(host).EnqueueAsync<StopJob>(o => o.WithRetries(2).WithRetryIntervals(60), AbortToken);
        var store = _Store(host);
        await _WaitForAsync(store, id, row => row.RetryCount == 1);
        var afterFailure = DateTime.UtcNow;

        // when
        await host.StopAsync(AbortToken);

        // then
        var stored = await store.GetTimeJobByIdAsync(id, AbortToken);
        stored!.Status.Should().Be(JobStatus.Idle);
        stored.RetryCount.Should().Be(1, "only the attempt that actually failed is charged");
        stored.OwnerId.Should().BeNull();
        stored.ExecutionTime.Should().BeOnOrAfter(beforeFailure.AddSeconds(60));
        stored.ExecutionTime.Should().BeOnOrBefore(afterFailure.AddSeconds(60));
        probe.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task should_hand_back_queued_work_without_starting_it()
    {
        // given: one worker, busy with a job that runs until it is cancelled, and a second job queued behind it
        var probe = new StopProbe(StopProbeMode.BlockUntilCancelled);
        using var host = _BuildHost(probe, maxConcurrency: 1);
        await host.StartAsync(AbortToken);
        var scheduler = _Scheduler(host);
        var running = await scheduler.EnqueueAsync<StopJob>(o => o.WithRetries(0), AbortToken);
        await probe.Started.WaitAsync(_WaitBudget, AbortToken);
        var queued = await scheduler.EnqueueAsync<StopJob>(o => o.WithRetries(0), AbortToken);

        // when
        await host.StopAsync(AbortToken);

        // then
        probe.Invocations.Should().Be(1, "the queued job must not start once the stop began");
        var store = _Store(host);
        foreach (var id in new[] { running, queued })
        {
            var stored = await store.GetTimeJobByIdAsync(id, AbortToken);
            stored!.Status.Should().Be(JobStatus.Idle);
            stored.RetryCount.Should().Be(0);
            stored.OwnerId.Should().BeNull();
        }
    }

    [Fact]
    public async Task should_cancel_at_the_release_window_and_release_before_the_stop_budget_ends()
    {
        // given: a four-second stop budget keeps a two-second release window (half the budget), and the host would
        // wait far longer, so only the scheduler's own budget can end this stop on time
        var budget = TimeSpan.FromSeconds(4);
        var releaseWindow = TimeSpan.FromSeconds(2);
        var probe = new StopProbe(StopProbeMode.BlockUntilCancelled);
        using var host = _BuildHost(probe, stopBudget: budget, hostShutdownTimeout: TimeSpan.FromSeconds(15));
        await host.StartAsync(AbortToken);
        var id = await _Scheduler(host).EnqueueAsync<StopJob>(o => o.WithRetries(0), AbortToken);
        await probe.Started.WaitAsync(_WaitBudget, AbortToken);

        // when
        var stopStarted = TimeProvider.System.GetTimestamp();
        await host.StopAsync(AbortToken);
        var stopElapsed = TimeProvider.System.GetElapsedTime(stopStarted);

        // then: the job ran until the release window opened, then was cancelled
        var cancelledAfter = TimeProvider.System.GetElapsedTime(stopStarted, probe.CancelledAt!.Value);
        cancelledAfter.Should().BeGreaterThan(budget - releaseWindow - TimeSpan.FromMilliseconds(250));
        stopElapsed.Should().BeLessThan(budget, "the stop must end inside its own budget");

        // and its uncharged release had committed when the stop returned
        var stored = await _Store(host).GetTimeJobByIdAsync(id, AbortToken);
        stored!.Status.Should().Be(JobStatus.Idle);
        stored.RetryCount.Should().Be(0);
        stored.OwnerId.Should().BeNull();
        stored.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task should_drain_a_job_the_fallback_sweep_dispatched_like_a_scheduler_dispatched_one()
    {
        // given: an overdue row written straight to the store, so the sleeping scheduler loop is never woken for it
        // and only the fallback sweep can dispatch it
        var probe = new StopProbe(StopProbeMode.BlockUntilReleased);
        using var host = _BuildHost(probe, fallbackInterval: TimeSpan.FromMilliseconds(200));
        await host.StartAsync(AbortToken);
        var store = _Store(host);
        var id = Guid.NewGuid();
        await store.AddTimeJobsAsync(
            [
                new TimeJobEntity
                {
                    Id = id,
                    Function = _Function,
                    ExecutionTime = DateTime.UtcNow.AddSeconds(-5),
                    Retries = 0,
                },
            ],
            AbortToken
        );
        await probe.Started.WaitAsync(_WaitBudget, AbortToken);

        // when: the host stops while the job is still running, and the job finishes inside the stop budget
        var stop = host.StopAsync(AbortToken);
        await Task.Delay(TimeSpan.FromMilliseconds(300), AbortToken);
        probe.CancelledAt.Should().BeNull("a running job keeps running until the release window opens");
        probe.Release();
        await stop;

        // then
        probe.CancelledAt.Should().BeNull();
        var stored = await store.GetTimeJobByIdAsync(id, AbortToken);
        stored!.Status.Should().BeOneOf(JobStatus.Succeeded, JobStatus.DueDone);
        stored.RetryCount.Should().Be(0);
        probe.Invocations.Should().Be(1);
    }

    private static IHost _BuildHost(
        StopProbe probe,
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity>? store = null,
        int maxConcurrency = 4,
        TimeSpan? stopBudget = null,
        TimeSpan? hostShutdownTimeout = null,
        TimeSpan? fallbackInterval = null
    )
    {
        return new HostBuilder()
            .ConfigureServices(services =>
            {
                services.Configure<HostOptions>(options =>
                    options.ShutdownTimeout = hostShutdownTimeout ?? _ShutdownTimeout
                );
                services.AddSingleton(probe);
                services.AddHeadlessJobs(options =>
                    options
                        .AddModule<StopJobsModule>()
                        .ConfigureScheduler(scheduler =>
                        {
                            scheduler.MaxConcurrency = maxConcurrency;
                            scheduler.ShutdownTimeout = stopBudget ?? _ShutdownTimeout;

                            if (fallbackInterval is { } interval)
                            {
                                scheduler.FallbackIntervalChecker = interval;
                            }
                        })
                );

                if (store is not null)
                {
                    services.AddSingleton(store);
                }
            })
            .Build();
    }

    private static IJobScheduler _Scheduler(IHost host) => host.Services.GetRequiredService<IJobScheduler>();

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

    private static async Task<TimeJobEntity> _WaitForAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid id,
        Func<TimeJobEntity, bool> condition
    )
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        budget.CancelAfter(_WaitBudget);

        while (true)
        {
            if (await store.GetTimeJobByIdAsync(id, budget.Token) is { } row && condition(row))
            {
                return row;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), budget.Token);
        }
    }

    public enum StopProbeMode
    {
        BlockUntilCancelled = 0,
        Succeed = 1,
        Fail = 2,
        BlockUntilReleased = 3,
    }

    public sealed class StopProbe(StopProbeMode mode)
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _invocations;
        private long _cancelledAt;

        public Task Started => _started.Task;

        public int Invocations => Volatile.Read(ref _invocations);

        // Monotonic timestamp at which a blocking run observed cancellation, or null while it has not.
        public long? CancelledAt => Interlocked.Read(ref _cancelledAt) is var at and not 0 ? at : null;

        public void Release() => _released.TrySetResult();

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocations);
            _started.TrySetResult();

            switch (mode)
            {
                case StopProbeMode.Succeed:
                    return;
                case StopProbeMode.Fail:
                    throw new InvalidOperationException("The first attempt fails.");
                default:
                    try
                    {
                        var until =
                            mode == StopProbeMode.BlockUntilReleased
                                ? _released.Task
                                : Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
                        await until.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Interlocked.Exchange(ref _cancelledAt, TimeProvider.System.GetTimestamp());
                        throw;
                    }

                    return;
            }
        }
    }

    public sealed class StopJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class StopJobsModule : IJobsModule
    {
        private StopJobsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog)
        {
            catalog.AddFunctions(
                new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
                {
                    [_Function] = new()
                    {
                        CronExpression = string.Empty,
                        Priority = JobPriority.Normal,
                        Delegate = static (services, _, cancellationToken) =>
                            services.GetRequiredService<StopProbe>().RunAsync(cancellationToken),
                        MaxConcurrency = 0,
                        JobType = typeof(StopJob),
                    },
                }
            );
            catalog.AddDescriptors(
                new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
                {
                    [_Function] = new(_Function, null, string.Empty, JobPriority.Normal, 0),
                }
            );
        }
    }
}
