// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class JobExecutionTaskHandlerTests : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_preserve_parent_child_order_with_or_without_activity(bool activityEnabled)
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        var instrumentation = Substitute.For<IJobsInstrumentation>();
        var activityNames = new ConcurrentBag<string>();
        instrumentation
            .StartJobActivity(Arg.Do<string>(activityNames.Add), Arg.Any<JobExecutionState>())
            .Returns(_ => activityEnabled ? new Activity("job-test").Start() : null);

        var services = new ServiceCollection();
        services.AddSingleton(manager);
        services.AddSingleton(instrumentation);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            instrumentation,
            manager,
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance
        );

        var parentStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inProgressStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parentCompleted = false;
        var deferredObservedParentCompletion = false;
        var parent = _Job(
            "Parent",
            RunCondition.InProgress,
            async (_, _, _) =>
            {
                parentStarted.TrySetResult();
                await inProgressStarted.Task.ConfigureAwait(false);
                parentCompleted = true;
            }
        );
        parent.TimeJobChildren.Add(
            _Job(
                "Concurrent",
                RunCondition.InProgress,
                async (_, _, _) =>
                {
                    await parentStarted.Task.ConfigureAwait(false);
                    inProgressStarted.TrySetResult();
                }
            )
        );
        parent.TimeJobChildren.Add(
            _Job(
                "Deferred",
                RunCondition.OnSuccess,
                (_, _, _) =>
                {
                    deferredObservedParentCompletion = parentCompleted;
                    return Task.CompletedTask;
                }
            )
        );

        await handler.ExecuteTaskAsync(parent, isDue: false, cancellationToken: AbortToken);

        parentCompleted.Should().BeTrue();
        deferredObservedParentCompletion.Should().BeTrue();
        activityNames.Should().OnlyContain(name => name == "job.execute.timejob");
    }

    private static JobExecutionState _Job(string functionName, RunCondition runCondition, JobFunctionDelegate function)
    {
        return new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = functionName,
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            RunCondition = runCondition,
            CachedDelegate = function,
        };
    }
}
