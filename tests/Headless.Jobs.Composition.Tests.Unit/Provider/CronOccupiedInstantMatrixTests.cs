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
/// The occupied-instant ACCOUNTING matrix (KTD1) for the in-memory provider, over every persisted state a row can be
/// in and against all three paths that read it: materialization, recovery, and the claim path.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory provider is a first-class backend here, not a test double, so it owes the identical verdicts to the
/// relational ones. Before this suite it silently violated the migration-replacement contract — the guarding
/// conformance test was overridden only on the two native providers.
/// </para>
/// <para>
/// The table below is stated LITERALLY rather than derived from <see cref="CronOccurrenceAccounting" />; a table
/// computed from the production rule would agree with any rule, including a wrong one. It is the deliberate twin of
/// <c>Tests.JobsOccupiedInstantConformanceTests{TFixture}.Matrix</c>, which drives the relational providers — the
/// two are kept in step by hand, which is the price of an independent statement of the contract.
/// </para>
/// </remarks>
public sealed class CronOccupiedInstantMatrixTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "node-a@incarnation";
    private static readonly DateTimeOffset _Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _Watermark = _Now.UtcDateTime.AddMinutes(-5);
    private static readonly DateTime _Projection = _Now.UtcDateTime.AddMinutes(-1);

    private enum RecoveryEffect
    {
        None,
        Repurposed,
        Created,
    }

    private sealed record MatrixCase(
        string Name,
        JobStatus Status,
        CronOccurrenceDisposition Disposition,
        bool AccountsForInstant,
        RecoveryEffect Recovery,
        string? SkippedReason = null
    );

    private static MatrixCase[] _Matrix =>
        [
            new("idle", JobStatus.Idle, CronOccurrenceDisposition.Accounted, true, RecoveryEffect.Repurposed),
            new("queued", JobStatus.Queued, CronOccurrenceDisposition.Accounted, true, RecoveryEffect.Repurposed),
            new("in-progress", JobStatus.InProgress, CronOccurrenceDisposition.Accounted, true, RecoveryEffect.None),
            new("succeeded", JobStatus.Succeeded, CronOccurrenceDisposition.Accounted, true, RecoveryEffect.None),
            new("due-done", JobStatus.DueDone, CronOccurrenceDisposition.Accounted, true, RecoveryEffect.None),
            new("failed", JobStatus.Failed, CronOccurrenceDisposition.Accounted, true, RecoveryEffect.None),
            new("cancelled", JobStatus.Cancelled, CronOccurrenceDisposition.Accounted, true, RecoveryEffect.None),
            new(
                "skipped-by-recovery",
                JobStatus.Skipped,
                CronOccurrenceDisposition.Accounted,
                true,
                RecoveryEffect.None,
                "Cron occurrence missed and resolved by recovery"
            ),
            new(
                "skipped-by-dead-node",
                JobStatus.Skipped,
                CronOccurrenceDisposition.Accounted,
                true,
                RecoveryEffect.None,
                "Node is not alive!"
            ),
            // The two producers of the IDENTICAL SkippedReason, with opposite answers. This pair is the whole reason
            // the rule reads a typed column: no string test can separate them.
            new(
                "skipped-by-runtime-edit",
                JobStatus.Skipped,
                CronOccurrenceDisposition.Superseded,
                true,
                RecoveryEffect.None,
                "Cron definition updated"
            ),
            new(
                "skipped-by-seeding-migration",
                JobStatus.Skipped,
                CronOccurrenceDisposition.ReplacementOwed,
                false,
                RecoveryEffect.Created,
                "Cron definition updated"
            ),
            // Fail closed: a status this binary does not recognize must neither throw on the read path nor become a
            // silent re-fire. Relationally that is a string a newer binary wrote; here it is the same out-of-range
            // value reaching the same branch.
            new(
                "unknown-status-from-a-newer-binary",
                (JobStatus)999,
                CronOccurrenceDisposition.Accounted,
                true,
                RecoveryEffect.None
            ),
        ];

    [Fact]
    public async Task the_occupied_instant_matrix_governs_materialization()
    {
        foreach (var testCase in _Matrix)
        {
            var provider = _Create();
            var definition = _Definition();
            await provider.InsertCronJobsAsync([definition], AbortToken);
            var seededId = await _SeedOccurrenceAsync(provider, definition, testCase, _Projection);

            var result = await provider.MaterializeCronScheduleOccurrenceAsync(
                _Materialization(definition),
                AbortToken
            );

            var expectedOutcome =
                !testCase.AccountsForInstant ? CronScheduleMaterializationOutcome.OccurrenceCreated
                : testCase.Status is JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress
                    ? CronScheduleMaterializationOutcome.OccurrenceExists
                : CronScheduleMaterializationOutcome.OccurrenceAlreadyTerminal;

            result
                .Outcome.Should()
                .Be(expectedOutcome, "case '{0}' must resolve materialization by the accounting matrix", testCase.Name);
            (await provider.GetAllCronJobOccurrencesAsync(null, AbortToken))
                .Should()
                .HaveCount(
                    testCase.AccountsForInstant ? 1 : 2,
                    "case '{0}' must {1} a second occurrence at the instant",
                    testCase.Name,
                    testCase.AccountsForInstant ? "not create" : "create"
                );

            if (testCase.AccountsForInstant)
            {
                result
                    .OccurrenceId.Should()
                    .Be(seededId, "case '{0}' must report the row that accounts for the instant", testCase.Name);
            }
            else
            {
                result
                    .OccurrenceId.Should()
                    .NotBe(seededId, "case '{0}' owes a NEW occurrence, not the retired one", testCase.Name);
            }
        }
    }

    [Fact]
    public async Task the_occupied_instant_matrix_governs_recovery()
    {
        foreach (var testCase in _Matrix)
        {
            var provider = _Create();
            var definition = _Definition();
            await provider.InsertCronJobsAsync([definition], AbortToken);
            var seededId = await _SeedOccurrenceAsync(provider, definition, testCase, _Projection);
            var coalescedId = Guid.NewGuid();

            var result = await provider.ApplyCronRecoveryAsync(
                new CronRecoveryRequest
                {
                    CronJobId = definition.Id,
                    ObservedReconciledThroughUtc = definition.ReconciledThroughUtc,
                    ExpectedScheduleRevision = definition.ScheduleRevision,
                    RecoveredThroughUtc = _Projection,
                    NextDueUtc = _Projection.AddMinutes(1),
                    BoundedProgressThroughUtc = _Projection,
                    NextDueAfterBoundedProgressUtc = _Projection.AddMinutes(1),
                    EvaluationSaturated = false,
                    Policy = MissedRunPolicy.Coalesce,
                    EarliestMissedUtc = _Projection,
                    MissedInstantsUtc = [_Projection],
                    CoalescedOccurrenceId = coalescedId,
                    OperationTimeUtc = _Now,
                },
                AbortToken
            );

            result.Should().NotBeNull("case '{0}' holds the observed fence, so recovery must apply", testCase.Name);
            var stored = await provider.GetAllCronJobOccurrencesAsync(null, AbortToken);

            switch (testCase.Recovery)
            {
                case RecoveryEffect.None:
                    result!.CoalescedRun.Should().BeNull("case '{0}' accounts for the missed instant", testCase.Name);
                    stored.Should().ContainSingle("case '{0}' must not gain a second row", testCase.Name);
                    break;
                case RecoveryEffect.Repurposed:
                    result!.CoalescedRun.Should().NotBeNull("case '{0}' is still claimable", testCase.Name);
                    result
                        .CoalescedRun!.Id.Should()
                        .Be(seededId, "case '{0}' repurposes IN PLACE rather than duplicating", testCase.Name);
                    stored.Should().ContainSingle();
                    break;
                case RecoveryEffect.Created:
                    result!
                        .CoalescedRun.Should()
                        .NotBeNull("case '{0}' leaves the instant unaccounted for", testCase.Name);
                    result
                        .CoalescedRun!.Id.Should()
                        .Be(
                            coalescedId,
                            "case '{0}' owes a NEW run rather than resurrecting the retired row",
                            testCase.Name
                        );
                    stored.Should().HaveCount(2, "case '{0}' adds the owed run beside the retired row", testCase.Name);
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled recovery effect for case '{testCase.Name}'.");
            }
        }
    }

    [Fact]
    public async Task the_occupied_instant_matrix_governs_the_claim_path()
    {
        foreach (var testCase in _Matrix)
        {
            var provider = _Create();
            var definition = _Definition();
            await provider.InsertCronJobsAsync([definition], AbortToken);
            var seededId = await _SeedOccurrenceAsync(provider, definition, testCase, _Projection);

            // NextCronOccurrence is null — the earliest-available read skips terminal rows, so dispatch takes the
            // insert path, which is the state the scheduler reaches after a cron-expression change.
            var context = new JobManagerDispatchContext(definition.Id)
            {
                FunctionName = definition.Function,
                Expression = definition.Expression,
                ScheduleRevision = definition.ScheduleRevision,
                OnNodeDeath = definition.OnNodeDeath,
                NextCronOccurrence = null,
            };

            var claimed = await provider
                .QueueCronJobOccurrencesAsync((_Projection, [context]), AbortToken)
                .ToArrayAsync(AbortToken);

            if (testCase.AccountsForInstant)
            {
                claimed
                    .Should()
                    .BeEmpty(
                        "case '{0}' accounts for the instant — firing again would double-run the tick",
                        testCase.Name
                    );
                (await provider.GetAllCronJobOccurrencesAsync(null, AbortToken)).Should().ContainSingle();
            }
            else
            {
                claimed
                    .Should()
                    .ContainSingle(
                        "case '{0}' still owes its fire — the seeding migration retired the row without creating a "
                            + "replacement",
                        testCase.Name
                    );
                claimed[0].Id.Should().NotBe(seededId);
                (await provider.GetAllCronJobOccurrencesAsync(null, AbortToken)).Should().HaveCount(2);
            }
        }
    }

    /// <summary>
    /// AE2/R3a. A terminal and a live row coexist at one instant — legal, because the unique index filters to live
    /// rows. Ordering by <c>CreatedAt</c> alone would report the older terminal row and hand the dispatcher an
    /// occurrence id that can never run.
    /// </summary>
    [Fact]
    public async Task a_live_row_is_reported_over_an_older_terminal_row_at_the_same_instant()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);

        // The terminal row carries the EARLIER CreatedAt, so CreatedAt ordering alone would surface it.
        var terminal = _Occurrence(definition, JobStatus.Cancelled, _Projection, _Now.AddMinutes(-3));
        var live = _Occurrence(definition, JobStatus.Idle, _Projection, _Now.AddMinutes(-2));
        await provider.InsertCronJobOccurrencesAsync([terminal, live], AbortToken);

        var result = await provider.MaterializeCronScheduleOccurrenceAsync(_Materialization(definition), AbortToken);

        result
            .Outcome.Should()
            .Be(
                CronScheduleMaterializationOutcome.OccurrenceExists,
                "a live row coexisting with a terminal one is the one that owns the instant"
            );
        result.OccurrenceId.Should().Be(live.Id, "the live row must not be masked by the older terminal one");
        (await provider.GetAllCronJobOccurrencesAsync(null, AbortToken)).Should().HaveCount(2);
    }

    /// <summary>
    /// KTD1a, from the WRITER side. Every other scenario here seeds a disposition directly, which proves the rule
    /// reads the column but proves nothing about who stamps what — collapsing both producers onto one value would
    /// leave all of them green. This drives the two real producers and pins them apart: they write the identical
    /// <c>SkippedReason</c> and owe opposite accounting answers, which is the entire reason the column exists.
    /// </summary>
    [Fact]
    public async Task the_two_cron_definition_updated_producers_stamp_opposite_dispositions()
    {
        var provider = _Create();

        // Producer 1 — startup seeding migration. It retires the old-expression row and creates NOTHING to take its
        // place, so the fire is still owed.
        await provider.MigrateDefinedCronJobsAsync(
            [new CronSeedDefinition("seeded-cron", "0 * * * * *", MissedRunPolicy.Coalesce, 60)],
            AbortToken
        );
        var seededDefinition = (await provider.GetCronJobsAsync(null, AbortToken)).Should().ContainSingle().Subject;
        var migratedOccurrence = _Occurrence(seededDefinition, JobStatus.Idle, _Projection, _Now.AddMinutes(-2));
        await provider.InsertCronJobOccurrencesAsync([migratedOccurrence], AbortToken);

        await provider.MigrateDefinedCronJobsAsync(
            [new CronSeedDefinition("seeded-cron", "*/5 * * * * *", MissedRunPolicy.Coalesce, 60)],
            AbortToken
        );

        var retiredBySeeding = (
            await provider.GetAllCronJobOccurrencesAsync(x => x.Id == migratedOccurrence.Id, AbortToken)
        )
            .Should()
            .ContainSingle()
            .Subject;
        retiredBySeeding.Status.Should().Be(JobStatus.Skipped);
        retiredBySeeding.SkippedReason.Should().Be("Cron definition updated");
        retiredBySeeding
            .Disposition.Should()
            .Be(
                CronOccurrenceDisposition.ReplacementOwed,
                "the seeding migration leaves no replacement behind, so the instant is still owed a fire"
            );

        // Producer 2 — a runtime schedule edit through ICronJobManager. It writes the SAME SkippedReason and then
        // installs its own replacement occurrence, so re-firing the retired instant would double-run the edit.
        var editProvider = _Create();
        var edited = _Definition();
        await editProvider.InsertCronJobsAsync([edited], AbortToken);
        var editedOccurrence = _Occurrence(edited, JobStatus.Idle, _Projection, _Now.AddMinutes(-2));
        await editProvider.InsertCronJobOccurrencesAsync([editedOccurrence], AbortToken);

        var replacementId = Guid.NewGuid();
        var update = new CronJobAtomicUpdate<FakeCronJob>(
            _Definition(edited.Id, "*/5 * * * * *"),
            edited.ScheduleRevision,
            anchor => new CronJobOccurrenceEntity<FakeCronJob>
            {
                Id = replacementId,
                CronJobId = edited.Id,
                ExecutionTime = anchor.AddMinutes(1),
                Status = JobStatus.Idle,
                OnNodeDeath = NodeDeathPolicy.Retry,
                CreatedAt = _Now,
                UpdatedAt = _Now,
            }
        );

        (await editProvider.UpdateCronJobsAtomicallyAsync([update], _Now, AbortToken))
            .Should()
            .NotBeNull("the edit holds the observed revision fence");

        var retiredByEdit = (
            await editProvider.GetAllCronJobOccurrencesAsync(x => x.Id == editedOccurrence.Id, AbortToken)
        )
            .Should()
            .ContainSingle()
            .Subject;
        retiredByEdit.Status.Should().Be(JobStatus.Skipped);
        retiredByEdit
            .SkippedReason.Should()
            .Be(
                retiredBySeeding.SkippedReason,
                "the two producers are indistinguishable by display text — only the disposition separates them"
            );
        retiredByEdit
            .Disposition.Should()
            .Be(
                CronOccurrenceDisposition.Superseded,
                "this path created the replacement itself, so re-firing the old instant would double-run the edit"
            );
        (await editProvider.GetAllCronJobOccurrencesAsync(x => x.Id == replacementId, AbortToken))
            .Should()
            .ContainSingle("the runtime edit installs its own replacement, which is why it owes nothing further");
    }

    private static async Task<Guid> _SeedOccurrenceAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        FakeCronJob definition,
        MatrixCase testCase,
        DateTime executionTime
    )
    {
        var occurrence = _Occurrence(definition, testCase.Status, executionTime, _Now.AddMinutes(-2));
        occurrence.SkippedReason = testCase.SkippedReason;
        occurrence.Disposition = testCase.Disposition;
        await provider.InsertCronJobOccurrencesAsync([occurrence], AbortToken);

        return occurrence.Id;
    }

    private static CronJobOccurrenceEntity<FakeCronJob> _Occurrence(
        FakeCronJob definition,
        JobStatus status,
        DateTime executionTime,
        DateTimeOffset createdAt
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CronJobId = definition.Id,
            CronJob = definition,
            ExecutionTime = executionTime,
            Status = status,
            OnNodeDeath = NodeDeathPolicy.Retry,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _Create()
    {
        var services = new ServiceCollection();
        services.AddHeadlessGuidGenerator();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _Owner });

        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }

    private static FakeCronJob _Definition(Guid? id = null, string expression = "0 * * * * *") =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Function = "cron-occupied-instant",
            Expression = expression,
            ScheduleRevision = 0,
            ReconciledThroughUtc = _Watermark,
            NextDueUtc = _Projection,
            CreatedAt = _Now.AddHours(-1),
            UpdatedAt = _Now.AddMinutes(-1),
        };

    private static CronScheduleMaterialization _Materialization(FakeCronJob definition) =>
        new()
        {
            Advance = new CronScheduleAdvance
            {
                CronJobId = definition.Id,
                ObservedReconciledThroughUtc = definition.ReconciledThroughUtc,
                ExpectedScheduleRevision = definition.ScheduleRevision,
                ReconciledThroughUtc = _Projection,
                NextDueUtc = _Projection.AddMinutes(1),
                RequireProjectionDue = true,
            },
            ExecutionTimeUtc = _Projection,
        };
}
