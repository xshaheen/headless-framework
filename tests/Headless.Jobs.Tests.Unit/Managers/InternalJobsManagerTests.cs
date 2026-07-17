// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Managers;

public sealed class InternalJobsManagerTests : TestBase
{
    public sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    public sealed class FakeCronJob : CronJobEntity;

    [Fact]
    public async Task cron_control_notifies_only_accepted_transitions_and_resume_uses_strict_next_utc_occurrence()
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var sender = Substitute.For<IJobsNotificationHubSender>();
        var now = new DateTimeOffset(2026, 7, 17, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(now);
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            timeProvider,
            sender,
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance
        );
        var definition = new FakeCronJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Expression = "0 31 10 * * *",
            IsPaused = true,
            ScheduleRevision = 4,
        };
        provider.PauseCronJobAsync(definition.Id, now.UtcDateTime, AbortToken).Returns((FakeCronJob?)null);
        provider.GetCronJobByIdAsync(definition.Id, AbortToken).Returns(definition);
        provider
            .ResumeCronJobAsync(
                definition.Id,
                definition.ScheduleRevision,
                Arg.Any<CronJobOccurrenceEntity<FakeCronJob>>(),
                now.UtcDateTime,
                AbortToken
            )
            .Returns(call =>
            {
                var occurrence = call.Arg<CronJobOccurrenceEntity<FakeCronJob>>();
                occurrence.ExecutionTime.Should().Be(now.UtcDateTime.AddMinutes(1));
                occurrence.Status.Should().Be(JobStatus.Idle);
                definition.IsPaused = false;
                return definition;
            });

        (await manager.PauseCronJobAsync(definition.Id, AbortToken)).Should().BeFalse();
        (await manager.ResumeCronJobAsync(definition.Id, AbortToken)).Should().BeTrue();

        await sender.Received(1).UpdateCronJobNotifyAsync(definition);
    }

    [Fact]
    public async Task request_time_job_cancellation_async_notifies_only_after_the_provider_accepts_the_transition()
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var sender = Substitute.For<IJobsNotificationHubSender>();
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            TimeProvider.System,
            sender,
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance
        );
        var acceptedId = Guid.NewGuid();
        var rejectedId = Guid.NewGuid();
        provider.RequestTimeJobCancellationAsync(acceptedId, AbortToken).Returns(true);
        provider.RequestTimeJobCancellationAsync(rejectedId, AbortToken).Returns(false);

        (await manager.RequestTimeJobCancellationAsync(acceptedId, AbortToken)).Should().BeTrue();
        (await manager.RequestTimeJobCancellationAsync(rejectedId, AbortToken)).Should().BeFalse();

        await sender.Received(1).CanceledJobNotifyAsync(acceptedId);
        await sender.DidNotReceive().CanceledJobNotifyAsync(rejectedId);
    }

    [Fact]
    public async Task request_time_job_cancellation_async_remains_accepted_when_notification_fails()
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var sender = Substitute.For<IJobsNotificationHubSender>();
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            TimeProvider.System,
            sender,
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance
        );
        var jobId = Guid.NewGuid();
        provider.RequestTimeJobCancellationAsync(jobId, AbortToken).Returns(true);
        sender.CanceledJobNotifyAsync(jobId).Returns<Task>(_ => throw new InvalidOperationException("offline"));

        (await manager.RequestTimeJobCancellationAsync(jobId, AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task set_tickers_in_progress_returns_and_notifies_only_rows_stamped_by_provider()
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var sender = Substitute.For<IJobsNotificationHubSender>();
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            TimeProvider.System,
            sender,
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance
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
    public async Task run_timed_out_tickers_projects_each_descendant_with_its_own_run_condition()
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var sender = Substitute.For<IJobsNotificationHubSender>();
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            TimeProvider.System,
            sender,
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance
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
