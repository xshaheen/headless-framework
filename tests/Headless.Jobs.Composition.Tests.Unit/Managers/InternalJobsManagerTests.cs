// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Abstractions;
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
    public async Task should_notify_and_use_strict_next_utc_occurrence_when_cron_control_is_accepted()
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var sender = Substitute.For<IJobsNotificationHubSender>();
        var now = new DateTimeOffset(2026, 7, 17, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(now);
        var occurrenceId = Guid.Parse("01981a13-d9c0-7000-8000-000000000001");
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(occurrenceId);
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            timeProvider,
            sender,
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            guidGenerator
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
                occurrence.Id.Should().Be(occurrenceId);
                occurrence.ExecutionTime.Should().Be(now.UtcDateTime.AddMinutes(1));
                occurrence.Status.Should().Be(JobStatus.Idle);
                definition.IsPaused = false;
                return definition;
            });

        (await manager.PauseCronJobAsync(definition.Id, AbortToken)).Should().BeFalse();
        (await manager.ResumeCronJobAsync(definition.Id, AbortToken)).Should().BeTrue();

        await sender.Received(1).UpdateCronJobNotifyAsync(definition);
    }

    [Theory]
    [InlineData("2026-03-08T05:00:00Z", "0 30 2 * * *", "2026-03-08T07:30:00Z")]
    [InlineData("2026-11-01T04:00:00Z", "0 30 1 * * *", "2026-11-01T06:30:00Z")]
    public async Task should_use_definition_iana_timezone_when_resume_crosses_dst_transition(
        string resumeTimeText,
        string expression,
        string expectedOccurrenceText
    )
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var resumeTime = DateTimeOffset.Parse(resumeTimeText, CultureInfo.InvariantCulture);
        var expectedOccurrence = DateTimeOffset.Parse(expectedOccurrenceText, CultureInfo.InvariantCulture).UtcDateTime;
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(resumeTime),
            Substitute.For<IJobsNotificationHubSender>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            Substitute.For<IGuidGenerator>()
        );
        var definition = new FakeCronJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Expression = expression,
            TimeZoneId = "America/New_York",
            IsPaused = true,
            ScheduleRevision = 5,
        };
        provider.GetCronJobByIdAsync(definition.Id, AbortToken).Returns(definition);
        provider
            .ResumeCronJobAsync(
                definition.Id,
                definition.ScheduleRevision,
                Arg.Any<CronJobOccurrenceEntity<FakeCronJob>>(),
                resumeTime.UtcDateTime,
                AbortToken
            )
            .Returns(call =>
            {
                var occurrence = call.Arg<CronJobOccurrenceEntity<FakeCronJob>>();
                occurrence.ExecutionTime.Should().Be(expectedOccurrence);
                occurrence.ExecutionTime.Kind.Should().Be(DateTimeKind.Utc);
                return definition;
            });

        (await manager.ResumeCronJobAsync(definition.Id, AbortToken)).Should().BeTrue();
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
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            Substitute.For<IGuidGenerator>()
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
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            Substitute.For<IGuidGenerator>()
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
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            Substitute.For<IGuidGenerator>()
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
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            Substitute.For<IGuidGenerator>()
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

    [Fact]
    public async Task run_timed_out_tickers_threads_the_persisted_tenant_at_every_chain_level()
    {
        // #278: the execute middleware restores ICurrentTenant from JobExecutionState.TenantId, which only exists if
        // _BuildQueuedTimeJobContext copies the persisted TenantId at root/child/grandchild. A copy-paste slip on one
        // of those three field assignments would silently run that level system-scope, so pin all three here.
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var sender = Substitute.For<IJobsNotificationHubSender>();
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            TimeProvider.System,
            sender,
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            Substitute.For<IGuidGenerator>()
        );

        var grandChild = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "grand-child",
            TenantId = "t-grand",
        };
        var child = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "child",
            TenantId = "t-child",
            Children = [grandChild],
        };
        var root = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "root",
            ExecutionTime = DateTime.UtcNow,
            TenantId = "t-root",
            Children = [child],
        };
        provider.QueueTimedOutTimeJobsAsync(Arg.Any<CancellationToken>()).Returns(new[] { root }.ToAsyncEnumerable());
        provider
            .QueueTimedOutCronJobOccurrencesAsync(Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<CronJobOccurrenceEntity<FakeCronJob>>());

        var contexts = await manager.RunTimedOutTickers(AbortToken);

        var rootContext = contexts.Should().ContainSingle().Which;
        rootContext.TenantId.Should().Be("t-root");
        var childContext = rootContext.TimeJobChildren.Should().ContainSingle().Which;
        childContext.TenantId.Should().Be("t-child");
        childContext.TimeJobChildren.Should().ContainSingle().Which.TenantId.Should().Be("t-grand");
    }

    [Fact]
    public async Task get_next_jobs_threads_the_persisted_tenant_at_every_chain_level()
    {
        // #278: same three-level TenantId threading assertion for the periodic-poll pickup path (GetNextJobs →
        // QueueTimeJobsAsync → _BuildQueuedTimeJobContext), the sibling of the timed-out pickup above.
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var sender = Substitute.For<IJobsNotificationHubSender>();
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            TimeProvider.System,
            sender,
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            Substitute.For<IGuidGenerator>()
        );

        var grandChild = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "grand-child",
            TenantId = "t-grand",
        };
        var child = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "child",
            TenantId = "t-child",
            Children = [grandChild],
        };
        var root = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "root",
            ExecutionTime = DateTime.UtcNow.AddSeconds(30),
            TenantId = "t-root",
            Children = [child],
        };

        // Route the cron side to empty so only the time-job pickup flows through GetNextJobs.
        provider.GetEarliestTimeJobsAsync(Arg.Any<CancellationToken>()).Returns(new[] { root });
        provider.GetAllCronJobExpressionsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CronJobEntity>());
        provider
            .GetEarliestAvailableCronOccurrenceAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns((CronJobOccurrenceEntity<FakeCronJob>)null!);
        provider
            .QueueTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(new[] { root }.ToAsyncEnumerable());

        var (_, functions) = await manager.GetNextJobs(AbortToken);

        var rootContext = functions.Should().ContainSingle().Which;
        rootContext.TenantId.Should().Be("t-root");
        var childContext = rootContext.TimeJobChildren.Should().ContainSingle().Which;
        childContext.TenantId.Should().Be("t-child");
        childContext.TimeJobChildren.Should().ContainSingle().Which.TenantId.Should().Be("t-grand");
    }
}
