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
/// The fingerprint sweep: detecting that a definition was positioned under schedule-interpretation rules that have
/// since changed, and rebasing it without manufacturing a backlog out of the change.
/// </summary>
/// <remarks>
/// Staleness is simulated by persisting a fingerprint the running evaluator does not produce, which is exactly what a
/// tzdata update looks like from the store's side. That keeps the tests independent of whatever IANA data the host
/// ships — a test that only fails on certain CI images teaches nothing.
/// </remarks>
public sealed class CronFingerprintSweepTests : TestBase
{
    public sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    public sealed class FakeCronJob : CronJobEntity;

    private const string _NodeA = "node-a";
    private static readonly DateTime _Now = new(2026, 07, 26, 12, 00, 00, DateTimeKind.Utc);

    [Fact]
    public async Task should_rebase_a_definition_whose_interpretation_rules_changed()
    {
        var (manager, provider) = _Create();
        // Projection far in the future, so a due-ness-gated sweep would never look at it — the case AE14 is about.
        var definition = _Definition(nextDue: _Now.AddDays(20), fingerprint: "stale-from-an-older-tzdata");
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var result = await manager.RebaseStaleFingerprintsAsync(limit: 50, cancellationToken: AbortToken);

        result.Rebased.Should().Be(1);
        var after = (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!;
        after
            .NextDueUtc.Should()
            .NotBe(definition.NextDueUtc, "the projection is re-derived under the rules now in force");
        after.EvaluationFingerprint.Should().NotBe("stale-from-an-older-tzdata");
        after
            .ReconciledThroughUtc.Should()
            .Be(
                _Now,
                "environmental rule drift is a deliberate non-replay boundary, so prior backlog is discarded at the store-time anchor"
            );
    }

    [Fact]
    public async Task should_rebase_even_when_the_new_instant_is_earlier_than_the_stale_projection()
    {
        var (manager, provider) = _Create();
        // The pathological case: the stale projection sits 20 days out, so nothing due-based would ever surface it,
        // yet current rules put the real next occurrence within the minute.
        var definition = _Definition(nextDue: _Now.AddDays(20), fingerprint: "stale");
        await provider.InsertCronJobsAsync([definition], AbortToken);

        await manager.RebaseStaleFingerprintsAsync(limit: 50, cancellationToken: AbortToken);

        var after = (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!;
        after
            .NextDueUtc.Should()
            .BeBefore(
                _Now.AddDays(20),
                "an occurrence the new rules moved earlier must not stay suppressed behind the stale later projection"
            );
    }

    [Fact]
    public async Task should_never_rebase_to_an_instant_in_the_past()
    {
        var (manager, provider) = _Create();
        // Watermark far behind: derived purely from it, the next occurrence would land in the past and read as a
        // misfire. Anchoring at or after the store instant is what stops a rule change becoming a fake backlog.
        var definition = _Definition(nextDue: _Now.AddDays(20), fingerprint: "stale");
        definition.ReconciledThroughUtc = _Now.AddDays(-30);
        await provider.InsertCronJobsAsync([definition], AbortToken);

        await manager.RebaseStaleFingerprintsAsync(limit: 50, cancellationToken: AbortToken);

        var after = (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!;
        after
            .NextDueUtc.Should()
            .BeOnOrAfter(_Now, "a tick the changed rules moved into the past must not be replayed as missed");
    }

    [Fact]
    public async Task should_not_touch_a_definition_whose_fingerprint_is_current()
    {
        var (manager, provider) = _Create();
        var cache = new CronScheduleCache(TimeZoneInfo.Utc);
        var definition = _Definition(
            nextDue: _Now.AddDays(20),
            fingerprint: cache.ComputeEvaluationFingerprint(timeZoneId: null)
        );
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var result = await manager.RebaseStaleFingerprintsAsync(limit: 50, cancellationToken: AbortToken);

        result.Rebased.Should().Be(0);
        var after = (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!;
        after.NextDueUtc.Should().Be(definition.NextDueUtc, "matching rules means there is nothing to re-derive");
    }

    [Fact]
    public async Task should_lose_the_fence_to_a_concurrent_definition_transition()
    {
        var (manager, provider) = _Create();
        var definition = _Definition(nextDue: _Now.AddDays(20), fingerprint: "stale");
        await provider.InsertCronJobsAsync([definition], AbortToken);

        // A pause commits between the sweep's read and its write: the revision moves, so the rebase must lose.
        await provider.PauseCronJobAsync(definition.Id, new DateTimeOffset(_Now, TimeSpan.Zero), AbortToken);

        var result = await manager.RebaseStaleFingerprintsAsync(limit: 50, cancellationToken: AbortToken);

        result
            .Rebased.Should()
            .Be(0, "a paused definition is not selectable, and a newer transition always wins the fence");
        var after = (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!;
        after.IsPaused.Should().BeTrue("the sweep must not resurrect or clobber a newer transition");
    }

    [Fact]
    public async Task should_rebase_a_definition_that_was_positioned_before_fingerprinting_existed()
    {
        var (manager, provider) = _Create();
        var definition = _Definition(nextDue: _Now.AddDays(20), fingerprint: null);
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var result = await manager.RebaseStaleFingerprintsAsync(limit: 50, cancellationToken: AbortToken);

        result.Rebased.Should().Be(1, "a null fingerprint records nothing about how the position was derived");
        (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!.EvaluationFingerprint.Should().NotBeNull();
    }

    /// <summary>
    /// The store applies the batch limit BEFORE the manager can confirm whether a candidate is really stale, so any
    /// definition that is permanently a candidate consumes batch budget on every sweep, forever. A timezone missing
    /// from the known-fingerprint set produces exactly that, and it starves the definitions the sweep exists to fix.
    /// </summary>
    [Fact]
    public async Task should_not_let_definitions_in_other_time_zones_crowd_out_a_stale_one()
    {
        var (manager, provider) = _Create();
        var cache = new CronScheduleCache(TimeZoneInfo.Utc);

        // Two definitions in a zone that is NOT the scheduler fallback, each already carrying the correct fingerprint
        // for its own zone, ordered ahead of the stale one by id.
        var current = cache.ComputeEvaluationFingerprint("America/New_York");
        var a = _Zoned(new Guid("00000001-0000-0000-0000-000000000000"), "America/New_York", current);
        var b = _Zoned(new Guid("00000002-0000-0000-0000-000000000000"), "America/New_York", current);
        var stale = _Zoned(new Guid("00000003-0000-0000-0000-000000000000"), timeZoneId: null, "stale-older-tzdata");

        await provider.InsertCronJobsAsync([a, b, stale], AbortToken);

        // A limit of 2 stands in for the default batch size with more zoned definitions than fit in one batch.
        var result = await manager.RebaseStaleFingerprintsAsync(limit: 2, cancellationToken: AbortToken);

        result
            .Rebased.Should()
            .Be(1, "the genuinely stale definition must be reached, not crowded out by current ones");
        (await provider.GetCronJobByIdAsync(stale.Id, AbortToken))!
            .EvaluationFingerprint.Should()
            .NotBe("stale-older-tzdata");
    }

    /// <summary>
    /// A zone this host cannot resolve is exactly what the tzdata update this sweep exists for can produce. Such a
    /// definition is permanently a candidate, so letting the resolve failure escape would abort every sweep at that
    /// row for good.
    /// </summary>
    [Fact]
    public async Task should_skip_an_unresolvable_time_zone_without_abandoning_the_sweep()
    {
        var (manager, provider) = _Create();
        var unresolvable = _Zoned(new Guid("00000001-0000-0000-0000-000000000000"), "Mars/Olympus", "stale");
        var healthy = _Zoned(new Guid("00000002-0000-0000-0000-000000000000"), timeZoneId: null, "stale");

        await provider.InsertCronJobsAsync([unresolvable, healthy], AbortToken);

        var result = await manager.RebaseStaleFingerprintsAsync(limit: 50, cancellationToken: AbortToken);

        result.Rebased.Should().Be(1, "the healthy definition is still rebased");
        (await provider.GetCronJobByIdAsync(healthy.Id, AbortToken))!.EvaluationFingerprint.Should().NotBe("stale");
        (await provider.GetCronJobByIdAsync(unresolvable.Id, AbortToken))!
            .EvaluationFingerprint.Should()
            .Be("stale", "a definition whose zone cannot be resolved here cannot be repositioned here either");
    }

    /// <summary>
    /// #830: whether a zone resolves is a property of the RUNNING HOST's timezone database, not of the definition, so
    /// one node with stale tzdata must not write the durable defer state that suppresses a definition fleet-wide.
    /// Every peer that resolves the zone keeps scheduling it.
    /// </summary>
    [Fact]
    public async Task should_not_durably_defer_a_definition_whose_time_zone_only_this_host_cannot_resolve()
    {
        var (manager, provider) = _Create();
        var unresolvable = _Zoned(Guid.NewGuid(), "Mars/Olympus", "stale");
        await provider.InsertCronJobsAsync([unresolvable], AbortToken);

        var result = await manager.RebaseStaleFingerprintsAsync(limit: 50, cancellationToken: AbortToken);

        result.Deferred.Should().Be(0, "a per-host condition must never be recorded as a definitional one");
        result.SkippedNodeLocal.Should().Be(1, "the node reports what it could not evaluate without acting on it");
        var stored = (await provider.GetCronJobByIdAsync(unresolvable.Id, AbortToken))!;
        stored
            .FingerprintRetryAfterUtc.Should()
            .BeNull("a durable retry boundary quarantines the definition on every node, not just this one");
        stored.FingerprintFailureCount.Should().Be(0);
    }

    /// <summary>
    /// The other side of the reclassification: an error every host reads the same way — an undefined policy, a
    /// negative grace, an unparseable expression, a blank zone identifier — still earns durable defer. Losing that
    /// would let a genuinely broken definition burn every node's poll forever.
    /// </summary>
    [Theory]
    [InlineData("blank-time-zone")]
    [InlineData("invalid-expression")]
    [InlineData("undefined-policy")]
    [InlineData("negative-grace")]
    public async Task should_still_durably_defer_a_definition_that_is_invalid_on_every_host(string defect)
    {
        var (manager, provider) = _Create();
        var invalid = _Zoned(Guid.NewGuid(), timeZoneId: null, "stale");

        switch (defect)
        {
            case "blank-time-zone":
                invalid.TimeZoneId = "   ";
                break;
            case "invalid-expression":
                invalid.Expression = "not-a-cron-expression";
                break;
            case "undefined-policy":
                invalid.OnMissedRun = (MissedRunPolicy)97;
                break;
            default:
                invalid.MissedRunGraceSeconds = -1;
                break;
        }

        await provider.InsertCronJobsAsync([invalid], AbortToken);

        var result = await manager.RebaseStaleFingerprintsAsync(limit: 50, cancellationToken: AbortToken);

        result.Deferred.Should().Be(1);
        result.SkippedNodeLocal.Should().Be(0, "the defect is in the definition, not in this host");
        var stored = (await provider.GetCronJobByIdAsync(invalid.Id, AbortToken))!;
        stored.FingerprintRetryAfterUtc.Should().NotBeNull();
        stored.FingerprintFailureCount.Should().Be(1);
    }

    [Fact]
    public async Task should_durably_defer_invalid_rows_without_starving_the_next_page()
    {
        var (manager, provider) = _Create();
        var invalidA = _Zoned(new Guid("00000001-0000-0000-0000-000000000000"), timeZoneId: null, "stale");
        invalidA.Expression = "not-a-cron-expression";
        var invalidB = _Zoned(new Guid("00000002-0000-0000-0000-000000000000"), timeZoneId: null, "stale");
        invalidB.OnMissedRun = (MissedRunPolicy)97;
        var healthy = _Zoned(new Guid("00000003-0000-0000-0000-000000000000"), timeZoneId: null, "stale");
        await provider.InsertCronJobsAsync([invalidA, invalidB, healthy], AbortToken);

        var first = await manager.RebaseStaleFingerprintsAsync(limit: 2, cancellationToken: AbortToken);
        var second = await manager.RebaseStaleFingerprintsAsync(
            limit: 2,
            afterId: first.NextCursorId,
            throughId: first.SnapshotHighWatermarkId,
            cancellationToken: AbortToken
        );

        first.Deferred.Should().Be(2);
        first.HasMore.Should().BeTrue();
        second.Rebased.Should().Be(1);
        (await provider.GetCronJobByIdAsync(invalidA.Id, AbortToken))!.FingerprintFailureCount.Should().Be(1);
        (await provider.GetCronJobByIdAsync(invalidA.Id, AbortToken))!.FingerprintRetryAfterUtc.Should().NotBeNull();
        (await provider.GetCronJobByIdAsync(healthy.Id, AbortToken))!.EvaluationFingerprint.Should().NotBe("stale");
    }

    [Fact]
    public async Task should_wrap_one_bounded_page_to_a_new_stale_low_id()
    {
        var (manager, provider) = _Create();
        var middle = _Zoned(new Guid("00000002-0000-0000-0000-000000000000"), timeZoneId: null, "stale");
        var high = _Zoned(new Guid("00000003-0000-0000-0000-000000000000"), timeZoneId: null, "stale");
        await provider.InsertCronJobsAsync([middle, high], AbortToken);
        var first = await manager.RebaseStaleFingerprintsAsync(limit: 1, cancellationToken: AbortToken);

        var low = _Zoned(new Guid("00000001-0000-0000-0000-000000000000"), timeZoneId: null, "stale");
        await provider.InsertCronJobsAsync([low], AbortToken);
        var wrapped = await manager.RebaseStaleFingerprintsAsync(
            limit: 2,
            afterId: first.NextCursorId,
            throughId: first.SnapshotHighWatermarkId,
            allowWrap: true,
            cancellationToken: AbortToken
        );

        wrapped.Rebased.Should().Be(2);
        (await provider.GetCronJobByIdAsync(low.Id, AbortToken))!.EvaluationFingerprint.Should().NotBe("stale");
        (await provider.GetCronJobByIdAsync(high.Id, AbortToken))!.EvaluationFingerprint.Should().NotBe("stale");
    }

    [Fact]
    public async Task should_return_a_wrapped_lookahead_after_an_exactly_full_forward_page()
    {
        var (manager, provider) = _Create();
        var low = _Zoned(new Guid("00000001-0000-0000-0000-000000000000"), timeZoneId: null, "stale");
        var middle = _Zoned(new Guid("00000003-0000-0000-0000-000000000000"), timeZoneId: null, "stale");
        var high = _Zoned(new Guid("00000004-0000-0000-0000-000000000000"), timeZoneId: null, "stale");
        await provider.InsertCronJobsAsync([low, middle, high], AbortToken);

        var forward = await manager.RebaseStaleFingerprintsAsync(
            limit: 2,
            afterId: new Guid("00000002-0000-0000-0000-000000000000"),
            throughId: high.Id,
            allowWrap: true,
            cancellationToken: AbortToken
        );
        var wrapped = await manager.RebaseStaleFingerprintsAsync(
            limit: 2,
            afterId: forward.NextCursorId,
            throughId: forward.SnapshotHighWatermarkId,
            allowWrap: !forward.Wrapped,
            cancellationToken: AbortToken
        );

        forward.Rebased.Should().Be(2);
        forward.HasMore.Should().BeTrue();
        forward.Wrapped.Should().BeFalse("the full forward page only probed the wrapped range");
        wrapped.Rebased.Should().Be(1);
        wrapped.Wrapped.Should().BeTrue();
        (await provider.GetCronJobByIdAsync(low.Id, AbortToken))!.EvaluationFingerprint.Should().NotBe("stale");
    }

    [Fact]
    public async Task should_reject_a_non_positive_page_limit()
    {
        var (manager, _) = _Create();

        var act = () => manager.RebaseStaleFingerprintsAsync(0, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task should_back_off_deterministic_failures_exponentially_and_cap_at_24_hours()
    {
        var (_, provider, time) = _CreateWithTime();
        var definition = _Zoned(Guid.NewGuid(), "Mars/Olympus", "stale");
        await provider.InsertCronJobsAsync([definition], AbortToken);
        var expectedHours = new[] { 1, 2, 4, 8, 16, 24, 24 };

        for (var index = 0; index < expectedHours.Length; index++)
        {
            var before = time.GetUtcNow().UtcDateTime;
            var accepted = await provider.DeferStaleFingerprintDefinitionAsync(
                new CronFingerprintDeferRequest
                {
                    CronJobId = definition.Id,
                    ExpectedScheduleRevision = definition.ScheduleRevision,
                    ObservedReconciledThroughUtc = definition.ReconciledThroughUtc,
                    ObservedEvaluationFingerprint = definition.EvaluationFingerprint,
                    InitialDelay = TimeSpan.FromHours(1),
                    MaximumDelay = TimeSpan.FromHours(24),
                },
                AbortToken
            );

            accepted.Should().BeTrue();
            var stored = (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!;
            stored.FingerprintFailureCount.Should().Be(index + 1);
            stored.FingerprintRetryAfterUtc.Should().Be(before.AddHours(expectedHours[index]));
            time.Advance(TimeSpan.FromHours(expectedHours[index]) + TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task should_leave_defer_state_unchanged_when_the_defer_fence_is_lost()
    {
        var (_, provider, _) = _CreateWithTime();
        var definition = _Definition(_Now.AddDays(1), "stale");
        definition.FingerprintFailureCount = 2;
        definition.FingerprintRetryAfterUtc = _Now.AddHours(1);
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var accepted = await provider.DeferStaleFingerprintDefinitionAsync(
            new CronFingerprintDeferRequest
            {
                CronJobId = definition.Id,
                ExpectedScheduleRevision = definition.ScheduleRevision + 1,
                ObservedReconciledThroughUtc = definition.ReconciledThroughUtc,
                ObservedEvaluationFingerprint = definition.EvaluationFingerprint,
                InitialDelay = TimeSpan.FromHours(1),
                MaximumDelay = TimeSpan.FromHours(24),
            },
            AbortToken
        );

        accepted.Should().BeFalse();
        var stored = (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!;
        stored.FingerprintFailureCount.Should().Be(2);
        stored.FingerprintRetryAfterUtc.Should().Be(_Now.AddHours(1));
    }

    [Fact]
    public async Task should_clear_defer_state_after_a_successful_rebase()
    {
        var (manager, provider, _) = _CreateWithTime();
        var definition = _Definition(_Now.AddDays(1), "stale");
        definition.FingerprintFailureCount = 3;
        definition.FingerprintRetryAfterUtc = _Now.AddMinutes(-1);
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var result = await manager.RebaseStaleFingerprintsAsync(limit: 1, cancellationToken: AbortToken);

        result.Rebased.Should().Be(1);
        var stored = (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!;
        stored.FingerprintFailureCount.Should().Be(0);
        stored.FingerprintRetryAfterUtc.Should().BeNull();
    }

    [Fact]
    public async Task should_propagate_provider_argument_failures_without_deferring_the_definition()
    {
        var definition = _Definition(_Now.AddDays(1), "stale");
        var candidate = _Candidate(definition);
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        provider.GetAllCronJobExpressionsAsync(AbortToken).Returns([definition]);
        provider
            .GetStaleFingerprintDefinitionsAsync(Arg.Any<CronFingerprintSweepRequest>(), AbortToken)
            .Returns(
                new CronFingerprintSweepPage
                {
                    Candidates = [candidate],
                    StoreUtcNow = _Now,
                    SnapshotHighWatermarkId = definition.Id,
                    HasMore = false,
                }
            );
        provider
            .AdvanceCronScheduleAsync(Arg.Any<CronScheduleAdvance>(), AbortToken)
            .Returns<Task<CronScheduleAdvanceResult?>>(_ => throw new ArgumentException("provider translation failed"));
        var manager = _CreateManager(provider, new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero)));

        var act = () => manager.RebaseStaleFingerprintsAsync(limit: 1, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("provider translation failed");
        await provider
            .DidNotReceive()
            .DeferStaleFingerprintDefinitionAsync(Arg.Any<CronFingerprintDeferRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_load_current_fingerprints_once_per_multi_page_snapshot()
    {
        var first = _Definition(_Now.AddDays(1), "stale");
        first.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = _Definition(_Now.AddDays(1), "stale");
        second.Id = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        provider.GetAllCronJobExpressionsAsync(AbortToken).Returns([first, second]);
        provider
            .GetStaleFingerprintDefinitionsAsync(Arg.Any<CronFingerprintSweepRequest>(), AbortToken)
            .Returns(
                new CronFingerprintSweepPage
                {
                    Candidates = [_Candidate(first)],
                    StoreUtcNow = _Now,
                    SnapshotHighWatermarkId = second.Id,
                    HasMore = true,
                },
                new CronFingerprintSweepPage
                {
                    Candidates = [_Candidate(second)],
                    StoreUtcNow = _Now,
                    SnapshotHighWatermarkId = second.Id,
                    HasMore = false,
                }
            );
        var manager = _CreateManager(provider, new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero)));

        var firstPage = await manager.RebaseStaleFingerprintsAsync(limit: 1, cancellationToken: AbortToken);
        await manager.RebaseStaleFingerprintsAsync(
            limit: 1,
            afterId: firstPage.NextCursorId,
            throughId: firstPage.SnapshotHighWatermarkId,
            cancellationToken: AbortToken
        );

        await provider.Received(1).GetAllCronJobExpressionsAsync(AbortToken);

        await manager.RebaseStaleFingerprintsAsync(limit: 1, cancellationToken: AbortToken);
        await provider.Received(2).GetAllCronJobExpressionsAsync(AbortToken);
    }

    private static CronDispatchCandidate _Candidate(FakeCronJob definition) =>
        new()
        {
            CronJobId = definition.Id,
            FunctionName = definition.Function,
            Expression = definition.Expression,
            TimeZoneId = definition.TimeZoneId,
            ScheduleRevision = definition.ScheduleRevision,
            ReconciledThroughUtc = definition.ReconciledThroughUtc,
            NextDueUtc = definition.NextDueUtc,
            Retries = definition.Retries,
            RetryIntervals = definition.RetryIntervals,
            OnNodeDeath = definition.OnNodeDeath,
            MissedRunGraceSeconds = definition.MissedRunGraceSeconds,
            OnMissedRun = definition.OnMissedRun,
            EvaluationFingerprint = definition.EvaluationFingerprint,
        };

    private static FakeCronJob _Zoned(Guid id, string? timeZoneId, string? fingerprint)
    {
        var definition = _Definition(nextDue: _Now.AddDays(20), fingerprint);
        definition.Id = id;
        definition.TimeZoneId = timeZoneId;

        return definition;
    }

    private static (
        InternalJobsManager<FakeTimeJob, FakeCronJob> Manager,
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider
    ) _Create()
    {
        var created = _CreateWithTime();
        return (created.Manager, created.Provider);
    }

    private static (
        InternalJobsManager<FakeTimeJob, FakeCronJob> Manager,
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider,
        FakeTimeProvider Time
    ) _CreateWithTime()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _NodeA });
        services.AddSingleton(Substitute.For<IJobsHostScheduler>());
        var sp = services.BuildServiceProvider();

        var provider = new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(sp);
        var manager = new InternalJobsManager<FakeTimeJob, FakeCronJob>(
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

        return (manager, provider, time);
    }

    private static InternalJobsManager<FakeTimeJob, FakeCronJob> _CreateManager(
        IJobPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        FakeTimeProvider time
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _NodeA });
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

    private static FakeCronJob _Definition(DateTime nextDue, string? fingerprint) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "cron-sweep",
            Expression = "0 * * * * *",
            ScheduleRevision = 2,
            ReconciledThroughUtc = _Now.AddMinutes(-1),
            NextDueUtc = nextDue,
            EvaluationFingerprint = fingerprint,
            CreatedAt = new DateTimeOffset(_Now.AddHours(-1), TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(_Now.AddMinutes(-1), TimeSpan.Zero),
        };
}
