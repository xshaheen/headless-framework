// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Internal;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

/// <summary>
/// Under <c>MembershipLostBehavior.StopMembershipOnly</c> losing membership stops the jobs loops and nothing else. The
/// Generic Host stops the whole process (<c>BackgroundServiceExceptionBehavior.StopHost</c>) when a background
/// service's <c>ExecuteTask</c> ends faulted or cancelled before the host itself stopped, so each loop must end with
/// <c>ExecuteTask</c> run to completion even when membership is lost while it waits out a restart or an error delay
/// inside one of its <see langword="catch"/> blocks, where its clean-exit handler cannot catch the cancellation.
/// </summary>
public sealed class JobsMembershipLossDuringRetryDelayTests : TestBase
{
    private const int _SchedulerStoppedOnMembershipLossEventId = 3220;
    private const int _FallbackStoppedOnMembershipLossEventId = 3201;

    private static readonly TimeSpan _IdleSleep = TimeSpan.FromDays(1);
    private static readonly TimeSpan _RestartDebounce = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan _RestartDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan _ErrorDelay = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task should_stop_only_the_scheduler_when_membership_is_lost_during_the_restart_delay()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager.GetNextJobs(Arg.Any<CancellationToken>()).Returns(_Idle());
        await using var fixture = new SchedulerFixture(manager);

        await fixture.Service.StartAsync(AbortToken);
        await fixture.RestartIntoRestartArmAsync(AbortToken);
        await fixture.Clock.WaitForTimerAsync(_RestartDelay, AbortToken).Bounded();

        await fixture.MembershipLost.CancelAsync();

