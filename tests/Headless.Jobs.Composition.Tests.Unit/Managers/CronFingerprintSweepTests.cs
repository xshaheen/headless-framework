// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
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

        var rebased = await manager.RebaseStaleFingerprintsAsync(limit: 50, AbortToken);

        rebased.Should().Be(1);
        var after = (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!;
        after
            .NextDueUtc.Should()
            .NotBe(definition.NextDueUtc, "the projection is re-derived under the rules now in force");
        after.EvaluationFingerprint.Should().NotBe("stale-from-an-older-tzdata");
        after
            .ReconciledThroughUtc.Should()
            .Be(
                definition.ReconciledThroughUtc,
                "nothing was reconciled, only re-interpreted — moving the watermark would declare the span since it "
                    + "as accounted for"
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

        await manager.RebaseStaleFingerprintsAsync(limit: 50, AbortToken);

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

        await manager.RebaseStaleFingerprintsAsync(limit: 50, AbortToken);

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

        var rebased = await manager.RebaseStaleFingerprintsAsync(limit: 50, AbortToken);

        rebased.Should().Be(0);
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

        var rebased = await manager.RebaseStaleFingerprintsAsync(limit: 50, AbortToken);

        rebased.Should().Be(0, "a paused definition is not selectable, and a newer transition always wins the fence");
        var after = (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!;
        after.IsPaused.Should().BeTrue("the sweep must not resurrect or clobber a newer transition");
    }

    [Fact]
    public async Task should_rebase_a_definition_that_was_positioned_before_fingerprinting_existed()
    {
        var (manager, provider) = _Create();
        var definition = _Definition(nextDue: _Now.AddDays(20), fingerprint: null);
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var rebased = await manager.RebaseStaleFingerprintsAsync(limit: 50, AbortToken);

        rebased.Should().Be(1, "a null fingerprint records nothing about how the position was derived");
        (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!.EvaluationFingerprint.Should().NotBeNull();
    }

    private static (
        InternalJobsManager<FakeTimeJob, FakeCronJob> Manager,
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider
    ) _Create()
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

        return (manager, provider);
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
