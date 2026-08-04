// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Dispatcher;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class JobsDispatcherTests : TestBase
{
    private static readonly TimeSpan _waitBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task dispatched_job_keeps_running_when_the_enqueuers_token_is_cancelled_after_dispatch()
    {
        // The enqueuing caller's token (typically an HTTP request lifetime) governs only pool admission. By
        // dispatch time the row is already durably InProgress with a lease, so the running job is owned by the
        // host lifetime: cancelling and disposing the caller's CTS after DispatchAsync returns must neither skip
        // the delegate nor cancel it mid-run — the row would otherwise sit out its whole lease and be resolved
        // by OnNodeDeath as if the node had died.
        var manager = _HealthyManager();
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = _Handler(serviceProvider, manager);
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 2, timeProvider: TimeProvider.System);
        var dispatcher = new JobsDispatcher(taskScheduler, handler, new JobFunctionConcurrencyGate());

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

        var dispatcher = new JobsDispatcher(taskScheduler, handler, new JobFunctionConcurrencyGate());

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
        return manager;
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
