// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// The two recovery policies and how each resolves occurrences already sitting in the missed window. The invariant
/// underneath every scenario: recovery must never leave two live occurrences for one instant, and must never execute
/// an instant twice.
/// </summary>
public sealed class CronRecoveryPolicyProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "node-a@incarnation";
    private static readonly DateTimeOffset _Now = new(2026, 7, 26, 17, 30, 0, TimeSpan.Zero);

    // An hourly definition reconciled through 14:00, recovered at 17:30 — the plan's outage shape.
    private static readonly DateTime _Watermark = new(2026, 7, 26, 14, 00, 0, DateTimeKind.Utc);
    private static readonly DateTime _EarliestMissed = new(2026, 7, 26, 15, 00, 0, DateTimeKind.Utc);
    private static readonly DateTime _SecondMissed = new(2026, 7, 26, 16, 00, 0, DateTimeKind.Utc);
    private static readonly DateTime _RecoveredThrough = _Now.UtcDateTime;

    /// <summary>AE1: coalesce over a multi-occurrence outage produces ONE run reporting the earliest missed instant.</summary>
    [Fact]
    public async Task should_materialize_exactly_one_run_reporting_the_earliest_missed_instant()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var result = await provider.ApplyCronRecoveryAsync(_Request(definition), AbortToken);

        result.Should().NotBeNull();
        result!.CoalescedRun.Should().NotBeNull();
        result
            .CoalescedRun!.ExecutionTime.Should()
            .Be(_EarliestMissed, "a coalesced run reports the earliest missed instant as its scheduled instant");
        result
            .CoalescedRun.RecoveredFromUtc.Should()
            .Be(_EarliestMissed, "the marker must survive independently — the watermark has already moved past it");
        result.CoalescedRun.IsRecoveryRun.Should().BeTrue();

        var occurrences = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken);
        occurrences.Should().ContainSingle("coalesce materializes one run regardless of how many were missed");

        var stored = await provider.GetCronJobByIdAsync(definition.Id, AbortToken);
        stored!.ReconciledThroughUtc.Should().Be(_RecoveredThrough, "the backlog it resolved is never reconsidered");
    }

    /// <summary>AE2: skip over the same outage produces no run and still advances the watermark.</summary>
    [Fact]
    public async Task should_materialize_nothing_under_skip_and_still_advance_the_watermark()
    {
        var provider = _Create();
        var definition = _Definition(MissedRunPolicy.Skip);
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var result = await provider.ApplyCronRecoveryAsync(_Request(definition, MissedRunPolicy.Skip), AbortToken);

        result.Should().NotBeNull();
        result!.CoalescedRun.Should().BeNull();
        (await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken))
            .Should()
            .BeEmpty();

        var stored = await provider.GetCronJobByIdAsync(definition.Id, AbortToken);
        stored!.ReconciledThroughUtc.Should().Be(_RecoveredThrough);
        stored.NextDueUtc.Should().BeAfter(_RecoveredThrough);
    }

    /// <summary>AE8: skip transitions an unowned pre-existing occurrence to skipped instead of executing it.</summary>
    [Fact]
    public async Task should_skip_a_pre_existing_unowned_occurrence_rather_than_execute_it()
    {
        var provider = _Create();
        var definition = _Definition(MissedRunPolicy.Skip);
        var resumeCreated = _Occurrence(definition.Id, _EarliestMissed, JobStatus.Idle);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([resumeCreated], AbortToken);

        var result = await provider.ApplyCronRecoveryAsync(_Request(definition, MissedRunPolicy.Skip), AbortToken);

        result!.SkippedOccurrenceCount.Should().Be(1);
        var stored = (
            await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken)
        ).Single();
        stored.Status.Should().Be(JobStatus.Skipped, "an unowned row in the missed window must not run under skip");
        stored.OwnerId.Should().BeNull();
    }

    /// <summary>AE15: a queued occurrence repurposed by coalesce has its ownership revoked and carries the stamp.</summary>
    [Fact]
    public async Task should_repurpose_a_queued_occurrence_and_revoke_its_ownership()
    {
        var provider = _Create();
        var definition = _Definition();
        var queued = _Occurrence(definition.Id, _EarliestMissed, JobStatus.Queued, owner: "node-b@1");
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([queued], AbortToken);

        var result = await provider.ApplyCronRecoveryAsync(_Request(definition), AbortToken);

        result!.CoalescedRun.Should().NotBeNull();
        result.CoalescedRun!.Id.Should().Be(queued.Id, "the existing row is reused, not duplicated");

        var stored = (
            await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken)
        ).Single();
        stored
            .OwnerId.Should()
            .BeNull(
                "revoking ownership is the whole mechanism — the claim path's in-progress transition requires "
                    + "OwnerId == owner, so the prior owner fails that predicate and drops the row"
            );
        stored.LockedUntil.Should().BeNull();
        stored.RecoveredFromUtc.Should().Be(_EarliestMissed);
        stored.Status.Should().Be(JobStatus.Idle, "released for re-claim rather than left queued to its old owner");
    }

    /// <summary>AE9: an occurrence another node is executing is left alone and not duplicated.</summary>
    [Fact]
    public async Task should_leave_an_executing_occurrence_alone_and_not_duplicate_its_instant()
    {
        var provider = _Create();
        var definition = _Definition();
        var running = _Occurrence(definition.Id, _EarliestMissed, JobStatus.InProgress, owner: "node-b@1");
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([running], AbortToken);

        var result = await provider.ApplyCronRecoveryAsync(_Request(definition), AbortToken);

        result!.CoalescedRun.Should().BeNull("the instant is already being executed; a second run would duplicate it");

        var occurrences = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken);
        var stored = occurrences.Should().ContainSingle().Subject;
        stored.Status.Should().Be(JobStatus.InProgress, "running work is never disturbed by recovery");
        stored.OwnerId.Should().Be("node-b@1", "its owner keeps it");

        (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!
            .ReconciledThroughUtc.Should()
            .Be(_RecoveredThrough, "the watermark still advances past the instant");
    }

    /// <summary>AE10: an occurrence that already completed is not executed a second time.</summary>
    [Fact]
    public async Task should_not_re_execute_an_already_completed_occurrence()
    {
        var provider = _Create();
        var definition = _Definition();
        var completed = _Occurrence(definition.Id, _EarliestMissed, JobStatus.Succeeded);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([completed], AbortToken);

        var result = await provider.ApplyCronRecoveryAsync(_Request(definition), AbortToken);

        result!.CoalescedRun.Should().BeNull("that instant already ran; the filtered index no longer covers it");

        var occurrences = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken);
        occurrences.Should().ContainSingle().Which.Status.Should().Be(JobStatus.Succeeded);
        (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!
            .ReconciledThroughUtc.Should()
            .Be(_RecoveredThrough);
    }

    [Fact]
    public async Task should_leave_no_more_than_one_live_occurrence_per_instant()
    {
        var provider = _Create();
        var definition = _Definition();
        // Two idle rows in the window: one at the earliest missed instant, one later.
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync(
            [
                _Occurrence(definition.Id, _EarliestMissed, JobStatus.Idle),
                _Occurrence(definition.Id, _SecondMissed, JobStatus.Idle),
            ],
            AbortToken
        );

        await provider.ApplyCronRecoveryAsync(_Request(definition), AbortToken);

        var occurrences = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken);
        occurrences
            .Count(x => x.Status is JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress)
            .Should()
            .Be(1, "the coalesced run stands in for the whole backlog; the rest are retired");
        occurrences.Single(x => x.ExecutionTime == _SecondMissed).Status.Should().Be(JobStatus.Skipped);
    }

    [Fact]
    public async Task should_report_null_when_another_node_recovered_the_same_backlog_first()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var first = await provider.ApplyCronRecoveryAsync(_Request(definition), AbortToken);
        first.Should().NotBeNull();

        // Same observed watermark: the losing node's view of the definition is now stale.
        var second = await provider.ApplyCronRecoveryAsync(_Request(definition), AbortToken);

        second.Should().BeNull("losing the fence is reported by a null result, never by an exception");
        (await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == definition.Id, AbortToken))
            .Should()
            .ContainSingle("the loser must not materialize a second run for the same backlog");
    }

    [Fact]
    public async Task should_report_null_and_write_nothing_when_the_revision_moved()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var stale = _Request(definition) with { ExpectedScheduleRevision = definition.ScheduleRevision + 1 };
        var result = await provider.ApplyCronRecoveryAsync(stale, AbortToken);

        result.Should().BeNull();
        (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!
            .ReconciledThroughUtc.Should()
            .Be(_Watermark, "a node holding a superseded definition must not resolve a backlog derived from it");
    }

    private static CronRecoveryRequest _Request(
        FakeCronJob definition,
        MissedRunPolicy policy = MissedRunPolicy.Coalesce
    ) =>
        new()
        {
            CronJobId = definition.Id,
            ObservedReconciledThroughUtc = _Watermark,
            ExpectedScheduleRevision = definition.ScheduleRevision,
            RecoveredThroughUtc = _RecoveredThrough,
            NextDueUtc = _RecoveredThrough.AddMinutes(30),
            Policy = policy,
            EarliestMissedUtc = _EarliestMissed,
            CoalescedOccurrenceId = Guid.NewGuid(),
            OnNodeDeath = NodeDeathPolicy.Retry,
            OperationTimeUtc = _Now,
        };

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _Create()
    {
        var services = new ServiceCollection();
        services.AddHeadlessGuidGenerator();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _Owner });
        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }

    private static FakeCronJob _Definition(MissedRunPolicy policy = MissedRunPolicy.Coalesce) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "cron-recovery",
            Expression = "0 0 * * * *",
            ScheduleRevision = 4,
            ReconciledThroughUtc = _Watermark,
            NextDueUtc = _EarliestMissed,
            OnMissedRun = policy,
            MissedRunGraceSeconds = 60,
            CreatedAt = _Now.AddDays(-1),
            UpdatedAt = _Now.AddHours(-4),
        };

    private static CronJobOccurrenceEntity<FakeCronJob> _Occurrence(
        Guid definitionId,
        DateTime executionTime,
        JobStatus status,
        string? owner = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CronJobId = definitionId,
            Status = status,
            OwnerId = owner,
            LockedUntil = owner is null ? null : _Now.UtcDateTime.AddMinutes(5),
            ExecutionTime = executionTime,
            CreatedAt = _Now.AddHours(-3),
            UpdatedAt = _Now.AddHours(-3),
        };
}
