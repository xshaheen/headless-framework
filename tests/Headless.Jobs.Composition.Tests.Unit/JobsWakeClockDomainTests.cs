// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Internal;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Managers;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// One clock domain for the wake/restart path (#818). Every due instant the scheduler arbitrates is a STORE instant;
/// the calling node's clock only measures how long to sleep, and it enters at exactly one place.
/// </summary>
/// <remarks>
/// The defect these pin: a store-derived duration folded into a node-domain deadline. On a node whose clock lags the
/// store by an hour, a 12:30 wake was recorded as 11:30, so a job enqueued for 12:05 looked LATER than the planned
/// wake and did not interrupt the sleep — it ran late or fell into misfire recovery instead. Fixing only the duration
/// (U6) or only the seeding (U5) leaves the mismatch reachable, which is why both land together.
/// </remarks>
public sealed class JobsWakeClockDomainTests : TestBase
{
    public sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    public sealed class FakeCronJob : CronJobEntity;

    // The node lags the store by one hour. Every assertion below fails under node-clock arithmetic by exactly that.
    private static readonly DateTime _StoreNow = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _NodeNow = new(2026, 8, 15, 11, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task time_job_wake_is_measured_against_the_store_clock_not_a_lagging_node()
    {
        // R7: the cron projection already carried StoreUtcNow; the time-job peek did not, so its remaining duration
        // was `executionTime - nodeNow`. A node an hour behind therefore slept an hour and thirty seconds for a job
        // the store considered due in thirty.
        var (manager, provider) = _CreateManager();
        await provider.AddTimeJobsAsync([_TimeJob(_StoreNow.AddSeconds(30))], AbortToken);

        var (wake, _) = await manager.GetNextJobs(AbortToken);

        wake.StoreUtcNow.Should().Be(_StoreNow);
        wake.WakeAtStoreUtc.Should().Be(_StoreNow.AddSeconds(30));
        wake.Remaining.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task an_overdue_time_job_wakes_immediately_on_a_lagging_node()
    {
        // The lagging node's own clock puts this job 59 minutes in the FUTURE; the store has it 60 seconds overdue.
        var (manager, provider) = _CreateManager();
        await provider.AddTimeJobsAsync([_TimeJob(_StoreNow.AddSeconds(-1))], AbortToken);

        var (wake, _) = await manager.GetNextJobs(AbortToken);

        wake.Remaining.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task nothing_pending_still_reports_the_store_anchor()
    {
        // The anchor is what keeps the scheduler's node/store offset fresh, so an empty poll must still carry it —
        // otherwise a quiet period silently decays the offset back to "the clocks agree".
        var (manager, _) = _CreateManager();

        var (wake, functions) = await manager.GetNextJobs(AbortToken);

        functions.Should().BeEmpty();
        wake.WakeAtStoreUtc.Should().BeNull();
        wake.Remaining.Should().Be(Timeout.InfiniteTimeSpan);
        wake.StoreUtcNow.Should().Be(_StoreNow);
    }

    [Fact]
    public async Task the_planned_wake_is_recorded_in_the_store_domain()
    {
        // JobsSchedulerBackgroundService used to record `nodeNow + remaining`. With a store-derived remaining that
        // produced a deadline shifted by the node's skew — the value RestartIfNeeded then compared store instants
        // against.
        await using var rig = SchedulerRig.Sleeping(_StoreNow, _StoreNow.AddMinutes(30));

        var planned = await rig.WaitForPlannedWakeAsync();

        planned.Should().Be(_StoreNow.AddMinutes(30));
        planned.Should().NotBe(_NodeNow.AddMinutes(30), "a node-domain deadline is the defect this closes");
    }

    [Fact]
    public async Task a_due_time_earlier_than_the_planned_wake_interrupts_a_skewed_sleep()
    {
        // AE6. Store 12:00, node 11:00, sleeping towards 12:30. A job enqueued for 12:05 is 25 minutes earlier than
        // the planned wake and must interrupt it. Under the old node-domain recording the planned wake was 11:30, so
        // 12:05 looked 35 minutes LATER and the sleep ran on.
        await using var rig = SchedulerRig.Sleeping(_StoreNow, _StoreNow.AddMinutes(30));
        await rig.WaitForPlannedWakeAsync();

        rig.Service.RestartIfNeeded(_StoreNow.AddMinutes(5));

        (await rig.WaitForRepollAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task an_already_due_store_instant_interrupts_a_skewed_sleep()
    {
        // The other arm of the same comparison: "already overdue" is decided against the store's now, not the node's.
        // A store instant of 12:00:01 is in the lagging node's future by ~an hour, so a node-clock comparison would
        // reject it as not-yet-due AND as later than the planned wake.
        await using var rig = SchedulerRig.Sleeping(_StoreNow, _StoreNow.AddMinutes(30));
        await rig.WaitForPlannedWakeAsync();

        rig.Service.RestartIfNeeded(_StoreNow.AddSeconds(1));

        (await rig.WaitForRepollAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task a_due_time_after_the_planned_wake_does_not_interrupt()
    {
        // The restart must stay conditional: waking for work that is not sooner than the planned wake would make
        // every enqueue a full re-selection round trip.
        await using var rig = SchedulerRig.Sleeping(_StoreNow, _StoreNow.AddMinutes(30));
        await rig.WaitForPlannedWakeAsync();

        rig.Service.RestartIfNeeded(_StoreNow.AddMinutes(45));

        (await rig.WaitForRepollAsync()).Should().BeFalse();
    }

    private static (
        InternalJobsManager<FakeTimeJob, FakeCronJob> Manager,
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider
    ) _CreateManager()
    {
        // The in-memory provider IS the store, so its TimeProvider is the store clock; the manager gets a separate,
        // lagging node clock. Without that split there is no skew to observe and every assertion here is vacuous.
        var storeTime = new FakeTimeProvider(new DateTimeOffset(_StoreNow, TimeSpan.Zero));
        var nodeTime = new FakeTimeProvider(new DateTimeOffset(_NodeNow, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(storeTime);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = "node-a" });
        services.AddSingleton(Substitute.For<IJobsHostScheduler>());
        var serviceProvider = services.BuildServiceProvider();

        var provider = new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(serviceProvider);
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            nodeTime,
            Substitute.For<IJobsNotificationHubSender>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            serviceProvider.GetRequiredService<IGuidGenerator>(),
            serviceProvider,
            serviceProvider.GetRequiredService<SchedulerOptionsBuilder>()
        );

        return (manager, provider);
    }

    private static FakeTimeJob _TimeJob(DateTime executionTime) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "wake-domain",
            Status = JobStatus.Idle,
            ExecutionTime = executionTime,
            CreatedAt = new DateTimeOffset(_StoreNow.AddMinutes(-5), TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(_StoreNow.AddMinutes(-5), TimeSpan.Zero),
            Request = [],
        };

    /// <summary>
    /// A real <see cref="JobsSchedulerBackgroundService"/> running on a lagging node clock against a stubbed manager
    /// that reports a fixed store-domain wake, driven entirely by a <see cref="FakeTimeProvider"/> and completion
    /// sources — no wall-clock waits decide anything here.
    /// </summary>
    private sealed class SchedulerRig : IAsyncDisposable
    {
        // Real-time ceilings only. Nothing is sequenced on them; they exist so a loop that stops making progress fails
        // the test instead of hanging the suite.
        private static readonly TimeSpan _SignalBound = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan _ArmBound = TimeSpan.FromSeconds(2);

        // Virtual window the re-poll pump may cross: wide enough for the restart debounce and the post-restart settle
        // delay, far short of the half-hour sleep the loop is parked on, so a re-poll inside it can only have been
        // caused by the restart.
        private static readonly TimeSpan _RepollVirtualBudget = TimeSpan.FromSeconds(1);

        private readonly FakeTimeProvider _nodeClock;
        private readonly ObservedNodeClock _serviceClock;
        private readonly JobsTaskScheduler _taskScheduler;
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource<DateTime?> _firstPlannedWake = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _secondPoll = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _pollCount;
        private Task? _loop;

        public required JobsSchedulerBackgroundService Service { get; init; }

        public static SchedulerRig Sleeping(DateTime storeNow, DateTime wakeAtStoreUtc)
        {
            var nodeClock = new FakeTimeProvider(new DateTimeOffset(_NodeNow, TimeSpan.Zero));
            var serviceClock = new ObservedNodeClock(nodeClock);
            var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: nodeClock);
            var manager = Substitute.For<IInternalJobManager>();
            var executionContext = new JobsExecutionContext();

            var rig = new SchedulerRig(nodeClock, serviceClock, taskScheduler)
            {
                Service = _BuildService(nodeClock, serviceClock, taskScheduler, manager, executionContext),
            };

            executionContext.NotifyCoreAction = (value, type) =>
            {
                if (type == CoreNotifyActionType.NotifyNextOccurence)
                {
                    rig._firstPlannedWake.TrySetResult((DateTime?)value);
                }
            };

            manager
                .GetNextJobs(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    if (Interlocked.Increment(ref rig._pollCount) > 1)
                    {
                        rig._secondPoll.TrySetResult();
                    }

                    return Task.FromResult(
                        (new JobsWakeSchedule(storeNow, wakeAtStoreUtc), Array.Empty<JobExecutionState>())
                    );
                });
            manager
                .ReleaseAcquiredResources(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            rig._loop = rig.Service.StartAsync(rig._stop.Token);

            return rig;
        }

        private SchedulerRig(
            FakeTimeProvider nodeClock,
            ObservedNodeClock serviceClock,
            JobsTaskScheduler taskScheduler
        )
        {
            _nodeClock = nodeClock;
            _serviceClock = serviceClock;
            _taskScheduler = taskScheduler;
        }

        public async Task<DateTime?> WaitForPlannedWakeAsync()
        {
            var planned = await _firstPlannedWake.Task.WaitAsync(_SignalBound, AbortToken);

            // The loop records the planned wake and only THEN parks on it. Returning on the notification alone hands
            // the caller a loop that may not have armed anything yet, and a restart delivered into that gap would
            // interrupt no sleep at all — the assertions would still pass while proving nothing.
            await _serviceClock.WaitForAnyArmAsync(_ArmBound, AbortToken);

            return planned;
        }

        /// <summary>
        /// Reports whether the loop actually re-polled, driving virtual time only to timers the scheduler has already
        /// armed.
        /// </summary>
        /// <remarks>
        /// This used to advance the node clock a fixed number of fixed-size steps and then stop. Virtual time moves
        /// only when this rig moves it, and the loop arms its post-restart settle delay on a continuation, so the burst
        /// regularly finished BEFORE that timer existed — leaving the loop parked on a clock that never ticked again.
        /// The test therefore failed when run alone and only "passed" when unrelated suite load slowed the burst enough
        /// for the continuation to land inside it. Waiting for each arm before advancing past it removes the race.
        /// </remarks>
        public async Task<bool> WaitForRepollAsync()
        {
            var budgetEnd = _serviceClock.GetUtcNow() + _RepollVirtualBudget;

            while (!_secondPoll.Task.IsCompleted)
            {
                // Captured before the inspection below, so an arm that lands between the two is never missed.
                var armed = _serviceClock.NextArmAsync();
                var dueAt = _serviceClock.NextDueAt(budgetEnd);

                if (dueAt is not null)
                {
                    _nodeClock.SetUtcNow(dueAt.Value);
                    continue;
                }

                // Nothing left to fire inside the budget: the loop is either between two awaits and about to arm its
                // next timer, or it declined the restart and is still parked on its half-hour sleep.
                try
                {
                    await armed.WaitAsync(_ArmBound, AbortToken);
                }
                catch (TimeoutException)
                {
                    break;
                }
            }

            return _secondPoll.Task.IsCompleted;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();

            try
            {
                await Service
                    .StopAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Shutdown raced the cancellation; the assertions already ran.
            }
            catch (TimeoutException)
            {
                // Same.
            }

            if (_loop is not null)
            {
                try
                {
                    await _loop.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
                catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
                {
                    // Same.
                }
            }

            Service.Dispose();
            await _taskScheduler.DisposeAsync();
            _stop.Dispose();
        }

        private static JobsSchedulerBackgroundService _BuildService(
            TimeProvider nodeClock,
            TimeProvider serviceClock,
            JobsTaskScheduler taskScheduler,
            IInternalJobManager manager,
            JobsExecutionContext executionContext
        )
        {
            var services = new ServiceCollection();
            services.AddSingleton(manager);
            var serviceProvider = services.BuildServiceProvider();
            var registry = JobFunctionRegistryBuilder.Build([], [], []);

            var handler = new JobsExecutionTaskHandler(
                serviceProvider,
                nodeClock,
                Substitute.For<IJobsInstrumentation>(),
                manager,
                registry,
                new JobsExecutionCancellationRegistry(),
                new SchedulerOptionsBuilder(),
                NullLogger<JobsExecutionTaskHandler>.Instance
            );

            var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
            ownerIdentity.MembershipLostToken.Returns(CancellationToken.None);

            // Only the service sees the observed view of the node clock: the task scheduler's idle backoff arms timers
            // of its own, and mixing those into the observation would let the pump below advance on a timer the
            // scheduler loop never armed.
            return new JobsSchedulerBackgroundService(
                executionContext,
                registry,
                handler,
                taskScheduler,
                manager,
                new JobFunctionConcurrencyGate(),
                serviceClock,
                ownerIdentity,
                new SchedulerOptionsBuilder(),
                TestActivationBarrier.Opened(),
                NullLogger<JobsSchedulerBackgroundService>.Instance
            );
        }

        /// <summary>
        /// The node clock as the scheduler service sees it: a pass-through over the rig's <see cref="FakeTimeProvider"/>
        /// that records which timers the loop has armed. Virtual time moves only when the rig moves it, so the rig has
        /// to know what the loop is actually waiting on before it can move it soundly.
        /// </summary>
        private sealed class ObservedNodeClock(FakeTimeProvider inner) : TimeProvider
        {
            private readonly Lock _gate = new();
            private readonly List<ObservedTimer> _live = [];
            private TaskCompletionSource _armed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _armCount;

            public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

            public override long GetTimestamp() => inner.GetTimestamp();

            public override long TimestampFrequency => inner.TimestampFrequency;

            public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                var timer = new ObservedTimer(this, inner.CreateTimer(callback, state, dueTime, period));

                lock (_gate)
                {
                    _live.Add(timer);
                }

                Arm(timer, dueTime);

                return timer;
            }

            /// <summary>
            /// The earliest instant a live timer is due at, ignoring one already fired and one due past
            /// <paramref name="notAfter"/>. Null means there is nothing inside the window left to fire.
            /// </summary>
            public DateTimeOffset? NextDueAt(DateTimeOffset notAfter)
            {
                var now = inner.GetUtcNow();
                DateTimeOffset? earliest = null;

                lock (_gate)
                {
                    foreach (var timer in _live)
                    {
                        var dueAt = timer.DueAt;

                        if (dueAt is null || dueAt <= now || dueAt > notAfter)
                        {
                            continue;
                        }

                        if (earliest is null || dueAt < earliest)
                        {
                            earliest = dueAt;
                        }
                    }
                }

                return earliest;
            }

            /// <summary>Completes on the next arm after this call.</summary>
            public Task NextArmAsync()
            {
                lock (_gate)
                {
                    return _armed.Task;
                }
            }

            /// <summary>Waits until the service has armed at least one timer, returning immediately if it already has.</summary>
            /// <exception cref="TimeoutException">The service armed nothing within <paramref name="timeout"/>.</exception>
            public async Task WaitForAnyArmAsync(TimeSpan timeout, CancellationToken cancellationToken)
            {
                while (true)
                {
                    var armed = NextArmAsync();

                    lock (_gate)
                    {
                        if (_armCount != 0)
                        {
                            return;
                        }
                    }

                    await armed.WaitAsync(timeout, cancellationToken);
                }
            }

            internal void Arm(ObservedTimer timer, TimeSpan dueTime)
            {
                TaskCompletionSource previous;

                lock (_gate)
                {
                    timer.DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : inner.GetUtcNow() + dueTime;
                    _armCount++;
                    previous = _armed;
                    _armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                previous.TrySetResult();
            }

            internal void Forget(ObservedTimer timer)
            {
                lock (_gate)
                {
                    _live.Remove(timer);
                }
            }
        }

        private sealed class ObservedTimer(ObservedNodeClock owner, ITimer inner) : ITimer
        {
            /// <summary>When this timer next fires, or null when it is not scheduled. Guarded by the owner's lock.</summary>
            public DateTimeOffset? DueAt { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                var changed = inner.Change(dueTime, period);
                owner.Arm(this, dueTime);

                return changed;
            }

            public void Dispose()
            {
                owner.Forget(this);
                inner.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                owner.Forget(this);

                return inner.DisposeAsync();
            }
        }
    }
}
