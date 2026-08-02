// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    [Fact]
    public async Task generic_host_drains_immediate_work_before_cancelling_its_execution_lifetime()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .GetNextJobs(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((Timeout.InfiniteTimeSpan, Array.Empty<JobExecutionState>())));
        manager
            .RunTimedOutTickers(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Array.Empty<JobExecutionState>()));
        manager.ReclaimStalledResources(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));

        JobStatus? persistedStatus = null;
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                persistedStatus = callInfo.Arg<JobExecutionState>().Status;
                return Task.FromResult(1);
            });

        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));
                services.AddHeadlessJobs();
                services.AddSingleton(manager);
            })
            .Build();
        await host.StartAsync(AbortToken);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionTokenWasCancelled = false;
        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "immediate-drain",
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            RunCondition = RunCondition.OnSuccess,
            CachedDelegate = async (_, _, cancellationToken) =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
                executionTokenWasCancelled = cancellationToken.IsCancellationRequested;
            },
        };

        await host.Services.GetRequiredService<IJobsDispatcher>().DispatchAsync([context], AbortToken);
        await started.Task.WaitAsync(_waitBudget);

        var applicationStopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stoppingRegistration = host
            .Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.Register(() => applicationStopping.TrySetResult());
        var stop = host.StopAsync(AbortToken);
        await applicationStopping.Task.WaitAsync(_waitBudget, AbortToken);

        stop.IsCompleted.Should().BeFalse("the Generic Host must allow immediate work to use its drain budget");
        executionTokenWasCancelled.Should().BeFalse("ApplicationStopping must not cancel immediate work before drain");

        release.TrySetResult();
        await stop.WaitAsync(_waitBudget);

        executionTokenWasCancelled.Should().BeFalse();
        context.Status.Should().Be(JobStatus.Succeeded);
        persistedStatus.Should().Be(JobStatus.Succeeded, "terminal status must be written during graceful drain");
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
