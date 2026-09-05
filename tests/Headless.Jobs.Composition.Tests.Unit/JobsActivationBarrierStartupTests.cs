// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Internal;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Models;
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
[Collection<JobsHelperCollection>]
public sealed class JobsActivationBarrierStartupTests : TestBase
{
    // AddHeadlessJobs reads the process-wide JobFunctionProvider registry, so these tests need it empty; sibling
    // classes in this collection register functions that would otherwise leak in.
    public JobsActivationBarrierStartupTests()
    {
        JobFunctionProvider.ResetForTests(discoveryComplete: false);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        JobFunctionProvider.ResetForTests();

        return base.DisposeAsyncCore();
    }

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

        probe.ReleaseDrain();
        await initializer.StartAsync(AbortToken);

        // The barrier is open, so the scheduler resumes — and its first selection observed a completed drain.
        (await probe.FirstSelection.Task.WaitAsync(AbortToken))
            .Should()
            .BeTrue();
        probe.SelectionsBeforeActivation.Should().Be(0);

        await scheduler.StopAsync(AbortToken);
        await fallback.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_not_select_any_work_until_the_activation_drain_releases_under_concurrent_service_start()
    {
        var probe = new ManagerProbe();

        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.Configure<HostOptions>(options => options.ServicesStartConcurrently = true);
                // Keeps the test from installing the process-wide Ctrl+C / ProcessExit handlers ConsoleLifetime adds.
                services.AddSingleton<IHostLifetime, NoopHostLifetime>();
                services.AddHeadlessJobs();
                services.AddSingleton(probe.Manager);
            })
            .Build();

        var startTask = host.StartAsync(AbortToken);

        // The initializer is inside the drain and is holding host startup open, while every other hosted service has
        // already been started concurrently alongside it.
        await probe.DrainEntered.Task.WaitAsync(AbortToken);
        startTask.IsCompleted.Should().BeFalse();

        probe.ReleaseDrain();
        await startTask;

        (await probe.FirstSelection.Task.WaitAsync(AbortToken)).Should().BeTrue();
        probe.SelectionsBeforeActivation.Should().Be(0);

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
            NullLogger<JobsExecutionTaskHandler>.Instance
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
        private int _drainCompleted;
        private int _selectionsBeforeActivation;

        public IInternalJobManager Manager { get; }

        /// <summary>Completes when the activation drain has been entered.</summary>
        public TaskCompletionSource DrainEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Carries whether the drain had already completed when the first dispatch selection happened.</summary>
        public TaskCompletionSource<bool> FirstSelection { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SelectionsBeforeActivation => Volatile.Read(ref _selectionsBeforeActivation);

        public ManagerProbe()
        {
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

                    return Array.Empty<JobExecutionState>();
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
            DrainEntered.TrySetResult();
            await _releaseDrain.Task.ConfigureAwait(false);
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
