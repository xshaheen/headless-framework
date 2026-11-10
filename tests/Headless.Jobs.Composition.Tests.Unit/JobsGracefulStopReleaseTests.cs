// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.JobsThreadPool;
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
    private const string _LongRunningFunction = "graceful.stop.long-running";
    private const string _GatedLongRunningFunction = "graceful.stop.long-running.gated";
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
        released.Lease.State.Should().NotBe(RowLeaseState.Active);
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
        stored.Lease.State.Should().NotBe(RowLeaseState.Active);
        (expected == JobStatus.Failed ? stored.ExceptionMessage : stored.SkippedReason).Should().Be(_ShutdownReason);
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
            stored.Lease.State.Should().NotBe(RowLeaseState.Active);
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
        // The scheduler bounds only its own stop by the budget; the whole host stop also carries the continuations
        // that resume after each wait, which a loaded runner delays by a few hundred milliseconds. The host would
        // otherwise wait out its fifteen-second shutdown timeout, so 1.5 s of slack still proves the budget ended it.
        stopElapsed
            .Should()
            .BeLessThan(budget + TimeSpan.FromSeconds(1.5), "the stop must end inside its own budget");

        // and its uncharged release had committed when the stop returned
        var stored = await _Store(host).GetTimeJobByIdAsync(id, AbortToken);
        stored!.Status.Should().Be(JobStatus.Idle);
        stored.RetryCount.Should().Be(0);
        stored.Lease.State.Should().NotBe(RowLeaseState.Active);
    }

    [Fact]
    public async Task should_count_the_stop_budget_from_the_host_stop_when_a_later_service_stops_first()
    {
        // given: a host stop that began, and spent the scheduler's whole budget, before the scheduler's own stop starts,
        // which is what a service stopping ahead of Jobs under the default sequential stop produces
        var budget = TimeSpan.FromMilliseconds(200);
        var probe = new StopProbe(StopProbeMode.BlockUntilCancelled);
        using var host = _BuildHost(probe, stopBudget: budget, hostShutdownTimeout: TimeSpan.FromSeconds(15));
        await host.StartAsync(AbortToken);
        var id = await _Scheduler(host).EnqueueAsync<StopJob>(o => o.WithRetries(0), AbortToken);
        await probe.Started.WaitAsync(_WaitBudget, AbortToken);

        // when: ApplicationStopping records the budget origin, the budget then elapses on the real clock the scheduler
        // measures it on (a loaded runner only lengthens this wait), and the scheduler stop begins
        host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        await Task.Delay(budget + TimeSpan.FromMilliseconds(50), AbortToken);
        var stop = host.Services.GetRequiredService<JobsSchedulerBackgroundService>().StopAsync(AbortToken);

        // then: the scheduler cancels before its first yield. Counting the budget from its own stop would first wait
        // out a fresh finish window, leaving the token live here. Checking the token itself, not when the cancelled
        // job's continuation runs, keeps a loaded runner from deciding the result.
        var taskScheduler = host.Services.GetRequiredService<JobsTaskScheduler>();
        taskScheduler.ExecutionCancellationToken.IsCancellationRequested.Should().BeTrue();
        await stop.Bounded();

        // and the cancelled job's uncharged release lands. With the budget already spent the scheduler's stop returns
        // without waiting for that write, so the row is awaited here instead of read the moment the stop returns.
        var stored = await _WaitForAsync(_Store(host), id, row => row.Status is not JobStatus.InProgress);
        stored.Status.Should().Be(JobStatus.Idle);
        stored.RetryCount.Should().Be(0);
        stored.Lease.State.Should().NotBe(RowLeaseState.Active);
    }

    [Fact]
    public async Task should_drain_a_job_the_fallback_sweep_dispatched_like_a_scheduler_dispatched_one()
    {
        // given: an overdue row written straight to the store, so the sleeping scheduler loop is never woken for it
        // and only the fallback sweep can dispatch it
        // A long stop budget keeps the release window, which cancels running jobs, far from the observation below, so
        // a slow runner cannot cancel the job before the test releases it.
        var probe = new StopProbe(StopProbeMode.BlockUntilReleased);
        using var host = _BuildHost(
            probe,
            stopBudget: TimeSpan.FromSeconds(20),
            hostShutdownTimeout: TimeSpan.FromSeconds(30),
            fallbackInterval: TimeSpan.FromMilliseconds(200)
        );
        await host.StartAsync(AbortToken);
        var store = _Store(host);
        var pool = host.Services.GetRequiredService<JobsTaskScheduler>();
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
        await _WaitUntilAsync(() => pool.IsDraining);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_release_a_claimed_long_running_job_still_waiting_to_start_uncharged(
        bool waitsOnFunctionGate
    )
    {
        // given: a long-running run that ignores cancellation past the stop budget holds either its function's only
        // permit or the pool's only long-running slot, and a second run is already claimed and waiting behind it. The
        // long-running slots follow the worker count, so four workers leave the gated run a free slot to wait in.
        var probe = new StopProbe(StopProbeMode.BlockIgnoringCancellation);
        using var host = _BuildHost(probe, maxConcurrency: waitsOnFunctionGate ? 4 : 1);
        await host.StartAsync(AbortToken);
        var scheduler = _Scheduler(host);
        var store = _Store(host);
        var waiting = Guid.Empty;
        try
        {
            if (waitsOnFunctionGate)
            {
                await scheduler.EnqueueAsync<GatedLongRunningStopJob>(o => o.WithRetries(3), AbortToken);
            }
            else
            {
                await scheduler.EnqueueAsync<LongRunningStopJob>(o => o.WithRetries(3), AbortToken);
            }

            await probe.Started.WaitAsync(_WaitBudget, AbortToken);
            waiting = waitsOnFunctionGate
                ? await scheduler.EnqueueAsync<GatedLongRunningStopJob>(o => o.WithRetries(3), AbortToken)
                : await scheduler.EnqueueAsync<LongRunningStopJob>(o => o.WithRetries(3), AbortToken);
            (await store.GetTimeJobByIdAsync(waiting, AbortToken))!.Status.Should().Be(JobStatus.InProgress);

            // when
            await host.StopAsync(AbortToken);
        }
        finally
        {
            probe.Release();
        }

        // then
        probe.Invocations.Should().Be(1, "the waiting run must not start once the stop began");
        var stored = await store.GetTimeJobByIdAsync(waiting, AbortToken);
        stored!.Status.Should().Be(JobStatus.Idle);
        stored.RetryCount.Should().Be(0, "a graceful stop is not an attempt the job used up");
        stored.Lease.State.Should().NotBe(RowLeaseState.Active);
    }

    [Fact]
    public async Task should_accept_an_enqueue_while_the_scheduler_drains_and_leave_the_job_idle_for_the_next_owner()
    {
        // given: a running job keeps the drain open
        var probe = new StopProbe(StopProbeMode.BlockUntilReleased);
        using var host = _BuildHost(probe, stopBudget: TimeSpan.FromSeconds(10), hostShutdownTimeout: _WaitBudget);
        await host.StartAsync(AbortToken);
        var scheduler = _Scheduler(host);
        var store = _Store(host);
        await scheduler.EnqueueAsync<StopJob>(o => o.WithRetries(0), AbortToken);
        await probe.Started.WaitAsync(_WaitBudget, AbortToken);
        var pool = host.Services.GetRequiredService<JobsTaskScheduler>();
        var stop = host.StopAsync(AbortToken);
        Guid enqueued;
        try
        {
            await _WaitUntilAsync(() => pool.IsDraining);

            // when
            enqueued = await scheduler.EnqueueAsync<StopJob>(o => o.WithRetries(2), AbortToken);
        }
        finally
        {
            probe.Release();
            await stop;
        }

        // then
        probe.Invocations.Should().Be(1, "a job enqueued during the drain must not start on the stopping node");
        var stored = await store.GetTimeJobByIdAsync(enqueued, AbortToken);
        stored!.Status.Should().Be(JobStatus.Idle);
        stored.RetryCount.Should().Be(0);
        stored.Lease.State.Should().NotBe(RowLeaseState.Active);
        stored.ExecutionTime.Should().BeOnOrBefore(DateTime.UtcNow, "the next owner claims it as soon as it polls");
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

    private static async Task _WaitUntilAsync(Func<bool> condition)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        budget.CancelAfter(_WaitBudget);

        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), budget.Token);
        }
    }

    public enum StopProbeMode
    {
        BlockUntilCancelled = 0,
        Succeed = 1,
        BlockUntilReleased = 3,
        BlockIgnoringCancellation = 4,
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
                default:
                    try
                    {
                        if (mode == StopProbeMode.BlockIgnoringCancellation)
                        {
                            await _released.Task.ConfigureAwait(false);
                            return;
                        }

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

    public sealed class LongRunningStopJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class GatedLongRunningStopJob : IJob
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
                    [_Function] = _Registration(JobPriority.Normal, 0, typeof(StopJob)),
                    [_LongRunningFunction] = _Registration(JobPriority.LongRunning, 0, typeof(LongRunningStopJob)),
                    [_GatedLongRunningFunction] = _Registration(
                        JobPriority.LongRunning,
                        1,
                        typeof(GatedLongRunningStopJob)
                    ),
                }
            );
            catalog.AddDescriptors(
                new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
                {
                    [_Function] = new(_Function, null, string.Empty, JobPriority.Normal, 0),
                    [_LongRunningFunction] = new(_LongRunningFunction, null, string.Empty, JobPriority.LongRunning, 0),
                    [_GatedLongRunningFunction] = new(
                        _GatedLongRunningFunction,
                        null,
                        string.Empty,
                        JobPriority.LongRunning,
                        1
                    ),
                }
            );
        }

        private static JobFunctionRegistration _Registration(JobPriority priority, int maxConcurrency, Type jobType)
        {
            return new()
            {
                CronExpression = string.Empty,
                Priority = priority,
                Delegate = static (services, _, cancellationToken) =>
                    services.GetRequiredService<StopProbe>().RunAsync(cancellationToken),
                MaxConcurrency = maxConcurrency,
                JobType = jobType,
            };
        }
    }
}
