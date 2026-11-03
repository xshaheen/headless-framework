// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// The in-memory fallback sweep agrees with the relational providers: an occurrence of a paused definition stays
/// where it is until the definition resumes, instead of being leased only for the start transition to refuse it.
/// </summary>
public sealed class PausedFallbackSweepProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private static readonly DateTimeOffset _Now = new(2026, 06, 17, 12, 00, 00, TimeSpan.Zero);

    [Fact]
    public async Task fallback_sweep_skips_occurrences_of_a_paused_definition()
    {
        var provider = _Create();
        var paused = new FakeCronJob
        {
            Id = Guid.NewGuid(),
            Function = "paused",
            Expression = "* * * * *",
            IsPaused = true,
        };
        var active = new FakeCronJob
        {
            Id = Guid.NewGuid(),
            Function = "active",
            Expression = "* * * * *",
        };
        await provider.InsertCronJobsAsync([paused, active], AbortToken);
        var pausedIdle = _Occurrence(paused, JobStatus.Idle, owner: null, lockedUntil: null);
        var pausedLapsed = _Occurrence(paused, JobStatus.Queued, "node-b", _Now.UtcDateTime.AddMinutes(-1));
        var activeIdle = _Occurrence(active, JobStatus.Idle, owner: null, lockedUntil: null);
        await provider.InsertCronJobOccurrencesAsync([pausedIdle, pausedLapsed, activeIdle], AbortToken);

        var claimed = await provider.QueueTimedOutCronJobOccurrencesAsync(AbortToken).ToArrayAsync(AbortToken);

        claimed.Select(x => x.Id).Should().Equal(activeIdle.Id);
        var stored = (await provider.GetAllCronJobOccurrencesAsync(predicate: null, AbortToken)).ToDictionary(x =>
            x.Id
        );
        stored[pausedIdle.Id].Status.Should().Be(JobStatus.Idle);
        stored[pausedIdle.Id].OwnerId.Should().BeNull();
        stored[pausedLapsed.Id].Status.Should().Be(JobStatus.Queued);
        stored[pausedLapsed.Id].OwnerId.Should().Be("node-b");
    }

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _Create()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(
            new SchedulerOptionsBuilder { NodeId = "node-a", LeaseDuration = TimeSpan.FromMinutes(5) }
        );
        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }

    private static CronJobOccurrenceEntity<FakeCronJob> _Occurrence(
        FakeCronJob definition,
        JobStatus status,
        string? owner,
        DateTime? lockedUntil
    )
    {
        return new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            Function = definition.Function,
            Status = status,
            OwnerId = owner,
            LockedUntil = lockedUntil,
            OnNodeDeath = NodeDeathPolicy.Retry,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-2),
            CronJobId = definition.Id,
            CronJob = definition,
        };
    }
}
