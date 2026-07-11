// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;

namespace Tests.Managers;

public sealed class InternalJobsManagerTests : TestBase
{
    public sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    public sealed class FakeCronJob : CronJobEntity;

    [Fact]
    public async Task SetTickersInProgress_returns_and_notifies_only_rows_stamped_by_provider()
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var sender = Substitute.For<IJobsNotificationHubSender>();
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            TimeProvider.System,
            sender,
            new CronScheduleCache(TimeZoneInfo.Utc)
        );

        var owned = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "owned",
            Type = JobType.TimeJob,
            Status = JobStatus.Queued,
        };
        var lost = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "lost",
            Type = JobType.TimeJob,
            Status = JobStatus.Queued,
        };

        provider
            .UpdateTimeJobsWithUnifiedContextAsync(Arg.Any<Guid[]>(), Arg.Any<JobExecutionState>(), AbortToken)
            .Returns(Task.FromResult<Guid[]>([owned.JobId]));
        sender.UpdateTimeJobFromExecutionState<FakeTimeJob>(Arg.Any<JobExecutionState>()).Returns(Task.CompletedTask);

        var stamped = await manager.SetTickersInProgress([owned, lost], AbortToken);

        stamped.Should().Equal(owned);
        owned.Status.Should().Be(JobStatus.InProgress);
        lost.Status.Should().Be(JobStatus.Queued);
        await sender.Received(1).UpdateTimeJobFromExecutionState<FakeTimeJob>(owned);
        await sender.DidNotReceive().UpdateTimeJobFromExecutionState<FakeTimeJob>(lost);
    }

    [Fact]
    public async Task RunTimedOutTickers_projects_each_descendant_with_its_own_run_condition()
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var sender = Substitute.For<IJobsNotificationHubSender>();
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            TimeProvider.System,
            sender,
            new CronScheduleCache(TimeZoneInfo.Utc)
        );

        // The grandchild must keep ITS OWN RunCondition; a regression to the parent's value would
        // change when the grandchild runs while every same-RunCondition test still passes.
        var grandChild = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "grand-child",
            RunCondition = RunCondition.OnCancelled,
        };
        var child = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "child",
            RunCondition = RunCondition.OnSuccess,
            Children = [grandChild],
        };
        var root = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "root",
            ExecutionTime = DateTime.UtcNow,
            Children = [child],
        };
        provider.QueueTimedOutTimeJobsAsync(Arg.Any<CancellationToken>()).Returns(new[] { root }.ToAsyncEnumerable());
        provider
            .QueueTimedOutCronJobOccurrencesAsync(Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<CronJobOccurrenceEntity<FakeCronJob>>());

        var contexts = await manager.RunTimedOutTickers(AbortToken);

        var childContext = contexts.Should().ContainSingle().Which.TimeJobChildren.Should().ContainSingle().Which;
        childContext.RunCondition.Should().Be(RunCondition.OnSuccess);
        var grandChildContext = childContext.TimeJobChildren.Should().ContainSingle().Which;
        grandChildContext.RunCondition.Should().Be(RunCondition.OnCancelled);
    }
}
