// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
        var scheduleAnchorUtc = now.UtcDateTime.AddHours(2);
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
            guidGenerator,
            Substitute.For<IServiceProvider>(),
            new SchedulerOptionsBuilder()
        );
        var definition = new FakeCronJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Expression = "0 * * * * *",
            IsPaused = true,
            ScheduleRevision = 4,
        };
        provider.PauseCronJobAsync(definition.Id, now, AbortToken).Returns((FakeCronJob?)null);
        provider.GetCronJobByIdAsync(definition.Id, AbortToken).Returns(definition);
        provider
            .ResumeCronJobAsync(
                definition.Id,
                definition.ScheduleRevision,
                Arg.Any<Func<DateTime, CronJobOccurrenceEntity<FakeCronJob>?>>(),
                now,
                AbortToken
            )
            .Returns(call =>
            {
                var occurrence = call.Arg<Func<DateTime, CronJobOccurrenceEntity<FakeCronJob>?>>()(scheduleAnchorUtc)!;
                occurrence.Id.Should().Be(occurrenceId);
                occurrence.ExecutionTime.Should().Be(scheduleAnchorUtc.AddMinutes(1));
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
            Substitute.For<IGuidGenerator>(),
            Substitute.For<IServiceProvider>(),
            new SchedulerOptionsBuilder()
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
                Arg.Any<Func<DateTime, CronJobOccurrenceEntity<FakeCronJob>?>>(),
                resumeTime,
                AbortToken
            )
            .Returns(call =>
            {
                var occurrence = call.Arg<Func<DateTime, CronJobOccurrenceEntity<FakeCronJob>?>>()(
                    resumeTime.UtcDateTime
                )!;
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
        var manager = _CreateManager(provider, sender);
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
            Substitute.For<IGuidGenerator>(),
            Substitute.For<IServiceProvider>(),
            new SchedulerOptionsBuilder()
        );
        var jobId = Guid.NewGuid();
        provider.RequestTimeJobCancellationAsync(jobId, AbortToken).Returns(true);
        sender.CanceledJobNotifyAsync(jobId).Returns(_ => throw new InvalidOperationException("offline"));

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
            Substitute.For<IGuidGenerator>(),
            Substitute.For<IServiceProvider>(),
            new SchedulerOptionsBuilder()
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
            Substitute.For<IGuidGenerator>(),
            Substitute.For<IServiceProvider>(),
            new SchedulerOptionsBuilder()
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
    public async Task should_dispatch_cron_claims_when_timed_out_notification_fails()
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
            Substitute.For<IGuidGenerator>(),
            Substitute.For<IServiceProvider>(),
            new SchedulerOptionsBuilder()
        );
        var cron = new FakeCronJob
        {
            Id = Guid.NewGuid(),
            Function = "daily-report",
            Expression = "0 0 5 * * *",
            Retries = 3,
            RetryIntervals = [10, 30, 60],
        };
        var occurrence = new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            CronJobId = cron.Id,
            CronJob = cron,
            ExecutionTime = new DateTime(2026, 8, 8, 5, 0, 0, DateTimeKind.Utc),
            RetryCount = 1,
        };
        var laterOccurrence = new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            CronJobId = cron.Id,
            CronJob = cron,
            ExecutionTime = occurrence.ExecutionTime.AddMinutes(1),
        };
        provider
            .QueueTimedOutTimeJobsAsync(Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<TimeJobEntity>());
        provider
            .QueueTimedOutCronJobOccurrencesAsync(AbortToken)
            .Returns(new[] { occurrence, laterOccurrence }.ToAsyncEnumerable());
        sender
            .UpdateCronOccurrenceFromExecutionState<FakeCronJob>(
                Arg.Is<JobExecutionState>(state => state.JobId == occurrence.Id)
            )
            .Returns(_ => throw new InvalidOperationException("hub offline"));

        var contexts = await manager.RunTimedOutTickers(AbortToken);

        contexts.Select(state => state.JobId).Should().Equal(occurrence.Id, laterOccurrence.Id);
        var context = contexts[0];
        context.JobId.Should().Be(occurrence.Id);
        context.ParentId.Should().Be(cron.Id);
        context.Type.Should().Be(JobType.CronJobOccurrence);
        context.FunctionName.Should().Be("daily-report");
        context.Retries.Should().Be(3);
        context.RetryCount.Should().Be(1);
        context.RetryIntervals.Should().Equal(10, 30, 60);
        context.ExecutionTime.Should().Be(occurrence.ExecutionTime);
        await sender.Received(2).UpdateCronOccurrenceFromExecutionState<FakeCronJob>(Arg.Any<JobExecutionState>());
        await provider.DidNotReceiveWithAnyArgs().ReleaseAcquiredCronJobOccurrencesAsync(default!, AbortToken);
    }

    /// <summary>
    /// R1/KTD1: the stranded-timed-child safety net is a last-resort backstop, but it sat on the scheduler's hot
    /// path — <c>GetNextJobs</c> runs it, and the scheduler loop sleeps 1ms whenever work is due, so an unbounded
    /// relational candidate scan ran at up to ~1kHz per node in every deployment. It must run on the fallback
    /// cadence instead, while still running on the first poll after startup so a host that starts with an already
    /// stranded child does not wait a whole interval.
    /// </summary>
    [Fact]
    public async Task should_run_the_stranded_child_safety_net_on_the_fallback_cadence_not_on_every_poll()
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.Zero)
        );
        var schedulerOptions = new SchedulerOptionsBuilder();
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            timeProvider,
            Substitute.For<IJobsNotificationHubSender>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            Substitute.For<IGuidGenerator>(),
            Substitute.For<IServiceProvider>(),
            schedulerOptions
        );

        // Nothing due on either side, so each poll returns right after the safety net and the call count is the only
        // thing under test. Without these stubs the auto-substituted occurrence carries a null CronJob and NREs, and
        // the time-job peek hands back a null result instead of the empty one its contract requires.
        provider
            .GetEarliestAvailableCronOccurrenceAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns((CronJobOccurrenceEntity<FakeCronJob>)null!);
        provider.GetEarliestTimeJobsAsync(Arg.Any<CancellationToken>()).Returns(EarliestTimeJobs.None);

        // First poll runs it: a host that starts with an already-stranded child must not wait out an interval.
        await manager.GetNextJobs(AbortToken);
        await provider.Received(1).SkipStrandedTimedChildrenAsync(Arg.Any<CancellationToken>());

        // Back-to-back polls inside the interval must NOT re-run it — this is the 1kHz regression being closed.
        await manager.GetNextJobs(AbortToken);
        await manager.GetNextJobs(AbortToken);
        await provider.Received(1).SkipStrandedTimedChildrenAsync(Arg.Any<CancellationToken>());

        // Once the cadence elapses it runs again, so liveness of the backstop is preserved.
        timeProvider.Advance(schedulerOptions.FallbackIntervalChecker + TimeSpan.FromSeconds(1));
        await manager.GetNextJobs(AbortToken);
        await provider.Received(2).SkipStrandedTimedChildrenAsync(Arg.Any<CancellationToken>());
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
            Substitute.For<IGuidGenerator>(),
            Substitute.For<IServiceProvider>(),
            new SchedulerOptionsBuilder()
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
            Substitute.For<IGuidGenerator>(),
            Substitute.For<IServiceProvider>(),
            new SchedulerOptionsBuilder()
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
        provider
            .GetEarliestTimeJobsAsync(Arg.Any<CancellationToken>())
            .Returns(new EarliestTimeJobs { StoreUtcNow = DateTime.UtcNow, Jobs = [root] });
        provider.GetAllCronJobExpressionsAsync(Arg.Any<CancellationToken>()).Returns([]);
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

    [Fact]
    public async Task release_acquired_resources_null_and_empty_both_release_everything_owned()
    {
        // The scheduler's fault path cannot know which rows a failed tick claimed, so both the null and the
        // empty-batch forms must reach the providers as the (owner-scoped) release-everything call. Previously []
        // short-circuited to a no-op and a faulted tick left its claims leased for a full LeaseDuration.
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var manager = _CreateManager(provider);

        await manager.ReleaseAcquiredResources(resources: null, AbortToken);

        await provider.Received(1).ReleaseAcquiredTimeJobsAsync(Arg.Is<Guid[]>(x => x.Length == 0), AbortToken);
        await provider
            .Received(1)
            .ReleaseAcquiredCronJobOccurrencesAsync(Arg.Is<Guid[]>(x => x.Length == 0), AbortToken);

        provider.ClearReceivedCalls();

        await manager.ReleaseAcquiredResources([], AbortToken);

        await provider.Received(1).ReleaseAcquiredTimeJobsAsync(Arg.Is<Guid[]>(x => x.Length == 0), AbortToken);
        await provider
            .Received(1)
            .ReleaseAcquiredCronJobOccurrencesAsync(Arg.Is<Guid[]>(x => x.Length == 0), AbortToken);
    }

    [Fact]
    public async Task release_acquired_resources_with_resources_releases_only_the_listed_ids()
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var manager = _CreateManager(provider);
        var timeJobId = Guid.NewGuid();
        var occurrenceId = Guid.NewGuid();

        await manager.ReleaseAcquiredResources(
            [_ReleaseState(timeJobId, JobType.TimeJob), _ReleaseState(occurrenceId, JobType.CronJobOccurrence)],
            AbortToken
        );

        await provider
            .Received(1)
            .ReleaseAcquiredTimeJobsAsync(Arg.Is<Guid[]>(x => x.Single() == timeJobId), AbortToken);
        await provider
            .Received(1)
            .ReleaseAcquiredCronJobOccurrencesAsync(Arg.Is<Guid[]>(x => x.Single() == occurrenceId), AbortToken);

        provider.ClearReceivedCalls();

        await manager.ReleaseAcquiredResources([_ReleaseState(timeJobId, JobType.TimeJob)], AbortToken);

        // A one-type batch must not escalate the other type into the release-everything form.
        await provider
            .Received(1)
            .ReleaseAcquiredTimeJobsAsync(Arg.Is<Guid[]>(x => x.Single() == timeJobId), AbortToken);
        await provider
            .DidNotReceive()
            .ReleaseAcquiredCronJobOccurrencesAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task release_dead_node_resources_batch_attempts_every_owner_and_reconciles_once()
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        provider
            .ReleaseDeadNodeTimeJobResourcesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));
        provider
            .ReleaseDeadNodeOccurrenceResourcesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));
        provider
            .ApplyParentTerminalRunConditionsAsync(parentId: null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DateTime?>(null));
        var manager = _CreateManager(provider);

        await manager.ReleaseDeadNodeResources(["node-a@1", "node-b@1", "node-c@1"], AbortToken);

        foreach (var owner in new[] { "node-a@1", "node-b@1", "node-c@1" })
        {
            await provider.Received(1).ReleaseDeadNodeTimeJobResourcesAsync(owner, AbortToken);
            await provider.Received(1).ReleaseDeadNodeOccurrenceResourcesAsync(owner, AbortToken);
        }

        await provider.Received(1).ApplyParentTerminalRunConditionsAsync(parentId: null, AbortToken);
    }

    private static InternalJobsManager<FakeTimeJob, FakeCronJob> _CreateManager(
        IJobPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        IJobsNotificationHubSender? sender = null
    )
    {
        return new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
                new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero)
            ),
            sender ?? Substitute.For<IJobsNotificationHubSender>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            Substitute.For<IGuidGenerator>(),
            Substitute.For<IServiceProvider>(),
            new SchedulerOptionsBuilder()
        );
    }

    private static JobExecutionState _ReleaseState(Guid jobId, JobType type)
    {
        return new JobExecutionState
        {
            JobId = jobId,
            FunctionName = "fn",
            Type = type,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            RunCondition = RunCondition.OnSuccess,
        };
    }

    /// <summary>
    /// The dashboard notification is awaited INSIDE the claim enumeration. When it threw (SignalR backplane outage,
    /// client backpressure) the exception aborted the enumeration, so rows the claim strategy had already committed
    /// as Queued+leased never reached the scheduler and stayed unclaimable by any node until the lease lapsed — a
    /// pure observability channel degrading core scheduling. It must be best-effort.
    /// </summary>
    [Fact]
    public async Task get_next_jobs_still_dispatches_claimed_jobs_when_the_dashboard_notification_throws()
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
            Substitute.For<IGuidGenerator>(),
            Substitute.For<IServiceProvider>(),
            new SchedulerOptionsBuilder()
        );

        var first = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "first",
            ExecutionTime = DateTime.UtcNow.AddSeconds(30),
        };
        var second = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "second",
            ExecutionTime = DateTime.UtcNow.AddSeconds(30),
        };

        provider
            .GetEarliestTimeJobsAsync(Arg.Any<CancellationToken>())
            .Returns(new EarliestTimeJobs { StoreUtcNow = DateTime.UtcNow, Jobs = [first, second] });
        provider.GetAllCronJobExpressionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        provider
            .GetEarliestAvailableCronOccurrenceAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns((CronJobOccurrenceEntity<FakeCronJob>)null!);
        provider
            .QueueTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(new[] { first, second }.ToAsyncEnumerable());
        // The FIRST notification fails, so a regression abandons both claims: the throwing row and everything the
        // enumeration would still have yielded after it.
        sender.UpdateTimeJobNotifyAsync(first).Returns(_ => throw new InvalidOperationException("hub offline"));

        var (_, functions) = await manager.GetNextJobs(AbortToken);

        functions.Select(x => x.JobId).Should().Equal(first.Id, second.Id);
        // Best-effort means best-effort: the claims stand, so nothing is released back to Idle.
        await provider.DidNotReceiveWithAnyArgs().ReleaseAcquiredTimeJobsAsync(default!, AbortToken);
    }

    /// <summary>
    /// When the claim enumeration itself aborts, the rows it already yielded are durably Queued+leased but sit in no
    /// execution context, so the scheduler's catch-all cannot return them. They must be released at the source.
    /// </summary>
    [Fact]
    public async Task get_next_jobs_releases_rows_claimed_before_the_claim_enumeration_aborted()
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            TimeProvider.System,
            Substitute.For<IJobsNotificationHubSender>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            Substitute.For<IGuidGenerator>(),
            Substitute.For<IServiceProvider>(),
            new SchedulerOptionsBuilder()
        );

        var claimed = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "claimed",
            ExecutionTime = DateTime.UtcNow.AddSeconds(30),
        };

        provider
            .GetEarliestTimeJobsAsync(Arg.Any<CancellationToken>())
            .Returns(new EarliestTimeJobs { StoreUtcNow = DateTime.UtcNow, Jobs = [claimed] });
        provider.GetAllCronJobExpressionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        provider
            .GetEarliestAvailableCronOccurrenceAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns((CronJobOccurrenceEntity<FakeCronJob>)null!);
        provider
            .QueueTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(_ClaimThenFail(claimed));

        var act = () => manager.GetNextJobs(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await provider
            .Received(1)
            .ReleaseAcquiredTimeJobsAsync(
                Arg.Is<Guid[]>(ids => ids.Single() == claimed.Id),
                Arg.Any<CancellationToken>()
            );
    }

    private static async IAsyncEnumerable<TimeJobEntity> _ClaimThenFail(TimeJobEntity claimed)
    {
        yield return claimed;
        await Task.Yield();

        throw new InvalidOperationException("claim batch failed");
    }

    [Fact]
    public async Task run_timed_out_tickers_preserves_the_cron_recovery_stamp()
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
            Substitute.For<IGuidGenerator>(),
            Substitute.For<IServiceProvider>(),
            new SchedulerOptionsBuilder()
        );
        var earliestMissed = new DateTime(2026, 7, 26, 15, 0, 0, DateTimeKind.Utc);
        var occurrence = new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            CronJobId = Guid.NewGuid(),
            ExecutionTime = earliestMissed,
            RecoveredFromUtc = earliestMissed,
            CronJob = new FakeCronJob { Function = "reclaimed-recovery" },
        };
        provider
            .QueueTimedOutTimeJobsAsync(Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<TimeJobEntity>());
        provider
            .QueueTimedOutCronJobOccurrencesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { occurrence }.ToAsyncEnumerable());

        var contexts = await manager.RunTimedOutTickers(AbortToken);

        var context = contexts.Should().ContainSingle().Which;
        context.JobId.Should().Be(occurrence.Id);
        context.ExecutionTime.Should().Be(earliestMissed);
        context.RecoveredFromUtc.Should().Be(earliestMissed);
    }
}
