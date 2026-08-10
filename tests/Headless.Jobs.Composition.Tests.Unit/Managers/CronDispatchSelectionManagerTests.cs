// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Managers;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Managers;

/// <summary>
/// Manager-level coverage of the indexed cron dispatch selection — the seam where the projection read, the
/// compare-and-advance, and the already-materialized-occurrence merge meet. The provider-level suites prove each
/// primitive in isolation; only this level proves the orchestration between them.
/// </summary>
/// <remarks>
/// Drives the REAL in-memory provider rather than a substitute. A substituted provider returns <see langword="null"/>
/// from the candidate read by default, which silently no-ops the entire selection path and lets a broken branch pass —
/// the exact blind spot that let two dispatch defects through provider-level tests.
/// </remarks>
public sealed class CronDispatchSelectionManagerTests : TestBase
{
    public sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    public sealed class FakeCronJob : CronJobEntity;

    private const string _NodeA = "node-a";
    private static readonly DateTime _Now = new(2026, 07, 26, 12, 00, 00, DateTimeKind.Utc);

    /// <summary>
    /// The invariant both dispatch defects violated: an advance commits durable state, so selection must never move a
    /// watermark for a definition it then declines to dispatch. There is no recovery path for an instant whose
    /// watermark advanced with nothing materialized — the occurrence is simply gone.
    /// </summary>
    [Fact]
    public async Task should_not_advance_a_definition_it_declines_to_dispatch()
    {
        var (manager, provider) = _Create();

        // A is due exactly now. B already has a materialized occurrence half a second earlier, so B sorts first and
        // wins the wake. A must therefore be left completely untouched, watermark included.
        var due = _Definition(nextDue: _Now);
        var stored = _Definition(nextDue: _Now.AddMinutes(10));
        await provider.InsertCronJobsAsync([due, stored], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([_Occurrence(stored.Id, _Now.AddMilliseconds(-500))], AbortToken);

        var beforeDue = (await provider.GetCronJobByIdAsync(due.Id, AbortToken))!;

        await manager.GetNextJobs(AbortToken);

        var afterDue = (await provider.GetCronJobByIdAsync(due.Id, AbortToken))!;
        afterDue
            .ReconciledThroughUtc.Should()
            .Be(
                beforeDue.ReconciledThroughUtc,
                "selection returned the earlier stored occurrence instead of this definition, so advancing its "
                    + "watermark would strand the instant with nothing materialized and no way to re-derive it"
            );
        afterDue.NextDueUtc.Should().Be(beforeDue.NextDueUtc);
    }

    /// <summary>
    /// The complementary half: when a definition IS the one selected, its watermark must actually move — otherwise the
    /// scheduler re-dispatches the same instant on every wake.
    /// </summary>
    [Fact]
    public async Task should_advance_the_definition_it_dispatches()
    {
        var (manager, provider) = _Create();
        var due = _Definition(nextDue: _Now);
        await provider.InsertCronJobsAsync([due], AbortToken);
        var before = (await provider.GetCronJobByIdAsync(due.Id, AbortToken))!;

        await manager.GetNextJobs(AbortToken);

        var after = (await provider.GetCronJobByIdAsync(due.Id, AbortToken))!;
        after.ReconciledThroughUtc.Should().Be(before.NextDueUtc, "the dispatched instant is now accounted for");
        after.NextDueUtc.Should().BeAfter(before.NextDueUtc, "the projection moves to the next occurrence");
    }

    [Fact]
    public async Task should_leave_definitions_that_are_not_due_untouched()
    {
        var (manager, provider) = _Create();
        var due = _Definition(nextDue: _Now);
        var future = _Definition(nextDue: _Now.AddHours(3));
        await provider.InsertCronJobsAsync([due, future], AbortToken);
        var futureBefore = (await provider.GetCronJobByIdAsync(future.Id, AbortToken))!;

        await manager.GetNextJobs(AbortToken);

        var futureAfter = (await provider.GetCronJobByIdAsync(future.Id, AbortToken))!;
        futureAfter.ReconciledThroughUtc.Should().Be(futureBefore.ReconciledThroughUtc);
        futureAfter
            .NextDueUtc.Should()
            .Be(futureBefore.NextDueUtc, "a definition that is not due must cost an index entry and nothing more");
    }

    [Fact]
    public async Task should_never_dispatch_or_advance_a_paused_definition()
    {
        var (manager, provider) = _Create();
        // Paused and long overdue: pause must win regardless of how far behind the projection is.
        var paused = _Definition(nextDue: _Now.AddHours(-5), isPaused: true);
        await provider.InsertCronJobsAsync([paused], AbortToken);
        var before = (await provider.GetCronJobByIdAsync(paused.Id, AbortToken))!;

        var (_, functions) = await manager.GetNextJobs(AbortToken);

        functions.Should().BeEmpty();
        var after = (await provider.GetCronJobByIdAsync(paused.Id, AbortToken))!;
        after.ReconciledThroughUtc.Should().Be(before.ReconciledThroughUtc);
        after.NextDueUtc.Should().Be(before.NextDueUtc);
    }

    /// <summary>
    /// The cross-arbitration half of the never-advance-then-discard invariant: an earlier time job must not make
    /// GetNextJobs drop a cron group whose watermarks already advanced inside the selection read. The arbitration
    /// may pick the wake instant, but an advanced instant with nothing materialized is unrecoverable — no sweep
    /// re-derives an instant the watermark has passed.
    /// </summary>
    [Fact]
    public async Task should_materialize_the_advanced_instant_even_when_an_earlier_time_job_wins_the_wake()
    {
        var (manager, provider) = _Create();

        // Cron due exactly now; a time job 600 ms earlier in the PREVIOUS second, still inside the time-job read's
        // one-second lookback, so the arbitration's different-second branch prefers the time job.
        var due = _Definition(nextDue: _Now);
        await provider.InsertCronJobsAsync([due], AbortToken);
        await provider.AddTimeJobsAsync(
            [
                new FakeTimeJob
                {
                    Id = Guid.NewGuid(),
                    Function = "time-dispatch",
                    Status = JobStatus.Idle,
                    ExecutionTime = _Now.AddMilliseconds(-600),
                    CreatedAt = new DateTimeOffset(_Now.AddMinutes(-5), TimeSpan.Zero),
                    UpdatedAt = new DateTimeOffset(_Now.AddMinutes(-5), TimeSpan.Zero),
                    Request = [],
                },
            ],
            AbortToken
        );

        await manager.GetNextJobs(AbortToken);

        var after = (await provider.GetCronJobByIdAsync(due.Id, AbortToken))!;
        after.ReconciledThroughUtc.Should().Be(_Now, "the due definition's advance committed during selection");

        var occurrences = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == due.Id, AbortToken);
        occurrences
            .Should()
            .ContainSingle(
                x => x.ExecutionTime == _Now,
                "an instant whose watermark advanced must be materialized in the same wake — discarding the group "
                    + "loses the tick with no recovery path"
            );
    }

