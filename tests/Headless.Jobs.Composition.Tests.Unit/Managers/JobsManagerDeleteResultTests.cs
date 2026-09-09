// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
using Headless.CommitCoordination;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Managers;

public sealed class JobsManagerDeleteResultTests : TestBase
{
    [Fact]
    public async Task should_return_failed_delete_result_when_provider_throws_database_exception()
    {
        var (manager, provider, scheduler) = _CreateManager();
        var jobId = Guid.NewGuid();
        var failure = new ProviderDbException();
        provider.RemoveTimeJobsAsync(Arg.Any<Guid[]>(), AbortToken).Returns(Task.FromException<int>(failure));

        var result = await manager.DeleteAsync(jobId, AbortToken);

        result.IsSucceeded.Should().BeFalse();
        result.Exception.Should().BeSameAs(failure);
        scheduler.DidNotReceive().Restart();
    }

    [Fact]
    public async Task should_return_affected_rows_and_restart_when_deleted_job_is_executing()
    {
        var jobId = Guid.NewGuid();
        var (manager, provider, scheduler) = _CreateManager(jobId);
        provider.RemoveTimeJobsAsync(Arg.Any<Guid[]>(), AbortToken).Returns(3);

        var result = await manager.DeleteAsync(jobId, AbortToken);

        result.IsSucceeded.Should().BeTrue();
        result.AffectedRows.Should().Be(3);
        scheduler.Received(1).Restart();
    }

    [Fact]
    public async Task should_return_failed_delete_result_when_provider_throws_cancellation()
    {
        var (manager, provider, scheduler) = _CreateManager();
        var failure = new OperationCanceledException(AbortToken);
        provider.RemoveTimeJobsAsync(Arg.Any<Guid[]>(), AbortToken).Returns(Task.FromException<int>(failure));

        var result = await manager.DeleteAsync(Guid.NewGuid(), AbortToken);

        result.IsSucceeded.Should().BeFalse();
        result.Exception.Should().BeSameAs(failure);
        scheduler.DidNotReceive().Restart();
    }

    [Fact]
    public async Task should_propagate_scheduler_restart_failure_after_delete()
    {
        var jobId = Guid.NewGuid();
        var (manager, provider, scheduler) = _CreateManager(jobId);
        var failure = new InvalidOperationException("restart failed");
        provider.RemoveTimeJobsAsync(Arg.Any<Guid[]>(), AbortToken).Returns(3);
        scheduler.When(x => x.Restart()).Do(_ => throw failure);

        var action = async () => await manager.DeleteAsync(jobId, AbortToken);

        (await action.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task should_return_one_failed_batch_delete_result_when_provider_throws()
    {
        var (manager, provider, scheduler) = _CreateManager();
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var failure = new ProviderDbException();
        provider.RemoveTimeJobsAsync(Arg.Any<Guid[]>(), AbortToken).Returns(Task.FromException<int>(failure));

        var result = await manager.DeleteBatchAsync(ids, AbortToken);

        result.IsSucceeded.Should().BeFalse();
        result.Exception.Should().BeSameAs(failure);
        await provider.Received(1).RemoveTimeJobsAsync(Arg.Is<Guid[]>(actual => actual.SequenceEqual(ids)), AbortToken);
        scheduler.DidNotReceive().Restart();
    }

    private static (
        ITimeJobManager<TimeJobEntity> Manager,
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> Provider,
        IJobsHostScheduler Scheduler
    ) _CreateManager(Guid? executingJobId = null)
    {
        var provider = Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var scheduler = Substitute.For<IJobsHostScheduler>();
        var executionContext = new JobsExecutionContext();
        var functionRegistry = JobFunctionRegistryBuilder.Build([], [], []);

        if (executingJobId is { } jobId)
        {
            executionContext.SetFunctions(
                [
                    new JobExecutionState
                    {
                        JobId = jobId,
                        FunctionName = "executing",
                        Type = JobType.TimeJob,
                    },
                ],
                functionRegistry
            );
        }

        var manager = new JobsManager<TimeJobEntity, CronJobEntity>(
            provider,
            scheduler,
            TimeProvider.System,
            Substitute.For<IGuidGenerator>(),
            Substitute.For<IJobsNotificationHubSender>(),
            executionContext,
            Substitute.For<IJobsDispatcher>(),
            Substitute.For<ICurrentCommitCoordinator>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            new SchedulerOptionsBuilder(),
            functionRegistry,
            NullLogger<JobsManager<TimeJobEntity, CronJobEntity>>.Instance
        );

        return (manager, provider, scheduler);
    }

    private sealed class ProviderDbException : DbException;
}
