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
    /// A definition created without a position must be initialized from the store's instant,
    /// never from its occurrence history — otherwise initialization replays
    /// every instant back to year one as a backlog.
    /// </summary>
    [Fact]
    public async Task should_initialize_a_positionless_definition_without_replaying_a_backlog()
    {
        var (manager, provider) = _Create();
        var uninitialized = _Definition(nextDue: default);
        uninitialized.ReconciledThroughUtc = default;
        await provider.InsertCronJobsAsync([uninitialized], AbortToken);

        var (_, functions) = await manager.GetNextJobs(AbortToken);

        functions.Should().BeEmpty("initialization claims responsibility for nothing; the next wake dispatches");

        var after = (await provider.GetCronJobByIdAsync(uninitialized.Id, AbortToken))!;
        after
            .ReconciledThroughUtc.Should()
            .Be(_Now, "the watermark anchors at the store's instant, so nothing before now counts as missed");
        after.NextDueUtc.Should().BeAfter(_Now, "the projection is the first occurrence after that watermark");

        // The decisive anti-backlog assertion: no occurrence was materialized for any of the instants between year one
        // and now, which is what a history-derived initialization would have produced.
        var occurrences = await provider.GetAllCronJobOccurrencesAsync(
            x => x.CronJobId == uninitialized.Id,
            AbortToken
        );
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

        var (wake, functions) = await manager.GetNextJobs(AbortToken);

        wake.Remaining.Should().Be(TimeSpan.Zero, "the store already authorized and claimed this occurrence as due");
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

        var (wake, functions) = await manager.GetNextJobs(AbortToken);

        wake.Remaining.Should().Be(TimeSpan.FromMinutes(10));
        functions.Should().BeEmpty();
    }

    [Fact]
    public async Task should_dispatch_healthy_work_after_activation_defers_an_invalid_definition()
    {
        var (manager, provider) = _Create();
        var invalid = _Definition(nextDue: _Now.AddHours(-1));
        invalid.Expression = "not-a-cron-expression";
        invalid.EvaluationFingerprint = "superseded";
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

    /// <summary>
    /// #830, containment half. With the durable defer gone, the dispatch query keeps returning a definition whose zone
    /// this host cannot resolve, and resolution fails again on every wake. Without a per-candidate guard that single
    /// definition aborts the entire cycle — so this asserts the node keeps scheduling BOTH an unrelated cron job and
    /// an unrelated time job, and that nothing durable was written about the definition it skipped.
    /// </summary>
    [Fact]
    public async Task should_keep_scheduling_everything_else_when_one_definition_names_an_unresolvable_time_zone()
    {
        var (manager, provider) = _Create();
        var unresolvable = _Definition(nextDue: _Now.AddHours(-1));
        unresolvable.TimeZoneId = "Mars/Olympus";
        var healthyCron = _Definition(nextDue: _Now);
        await provider.InsertCronJobsAsync([unresolvable, healthyCron], AbortToken);
        await provider.AddTimeJobsAsync([_TimeJob(_Now.AddMilliseconds(-600))], AbortToken);

        // Twice: the first wake records the suppression, the second proves a suppressed definition stays skipped
        // rather than re-failing the cycle once the durable defer is no longer there to hide it.
        await manager.GetNextJobs(AbortToken);
        var (_, functions) = await manager.GetNextJobs(AbortToken);

        functions
            .Should()
            .Contain(x => x.Type == JobType.TimeJob, "an unrelated time job must not be blocked by a cron definition");
        var stored = (await provider.GetCronJobByIdAsync(unresolvable.Id, AbortToken))!;
        stored.FingerprintRetryAfterUtc.Should().BeNull("node-local evidence must never become fleet-visible state");
        stored.FingerprintFailureCount.Should().Be(0);
        stored
            .ReconciledThroughUtc.Should()
            .Be(
                unresolvable.ReconciledThroughUtc,
                "a definition this node cannot evaluate must not have its schedule position moved by this node"
            );

        var dispatched = await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == healthyCron.Id, AbortToken);
        dispatched.Should().ContainSingle("the healthy cron definition still dispatches on the unhealthy node");
        (await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == unresolvable.Id, AbortToken))
            .Should()
            .BeEmpty();
    }

    /// <summary>
    /// AE5, the fleet half: the definition an unhealthy node excluded must still dispatch on a peer that resolves its
    /// zone, at the SAME schedule revision and with no intervening correction.
    /// </summary>
    /// <remarks>
    /// The per-host variable is the timezone database, which no test can vary per process — so it is modelled at the
    /// seam that consumes it: the definition's identifier is swapped for one the running host resolves between the two
    /// nodes' wakes, leaving the row, its revision, and its position untouched. What the test actually proves is the
    /// property the fix turns on: the unhealthy node left nothing behind that stops a peer, and suppression lives in
    /// the manager instance rather than in the store, so a second node's set is empty.
    /// </remarks>
    [Fact]
    public async Task should_still_dispatch_on_a_peer_the_definition_an_unhealthy_node_excluded()
    {
        var (unhealthyNode, provider) = _Create();
        var zoned = _Definition(nextDue: _Now);
        zoned.TimeZoneId = "Mars/Olympus";

        // The in-memory provider stores the instance, so the test holds the live row.
        await provider.InsertCronJobsAsync([zoned], AbortToken);
        var revisionBefore = zoned.ScheduleRevision;

        await unhealthyNode.GetNextJobs(AbortToken);

        (await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == zoned.Id, AbortToken))
            .Should()
            .BeEmpty("the node that cannot resolve the zone dispatches nothing for it");
        (await provider.GetCronJobByIdAsync(zoned.Id, AbortToken))!
            .FingerprintRetryAfterUtc.Should()
            .BeNull("nothing durable may stand between the definition and a healthy peer");

        // The peer resolves the identifier; the row is otherwise exactly as the unhealthy node left it.
        zoned.TimeZoneId = null;
        var peerNode = _Node(provider);

        var (_, functions) = await peerNode.GetNextJobs(AbortToken);

        functions.Should().ContainSingle().Which.ParentId.Should().Be(zoned.Id);
        var after = (await provider.GetCronJobByIdAsync(zoned.Id, AbortToken))!;
        after
            .ScheduleRevision.Should()
            .Be(revisionBefore, "the peer dispatched the definition as the unhealthy node left it");
        after.ReconciledThroughUtc.Should().Be(_Now);
    }

    /// <summary>
    /// R6a. The candidate read is bounded, so filtering an already-read page would trade a stalled node for a starved
    /// definition: a page whose entries are all suppressed empties on every poll and a healthy later definition never
    /// enters the window. More suppressed definitions than one page holds are placed ahead of the healthy one.
    /// </summary>
    [Fact]
    public async Task should_dispatch_a_healthy_definition_ordered_behind_more_suppressed_ones_than_one_page_holds()
    {
        var (manager, provider) = _Create();

        // 150 > the 64-candidate read bound and > the 100-row claim bound, so no single page can contain both the
        // suppressed block and the healthy definition.
        var suppressed = Enumerable
            .Range(1, 150)
            .Select(index =>
            {
                var definition = _Definition(nextDue: _Now.AddSeconds(-index - 60));
                definition.TimeZoneId = "Mars/Olympus";

                return definition;
            })
            .ToArray();
        var healthy = _Definition(nextDue: _Now);
        await provider.InsertCronJobsAsync([.. suppressed, healthy], AbortToken);

        var (_, functions) = await manager.GetNextJobs(AbortToken);

        functions
            .Should()
            .ContainSingle("a page full of definitions this node cannot evaluate must not hide the healthy one")
            .Which.ParentId.Should()
            .Be(healthy.Id);
        var after = (await provider.GetCronJobByIdAsync(healthy.Id, AbortToken))!;
        after.ReconciledThroughUtc.Should().Be(_Now, "the healthy definition really advanced, not just appeared");
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

        var (wake, functions) = await manager.GetNextJobs(AbortToken);

        wake.Remaining.Should().Be(TimeSpan.FromMinutes(10));
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

    /// <summary>
    /// The ordinary dispatch path reuses the pending walk's stopping instant as its new projection instead of
    /// evaluating the expression a third time. That reuse is only sound while the walk stopped ON the instant being
    /// dispatched; this pins the case where it did not.
    /// </summary>
    /// <remarks>
    /// A persisted projection can disagree with the instant the expression yields from the watermark — a stale
    /// projection left by a timezone-rule change or an edit. Here the watermark is 11:59:30 and the expression yields
    /// 12:00:00, but the persisted projection says 11:59:45. Dispatch advances the watermark to the PERSISTED instant,
    /// so the new projection must be derived from that instant (12:00:00) and not from where the walk happened to stop
    /// (which would give 12:01:00 and silently strand the 12:00:00 tick with no occurrence and no way to re-derive it).
    /// Without the equality guard this test fails; the whole suite passes with the guard removed otherwise, which is
    /// why it exists.
    /// </remarks>
    [Fact]
    public async Task should_derive_the_projection_from_the_dispatched_instant_when_it_differs_from_the_walk()
    {
        var (manager, provider) = _Create();

        // Persisted projection deliberately BEHIND the instant the expression yields from the watermark.
        var divergent = _Definition(nextDue: _Now.AddSeconds(-15));
        await provider.InsertCronJobsAsync([divergent], AbortToken);

        await manager.GetNextJobs(AbortToken);

        var after = (await provider.GetCronJobByIdAsync(divergent.Id, AbortToken))!;

        after.ReconciledThroughUtc.Should().Be(_Now.AddSeconds(-15), "dispatch advances to the instant it dispatched");
        after
            .NextDueUtc.Should()
            .Be(
                _Now,
                "the projection is the next occurrence after the DISPATCHED instant. Reusing the pending walk's "
                    + "stopping value here would project 12:01:00 and skip the 12:00:00 occurrence entirely"
            );
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

    /// <summary>
    /// A second node over the SAME store. Its node-local suppression set is its own and starts empty, which is the
    /// property the fleet half of #830 rests on.
    /// </summary>
    private static InternalJobsManager<FakeTimeJob, FakeCronJob> _Node(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> provider
    )
    {
        var time = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = "node-b" });
        services.AddSingleton(Substitute.For<IJobsHostScheduler>());
        var sp = services.BuildServiceProvider();

        return new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            time,
            Substitute.For<IJobsNotificationHubSender>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            sp.GetRequiredService<IGuidGenerator>(),
            sp,
            sp.GetRequiredService<SchedulerOptionsBuilder>()
        );
    }

    private static FakeTimeJob _TimeJob(DateTime executionTime) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "time-dispatch",
            Status = JobStatus.Idle,
            ExecutionTime = executionTime,
            CreatedAt = new DateTimeOffset(_Now.AddMinutes(-5), TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(_Now.AddMinutes(-5), TimeSpan.Zero),
            Request = [],
        };

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
