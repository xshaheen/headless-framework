// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

/// <summary>
/// Shutdown must survive disposal racing it. A test host (WebApplicationFactory) starts the application's real
/// entry point, and disposing the factory fires ApplicationStopping, which releases that entry point's
/// <c>RunAsync</c> into its own concurrent host stop-then-dispose while the factory's own dispose sequence runs the
/// same host. The Generic Host does not serialize those sequences: whichever finishes first disposes the service
/// provider — and with it the JobsTaskScheduler's execution lifetime — while the other is still draining in-flight
/// jobs inside StopAsync. Every member here asserts that the losing racer completes without throwing instead of
/// surfacing ObjectDisposedException out of a hosted-service stop.
/// </summary>
public sealed class JobsSchedulerShutdownDisposeRaceTests : TestBase
{
    private static readonly TimeSpan _waitBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task stop_completes_when_the_scheduler_is_disposed_mid_drain()
    {
        // Dispose-before-StopAsync while a job holds the drain open: StopAsync parks on
        // WaitForRunningTasksAsync, provider disposal disposes the scheduler's shutdown source, and the
        // post-drain cooperative cancel then threw ObjectDisposedException from the disposed source.
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        using var service = _Service(taskScheduler);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await taskScheduler.QueueAsync(
            async _ =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
            },
            JobPriority.Normal,
            AbortToken
        );
        await started.Task.WaitAsync(_waitBudget, AbortToken);

        var stop = service.StopAsync(CancellationToken.None);

        // The drain is observably open; disposing here is what a concurrent host dispose does once it wins the race.
        await Task.Delay(200, AbortToken);
        stop.IsCompleted.Should().BeFalse("the drain must still be waiting on the in-flight job");
        await taskScheduler.DisposeAsync();

        release.TrySetResult();
        var act = async () => await stop.WaitAsync(_waitBudget, AbortToken);
        await act.Should().NotThrowAsync("a disposal racing the drain must not turn a graceful stop into a fault");
    }

    [Fact]
    public async Task stop_completes_when_the_scheduler_is_disposed_before_stop()
    {
        // The other interleaving: disposal wins completely before StopAsync runs, so the cooperative cancel and
        // the drain both run against an already-disposed scheduler. StopAsync must still complete gracefully.
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        using var service = _Service(taskScheduler);

        await service.StartAsync(AbortToken);
        await taskScheduler.DisposeAsync();

        var act = async () => await service.StopAsync(CancellationToken.None).WaitAsync(_waitBudget, AbortToken);
        await act.Should().NotThrowAsync("stopping after disposal must be a no-op, not a fault");
    }

    [Fact]
    public async Task concurrent_host_stop_and_dispose_do_not_fault_the_scheduler_stop()
    {
        // The host-level shape of the same race: a stop that is still draining when the provider is disposed.
        // WebApplicationFactory's dispose and the entry point's RunAsync continuation produce exactly these two
        // concurrent sequences over one host; neither the hosted-service stop nor the dispose may throw.
        var manager = _IdleManager();

        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));
                services.AddHeadlessJobs();
                services.AddHeadlessSeedDuringDeploy();
                services.AddSingleton(manager);
            })
            .Build();
        await host.StartAsync(AbortToken);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "dispose-race-drain",
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            RunCondition = RunCondition.OnSuccess,
            CachedDelegate = async (_, _, _) =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
            },
        };

        await host.Services.GetRequiredService<IJobsDispatcher>().DispatchAsync([context], AbortToken);
        await started.Task.WaitAsync(_waitBudget, AbortToken);

        var stop = host.StopAsync(CancellationToken.None);
        await Task.Delay(200, AbortToken);
        stop.IsCompleted.Should().BeFalse("the drain must still be waiting on the in-flight job");

        // IHost does not declare IAsyncDisposable; the concrete Host does, and RunAsync's finally disposes it
        // through that interface when it wins the race.
        var dispose = ((IAsyncDisposable)host).DisposeAsync().AsTask();

        release.TrySetResult();

        var act = async () =>
        {
            await stop.WaitAsync(_waitBudget, AbortToken);
            await dispose.WaitAsync(_waitBudget, AbortToken);
        };
        await act.Should().NotThrowAsync("a concurrent dispose must not fault the draining stop sequence");
    }

    private static IInternalJobManager _IdleManager()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .GetNextJobs(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((JobsWakeSchedule.Idle, Array.Empty<JobExecutionState>())));
        manager
            .RunTimedOutTickers(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Array.Empty<JobExecutionState>()));
        manager.ReclaimStalledResources(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        manager
            .RebaseStaleFingerprintsAsync(
                Arg.Any<int>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new CronFingerprintSweepResult
                {
                    Scanned = 0,
                    Rebased = 0,
                    Deferred = 0,
                    LostFence = 0,
                    HasMore = false,
                }
            );
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        return manager;
    }

    private static JobsSchedulerBackgroundService _Service(
        JobsTaskScheduler taskScheduler,
        IInternalJobManager? manager = null
    )
    {
        manager ??= _IdleManager();
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        var serviceProvider = services.BuildServiceProvider();

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

        return new JobsSchedulerBackgroundService(
            new JobsExecutionContext(),
            JobFunctionRegistryBuilder.Build([], [], []),
            handler,
            taskScheduler,
            manager,
            new JobFunctionConcurrencyGate(),
            TimeProvider.System,
            ownerIdentity,
            new SchedulerOptionsBuilder(),
            TestActivationBarrier.Opened(),
            NullLogger<JobsSchedulerBackgroundService>.Instance
        );
    }
}
