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
}
