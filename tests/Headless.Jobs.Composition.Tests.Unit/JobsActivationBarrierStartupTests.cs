// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Internal;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

/// <summary>
/// The cron fingerprint activation gate must hold whatever order the host starts its hosted services in. Hosted-service
/// registration order is not a guarantee: <c>HostOptions.ServicesStartConcurrently</c> is a supported Generic Host
/// setting a consuming application can enable, and under it the scheduler can reach its first dispatch selection while
/// the activation drain is still running — dispatching a uninitialized or stale-fingerprint definition under an
/// unverified schedule interpretation, which is exactly what the gate exists to prevent.
/// </summary>
public sealed class JobsActivationBarrierStartupTests : TestBase
{
    [Fact]
    public async Task should_not_select_any_work_until_activation_completes_when_the_loops_start_before_the_initializer()
    {
        // Worst-case ordering, and fully deterministic: both loops are started BEFORE the initializer runs at all.
        // BackgroundService.StartAsync returns only once ExecuteAsync has reached its first await — which is now the
        // activation barrier — so the assertions below need no polling, no delays, and no host-internal timing.
        var probe = new ManagerProbe();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs();
        services.AddSingleton(probe.Manager);

        await using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<JobsSchedulerBackgroundService>();
        var fallback = provider.GetRequiredService<JobsFallbackBackgroundService>();
        var initializer = provider.GetServices<IHostedService>().OfType<JobsInitializationHostedService>().Single();

        await scheduler.StartAsync(AbortToken);
        await fallback.StartAsync(AbortToken);

        // Both loops are parked on the barrier: nothing has been selected, claimed, or reclaimed.
        await probe.Manager.DidNotReceive().GetNextJobs(Arg.Any<CancellationToken>());
        await probe.Manager.DidNotReceive().RunTimedOutTickers(Arg.Any<CancellationToken>());
        await probe.Manager.DidNotReceive().ReclaimStalledResources(Arg.Any<CancellationToken>());

        // The initializer's start leaves the barrier closed: the drain that opens it runs after the host start.
        await initializer.StartAsync(AbortToken);
        provider.GetRequiredService<JobsActivationBarrier>().IsActivated.Should().BeFalse();
        await probe.Manager.DidNotReceive().GetNextJobs(Arg.Any<CancellationToken>());

        probe.ReleaseDrain();
        await _Warmup(provider).WarmUpAsync(AbortToken);

        // The barrier is open, so the scheduler resumes — and its first selection observed a completed drain.
        (await probe.FirstSelection.Task.WaitAsync(AbortToken))
            .Should()
            .BeTrue();
        probe.SelectionsBeforeActivation.Should().Be(0);

        await scheduler.StopAsync(AbortToken);
        await fallback.StopAsync(AbortToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_return_from_the_host_start_before_the_drain_and_select_nothing_until_it_completes(
        bool startConcurrently
    )
    {
        var probe = new ManagerProbe();
        using var host = _BuildHost(probe, startConcurrently);
        var barrier = host.Services.GetRequiredService<JobsActivationBarrier>();

        var startTask = host.StartAsync(AbortToken);

        // The drain runs after the start and is still blocked, yet the start has returned: it no longer waits on a
        // store round trip per page and per stale row.
        await probe.DrainEntered.Task.WaitAsync(AbortToken);
        await startTask.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
        barrier.IsActivated.Should().BeFalse();
        probe.SelectionsBeforeActivation.Should().Be(0);

        probe.ReleaseDrain();

        (await probe.FirstSelection.Task.WaitAsync(AbortToken)).Should().BeTrue();
        probe.SelectionsBeforeActivation.Should().Be(0);
        barrier.IsActivated.Should().BeTrue();

        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_keep_the_barrier_closed_and_retry_a_failed_drain_without_stopping_the_host()
    {
        var probe = new ManagerProbe(failedDrains: 1);
        probe.ReleaseDrain();
        using var host = _BuildHost(probe, startConcurrently: false);
        var barrier = host.Services.GetRequiredService<JobsActivationBarrier>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        // A store outage during the drain no longer aborts the start.
        await host.StartAsync(AbortToken);
        await probe.FirstDrainFailed.Task.WaitAsync(AbortToken);

        // Failed closed: the barrier stays shut, nothing was selected, and the host keeps running.
        barrier.IsActivated.Should().BeFalse();
        probe.SelectionsBeforeActivation.Should().Be(0);
        lifetime.ApplicationStopping.IsCancellationRequested.Should().BeFalse();

        // The warm-up runner retries the drain; once it succeeds the barrier opens and the scheduler selects.
        (await probe.FirstSelection.Task.WaitAsync(AbortToken))
            .Should()
            .BeTrue();
        probe.DrainAttempts.Should().Be(2);
        probe.SelectionsBeforeActivation.Should().Be(0);
        barrier.IsActivated.Should().BeTrue();

        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_honor_manual_start_mode_when_the_scheduler_starts_before_the_initializer()
    {
        // Manual start mode used to be established by the initializer pushing SkipFirstRun onto the scheduler before
        // the scheduler's StartAsync read it. In this reverse order — reachable under ServicesStartConcurrently — the
        // push lands too late, so the loop started anyway and, once the barrier opened, dispatched.
        var probe = new ManagerProbe();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options =>
            options.ConfigureScheduler(scheduler => scheduler.StartMode = JobsStartMode.Manual)
        );
        services.AddSingleton(probe.Manager);

        await using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<JobsSchedulerBackgroundService>();
        var fallback = provider.GetRequiredService<JobsFallbackBackgroundService>();
        var initializer = provider.GetServices<IHostedService>().OfType<JobsInitializationHostedService>().Single();

        await scheduler.StartAsync(AbortToken);
        await fallback.StartAsync(AbortToken);

        // Manual mode: the loop was never started, so there is no dispatch selection to race with.
        scheduler.IsRunning.Should().BeFalse();

        probe.ReleaseDrain();
        await initializer.StartAsync(AbortToken);
        await _Warmup(provider).WarmUpAsync(AbortToken);

        // The barrier is now open — which under manual mode must still dispatch nothing. The fallback loop IS running
        // and past its barrier wait; it stays idle because manual mode leaves the shared task pool frozen.
        scheduler.IsRunning.Should().BeFalse();
        await probe.Manager.DidNotReceive().GetNextJobs(Arg.Any<CancellationToken>());
        await probe.Manager.DidNotReceive().RunTimedOutTickers(Arg.Any<CancellationToken>());
        await probe.Manager.DidNotReceive().ReclaimStalledResources(Arg.Any<CancellationToken>());

        // The explicit manual trigger — and only it — starts dispatch.
        await scheduler.StartAsync(AbortToken);

        scheduler.IsRunning.Should().BeTrue();
        (await probe.FirstSelection.Task.WaitAsync(AbortToken)).Should().BeTrue();

        await scheduler.StopAsync(AbortToken);
        await fallback.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_honor_manual_start_mode_when_the_initializer_starts_first()
    {
        // The same guarantee in registration order, protecting the scheduler's self-owned start-mode gate.
        var probe = new ManagerProbe();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options =>
            options.ConfigureScheduler(scheduler => scheduler.StartMode = JobsStartMode.Manual)
        );
        services.AddSingleton(probe.Manager);

        await using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<JobsSchedulerBackgroundService>();
        var initializer = provider.GetServices<IHostedService>().OfType<JobsInitializationHostedService>().Single();

        probe.ReleaseDrain();
        await initializer.StartAsync(AbortToken);
        await _Warmup(provider).WarmUpAsync(AbortToken);
        await scheduler.StartAsync(AbortToken);

        scheduler.IsRunning.Should().BeFalse();
        await probe.Manager.DidNotReceive().GetNextJobs(Arg.Any<CancellationToken>());

        await scheduler.StartAsync(AbortToken);

        scheduler.IsRunning.Should().BeTrue();
        (await probe.FirstSelection.Task.WaitAsync(AbortToken)).Should().BeTrue();

        await scheduler.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_report_membership_loss_that_happens_while_the_scheduler_waits_for_activation()
    {
        // Under MembershipLostBehavior.StopMembershipOnly the process keeps running with the loop permanently stopped,
        // so this exit must never be silent just because it happened before activation rather than inside the loop.
        using var membershipLostCts = new CancellationTokenSource();
        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(membershipLostCts.Token);
        var manager = Substitute.For<IInternalJobManager>();
        var logger = new CapturingLogger<JobsSchedulerBackgroundService>();

        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);

        using var service = new JobsSchedulerBackgroundService(
            new JobsExecutionContext(),
            JobFunctionRegistryBuilder.Build([], [], []),
            _Handler(serviceProvider, manager),
            taskScheduler,
            manager,
            new JobFunctionConcurrencyGate(),
            TimeProvider.System,
            ownerIdentity,
            new SchedulerOptionsBuilder(),
            // Never opened: the loop is still parked on activation when membership is lost.
            new JobsActivationBarrier(),
            logger
        );

        await service.StartAsync(AbortToken);
        await membershipLostCts.CancelAsync();
        await service.ExecuteTask!.WaitAsync(AbortToken);

        logger.Entries.Should().ContainSingle(entry => entry.EventId == 3220 && entry.Level == LogLevel.Warning);
        await manager.DidNotReceive().GetNextJobs(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_report_membership_loss_that_happens_while_the_fallback_waits_for_activation()
    {
        using var membershipLostCts = new CancellationTokenSource();
        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(membershipLostCts.Token);
        var manager = Substitute.For<IInternalJobManager>();
        var logger = new CapturingLogger<JobsFallbackBackgroundService>();

        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);

        using var service = new JobsFallbackBackgroundService(
            manager,
            new SchedulerOptionsBuilder(),
            _Handler(serviceProvider, manager),
            taskScheduler,
            new JobFunctionConcurrencyGate(),
            JobFunctionRegistryBuilder.Build([], [], []),
            TimeProvider.System,
            ownerIdentity,
            new JobsActivationBarrier(),
            logger
        );

        await service.StartAsync(AbortToken);
        await membershipLostCts.CancelAsync();
        await service.ExecuteTask!.WaitAsync(AbortToken);

        logger.Entries.Should().ContainSingle(entry => entry.EventId == 3201 && entry.Level == LogLevel.Warning);
        await manager.DidNotReceive().ReclaimStalledResources(Arg.Any<CancellationToken>());
    }

    private static JobsActivationWarmup _Warmup(IServiceProvider provider)
    {
        return provider.GetServices<IWarmup>().OfType<JobsActivationWarmup>().Single();
    }

    private static IHost _BuildHost(ManagerProbe probe, bool startConcurrently)
    {
        return new HostBuilder()
            .ConfigureServices(services =>
            {
                services.Configure<HostOptions>(options => options.ServicesStartConcurrently = startConcurrently);
                // Keeps the test from installing the process-wide Ctrl+C / ProcessExit handlers ConsoleLifetime adds.
                services.AddSingleton<IHostLifetime, NoopHostLifetime>();
                services.AddHeadlessJobs();
                services.AddSingleton(probe.Manager);
            })
            .Build();
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

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, int EventId)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
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
            Entries.Add((logLevel, eventId.Id));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }

    /// <summary>
    /// A manager stub whose fingerprint drain blocks until released, recording whether any selecting or claiming call
    /// arrived while the drain was still in flight.
    /// </summary>
    private sealed class ManagerProbe
    {
        private readonly TaskCompletionSource _releaseDrain = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _failedDrainsLeft;
        private int _drainAttempts;
        private int _drainCompleted;
        private int _selectionsBeforeActivation;

        /// <summary>Completes when the first drain attempt has failed.</summary>
        public TaskCompletionSource FirstDrainFailed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DrainAttempts => Volatile.Read(ref _drainAttempts);

        public IInternalJobManager Manager { get; }

        /// <summary>Completes when the activation drain has been entered.</summary>
        public TaskCompletionSource DrainEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Carries whether the drain had already completed when the first dispatch selection happened.</summary>
        public TaskCompletionSource<bool> FirstSelection { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SelectionsBeforeActivation => Volatile.Read(ref _selectionsBeforeActivation);

        public ManagerProbe(int failedDrains = 0)
        {
            _failedDrainsLeft = failedDrains;
            var manager = Substitute.For<IInternalJobManager>();

            manager
                .RebaseStaleFingerprintsAsync(
                    Arg.Any<int>(),
                    Arg.Any<Guid?>(),
                    Arg.Any<Guid?>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns<Task<CronFingerprintSweepResult>>(_ => _DrainAsync());

            manager
                .GetNextJobs(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    _RecordSelection();
                    FirstSelection.TrySetResult(Volatile.Read(ref _drainCompleted) == 1);

                    // No wake instant parks the loop for a day instead of spinning through the stub.
                    return (JobsWakeSchedule.Idle, Array.Empty<JobExecutionState>());
                });

            manager
                .RunTimedOutTickers(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    _RecordSelection();

                    return [];
                });

            manager
                .ReclaimStalledResources(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    _RecordSelection();

                    return 0;
                });

            Manager = manager;
        }

        public void ReleaseDrain()
        {
            _releaseDrain.TrySetResult();
        }

        private async Task<CronFingerprintSweepResult> _DrainAsync()
        {
            Interlocked.Increment(ref _drainAttempts);
            DrainEntered.TrySetResult();
            await _releaseDrain.Task.ConfigureAwait(false);

            if (Interlocked.Decrement(ref _failedDrainsLeft) >= 0)
            {
                FirstDrainFailed.TrySetResult();

                throw new InvalidOperationException("store unavailable");
            }

            Volatile.Write(ref _drainCompleted, 1);

            return new CronFingerprintSweepResult
            {
                Scanned = 0,
                Rebased = 0,
                Deferred = 0,
                LostFence = 0,
                HasMore = false,
            };
        }

        private void _RecordSelection()
        {
            if (Volatile.Read(ref _drainCompleted) == 0)
            {
                Interlocked.Increment(ref _selectionsBeforeActivation);
            }
        }
    }

    private sealed class NoopHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
