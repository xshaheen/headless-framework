// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// In-memory parity for the cron schedule-position advance. The in-memory provider must be observably
/// indistinguishable from the relational one: the same three-part fence (observed watermark, observed revision,
/// non-paused), the same single-winner outcome under concurrency, and the same committed-value result shape. A caller
/// that behaves correctly against one provider must behave correctly against the other.
/// </summary>
/// <remarks>
/// The clock here is the injected <c>TimeProvider</c>, which is this provider's coherent single-process authority —
/// there is no separate store whose clock could disagree — and the deterministic seam a frozen
/// <c>FakeTimeProvider</c> drives.
/// </remarks>
public sealed class CronSchedulePositionProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "node-a@incarnation";
    private static readonly DateTimeOffset _Now = new(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _Watermark = _Now.UtcDateTime.AddMinutes(-5);
    private static readonly DateTime _Projection = _Now.UtcDateTime.AddMinutes(-1);

    [Fact]
    public async Task should_persist_the_new_position_and_return_committed_values_when_the_fence_holds()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);
        var newProjection = _Projection.AddMinutes(1);

        var result = await provider.AdvanceCronScheduleAsync(
            _Advance(definition, reconciledThroughUtc: _Projection, nextDueUtc: newProjection),
            AbortToken
        );

        result.Should().NotBeNull();
        result!.ReconciledThroughUtc.Should().Be(_Projection);
        result.NextDueUtc.Should().Be(newProjection);
        result.StoreUtcNow.Should().Be(_Now.UtcDateTime);

        var stored = await provider.GetCronJobByIdAsync(definition.Id, AbortToken);
        stored!.ReconciledThroughUtc.Should().Be(_Projection);
        stored.NextDueUtc.Should().Be(newProjection);
    }

    [Fact]
    public async Task should_reject_the_advance_when_the_observed_watermark_no_longer_matches()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var result = await provider.AdvanceCronScheduleAsync(
            _Advance(definition, _Projection, _Projection.AddMinutes(1)) with
            {
                ObservedReconciledThroughUtc = _Watermark.AddSeconds(-30),
            },
            AbortToken
        );

        result.Should().BeNull("losing the watermark fence is reported by a null result, never by an exception");
        await _AssertPositionUnchangedAsync(provider, definition.Id);
    }

    [Fact]
    public async Task should_reject_the_advance_when_the_observed_revision_no_longer_matches()
    {
        var provider = _Create();
        var definition = _Definition(revision: 7);
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var result = await provider.AdvanceCronScheduleAsync(
            _Advance(definition, _Projection, _Projection.AddMinutes(1)) with
            {
                ExpectedScheduleRevision = 6,
            },
            AbortToken
        );

        result.Should().BeNull("a node holding a superseded definition snapshot must not apply a derived position");
        await _AssertPositionUnchangedAsync(provider, definition.Id);
    }

    [Fact]
    public async Task should_reject_the_advance_when_the_definition_is_paused()
    {
        var provider = _Create();
        var definition = _Definition(isPaused: true);
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var result = await provider.AdvanceCronScheduleAsync(
            _Advance(definition, _Projection, _Projection.AddMinutes(1)),
            AbortToken
        );

        result.Should().BeNull("a paused definition must not advance even when its projection is past due");
        await _AssertPositionUnchangedAsync(provider, definition.Id);
    }

    [Fact]
    public async Task should_reject_the_advance_when_the_projection_is_not_yet_due()
    {
        var provider = _Create();
        var futureProjection = _Now.UtcDateTime.AddMinutes(30);
        var definition = _Definition();
        definition.NextDueUtc = futureProjection;
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var notDue = await provider.AdvanceCronScheduleAsync(
            _Advance(definition, futureProjection, futureProjection.AddMinutes(1)) with
            {
                RequireProjectionDue = true,
            },
            AbortToken
        );

        notDue.Should().BeNull("the projection is 30 minutes out, so a due-gated advance must not apply");

        // Without the due gate the same advance is accepted — proving the rejection came from due-ness alone and not
        // from an unrelated arm of the fence.
        var ungated = await provider.AdvanceCronScheduleAsync(
            _Advance(definition, futureProjection, futureProjection.AddMinutes(1)),
            AbortToken
        );

        ungated.Should().NotBeNull();
    }

    [Fact]
    public async Task should_produce_exactly_one_winner_when_advances_race_from_the_same_watermark()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);

        // Every racer submits the SAME observed watermark, exactly as N scheduler nodes waking together would.
        var advance = _Advance(definition, _Projection, _Projection.AddMinutes(1));

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(async _ =>
                {
                    await Task.Yield();
                    return await provider.AdvanceCronScheduleAsync(advance, AbortToken);
                })
        );

        results
            .Count(result => result is not null)
            .Should()
            .Be(1, "the compare-and-advance must serialize concurrent callers down to a single winner");
    }

    [Fact]
    public async Task should_leave_a_sibling_definition_untouched_when_one_advances()
    {
        var provider = _Create();
        var advanced = _Definition();
        var sibling = _Definition();
        await provider.InsertCronJobsAsync([advanced, sibling], AbortToken);

        var result = await provider.AdvanceCronScheduleAsync(
            _Advance(advanced, _Projection, _Projection.AddMinutes(1)),
            AbortToken
        );

        result.Should().NotBeNull();
        await _AssertPositionUnchangedAsync(provider, sibling.Id);
    }

    private static async Task _AssertPositionUnchangedAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        Guid definitionId
    )
    {
        var stored = await provider.GetCronJobByIdAsync(definitionId, AbortToken);
        stored!.ReconciledThroughUtc.Should().Be(_Watermark);
        stored.NextDueUtc.Should().Be(_Projection);
    }

    private static CronScheduleAdvance _Advance(
        FakeCronJob definition,
        DateTime reconciledThroughUtc,
        DateTime nextDueUtc
    ) =>
        new()
        {
            CronJobId = definition.Id,
            ObservedReconciledThroughUtc = definition.ReconciledThroughUtc,
            ExpectedScheduleRevision = definition.ScheduleRevision,
            ReconciledThroughUtc = reconciledThroughUtc,
            NextDueUtc = nextDueUtc,
        };

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _Create()
    {
        var services = new ServiceCollection();
        services.AddHeadlessGuidGenerator();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _Owner });
        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }

    private static FakeCronJob _Definition(bool isPaused = false, long revision = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "cron-position",
            Expression = "0 * * * * *",
            IsPaused = isPaused,
            ScheduleRevision = revision,
            ReconciledThroughUtc = _Watermark,
            NextDueUtc = _Projection,
            CreatedAt = _Now.AddHours(-1),
            UpdatedAt = _Now.AddMinutes(-1),
        };
}
