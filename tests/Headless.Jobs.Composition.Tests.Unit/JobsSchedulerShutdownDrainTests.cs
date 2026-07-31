// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class JobsSchedulerShutdownDrainTests : TestBase
{
    private static readonly TimeSpan _waitBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task stop_waits_for_in_flight_work_to_complete_within_the_shutdown_budget()
    {
        // A clean stop must drain: previously StopAsync froze the pool and cancelled every in-flight job with no
        // terminal write, so a routine deploy was indistinguishable from node death.
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        using var service = _Service(taskScheduler);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;
        await taskScheduler.QueueAsync(
            async _ =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
                completed = true;
            },
            JobPriority.Normal,
            AbortToken
        );
        await started.Task.WaitAsync(_waitBudget);

        var stop = service.StopAsync(CancellationToken.None);

        // The drain must be observably waiting on the in-flight job, not returning early.
        await Task.Delay(200, AbortToken);
        stop.IsCompleted.Should().BeFalse("stop must wait for in-flight work, not freeze-and-abandon it");

        release.TrySetResult();
        await stop.WaitAsync(_waitBudget);

        completed.Should().BeTrue("the drained job ran to completion instead of being cancelled");
    }

    [Fact]
    public async Task stop_returns_after_the_shutdown_budget_when_work_does_not_finish()
    {
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
        await started.Task.WaitAsync(_waitBudget);

        using var shutdownBudget = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await service.StopAsync(shutdownBudget.Token).WaitAsync(_waitBudget);

        release.TrySetResult();
    }

    private static JobsSchedulerBackgroundService _Service(JobsTaskScheduler taskScheduler)
    {
        var manager = Substitute.For<IInternalJobManager>();
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
            NullLogger<JobsExecutionTaskHandler>.Instance
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
            NullLogger<JobsSchedulerBackgroundService>.Instance
        );
    }
}
