// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
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

    [Fact]
    public async Task should_commit_an_idle_occurrence_and_position_as_one_materialization()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);

        var result = await provider.MaterializeCronScheduleOccurrenceAsync(_Materialization(definition), AbortToken);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceCreated);
        result.SchedulePosition.Should().NotBeNull();
        result.SchedulePosition!.ReconciledThroughUtc.Should().Be(_Projection);
        result.OccurrenceId.Should().NotBeNull();

        var occurrences = await provider.GetAllCronJobOccurrencesAsync(null, AbortToken);
        occurrences.Should().ContainSingle();
        occurrences[0].Id.Should().Be(result.OccurrenceId!.Value);
        occurrences[0].ExecutionTime.Should().Be(_Projection);
        occurrences[0].Status.Should().Be(JobStatus.Idle);
        occurrences[0].OwnerId.Should().BeNull();
        occurrences[0].LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task should_create_one_occurrence_when_materializations_race_from_the_same_position()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);
        var materialization = _Materialization(definition);

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(async _ =>
                {
                    await Task.Yield();
                    return await provider.MaterializeCronScheduleOccurrenceAsync(materialization, AbortToken);
                })
        );

        results.Count(x => x.Outcome == CronScheduleMaterializationOutcome.OccurrenceCreated).Should().Be(1);
        results.Count(x => x.Outcome == CronScheduleMaterializationOutcome.LostFence).Should().Be(7);
        (await provider.GetAllCronJobOccurrencesAsync(null, AbortToken)).Should().ContainSingle();
    }

    [Fact]
    public async Task should_report_stale_and_future_materializations_as_distinct_no_mutation_outcomes()
    {
        var provider = _Create();
        var stale = _Definition(revision: 7);
        var future = _Definition();
        future.NextDueUtc = _Now.UtcDateTime.AddMinutes(30);
        await provider.InsertCronJobsAsync([stale, future], AbortToken);

        var staleResult = await provider.MaterializeCronScheduleOccurrenceAsync(
            _Materialization(stale) with
            {
                Advance = _Materialization(stale).Advance with { ExpectedScheduleRevision = 6 },
            },
            AbortToken
        );
        var futureResult = await provider.MaterializeCronScheduleOccurrenceAsync(
            new CronScheduleMaterialization
            {
                Advance = _Advance(future, future.NextDueUtc, future.NextDueUtc.AddMinutes(1)) with
                {
                    RequireProjectionDue = false,
                },
                ExecutionTimeUtc = future.NextDueUtc,
            },
            AbortToken
        );

        staleResult.Outcome.Should().Be(CronScheduleMaterializationOutcome.LostFence);
        futureResult
            .Outcome.Should()
            .Be(CronScheduleMaterializationOutcome.NotDue, "materialization is intrinsically due-gated");
        (await provider.GetAllCronJobOccurrencesAsync(null, AbortToken)).Should().BeEmpty();
        await _AssertPositionUnchangedAsync(provider, stale.Id);
        (await provider.GetCronJobByIdAsync(future.Id, AbortToken))!.NextDueUtc.Should().Be(future.NextDueUtc);
    }

    [Fact]
    public async Task should_advance_without_dispatch_when_a_terminal_occurrence_already_accounts_for_the_instant()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);
        var terminal = new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            CronJobId = definition.Id,
            CronJob = definition,
            ExecutionTime = _Projection,
            Status = JobStatus.Succeeded,
            OnNodeDeath = NodeDeathPolicy.Retry,
            CreatedAt = _Now.AddMinutes(-2),
            UpdatedAt = _Now.AddMinutes(-1),
        };
        await provider.InsertCronJobOccurrencesAsync([terminal], AbortToken);

        var result = await provider.MaterializeCronScheduleOccurrenceAsync(_Materialization(definition), AbortToken);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceAlreadyTerminal);
        result.OccurrenceId.Should().Be(terminal.Id);
        (await provider.GetAllCronJobOccurrencesAsync(null, AbortToken)).Should().ContainSingle();
        (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!.ReconciledThroughUtc.Should().Be(_Projection);
    }

    [Fact]
    public async Task should_reuse_a_non_terminal_occurrence_and_advance_the_position()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);
        var existing = new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            CronJobId = definition.Id,
            CronJob = definition,
            ExecutionTime = _Projection,
            Status = JobStatus.Idle,
            OnNodeDeath = NodeDeathPolicy.Retry,
            CreatedAt = _Now.AddMinutes(-2),
            UpdatedAt = _Now.AddMinutes(-1),
        };
        await provider.InsertCronJobOccurrencesAsync([existing], AbortToken);

        var result = await provider.MaterializeCronScheduleOccurrenceAsync(_Materialization(definition), AbortToken);

        result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceExists);
        result.OccurrenceId.Should().Be(existing.Id);
        result.OccurrenceCreatedAt.Should().Be(existing.CreatedAt);
        result.SchedulePosition!.ReconciledThroughUtc.Should().Be(_Projection);
        result.SchedulePosition.NextDueUtc.Should().Be(_Projection.AddMinutes(1));
        (await provider.GetAllCronJobOccurrencesAsync(null, AbortToken)).Should().ContainSingle();
        var persisted = await provider.GetCronJobByIdAsync(definition.Id, AbortToken);
        persisted!.ReconciledThroughUtc.Should().Be(_Projection);
        persisted.NextDueUtc.Should().Be(_Projection.AddMinutes(1));
    }

    [Fact]
    public async Task should_leave_position_and_occurrences_unchanged_when_cancelled_before_materialization()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var action = () =>
            provider.MaterializeCronScheduleOccurrenceAsync(_Materialization(definition), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        await _AssertPositionUnchangedAsync(provider, definition.Id);
        (await provider.GetAllCronJobOccurrencesAsync(null, AbortToken)).Should().BeEmpty();
    }

    [Fact]
