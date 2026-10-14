// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Pins how often the fallback loop sweeps for stalled work. While timed-out work drains, the loop spins every 10 ms;
/// the stall sweep must not ride along on every spin, only once per <see cref="SchedulerOptionsBuilder.FallbackIntervalChecker" />,
/// the same cadence an idle loop already has.
/// </summary>
public sealed class JobsFallbackStallSweepCadenceTests : TestBase
{
    private static readonly TimeSpan _period = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _drainSpin = TimeSpan.FromMilliseconds(10);

    [Fact]
    public async Task should_sweep_stalled_work_before_the_first_timed_out_pass_and_once_per_period_when_idle()
    {
        var manager = _Manager(drainingPasses: 0);
        var clock = new TimerSignalingTimeProvider();
        await using var harness = _Service(manager, clock);
        await harness.Service.StartAsync(AbortToken);

        try
        {
            await clock.WaitForTimerAsync(_period, AbortToken);

            Received.InOrder(() =>
            {
                _ = manager.ReclaimStalledResources(Arg.Any<CancellationToken>());
                _ = manager.RunTimedOutTickers(Arg.Any<CancellationToken>());
            });

            clock.Advance(_period);
            await clock.WaitForTimerAsync(_period, AbortToken);

            await manager.Received(2).ReclaimStalledResources(Arg.Any<CancellationToken>());
            await manager.Received(2).RunTimedOutTickers(Arg.Any<CancellationToken>());
        }
        finally
        {
            await harness.Service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task should_sweep_stalled_work_once_when_timed_out_work_drains_within_one_period()
    {
        const int drainingPasses = 4;
        var manager = _Manager(drainingPasses);
        var clock = new TimerSignalingTimeProvider();
        await using var harness = _Service(manager, clock);
        await harness.Service.StartAsync(AbortToken);

        try
        {
            for (var pass = 0; pass < drainingPasses; pass++)
            {
                await clock.WaitForTimerAsync(_drainSpin, AbortToken);
                clock.Advance(_drainSpin);
            }

            // The pass that finds nothing left parks the loop for a full period.
            await clock.WaitForTimerAsync(_period, AbortToken);

            await manager.Received(drainingPasses + 1).RunTimedOutTickers(Arg.Any<CancellationToken>());
            await manager.Received(1).ReclaimStalledResources(Arg.Any<CancellationToken>());
        }
        finally
        {
            await harness.Service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task should_sweep_stalled_work_again_when_a_period_elapses_mid_drain()
    {
        const int drainingPasses = 3;
        var manager = _Manager(drainingPasses);
        var clock = new TimerSignalingTimeProvider();
        await using var harness = _Service(manager, clock);
        await harness.Service.StartAsync(AbortToken);

        try
        {
            // Pass 1 sweeps; pass 2, 10 ms later, does not.
            await clock.WaitForTimerAsync(_drainSpin, AbortToken);
            clock.Advance(_drainSpin);
            await clock.WaitForTimerAsync(_drainSpin, AbortToken);

            // Pass 3 starts a full period after the first sweep, so it sweeps again.
            clock.Advance(_period);
            await clock.WaitForTimerAsync(_drainSpin, AbortToken);
            clock.Advance(_drainSpin);
            await clock.WaitForTimerAsync(_period, AbortToken);

            await manager.Received(drainingPasses + 1).RunTimedOutTickers(Arg.Any<CancellationToken>());
            await manager.Received(2).ReclaimStalledResources(Arg.Any<CancellationToken>());
        }
        finally
        {
            await harness.Service.StopAsync(CancellationToken.None);
        }
    }

    // Returns one timed-out job for the first drainingPasses passes, then nothing. The admission claim reports the
    // job as taken by another node, so the queued work item ends without executing anything.
    private static IInternalJobManager _Manager(int drainingPasses)
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager.ReclaimStalledResources(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        var passes = 0;
        manager
            .RunTimedOutTickers(Arg.Any<CancellationToken>())
            .Returns(_ =>
                Task.FromResult<JobExecutionState[]>(
                    Interlocked.Increment(ref passes) <= drainingPasses
                        ? [new JobExecutionState { JobId = Guid.NewGuid(), FunctionName = "timed-out" }]
                        : []
                )
            );
        manager
            .SetTickersInProgress(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Array.Empty<JobExecutionState>()));

        return manager;
    }

    private static FallbackServiceHarness _Service(IInternalJobManager manager, TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        var serviceProvider = services.BuildServiceProvider();
        var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance,
            new JobsFunctionTagBound(JobFunctionRegistryBuilder.Build([], [], []))
        );

        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(CancellationToken.None);

        return new FallbackServiceHarness(
#pragma warning disable CA2000 // FallbackServiceHarness takes ownership of the service and disposes it with the scheduler and provider.
            new JobsFallbackBackgroundService(
                manager,
                new SchedulerOptionsBuilder { FallbackIntervalChecker = _period },
                handler,
                taskScheduler,
                new JobFunctionConcurrencyGate(),
                JobFunctionRegistryBuilder.Build([], [], []),
                clock,
                ownerIdentity,
                TestActivationBarrier.Opened(),
                NullLogger<JobsFallbackBackgroundService>.Instance
            ),
#pragma warning restore CA2000
            taskScheduler,
            serviceProvider
        );
    }

    private sealed class FallbackServiceHarness(
        JobsFallbackBackgroundService service,
        JobsTaskScheduler taskScheduler,
        ServiceProvider serviceProvider
    ) : IAsyncDisposable
    {
        public JobsFallbackBackgroundService Service { get; } = service;

        public async ValueTask DisposeAsync()
        {
            Service.Dispose();
            await taskScheduler.DisposeAsync().ConfigureAwait(false);
            await serviceProvider.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reports each timer the loop arms, so a test advances the clock only once the loop is parked on its delay.
    /// Advancing first would fire nothing, because the delay would be created against the later time.
    /// </summary>
    private sealed class TimerSignalingTimeProvider : FakeTimeProvider
    {
        private readonly Channel<TimeSpan> _createdTimers = Channel.CreateUnbounded<TimeSpan>();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _createdTimers.Writer.TryWrite(dueTime);

            return timer;
        }

        /// <summary>Waits for the next armed timer and fails when its due time is not the expected one.</summary>
        public async Task WaitForTimerAsync(TimeSpan expected, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var dueTime = await _createdTimers.Reader.ReadAsync(timeout.Token);
            dueTime.Should().Be(expected);
        }
    }
}
