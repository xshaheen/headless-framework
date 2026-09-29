// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
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
/// What the scheduler does with an occurrence the overlap policy skipped: it never reaches the claim path, and the
/// skip is distinguishable from every other skip in telemetry.
/// </summary>
public sealed class CronOverlapDispatchTests : TestBase
{
    public sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    public sealed class FakeCronJob : CronJobEntity;

    private static readonly DateTime _Now = new(2026, 07, 26, 12, 00, 30, DateTimeKind.Utc);
    private static readonly DateTime _Due = new(2026, 07, 26, 12, 00, 00, DateTimeKind.Utc);

    [Fact]
    public async Task should_not_dispatch_an_occurrence_skipped_for_overlap_and_should_report_it()
    {
        var function = $"cron-overlap-{Guid.NewGuid():N}";
        var (manager, provider, skips) = _Create();
        var definition = _Definition(function, CronOverlapPolicy.Skip);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([_Running(definition.Id)], AbortToken);
        using var counter = new SkipCounter(function);

        var (_, functions) = await manager.GetNextJobs(AbortToken);

        functions.Should().BeEmpty("a skipped occurrence must never reach the claim path");
        var skip = skips.Should().ContainSingle().Subject;
        skip.CronJobId.Should().Be(definition.Id);
        skip.ExecutionTimeUtc.Should().Be(_Due);
        skip.IsRecoveryRun.Should().BeFalse();
        counter.Measurements.Should().Equal((1L, "overlap"));
    }

    [Fact]
    public async Task should_dispatch_alongside_an_unfinished_occurrence_under_allow()
    {
        var function = $"cron-overlap-{Guid.NewGuid():N}";
        var (manager, provider, skips) = _Create();
        var definition = _Definition(function, CronOverlapPolicy.Allow);
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([_Running(definition.Id)], AbortToken);
        using var counter = new SkipCounter(function);

        var (_, functions) = await manager.GetNextJobs(AbortToken);

        functions.Should().NotBeEmpty("Allow keeps today's behavior: the due occurrence dispatches");
        skips.Should().BeEmpty();
        counter.Measurements.Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_a_recovery_run_skipped_for_overlap_as_a_recovery_run()
    {
        var function = $"cron-overlap-{Guid.NewGuid():N}";
        var (manager, provider, skips) = _Create();
        var definition = _Definition(function, CronOverlapPolicy.Skip);
        // Four hours behind with a one-minute grace: the scheduler recovers rather than dispatching a tick.
        definition.ReconciledThroughUtc = _Due.AddHours(-4);
        definition.NextDueUtc = _Due.AddHours(-3);
        definition.MissedRunGraceSeconds = 60;
        await provider.InsertCronJobsAsync([definition], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([_Running(definition.Id, _Due.AddHours(-4))], AbortToken);
        using var counter = new SkipCounter(function);

        var (_, functions) = await manager.GetNextJobs(AbortToken);

        functions.Should().BeEmpty();
        skips.Should().ContainSingle().Which.IsRecoveryRun.Should().BeTrue();
        counter.Measurements.Should().Contain((1L, "overlap"));
    }

    private sealed record OverlapSkip(Guid CronJobId, DateTime ExecutionTimeUtc, bool IsRecoveryRun);

    private sealed class CapturingInstrumentation(List<OverlapSkip> skips) : IJobsInstrumentation
    {
        public void LogCronOccurrenceSkippedForOverlap(
            Guid cronJobId,
            string functionName,
            Guid occurrenceId,
            DateTime executionTimeUtc,
            bool isRecoveryRun
        ) => skips.Add(new OverlapSkip(cronJobId, executionTimeUtc, isRecoveryRun));

        public void LogCronRecoveryApplied(
            Guid cronJobId,
            string functionName,
            MissedRunPolicy policy,
            int missedCount,
            bool countIsLowerBound,
            DateTime earliestMissedUtc,
            DateTime latestMissedUtc,
            int skippedOccurrenceCount
        ) { }

        public void LogCronFingerprintRebased(
            Guid cronJobId,
            string functionName,
            string? previousFingerprint,
            string currentFingerprint,
            DateTime previousReconciledThroughUtc,
            DateTime rebaseAnchorUtc,
            DateTime previousNextDueUtc,
            DateTime rebasedNextDueUtc
        ) { }

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

    /// <summary>Listens to the shared meter, filtered to one function so parallel tests cannot bleed into it.</summary>
    private sealed class SkipCounter : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Lock _gate = new();

        public List<(long Value, string Reason)> Measurements { get; } = [];

        public SkipCounter(string function)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (
                    ReferenceEquals(instrument.Meter, JobsDiagnostics.Meter)
                    && string.Equals(instrument.Name, JobsMetrics.CronOccurrencesSkippedName, StringComparison.Ordinal)
                )
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(
                (_, measurement, tags, _) =>
                {
                    string? reason = null;
                    string? taggedFunction = null;

                    foreach (var tag in tags)
                    {
                        if (string.Equals(tag.Key, JobsMetrics.TagSkipReason, StringComparison.Ordinal))
                        {
                            reason = tag.Value as string;
                        }
                        else if (string.Equals(tag.Key, JobsMetrics.TagFunction, StringComparison.Ordinal))
                        {
                            taggedFunction = tag.Value as string;
                        }
                    }

                    if (!string.Equals(taggedFunction, function, StringComparison.Ordinal))
                    {
                        return;
                    }

                    lock (_gate)
                    {
                        Measurements.Add((measurement, reason ?? string.Empty));
                    }
                }
            );
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    private static (
        InternalJobsManager<FakeTimeJob, FakeCronJob> Manager,
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider,
        List<OverlapSkip> Skips
    ) _Create()
    {
        var skips = new List<OverlapSkip>();
        var time = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = "node-a" });
        services.AddSingleton(Substitute.For<IJobsHostScheduler>());
        services.AddSingleton<IJobsInstrumentation>(new CapturingInstrumentation(skips));
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

        return (manager, provider, skips);
    }

    private static FakeCronJob _Definition(string function, CronOverlapPolicy policy) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = "0 0 * * * *",
            ScheduleRevision = 0,
            ReconciledThroughUtc = _Due.AddHours(-1),
            NextDueUtc = _Due,
            OnMissedRun = MissedRunPolicy.Coalesce,
            MissedRunGraceSeconds = 60,
            OnOverlap = policy,
            CreatedAt = new DateTimeOffset(_Now.AddDays(-1), TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(_Now.AddHours(-4), TimeSpan.Zero),
        };

    private static CronJobOccurrenceEntity<FakeCronJob> _Running(Guid definitionId, DateTime? executionTime = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            CronJobId = definitionId,
            ExecutionTime = executionTime ?? _Due.AddHours(-1),
            Status = JobStatus.InProgress,
            OwnerId = "node-b@1",
            LockedUntil = _Now.AddMinutes(5),
            CreatedAt = new DateTimeOffset(_Now.AddHours(-1), TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(_Now.AddHours(-1), TimeSpan.Zero),
        };
}