        await fixture.AssertStoppedOnMembershipLossOnlyAsync();
    }

    [Fact]
    public async Task should_stop_only_the_scheduler_when_membership_is_lost_while_the_restart_releases_claims()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .GetNextJobs(Arg.Any<CancellationToken>())
            .Returns(_Idle([new JobExecutionState { FunctionName = "membership-probe" }]));

        // The release parks until the test lets it finish, and gives up as soon as its token is cancelled, as a
        // provider's database call would.
        var releaseEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager
            .ReleaseAcquiredResources(Arg.Any<JobExecutionState[]?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                releaseEntered.TrySetResult();

                return releaseGate.Task.WaitAsync(call.Arg<CancellationToken>());
            });
        await using var fixture = new SchedulerFixture(manager);

        await fixture.Service.StartAsync(AbortToken);
        await fixture.RestartIntoRestartArmAsync(AbortToken);
        await releaseEntered.Task.Bounded();

        await fixture.MembershipLost.CancelAsync();
        releaseGate.TrySetResult();

        await fixture.AssertStoppedOnMembershipLossOnlyAsync();
    }

    [Fact]
    public async Task should_stop_only_the_scheduler_when_membership_is_lost_during_the_error_delay()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .GetNextJobs(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<(JobsWakeSchedule, JobExecutionState[])>(
                    new InvalidOperationException("store unavailable")
                )
            );
        await using var fixture = new SchedulerFixture(manager);

        await fixture.Service.StartAsync(AbortToken);
        await fixture.Clock.WaitForTimerAsync(_ErrorDelay, AbortToken).Bounded();

        await fixture.MembershipLost.CancelAsync();

        await fixture.AssertStoppedOnMembershipLossOnlyAsync();
    }

    [Fact]
    public async Task should_stop_only_the_fallback_when_membership_is_lost_during_the_error_delay()
    {
        var fallbackPeriod = TimeSpan.FromSeconds(7);
        var clock = new TimerSignalingTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var membershipLost = new CancellationTokenSource();
        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(membershipLost.Token);
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .ReclaimStalledResources(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<JobReclaimCounts>(new InvalidOperationException("store unavailable")));
        var logger = new CapturingLogger<JobsFallbackBackgroundService>();

        await using var serviceProvider = _ServiceProvider(manager);
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        using var service = new JobsFallbackBackgroundService(
            manager,
            new SchedulerOptionsBuilder { FallbackIntervalChecker = fallbackPeriod },
            _Handler(serviceProvider, manager),
            taskScheduler,
            new JobFunctionConcurrencyGate(),
            JobFunctionRegistryBuilder.Build([], [], []),
            clock,
            ownerIdentity,
            TestActivationBarrier.Opened(),
            logger
        );

        await service.StartAsync(AbortToken);
        await clock.WaitForTimerAsync(fallbackPeriod, AbortToken).Bounded();

        await membershipLost.CancelAsync();

        await _AssertCompletedAsync(service.ExecuteTask);
        logger
            .Entries.Should()
            .ContainSingle(entry =>
                entry.EventId == _FallbackStoppedOnMembershipLossEventId && entry.Level == LogLevel.Warning
            );

        await service.StopAsync(AbortToken);
    }

    private static Task<(JobsWakeSchedule, JobExecutionState[])> _Idle(JobExecutionState[]? functions = null)
    {
        // No next wake: the loop sleeps its one-day idle period until a restart cuts it short.
        return Task.FromResult((JobsWakeSchedule.Idle, functions ?? []));
    }

    private static async Task _AssertCompletedAsync(Task? executeTask)
    {
        executeTask.Should().NotBeNull();

        // Awaited without rethrowing so a cancelled or faulted loop fails on the status assertion below, which names
        // the outcome the host would have reacted to.
        await Task.WhenAny(executeTask!).Bounded();

        executeTask
            .Status.Should()
            .Be(
                TaskStatus.RanToCompletion,
                "a cancelled or faulted ExecuteTask makes the host stop the whole process under StopHost, {0}",
                executeTask.Exception?.InnerException?.ToString() ?? "the loop was cancelled"
            );
    }

    private static ServiceProvider _ServiceProvider(IInternalJobManager manager)
    {
        var services = new ServiceCollection();
        services.AddSingleton(manager);

        return services.BuildServiceProvider();
    }

    private static JobsExecutionTaskHandler _Handler(IServiceProvider serviceProvider, IInternalJobManager manager)
    {
        return new JobsExecutionTaskHandler(
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
    }

    private sealed class SchedulerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly JobsTaskScheduler _taskScheduler;

        public SchedulerFixture(IInternalJobManager manager)
        {
            Manager = manager;
            var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
            ownerIdentity.MembershipLostToken.Returns(MembershipLost.Token);

            _serviceProvider = _ServiceProvider(Manager);
            _taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
            Service = new JobsSchedulerBackgroundService(
                new JobsExecutionContext(),
                JobFunctionRegistryBuilder.Build([], [], []),
                _Handler(_serviceProvider, Manager),
                _taskScheduler,
                Manager,
                new JobFunctionConcurrencyGate(),
                Clock,
                ownerIdentity,
                new SchedulerOptionsBuilder(),
                TestActivationBarrier.Opened(),
                Logger
            );
        }

        public TimerSignalingTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        public CancellationTokenSource MembershipLost { get; } = new();

        public IInternalJobManager Manager { get; }

        public CapturingLogger<JobsSchedulerBackgroundService> Logger { get; } = new();

        public JobsSchedulerBackgroundService Service { get; }

        /// <summary>
        /// Waits for the loop to park in its idle sleep, then requests a restart and lets its debounce fire, which
        /// cancels the sleep and sends the loop into its restart arm.
        /// </summary>
        public async Task RestartIntoRestartArmAsync(CancellationToken cancellationToken)
        {
            await Clock.WaitForTimerAsync(_IdleSleep, cancellationToken).Bounded();
            Service.Restart();
            await Clock.WaitForTimerAsync(_RestartDebounce, cancellationToken).Bounded();
            Clock.Advance(_RestartDebounce);
        }

        public async Task AssertStoppedOnMembershipLossOnlyAsync()
        {
            await _AssertCompletedAsync(Service.ExecuteTask);
            Logger
                .Entries.Should()
                .ContainSingle(entry =>
                    entry.EventId == _SchedulerStoppedOnMembershipLossEventId && entry.Level == LogLevel.Warning
                );
        }

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(CancellationToken.None);
            Service.Dispose();
            await _taskScheduler.DisposeAsync();
            await _serviceProvider.DisposeAsync();
            MembershipLost.Dispose();
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly Lock _lock = new();
        private readonly List<(LogLevel Level, int EventId)> _entries = [];

        public IReadOnlyList<(LogLevel Level, int EventId)> Entries
        {
            get
            {
                lock (_lock)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (_lock)
            {
                _entries.Add((logLevel, eventId.Id));
            }
        }
    }
}
