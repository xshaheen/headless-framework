// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
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
/// What an operator can see when a schedule falls behind or its interpretation changes. Asserted through the
/// instrumentation surface rather than a metrics backend, so the outcomes are observable in a plain unit test.
/// </summary>
public sealed class CronRecoveryObservabilityTests : TestBase
{
    public sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    public sealed class FakeCronJob : CronJobEntity;

    private static readonly DateTime _Now = new(2026, 07, 26, 12, 00, 00, DateTimeKind.Utc);

    private sealed record RecoveryOutcome(
        MissedRunPolicy Policy,
        int MissedCount,
        bool CountIsLowerBound,
        DateTime EarliestMissedUtc,
        int SkippedOccurrenceCount
    );

    [Fact]
    public async Task should_report_a_coalesced_recovery_with_its_missed_count()
    {
        var (manager, provider, outcomes, _) = _Create();
        // Hourly, four hours behind: several occurrences missed, well past the grace threshold.
        await provider.InsertCronJobsAsync([_Behind(MissedRunPolicy.Coalesce)], AbortToken);

        await manager.GetNextJobs(AbortToken);

        var outcome = outcomes.Should().ContainSingle().Subject;
        outcome.Policy.Should().Be(MissedRunPolicy.Coalesce);
        outcome.MissedCount.Should().BeGreaterThan(1);
        outcome.CountIsLowerBound.Should().BeFalse("a four-hour hourly backlog is far inside the ceiling");
        outcome.EarliestMissedUtc.Should().BeBefore(_Now);
    }

    [Fact]
    public async Task should_report_a_skipped_recovery_distinctly_from_a_coalesced_one()
    {
        var (manager, provider, outcomes, _) = _Create();
        await provider.InsertCronJobsAsync([_Behind(MissedRunPolicy.Skip)], AbortToken);

        await manager.GetNextJobs(AbortToken);

        outcomes
            .Should()
            .ContainSingle()
            .Which.Policy.Should()
            .Be(MissedRunPolicy.Skip, "the outcome must say what recovery DID, not merely that it triggered");
    }

    /// <summary>
    /// A saturated count and an exact one call for different operator responses, and the distinction cannot be
    /// recovered downstream — so it has to travel with the number.
    /// </summary>
    [Fact]
    public async Task should_mark_a_saturated_count_as_a_lower_bound()
    {
        var (manager, provider, outcomes, _) = _Create();
        // One-second schedule, four hours behind: ~14,400 instants, far past the evaluation ceiling.
        var definition = _Behind(MissedRunPolicy.Coalesce);
        definition.Expression = "* * * * * *";
        await provider.InsertCronJobsAsync([definition], AbortToken);

        await manager.GetNextJobs(AbortToken);

        var outcome = outcomes.Should().ContainSingle().Subject;
        outcome
            .CountIsLowerBound.Should()
            .BeTrue("the walk stopped at the ceiling, so the number is 'at least', not 'exactly'");
        outcome.MissedCount.Should().Be(JobsRecoveryDefaults.EvaluationCeiling);
        outcome
            .EarliestMissedUtc.Should()
            .Be(
                _Now.AddHours(-4).AddSeconds(1),
                "the earliest instant stays exact under saturation — it is what the coalesced run reports"
            );
    }

    [Fact]
    public async Task should_report_a_fingerprint_rebase()
    {
        var (manager, provider, _, rebases) = _Create();
        var definition = _Behind(MissedRunPolicy.Coalesce);
        definition.NextDueUtc = _Now.AddDays(20);
        definition.EvaluationFingerprint = "stale";
        await provider.InsertCronJobsAsync([definition], AbortToken);

        await manager.RebaseStaleFingerprintsAsync(limit: 50, AbortToken);

        rebases.Should().ContainSingle("the whole point of the fingerprint is that this is otherwise invisible");
    }

    [Fact]
    public async Task should_report_no_recovery_outcome_for_ordinary_dispatch()
    {
        var (manager, provider, outcomes, _) = _Create();
        var onTime = _Behind(MissedRunPolicy.Coalesce);
        onTime.Expression = "0 * * * * *";
        onTime.ReconciledThroughUtc = _Now.AddSeconds(-30);
        onTime.NextDueUtc = _Now;
        await provider.InsertCronJobsAsync([onTime], AbortToken);

        await manager.GetNextJobs(AbortToken);

        outcomes.Should().BeEmpty("a punctual dispatch is not a recovery and must not read like one");
    }

    private sealed class CapturingInstrumentation(List<RecoveryOutcome> outcomes, List<Guid> rebases)
        : IJobsInstrumentation
    {
        public void LogCronRecoveryApplied(
            Guid cronJobId,
            string functionName,
            MissedRunPolicy policy,
            int missedCount,
            bool countIsLowerBound,
            DateTime earliestMissedUtc,
            DateTime latestMissedUtc,
            int skippedOccurrenceCount
        ) =>
            outcomes.Add(
                new RecoveryOutcome(policy, missedCount, countIsLowerBound, earliestMissedUtc, skippedOccurrenceCount)
            );

        public void LogCronFingerprintRebased(
            Guid cronJobId,
            string functionName,
            DateTime previousNextDueUtc,
            DateTime rebasedNextDueUtc
        ) => rebases.Add(cronJobId);

        public Activity? StartJobActivity(string name, JobExecutionState state) => null;

        public void LogJobEnqueued(string jobType, string functionName, Guid jobId, string? enqueuedFrom = null) { }

        public void LogJobCompleted(Guid jobId, string functionName, long executionTimeMs, bool success) { }

        public void LogJobFailed(Guid jobId, string functionName, Exception exception, int retryCount) { }

        public void LogJobCancelled(Guid jobId, string functionName, string reason) { }

        public void LogJobSkipped(Guid jobId, string functionName, string reason) { }

        public void LogSeedingDataStarted(string seedingDataType) { }

        public void LogSeedingDataCompleted(string seedingDataType) { }

        public void LogRequestDeserializationFailure(
            string requestType,
            string functionName,
            Guid jobId,
            JobType type,
            Exception exception
        ) { }
    }

    private static (
        InternalJobsManager<FakeTimeJob, FakeCronJob> Manager,
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider,
        List<RecoveryOutcome> Outcomes,
        List<Guid> Rebases
    ) _Create()
    {
        var outcomes = new List<RecoveryOutcome>();
        var rebases = new List<Guid>();
        var time = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = "node-a" });
        services.AddSingleton(Substitute.For<IJobsHostScheduler>());
        services.AddSingleton<IJobsInstrumentation>(new CapturingInstrumentation(outcomes, rebases));
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

        return (manager, provider, outcomes, rebases);
    }

    private static FakeCronJob _Behind(MissedRunPolicy policy) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "cron-observability",
            Expression = "0 0 * * * *",
            ScheduleRevision = 0,
            ReconciledThroughUtc = _Now.AddHours(-4),
            NextDueUtc = _Now.AddHours(-3),
            OnMissedRun = policy,
            MissedRunGraceSeconds = 60,
            CreatedAt = new DateTimeOffset(_Now.AddDays(-1), TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(_Now.AddHours(-4), TimeSpan.Zero),
        };
}
