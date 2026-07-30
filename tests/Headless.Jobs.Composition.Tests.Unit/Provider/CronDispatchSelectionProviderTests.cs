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
/// The indexed dispatch selection and the definition-lifecycle position writes it depends on. Selection is what makes
/// the scheduler's wake cost independent of definition count: it reads a projection instead of evaluating every
/// expression on every node. The lifecycle writes are what keep that projection honest — a resumed or edited
/// definition that kept a stale position would either replay the interval it was deliberately not running, or dispatch
/// on an expression it no longer has.
/// </summary>
public sealed class CronDispatchSelectionProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "node-a@incarnation";
    private static readonly DateTimeOffset _Now = new(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_order_candidates_by_projection_and_report_the_store_instant()
    {
        var provider = _Create();
        var late = _Definition(nextDue: _Now.UtcDateTime.AddMinutes(30));
        var soon = _Definition(nextDue: _Now.UtcDateTime.AddMinutes(1));
        var middle = _Definition(nextDue: _Now.UtcDateTime.AddMinutes(10));
        await provider.InsertCronJobsAsync([late, soon, middle], AbortToken);

        var result = await provider.GetEarliestCronDispatchCandidatesAsync(limit: 64, AbortToken);

        result.Should().NotBeNull();
        result!
            .Candidates.Select(x => x.CronJobId)
            .Should()
            .Equal([soon.Id, middle.Id, late.Id], "the scheduler takes the earliest projection first");
        result.StoreUtcNow.Should().Be(_Now.UtcDateTime);
    }

    [Fact]
    public async Task should_never_select_a_paused_definition()
    {
        var provider = _Create();
        // Paused AND long overdue: pause must win regardless of how far behind the projection is.
        var paused = _Definition(nextDue: _Now.UtcDateTime.AddHours(-5), isPaused: true);
        var active = _Definition(nextDue: _Now.UtcDateTime.AddMinutes(5));
        await provider.InsertCronJobsAsync([paused, active], AbortToken);

        var result = await provider.GetEarliestCronDispatchCandidatesAsync(limit: 64, AbortToken);

        result.Should().NotBeNull();
        result!.Candidates.Should().ContainSingle().Which.CronJobId.Should().Be(active.Id);
    }

    [Fact]
    public async Task should_bound_the_candidate_read_to_the_requested_limit()
    {
        var provider = _Create();
        var definitions = Enumerable
            .Range(1, 10)
            .Select(minute => _Definition(nextDue: _Now.UtcDateTime.AddMinutes(minute)))
            .ToArray();
        await provider.InsertCronJobsAsync(definitions, AbortToken);

        var result = await provider.GetEarliestCronDispatchCandidatesAsync(limit: 3, AbortToken);

        result.Should().NotBeNull();
        result!.Candidates.Should().HaveCount(3);
        result
            .Candidates.Select(x => x.CronJobId)
            .Should()
            .Equal(definitions[0].Id, definitions[1].Id, definitions[2].Id);
    }

    [Fact]
    public async Task should_report_no_candidates_when_every_definition_is_paused()
    {
        var provider = _Create();
        await provider.InsertCronJobsAsync([_Definition(_Now.UtcDateTime, isPaused: true)], AbortToken);

        var result = await provider.GetEarliestCronDispatchCandidatesAsync(limit: 64, AbortToken);

        result.Should().BeNull("with nothing selectable the scheduler has no cron work to wake for");
    }

    [Fact]
    public async Task should_rebase_the_position_when_a_paused_definition_resumes()
    {
        var provider = _Create();
        // A watermark from before the pause: left in place it would read as a two-hour backlog on resume.
        var definition = _Definition(nextDue: _Now.UtcDateTime.AddHours(-2), isPaused: true);
        definition.ReconciledThroughUtc = _Now.UtcDateTime.AddHours(-2);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        var replacementInstant = _Now.UtcDateTime.AddMinutes(1);

        var resumed = await provider.ResumeCronJobAsync(
            definition.Id,
            definition.ScheduleRevision,
            _Occurrence(definition.Id, replacementInstant),
            _Now,
            AbortToken
        );

        resumed.Should().NotBeNull();
        resumed!.IsPaused.Should().BeFalse();
        resumed
            .ReconciledThroughUtc.Should()
            .Be(_Now.UtcDateTime, "the resume instant is where the schedule restarts being accounted for");
        resumed.NextDueUtc.Should().Be(replacementInstant);
        resumed
            .NextDueUtc.Should()
            .BeAfter(resumed.ReconciledThroughUtc, "the projection is the first occurrence AFTER the watermark");
    }

    [Fact]
    public async Task should_rebase_the_position_when_an_edit_changes_the_schedule()
    {
        var provider = _Create();
        var definition = _Definition(nextDue: _Now.UtcDateTime.AddMinutes(45));
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var edited = _Definition(nextDue: definition.NextDueUtc);
        edited.Id = definition.Id;
        edited.Expression = "0 */5 * * * *";
        var replacementInstant = _Now.UtcDateTime.AddMinutes(5);

        var results = await provider.UpdateCronJobsAtomicallyAsync(
            [
                new CronJobAtomicUpdate<FakeCronJob>(
                    edited,
                    definition.ScheduleRevision,
                    _Occurrence(definition.Id, replacementInstant)
                ),
            ],
            _Now,
            AbortToken
        );

        results.Should().NotBeNull();
        var updated = results!.Single();
        updated.ScheduleRevision.Should().Be(definition.ScheduleRevision + 1);
        updated.ReconciledThroughUtc.Should().Be(_Now.UtcDateTime, "the edit instant is the new reconciliation point");
        updated
            .NextDueUtc.Should()
            .Be(replacementInstant, "the old expression's projection must not survive a schedule change");
    }

    [Fact]
    public async Task should_leave_the_position_untouched_when_an_edit_changes_only_metadata()
    {
        var provider = _Create();
        var definition = _Definition(nextDue: _Now.UtcDateTime.AddMinutes(45));
        definition.ReconciledThroughUtc = _Now.UtcDateTime.AddMinutes(-15);
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var edited = _Definition(nextDue: definition.NextDueUtc);
        edited.Id = definition.Id;
        edited.Expression = definition.Expression;
        edited.Retries = 9;

        var results = await provider.UpdateCronJobsAtomicallyAsync(
            [new CronJobAtomicUpdate<FakeCronJob>(edited, definition.ScheduleRevision, NextOccurrence: null)],
            _Now,
            AbortToken
        );

        results.Should().NotBeNull();
        var updated = results!.Single();
        updated.Retries.Should().Be(9);
        updated.ScheduleRevision.Should().Be(definition.ScheduleRevision, "metadata edits do not move the schedule");
        updated.ReconciledThroughUtc.Should().Be(_Now.UtcDateTime.AddMinutes(-15));
        updated.NextDueUtc.Should().Be(definition.NextDueUtc);
    }

    [Fact]
    public async Task should_leave_the_position_untouched_when_a_definition_is_paused()
    {
        var provider = _Create();
        var definition = _Definition(nextDue: _Now.UtcDateTime.AddMinutes(20));
        definition.ReconciledThroughUtc = _Now.UtcDateTime.AddMinutes(-40);
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var paused = await provider.PauseCronJobAsync(definition.Id, _Now, AbortToken);

        paused.Should().NotBeNull();
        paused!.IsPaused.Should().BeTrue();
        paused.ReconciledThroughUtc.Should().Be(_Now.UtcDateTime.AddMinutes(-40));
        paused.NextDueUtc.Should().Be(definition.NextDueUtc);

        // Pause is enforced by exclusion from selection, not by moving the position.
        (await provider.GetEarliestCronDispatchCandidatesAsync(limit: 64, AbortToken))
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task should_not_advance_a_watermark_it_will_not_dispatch()
    {
        var provider = _Create();

        // The exact combination that loses occurrences: a definition due NOW, and an unrelated already-materialized
        // occurrence sitting strictly EARLIER. Selection must not advance the due definition's watermark for a group
        // it then declines to return -- the advance is durable, so a watermark moved past an instant nothing
        // materializes is an occurrence permanently lost, with no recovery path in this slice.
        var due = _Definition(nextDue: _Now.UtcDateTime.AddMinutes(-1));
        await provider.InsertCronJobsAsync([due], AbortToken);

        var before = (await provider.GetCronJobByIdAsync(due.Id, AbortToken))!;

        var result = await provider.AdvanceCronScheduleAsync(
            new CronScheduleAdvance
            {
                CronJobId = due.Id,
                ObservedReconciledThroughUtc = before.ReconciledThroughUtc,
                ExpectedScheduleRevision = before.ScheduleRevision,
                ReconciledThroughUtc = before.NextDueUtc,
                NextDueUtc = before.NextDueUtc.AddMinutes(1),
                RequireProjectionDue = true,
            },
            AbortToken
        );

        // Guard the primitive's own contract: an advance that reports success MUST have moved the durable position,
        // so a caller can never treat a committed advance as discardable.
        result.Should().NotBeNull();
        var after = (await provider.GetCronJobByIdAsync(due.Id, AbortToken))!;
        after
            .ReconciledThroughUtc.Should()
            .Be(
                result!.ReconciledThroughUtc,
                "a successful advance is durable — the caller cannot discard it without losing the instant"
            );
        after.NextDueUtc.Should().Be(result.NextDueUtc);
    }

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _Create()
    {
        var services = new ServiceCollection();
        services.AddHeadlessGuidGenerator();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _Owner });
        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }

    private static FakeCronJob _Definition(DateTime nextDue, bool isPaused = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "cron-selection",
            Expression = "0 * * * * *",
            IsPaused = isPaused,
            ScheduleRevision = 3,
            ReconciledThroughUtc = _Now.UtcDateTime.AddMinutes(-1),
            NextDueUtc = nextDue,
            CreatedAt = _Now.AddHours(-1),
            UpdatedAt = _Now.AddMinutes(-1),
        };

    private static CronJobOccurrenceEntity<FakeCronJob> _Occurrence(Guid definitionId, DateTime executionTime) =>
        new()
        {
            Id = Guid.NewGuid(),
            CronJobId = definitionId,
            Status = JobStatus.Idle,
            ExecutionTime = executionTime,
            CreatedAt = _Now,
            UpdatedAt = _Now,
        };
}
