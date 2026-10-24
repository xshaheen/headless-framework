// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Coordination;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Coordination;

/// <summary>
/// The Jobs drain participant holds the node in coordination membership until the scheduler has drained, so a peer
/// never reclaims a row the stopping node is still settling.
/// </summary>
public sealed class JobsDrainParticipantTests : TestBase
{
    private static readonly TimeSpan _WaitBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task should_complete_at_once_when_the_scheduler_never_started()
    {
        // given
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        using var scheduler = _Scheduler(taskScheduler);
        var sut = new JobsDrainParticipant(scheduler);

        // when
        var wait = sut.WaitUntilDrainedAsync(AbortToken);

        // then
        wait.IsCompletedSuccessfully.Should().BeTrue();
        await wait;
    }

    [Fact]
    public async Task should_complete_only_after_the_scheduler_stop_drained_running_work()
    {
        // given: a started scheduler with one job still running
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        using var scheduler = _Scheduler(taskScheduler);
        var sut = new JobsDrainParticipant(scheduler);
        await scheduler.StartAsync(AbortToken);

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
        await started.Task.WaitAsync(_WaitBudget, AbortToken);

        // when
        var wait = sut.WaitUntilDrainedAsync(AbortToken);
        var stop = scheduler.StopAsync(CancellationToken.None);

        // then: the job holds the stop, so the participant still holds the node
        wait.IsCompleted.Should().BeFalse();
        stop.IsCompleted.Should().BeFalse();

        release.TrySetResult();
        await stop.WaitAsync(_WaitBudget, AbortToken);
        await wait.WaitAsync(_WaitBudget, AbortToken);
    }

    [Fact]
    public async Task should_stop_waiting_when_the_host_stop_token_fires()
    {
        // given
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        using var scheduler = _Scheduler(taskScheduler);
        var sut = new JobsDrainParticipant(scheduler);
        await scheduler.StartAsync(AbortToken);
        using var stopToken = new CancellationTokenSource();

        // when
        var wait = sut.WaitUntilDrainedAsync(stopToken.Token);
        await stopToken.CancelAsync();

        // then
        await FluentActions.Awaiting(() => wait).Should().ThrowAsync<OperationCanceledException>();
        await scheduler.StopAsync(CancellationToken.None).WaitAsync(_WaitBudget, AbortToken);
    }

    [Fact]
    public void should_register_the_participant_only_on_a_host_that_claims_work()
    {
        // given
        var claiming = new ServiceCollection();
        claiming.AddSingleton<INodeMembership>(Substitute.For<INodeMembership>());
        var notClaiming = new ServiceCollection();
        notClaiming.AddSingleton<INodeMembership>(Substitute.For<INodeMembership>());

        // when
        claiming.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(o => o.UseEntityFramework());
        notClaiming.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(o =>
        {
            o.DisableBackgroundServices();
            o.UseEntityFramework();
        });

        // then
        claiming
            .Where(static d => d.ServiceType == typeof(INodeDrainParticipant))
            .Should()
            .ContainSingle()
            .Which.ImplementationType.Should()
            .Be<JobsDrainParticipant>();
        notClaiming.Should().NotContain(static d => d.ServiceType == typeof(INodeDrainParticipant));
    }

    private static JobsSchedulerBackgroundService _Scheduler(JobsTaskScheduler taskScheduler)
    {
        // The loop idles: these tests drive the drain through work queued straight into the pool.
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .GetNextJobs(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((JobsWakeSchedule.Idle, Array.Empty<JobExecutionState>())));
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        var serviceProvider = services.BuildServiceProvider();
        var registry = JobFunctionRegistryBuilder.Build([], [], []);

        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            registry,
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance,
            new JobsFunctionTagBound(registry)
        );

        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(CancellationToken.None);

        return new JobsSchedulerBackgroundService(
            new JobsExecutionContext(),
            registry,
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
