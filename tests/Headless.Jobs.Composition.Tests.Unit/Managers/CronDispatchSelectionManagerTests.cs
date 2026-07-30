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

    private static FakeCronJob _Definition(DateTime nextDue, bool isPaused = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "cron-dispatch",
            // Every minute, so the projection after any instant is at most a minute later.
            Expression = "0 * * * * *",
            IsPaused = isPaused,
            ScheduleRevision = 0,
            ReconciledThroughUtc = _Now.AddMinutes(-5),
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