    /// <summary>
    /// AE10: an occurrence at the projection's instant that already completed is invisible to the claimable-row
    /// reuse read and no longer covered by the filtered unique index, so dispatch must step past the instant
    /// without materializing a second run.
    /// </summary>
    [Fact]
    public async Task should_not_rerun_an_instant_whose_occurrence_already_completed()
    {
        var (manager, provider) = _Create();
        var due = _Definition(nextDue: _Now);
        await provider.InsertCronJobsAsync([due], AbortToken);

        var completed = _Occurrence(due.Id, _Now);
        completed.Status = JobStatus.Succeeded;
        completed.ExecutedAt = new DateTimeOffset(_Now.AddSeconds(-1), TimeSpan.Zero);
        await provider.InsertCronJobOccurrencesAsync([completed], AbortToken);

        var (_, functions) = await manager.GetNextJobs(AbortToken);

        functions.Should().BeEmpty("the succeeded occurrence already stands for this instant");

        var after = (await provider.GetCronJobByIdAsync(due.Id, AbortToken))!;
        after.ReconciledThroughUtc.Should().Be(_Now, "the instant is accounted for, so the watermark advances past it");

        var occurrences = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == due.Id, AbortToken);
        occurrences.Should().ContainSingle("the completed run must not be joined by a re-materialized duplicate");
        occurrences[0].Id.Should().Be(completed.Id);
        occurrences[0].Status.Should().Be(JobStatus.Succeeded, "the existing terminal row is left undisturbed");
    }

    /// <summary>
    /// A definition carrying no position (seeded before the field existed, or created by a path that did not set it)
    /// must be initialized from the store's instant, never from its occurrence history — otherwise an upgrade replays
    /// every instant back to year one as a backlog.
    /// </summary>
    [Fact]
    public async Task should_initialize_a_positionless_definition_without_replaying_a_backlog()
    {
        var (manager, provider) = _Create();
        var legacy = _Definition(nextDue: default);
        legacy.ReconciledThroughUtc = default;
        await provider.InsertCronJobsAsync([legacy], AbortToken);

        var (_, functions) = await manager.GetNextJobs(AbortToken);

        functions.Should().BeEmpty("initialization claims responsibility for nothing; the next wake dispatches");

        var after = (await provider.GetCronJobByIdAsync(legacy.Id, AbortToken))!;
        after
            .ReconciledThroughUtc.Should()
            .Be(_Now, "the watermark anchors at the store's instant, so nothing before now counts as missed");
        after.NextDueUtc.Should().BeAfter(_Now, "the projection is the first occurrence after that watermark");

        // The decisive anti-backlog assertion: no occurrence was materialized for any of the instants between year one
        // and now, which is what a history-derived initialization would have produced.
        var occurrences = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == legacy.Id, AbortToken);
        occurrences.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_zero_delay_after_the_store_materializes_a_due_occurrence_when_node_clock_lags()
    {
        var storeTime = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var nodeTime = new FakeTimeProvider(new DateTimeOffset(_Now.AddHours(-1), TimeSpan.Zero));
        var (manager, provider) = _Create(storeTime, nodeTime);
        var due = _Definition(nextDue: _Now);
        await provider.InsertCronJobsAsync([due], AbortToken);

        var (timeRemaining, functions) = await manager.GetNextJobs(AbortToken);

        timeRemaining.Should().Be(TimeSpan.Zero, "the store already authorized and claimed this occurrence as due");
        functions.Should().ContainSingle();
        functions[0].JobId.Should().NotBeEmpty();
        functions[0].FunctionName.Should().Be(due.Function);
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(60)]
    public async Task should_calculate_a_future_cron_wake_from_the_store_clock(int nodeOffsetMinutes)
    {
        var storeTime = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var nodeTime = new FakeTimeProvider(new DateTimeOffset(_Now.AddMinutes(nodeOffsetMinutes), TimeSpan.Zero));
        var (manager, provider) = _Create(storeTime, nodeTime);
        var future = _Definition(nextDue: _Now.AddMinutes(10));
        await provider.InsertCronJobsAsync([future], AbortToken);

        var (timeRemaining, functions) = await manager.GetNextJobs(AbortToken);

        timeRemaining.Should().Be(TimeSpan.FromMinutes(10));
        functions.Should().BeEmpty();
    }

    [Fact]
    public async Task should_dispatch_healthy_work_after_activation_defers_an_invalid_definition()
    {
        var (manager, provider) = _Create();
        var invalid = _Definition(nextDue: _Now.AddHours(-1));
        invalid.TimeZoneId = "Invalid/Recovery-Zone";
        invalid.EvaluationFingerprint = "legacy";
        var healthy = _Definition(nextDue: _Now);
        healthy.EvaluationFingerprint = new CronScheduleCache(TimeZoneInfo.Utc).ComputeEvaluationFingerprint(null);
        await provider.InsertCronJobsAsync([invalid, healthy], AbortToken);

        var activation = await manager.RebaseStaleFingerprintsAsync(limit: 64, cancellationToken: AbortToken);
        var (_, functions) = await manager.GetNextJobs(AbortToken);

        activation.Deferred.Should().Be(1);
        functions.Should().ContainSingle().Which.ParentId.Should().Be(healthy.Id);
        var deferred = (await provider.GetCronJobByIdAsync(invalid.Id, AbortToken))!;
        deferred.FingerprintRetryAfterUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task should_wake_for_an_earlier_projection_without_claiming_a_later_stored_occurrence()
    {
        var (manager, provider) = _Create();
        var projection = _Definition(nextDue: _Now.AddMinutes(10));
        var storedDefinition = _Definition(nextDue: _Now.AddMinutes(30));
        var storedOccurrence = _Occurrence(storedDefinition.Id, _Now.AddMinutes(20));
        await provider.InsertCronJobsAsync([projection, storedDefinition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([storedOccurrence], AbortToken);

        var (timeRemaining, functions) = await manager.GetNextJobs(AbortToken);

        timeRemaining.Should().Be(TimeSpan.FromMinutes(10));
        functions.Should().BeEmpty("the stored occurrence is later than the earlier projection wake");
        var persisted = (await provider.GetAllCronJobOccurrencesAsync(x => x.Id == storedOccurrence.Id, AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        persisted.Status.Should().Be(JobStatus.Idle);
        persisted.OwnerId.Should().BeNull();
        persisted.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task should_refuse_a_known_occurrence_when_the_claim_key_is_a_different_instant()
    {
        var (_, provider) = _Create();
        var definition = _Definition(nextDue: _Now.AddMinutes(30));
        var storedOccurrence = _Occurrence(definition.Id, _Now.AddMinutes(20));
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([storedOccurrence], AbortToken);
        var context = new JobManagerDispatchContext(definition.Id)
        {
            FunctionName = definition.Function,
            Expression = definition.Expression,
            ScheduleRevision = definition.ScheduleRevision,
            OnNodeDeath = definition.OnNodeDeath,
            NextCronOccurrence = new NextCronOccurrence(storedOccurrence.Id, storedOccurrence.CreatedAt),
        };

        var claimed = await provider
            .QueueCronJobOccurrencesAsync((_Now.AddMinutes(10), [context]), AbortToken)
            .ToArrayAsync(AbortToken);

        claimed.Should().BeEmpty();
        var persisted = (await provider.GetAllCronJobOccurrencesAsync(x => x.Id == storedOccurrence.Id, AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        persisted.Status.Should().Be(JobStatus.Idle);
        persisted.OwnerId.Should().BeNull();
    }

    [Fact]
    public async Task should_dispatch_a_coalesced_recovery_with_its_persisted_stamp()
    {
        var (manager, provider) = _Create();
        var definition = _Definition(nextDue: _Now.AddHours(-3));
        definition.Expression = "0 0 * * * *";
        definition.ReconciledThroughUtc = _Now.AddHours(-4);
        definition.OnMissedRun = MissedRunPolicy.Coalesce;
        definition.MissedRunGraceSeconds = 60;
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var (_, functions) = await manager.GetNextJobs(AbortToken);

        var context = functions.Should().ContainSingle().Which;
        context.ExecutionTime.Should().Be(_Now.AddHours(-3));
        context.RecoveredFromUtc.Should().Be(_Now.AddHours(-3));
    }

    private static (
        InternalJobsManager<FakeTimeJob, FakeCronJob> Manager,
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider
    ) _Create(TimeProvider? storeTime = null, TimeProvider? nodeTime = null)
    {
        storeTime ??= new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        nodeTime ??= storeTime;
        var services = new ServiceCollection();
        services.AddSingleton(storeTime);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _NodeA });
        services.AddSingleton(Substitute.For<IJobsHostScheduler>());
        var sp = services.BuildServiceProvider();

        var provider = new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(sp);
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            nodeTime,
            Substitute.For<IJobsNotificationHubSender>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            sp.GetRequiredService<IGuidGenerator>(),
            sp,
            sp.GetRequiredService<SchedulerOptionsBuilder>()
        );

        return (manager, provider);
    }

    private static FakeCronJob _Definition(DateTime nextDue, bool isPaused = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "cron-dispatch",
            // Every minute, so the projection after any instant is at most a minute later.
            Expression = "0 * * * * *",
            IsPaused = isPaused,
            ScheduleRevision = 0,
            // Exactly one pending instant, dispatched on time: these scenarios exercise NORMAL dispatch, not recovery.
            ReconciledThroughUtc = _Now.AddSeconds(-30),
            NextDueUtc = nextDue,
            CreatedAt = new DateTimeOffset(_Now.AddHours(-1), TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(_Now.AddMinutes(-1), TimeSpan.Zero),
        };

    private static CronJobOccurrenceEntity<FakeCronJob> _Occurrence(Guid definitionId, DateTime executionTime) =>
        new()
        {
            Id = Guid.NewGuid(),
            CronJobId = definitionId,
            Status = JobStatus.Idle,
            ExecutionTime = executionTime,
            CreatedAt = new DateTimeOffset(_Now.AddMinutes(-5), TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(_Now.AddMinutes(-5), TimeSpan.Zero),
        };
}
