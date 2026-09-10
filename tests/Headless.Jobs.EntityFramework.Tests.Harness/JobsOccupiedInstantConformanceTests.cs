// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// The occupied-instant ACCOUNTING matrix (KTD1), driven over every persisted state a row can be in, against both
/// paths that read it — materialization and recovery — and against both claim strategies.
/// </summary>
/// <remarks>
/// <para>
/// This suite exists because the four providers used to disagree completely: both natives re-materialized on every
/// terminal status, the EF and in-memory guards suppressed on every one, and <c>ApplyCronRecoveryAsync</c> sided
/// with the latter — so one row was suppressed via recovery and re-fired via the native claim path. Nothing caught
/// it, because the one test guarding the contract never started its host and so never reached the branch.
/// </para>
/// <para>
/// The expectations below are stated LITERALLY, not derived from
/// <see cref="CronOccurrenceAccounting" />; a table computed from the production rule would agree with any rule,
/// including a wrong one. <c>Tests.Provider.CronOccupiedInstantMatrixTests</c> carries the identical table for the
/// in-memory provider — the two must be kept in step by hand, which is the price of an independent statement.
/// </para>
/// </remarks>
public abstract class JobsOccupiedInstantConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    /// <summary>What recovery does with the single missed instant the case seeds.</summary>
    public enum RecoveryEffect
    {
        /// <summary>The instant is accounted for; recovery steps past it and materializes nothing.</summary>
        None,

        /// <summary>The seeded row is still claimable, so recovery repurposes it in place as the coalesced run.</summary>
        Repurposed,

        /// <summary>Nothing accounts for the instant, so recovery materializes a fresh coalesced run there.</summary>
        Created,
    }

    /// <summary>One persisted row state and the durable verdict every provider owes for it.</summary>
    /// <param name="Name">Case label, surfaced in every assertion so a failure names the row state.</param>
    /// <param name="Status">Status to seed; ignored when <paramref name="RawStatus" /> is supplied.</param>
    /// <param name="Disposition">Typed accounting disposition — the sole accounting input.</param>
    /// <param name="AccountsForInstant">Whether the row stands for its instant, so no new occurrence may appear.</param>
    /// <param name="Recovery">What <c>ApplyCronRecoveryAsync</c> owes for the same row.</param>
    /// <param name="SkippedReason">Display text, seeded so the row looks like its production counterpart.</param>
    /// <param name="RawStatus">Verbatim Status string, for a value no current binary would write.</param>
    public sealed record MatrixCase(
        string Name,
        JobStatus Status,
        CronOccurrenceDisposition Disposition,
        bool AccountsForInstant,
        RecoveryEffect Recovery,
        string? SkippedReason = null,
        string? RawStatus = null
    );

    /// <summary>
    /// Total over every <see cref="JobStatus" />, both meaningful <see cref="CronOccurrenceDisposition" /> values on
    /// <c>Skipped</c>, and one value no binary in this repo can write.
    /// </summary>
    public static MatrixCase[] Matrix =>
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
                SkippedReason: "Cron occurrence missed and resolved by recovery"
            ),
            new(
                "skipped-by-dead-node",
                JobStatus.Skipped,
                CronOccurrenceDisposition.Accounted,
                true,
                RecoveryEffect.None,
                SkippedReason: "Node is not alive!"
            ),
            // The two producers of the IDENTICAL SkippedReason, with opposite answers. This pair is the whole reason
            // the rule reads a typed column: no string test can separate them.
            new(
                "skipped-by-runtime-edit",
                JobStatus.Skipped,
                CronOccurrenceDisposition.Superseded,
                true,
                RecoveryEffect.None,
                SkippedReason: "Cron definition updated"
            ),
            new(
                "skipped-by-seeding-migration",
                JobStatus.Skipped,
                CronOccurrenceDisposition.ReplacementOwed,
                false,
                RecoveryEffect.Created,
                SkippedReason: "Cron definition updated"
            ),
            // Fail closed: a status this binary does not recognize must neither throw on the read path nor become a
            // silent re-fire.
            new(
                "unknown-status-from-a-newer-binary",
                JobStatus.Idle,
                CronOccurrenceDisposition.Accounted,
                true,
                RecoveryEffect.None,
                RawStatus: "SomeFutureStatus"
            ),
        ];

    public virtual async Task the_occupied_instant_matrix_governs_materialization()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("matrix-materialize");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        foreach (var testCase in Matrix)
        {
            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(
                cronId,
                "matrix-materialize",
                "* * * * *",
                NodeDeathPolicy.Retry,
                ct,
                reconciledThroughOffsetSeconds: -600,
                nextDueOffsetSeconds: -300
            );
            var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
            var occurrenceId = Guid.NewGuid();
            await _SeedCaseAsync(cronId, occurrenceId, testCase, seeded.NextDueUtc, ct);

            var result = await persistence.MaterializeCronScheduleOccurrenceAsync(_Materialization(cronId, seeded), ct);

            var expectedOutcome =
                !testCase.AccountsForInstant ? CronScheduleMaterializationOutcome.OccurrenceCreated
                : _IsLive(testCase) ? CronScheduleMaterializationOutcome.OccurrenceExists
                : CronScheduleMaterializationOutcome.OccurrenceAlreadyTerminal;

            result
                .Outcome.Should()
                .Be(expectedOutcome, "case '{0}' must resolve materialization by the accounting matrix", testCase.Name);
            (await _CountAtInstantAsync(cronId, ct))
                .Should()
                .Be(
                    testCase.AccountsForInstant ? 1 : 2,
                    "case '{0}' must {1} a second occurrence at the instant",
                    testCase.Name,
                    testCase.AccountsForInstant ? "not create" : "create"
                );

            if (testCase.AccountsForInstant)
            {
                result
                    .OccurrenceId.Should()
                    .Be(occurrenceId, "case '{0}' must report the row that accounts for the instant", testCase.Name);
            }
            else
            {
                result
                    .OccurrenceId.Should()
                    .NotBe(
                        occurrenceId,
                        "case '{0}' owes a NEW occurrence, not the retired one it replaces",
                        testCase.Name
                    );
            }

            // The seeded row is never disturbed, whichever way the verdict fell.
            var persisted = await fixture.ReadCronOccurrenceDispositionAsync(occurrenceId, ct);
            persisted
                .Status.Should()
                .Be(
                    testCase.RawStatus ?? testCase.Status.ToString(),
                    "case '{0}' must leave the pre-existing row alone",
                    testCase.Name
                );
            persisted.Disposition.Should().Be(testCase.Disposition.ToString());
        }
    }

    public virtual async Task the_occupied_instant_matrix_governs_recovery()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("matrix-recovery");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        foreach (var testCase in Matrix)
        {
            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(
                cronId,
                "matrix-recovery",
                "* * * * *",
                NodeDeathPolicy.Retry,
                ct,
                reconciledThroughOffsetSeconds: -600,
                nextDueOffsetSeconds: -300
            );
            var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);
            var missedInstant = seeded.NextDueUtc;
            var occurrenceId = Guid.NewGuid();
            await _SeedCaseAsync(cronId, occurrenceId, testCase, missedInstant, ct);
            var coalescedId = Guid.NewGuid();

            var result = await persistence.ApplyCronRecoveryAsync(
                new CronRecoveryRequest
                {
                    CronJobId = cronId,
                    ObservedReconciledThroughUtc = seeded.ReconciledThroughUtc,
                    ExpectedScheduleRevision = 0,
                    RecoveredThroughUtc = missedInstant,
                    NextDueUtc = missedInstant.AddMinutes(1),
                    BoundedProgressThroughUtc = missedInstant,
                    NextDueAfterBoundedProgressUtc = missedInstant.AddMinutes(1),
                    EvaluationSaturated = false,
                    Policy = MissedRunPolicy.Coalesce,
                    EarliestMissedUtc = missedInstant,
                    MissedInstantsUtc = [missedInstant],
                    CoalescedOccurrenceId = coalescedId,
                    OperationTimeUtc = DateTimeOffset.UtcNow,
                },
                ct
            );

            result.Should().NotBeNull("case '{0}' holds the observed fence, so recovery must apply", testCase.Name);

            switch (testCase.Recovery)
            {
                case RecoveryEffect.None:
                    result!
                        .CoalescedRun.Should()
                        .BeNull(
                            "case '{0}' accounts for the missed instant, so recovery owes no run there",
                            testCase.Name
                        );
                    (await _CountAtInstantAsync(cronId, ct))
                        .Should()
                        .Be(1, "case '{0}' must not gain a second row at the instant", testCase.Name);
                    break;
                case RecoveryEffect.Repurposed:
                    result!
                        .CoalescedRun.Should()
                        .NotBeNull("case '{0}' is still claimable, so recovery repurposes it", testCase.Name);
                    result
                        .CoalescedRun!.Id.Should()
                        .Be(occurrenceId, "case '{0}' repurposes IN PLACE rather than duplicating", testCase.Name);
                    (await _CountAtInstantAsync(cronId, ct)).Should().Be(1);
                    break;
                case RecoveryEffect.Created:
                    result!
                        .CoalescedRun.Should()
                        .NotBeNull(
                            "case '{0}' leaves the instant unaccounted for, so the owed run is materialized",
                            testCase.Name
                        );
                    result
                        .CoalescedRun!.Id.Should()
                        .Be(
                            coalescedId,
                            "case '{0}' owes a NEW run — reusing the retired row would resurrect it",
                            testCase.Name
                        );
                    (await _CountAtInstantAsync(cronId, ct))
                        .Should()
                        .Be(2, "case '{0}' must add the owed run beside the retired row", testCase.Name);
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled recovery effect for case '{testCase.Name}'.");
            }
        }
    }

    public virtual async Task the_occupied_instant_matrix_governs_the_claim_path()
    {
        var ct = AbortToken;

        // Both strategies, because the guard lives in a different place in each: raw INSERT … WHERE NOT EXISTS in
        // the natives, a batched projection probe in the portable CAS path. Divergence between them is exactly the
        // defect this suite exists to prevent.
        foreach (var useNativeClaims in new[] { true, false })
        {
            await fixture.ResetDatabaseAsync(ct);
            using var host = fixture.BuildHost(
                $"matrix-claim-{(useNativeClaims ? "native" : "cas")}",
                useNativeClaims: useNativeClaims
            );
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
            await host.StartAsync(ct);

            try
            {
                var persistence = _Persistence(host);

                foreach (var testCase in Matrix)
                {
                    var cronId = Guid.NewGuid();
                    await fixture.SeedCronJobAsync(cronId, "matrix-claim", "* * * * *", NodeDeathPolicy.Retry, ct);
                    var executionTime = _WholeSecondsUtcNow().AddMinutes(-1);
                    var occurrenceId = Guid.NewGuid();
                    await _SeedCaseAsync(cronId, occurrenceId, testCase, executionTime, ct);

                    var context = new JobManagerDispatchContext(cronId)
                    {
                        FunctionName = "matrix-claim",
                        Expression = "* * * * *",
                        OnNodeDeath = NodeDeathPolicy.Retry,
                        NextCronOccurrence = null,
                    };

                    var claimed = await persistence
                        .QueueCronJobOccurrencesAsync((executionTime, [context]), ct)
                        .ToArrayAsync(ct);

                    var strategy = useNativeClaims ? "native" : "portable CAS";

                    if (testCase.AccountsForInstant)
                    {
                        claimed
                            .Should()
                            .BeEmpty(
                                "case '{0}' on the {1} strategy accounts for the instant — firing again would "
                                    + "double-run the tick",
                                testCase.Name,
                                strategy
                            );
                        (await _CountAtInstantAsync(cronId, ct))
                            .Should()
                            .Be(1, "case '{0}' on the {1} strategy must not insert", testCase.Name, strategy);
                    }
                    else
                    {
                        claimed
                            .Should()
                            .ContainSingle(
                                "case '{0}' on the {1} strategy still owes its fire — the seeding migration retired "
                                    + "the row without creating a replacement",
                                testCase.Name,
                                strategy
                            );
                        claimed[0].Id.Should().NotBe(occurrenceId);
                        (await _CountAtInstantAsync(cronId, ct))
                            .Should()
                            .Be(2, "case '{0}' on the {1} strategy inserts the owed row", testCase.Name, strategy);
                    }
                }
            }
            finally
            {
                await host.StopAsync(ct);
            }
        }
    }

    /// <summary>
    /// AE2/R3a. A terminal and a live row coexist at one instant — legal, because the unique index filters to live
    /// rows. Ordering by <c>CreatedAt</c> alone would report the older terminal row and hand the dispatcher an
    /// occurrence id that can never run.
    /// </summary>
    public virtual async Task a_live_row_is_reported_over_an_older_terminal_row_at_the_same_instant()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("matrix-coexist");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronId,
            "matrix-coexist",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -600,
            nextDueOffsetSeconds: -300
        );
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronId, ct);

        // The terminal row is created FIRST, so CreatedAt ordering alone would surface it.
        var terminalId = Guid.NewGuid();
        await fixture.SeedCronOccurrenceAsync(
            terminalId,
            cronId,
            (int)JobStatus.Cancelled,
            ownerId: null,
            NodeDeathPolicy.Retry,
            lockedUntil: null,
            seeded.NextDueUtc,
            ct
        );
        var liveId = Guid.NewGuid();
        await fixture.SeedCronOccurrenceAsync(
            liveId,
            cronId,
            (int)JobStatus.Idle,
            ownerId: null,
            NodeDeathPolicy.Retry,
            lockedUntil: null,
            seeded.NextDueUtc,
            ct
        );

        var result = await persistence.MaterializeCronScheduleOccurrenceAsync(_Materialization(cronId, seeded), ct);

        result
            .Outcome.Should()
            .Be(
                CronScheduleMaterializationOutcome.OccurrenceExists,
                "a live row coexisting with a terminal one is the one that owns the instant"
            );
        result.OccurrenceId.Should().Be(liveId, "the live row must not be masked by the older terminal one");
        (await _CountAtInstantAsync(cronId, ct)).Should().Be(2, "neither row is disturbed and none is added");
    }

    private static bool _IsLive(MatrixCase testCase)
    {
        // A raw status no binary writes is not live by construction, whatever the enum field says.
        return testCase.RawStatus is null
            && testCase.Status is JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress;
    }

    private Task _SeedCaseAsync(
        Guid cronJobId,
        Guid occurrenceId,
        MatrixCase testCase,
        DateTime executionTime,
        CancellationToken cancellationToken
    )
    {
        // A live row is seeded WITHOUT a lease so the claim path's acquire predicate is not the thing under test;
        // the accounting verdict is.
        return fixture.SeedCronOccurrenceAsync(
            occurrenceId,
            cronJobId,
            (int)testCase.Status,
            ownerId: null,
            NodeDeathPolicy.Retry,
            lockedUntil: null,
            executionTime,
            cancellationToken,
            testCase.SkippedReason,
            testCase.Disposition,
            testCase.RawStatus
        );
    }

    private async Task<int> _CountAtInstantAsync(Guid cronJobId, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM {fixture.QualifiedCronJobOccurrencesTable} WHERE \"CronJobId\" = @cronJobId;";
        JobsCoordinationFixtureExtensions.AddParameter(command, "@cronJobId", cronJobId);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// PostgreSQL materializes <c>DateTime</c> at microsecond granularity, so a tick-precision instant would miss
    /// every equality predicate under test and the scenario would pass for a reason unrelated to accounting.
    /// </summary>
    private static DateTime _WholeSecondsUtcNow()
    {
        var now = DateTime.UtcNow;

        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);
    }

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Persistence(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

    private static CronScheduleMaterialization _Materialization(
        Guid cronJobId,
        (DateTime ReconciledThroughUtc, DateTime NextDueUtc) position
    ) =>
        new()
        {
            Advance = new CronScheduleAdvance
            {
                CronJobId = cronJobId,
                ObservedReconciledThroughUtc = position.ReconciledThroughUtc,
                ExpectedScheduleRevision = 0,
                ReconciledThroughUtc = position.NextDueUtc,
                NextDueUtc = position.NextDueUtc.AddMinutes(1),
                RequireProjectionDue = true,
            },
            ExecutionTimeUtc = position.NextDueUtc,
        };
}
