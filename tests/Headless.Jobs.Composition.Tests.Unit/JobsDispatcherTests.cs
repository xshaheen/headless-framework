// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Dispatcher;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class JobsDispatcherTests : TestBase
{
    private static readonly TimeSpan _waitBudget = HeadlessTestTaskExtensions.DefaultTimeout;

    [Fact]
    public async Task dispatched_job_keeps_running_when_the_enqueuers_token_is_cancelled_after_dispatch()
    {
        // The enqueuing caller's token (typically an HTTP request lifetime) governs only pool admission. By
        // dispatch time the row is already durably Queued with a lease, so the job is owned by the
        // host lifetime: cancelling and disposing the caller's CTS after DispatchAsync returns must neither skip
        // the delegate nor cancel it mid-run — the row would otherwise sit out its whole lease and be resolved
        // by OnNodeDeath as if the node had died.
        var manager = _HealthyManager();
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                persisted.TrySetResult();
                return Task.FromResult(1);
            });
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = _Handler(serviceProvider, manager);
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 2, timeProvider: TimeProvider.System);
        var dispatcher = _Dispatcher(taskScheduler, handler, manager);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = _Job(
            "fn-a",
            async (_, _, ct) =>
            {
                await release.Task.ConfigureAwait(false);
                completed.TrySetResult(ct.IsCancellationRequested);
            }
        );

        var callerCts = new CancellationTokenSource();
        await dispatcher.DispatchAsync([context], callerCts.Token);

        // Cancel AND dispose after dispatch returns — the recycled request-abort source shape.
        await callerCts.CancelAsync();
        callerCts.Dispose();
        release.TrySetResult();

        var observedCancellation = await completed.Task.WaitAsync(_waitBudget, AbortToken);
        observedCancellation
            .Should()
            .BeFalse("a job the store says is running must not follow the enqueuer's lifetime");
        (await taskScheduler.WaitForRunningTasksAsync(_waitBudget))
            .Should()
            .BeTrue("the completion update must finish before it is asserted");
        await manager.Received().UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task dispatched_job_is_cancelled_when_the_scheduler_execution_lifetime_ends()
    {
        // Separating admission from execution must not detach immediate jobs from scheduler shutdown.
        var manager = _HealthyManager();
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = _Handler(serviceProvider, manager);
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 2, timeProvider: TimeProvider.System);

        var dispatcher = _Dispatcher(taskScheduler, handler, manager);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = _Job(
            "fn-b",
            async (_, _, ct) =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Cooperative exit on shutdown.
                }

                observed.TrySetResult(ct.IsCancellationRequested);
            }
        );

        await dispatcher.DispatchAsync([context], AbortToken);
        await started.Task.WaitAsync(_waitBudget, AbortToken);

        await taskScheduler.CancelExecutionsAsync();

        var observedCancellation = await observed.Task.WaitAsync(_waitBudget, AbortToken);
        observedCancellation.Should().BeTrue("scheduler shutdown must still reach immediate-dispatch jobs");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_hand_back_every_claimed_context_when_the_callers_token_cancels_before_admission(
        bool releaseFails
    )
    {
        // An HTTP request enqueues due-now jobs and its client disconnects before the pool admits them. The rows are
        // Queued with a lease. Handing them back lets the scheduler claim them again immediately.
        var manager = _HealthyManager();
        if (releaseFails)
        {
            manager
                .ReleaseAcquiredResources(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromException(new InvalidOperationException("store is down")));
        }

        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = _Handler(serviceProvider, manager);
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var dispatcher = _Dispatcher(taskScheduler, handler, manager);

        var ran = 0;
        var first = _Job(
            "fn-c",
            (_, _, _) =>
            {
                Interlocked.Increment(ref ran);
                return Task.CompletedTask;
            }
        );
        var second = _Job(
            "fn-c",
            (_, _, _) =>
            {
                Interlocked.Increment(ref ran);
                return Task.CompletedTask;
            }
        );
        using var callerCts = new CancellationTokenSource();
        await callerCts.CancelAsync();

        var dispatch = () => dispatcher.DispatchAsync([first, second], callerCts.Token);

        await dispatch.Should().NotThrowAsync("the rows are durably enqueued; only this node will not run them");
        await manager
            .Received(1)
            .ReleaseAcquiredResources(
                Arg.Is<JobExecutionState[]>(claims => claims.Length == 2 && claims[0] == first && claims[1] == second),
                CancellationToken.None
            );
        Volatile.Read(ref ran).Should().Be(0);
    }

    [Fact]
    public async Task should_hand_back_a_long_running_context_when_the_backlog_cap_rejects_it()
    {
        var manager = _HealthyManager();
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = _Handler(serviceProvider, manager);
        await using var taskScheduler = new JobsTaskScheduler(
            maxConcurrency: 1,
            timeProvider: TimeProvider.System,
            maxLongRunningConcurrency: 1
        );
        var dispatcher = _Dispatcher(taskScheduler, handler, manager);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unblock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = _LongRunningJob(
            "fn-long",
            async (_, _, _) =>
            {
                started.TrySetResult();
                await unblock.Task.ConfigureAwait(false);
            }
        );
        await dispatcher.DispatchAsync([running], AbortToken);
        await started.Task.WaitAsync(_waitBudget, AbortToken);

        // One slot, so the backlog holds two parked admissions; the third is over the cap.
        var parkedA = _LongRunningJob("fn-long", (_, _, _) => Task.CompletedTask);
        var parkedB = _LongRunningJob("fn-long", (_, _, _) => Task.CompletedTask);
        var rejected = _LongRunningJob("fn-long", (_, _, _) => Task.CompletedTask);

        try
        {
            await dispatcher.DispatchAsync([parkedA, parkedB, rejected], AbortToken);

            await manager
                .Received(1)
                .ReleaseAcquiredResources(
                    Arg.Is<JobExecutionState[]>(claims => claims.Single() == rejected),
                    CancellationToken.None
                );
        }
        finally
        {
            unblock.TrySetResult();
        }

        (await taskScheduler.WaitForRunningTasksAsync(_waitBudget)).Should().BeTrue();
    }

    [Fact]
    public async Task should_hand_back_a_parked_long_running_context_when_the_callers_token_cancels()
    {
        var manager = _HealthyManager();
        var handedBack = new TaskCompletionSource<JobExecutionState>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        manager
            .ReleaseAcquiredResources(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                handedBack.TrySetResult(call.Arg<JobExecutionState[]>().Single());
                return Task.CompletedTask;
            });
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = _Handler(serviceProvider, manager);
        await using var taskScheduler = new JobsTaskScheduler(
            maxConcurrency: 1,
            timeProvider: TimeProvider.System,
            maxLongRunningConcurrency: 1
        );
        var dispatcher = _Dispatcher(taskScheduler, handler, manager);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unblock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = _LongRunningJob(
            "fn-long",
            async (_, _, _) =>
            {
                started.TrySetResult();
                await unblock.Task.ConfigureAwait(false);
            }
        );
        await dispatcher.DispatchAsync([running], AbortToken);
        await started.Task.WaitAsync(_waitBudget, AbortToken);

        var parkedRan = false;
        var parked = _LongRunningJob(
            "fn-long",
            (_, _, _) =>
            {
                parkedRan = true;
                return Task.CompletedTask;
            }
        );

        try
        {
            using var callerCts = new CancellationTokenSource();
            await dispatcher.DispatchAsync([parked], callerCts.Token);
            await callerCts.CancelAsync();

            var released = await handedBack.Task.WaitAsync(_waitBudget, AbortToken);
            released.Should().BeSameAs(parked);
        }
        finally
        {
            unblock.TrySetResult();
        }

        (await taskScheduler.WaitForRunningTasksAsync(_waitBudget)).Should().BeTrue();
        parkedRan.Should().BeFalse("a dropped admission never reaches its handler");
    }

    private static JobExecutionState _LongRunningJob(string functionName, JobFunctionDelegate function)
    {
        var job = _Job(functionName, function);
        job.CachedPriority = JobPriority.LongRunning;
        return job;
    }

    private static IInternalJobManager _HealthyManager()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        manager
            .SetTickersInProgress(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<JobExecutionState[]>()));
        return manager;
    }

    private static JobsDispatcher _Dispatcher(
        JobsTaskScheduler taskScheduler,
        JobsExecutionTaskHandler taskHandler,
        IInternalJobManager internalJobsManager
    ) =>
        new(
            taskScheduler,
            taskHandler,
            new JobFunctionConcurrencyGate(),
            internalJobsManager,
            NullLogger<JobsDispatcher>.Instance
        );

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

    private static JobExecutionState _Job(string functionName, JobFunctionDelegate function)
    {
        return new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = functionName,
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            RunCondition = RunCondition.OnSuccess,
            CachedDelegate = function,
        };
    }
}