#pragma warning disable MA0158 // The regression must contend on the provider's existing object-backed monitor.
    public async Task should_honor_cancellation_received_while_waiting_for_the_definition_lock()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);
        using var cancellation = new CancellationTokenSource();
        using var workerStarted = new ManualResetEventSlim();
        Exception? failure = null;
        var definitionLock = typeof(JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>)
            .GetMethod(
                "_GetCronDefinitionLock",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                binder: null,
                types: [typeof(Guid)],
                modifiers: null
            )!
            .Invoke(provider, [definition.Id])!;
        var worker = new Thread(() =>
        {
            workerStarted.Set();
            try
            {
                provider
                    .MaterializeCronScheduleOccurrenceAsync(_Materialization(definition), cancellation.Token)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        lock (definitionLock)
        {
            worker.Start();
            workerStarted.Wait(AbortToken);
            SpinWait
                .SpinUntil(() => worker.ThreadState.HasFlag(ThreadState.WaitSleepJoin), TimeSpan.FromSeconds(5))
                .Should()
                .BeTrue("the materializer must be waiting on the held definition lock before cancellation");
#pragma warning disable VSTHRD103 // Cancellation must be raised synchronously while the monitor is still held.
            cancellation.Cancel();
#pragma warning restore VSTHRD103
        }

        worker.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        failure.Should().BeOfType<OperationCanceledException>();
        await _AssertPositionUnchangedAsync(provider, definition.Id);
        (await provider.GetAllCronJobOccurrencesAsync(null, AbortToken)).Should().BeEmpty();
    }
#pragma warning restore MA0158

    [Fact]
    public async Task should_claim_the_committed_idle_occurrence_with_provider_owned_lease_time()
    {
        var provider = _Create();
        var definition = _Definition();
        await provider.InsertCronJobsAsync([definition], AbortToken);
        var result = await provider.MaterializeCronScheduleOccurrenceAsync(_Materialization(definition), AbortToken);
        var context = new JobManagerDispatchContext(definition.Id)
        {
            FunctionName = definition.Function,
            Expression = definition.Expression,
            ScheduleRevision = definition.ScheduleRevision,
            OnNodeDeath = definition.OnNodeDeath,
            NextCronOccurrence = new NextCronOccurrence(result.OccurrenceId!.Value, result.OccurrenceCreatedAt!.Value),
        };

        var claimed = await provider.QueueCronJobOccurrencesAsync((_Projection, [context]), AbortToken).ToArrayAsync();

        claimed.Should().ContainSingle();
        claimed[0].Status.Should().Be(JobStatus.Queued);
        claimed[0].OwnerId.Should().Be(_Owner);
        claimed[0].LockedUntil.Should().Be(_Now.UtcDateTime.AddMinutes(5));
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

    private static CronScheduleMaterialization _Materialization(FakeCronJob definition) =>
        new()
        {
            Advance = _Advance(definition, _Projection, _Projection.AddMinutes(1)) with { RequireProjectionDue = true },
            ExecutionTimeUtc = _Projection,
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
