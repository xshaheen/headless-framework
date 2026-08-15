// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// Drives the shared recovery scenarios against the in-memory provider. It is a first-class backend here, not a test
/// double, so it owes the identical decision to the relational ones.
/// </summary>
public sealed class InMemoryCronRecoveryScenarioBackend : ICronRecoveryScenarioBackend
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    /// <summary>Whole seconds, matching the relational backends: PostgreSQL truncates to microseconds.</summary>
    private static readonly DateTimeOffset _Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    public string BackendName => "in-memory";

    public Task PrepareAsync(CancellationToken cancellationToken)
    {
        // Nothing to bring up: every scenario gets its own provider instance, which is already an empty store.
        return Task.CompletedTask;
    }

    public async Task<ICronRecoveryScenarioWorld> BeginScenarioAsync(
        string scenarioName,
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        services.AddHeadlessGuidGenerator();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = "node-a@incarnation" });
        var provider = new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());

        // The definition's projection IS instant 0 of the grid, and its watermark sits an hour before it.
        var firstInstant = _Now.UtcDateTime.AddHours(-8);
        var definition = new FakeCronJob
        {
            Id = Guid.NewGuid(),
            Function = scenarioName,
            Expression = "0 0 * * * *",
            ScheduleRevision = 0,
            ReconciledThroughUtc = firstInstant.AddHours(-1),
            NextDueUtc = firstInstant,
            OnNodeDeath = NodeDeathPolicy.Retry,
            CreatedAt = _Now.AddHours(-9),
            UpdatedAt = _Now.AddHours(-9),
        };

        await provider.InsertCronJobsAsync([definition], cancellationToken);

        return new World(provider, definition, firstInstant);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private sealed class World(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        FakeCronJob definition,
        DateTime firstInstantUtc
    ) : ICronRecoveryScenarioWorld
    {
        public Guid CronJobId => definition.Id;

        public DateTime ObservedReconciledThroughUtc => definition.ReconciledThroughUtc;

        public long ScheduleRevision => definition.ScheduleRevision;

        public DateTime FirstInstantUtc => firstInstantUtc;

        public async Task<Guid> SeedOccurrenceAsync(
            CronRecoverySeedRow row,
            DateTime executionTimeUtc,
            CancellationToken cancellationToken
        )
        {
            var occurrence = new CronJobOccurrenceEntity<FakeCronJob>
            {
                Id = Guid.NewGuid(),
                CronJobId = definition.Id,
                CronJob = definition,
                ExecutionTime = executionTimeUtc,
                // A status no binary recognizes is an out-of-range enum value here; relationally it is a string a
                // newer binary wrote. Both reach the same fail-closed branch.
                Status = row.RawStatus is null ? row.Status : (JobStatus)999,
                Disposition = row.Disposition,
                SkippedReason = row.SkippedReason,
                OwnerId = row.OwnerId,
                OnNodeDeath = NodeDeathPolicy.Retry,
                CreatedAt = _Now.AddHours(-1).AddSeconds(row.CreatedAtRank),
                UpdatedAt = _Now.AddHours(-1).AddSeconds(row.CreatedAtRank),
            };

            await provider.InsertCronJobOccurrencesAsync([occurrence], cancellationToken);

            return occurrence.Id;
        }

        public async Task<CronRecoveryOutcomeSnapshot?> ApplyRecoveryAsync(
            CronRecoveryRequest request,
            CancellationToken cancellationToken
        )
        {
            var result = await provider.ApplyCronRecoveryAsync(request, cancellationToken);

            if (result is null)
            {
                return null;
            }

            var run = result.CoalescedRun is { } coalesced
                ? new CronRecoveryRunSnapshot(
                    coalesced.Id,
                    coalesced.ExecutionTime,
                    coalesced.RecoveredFromUtc,
                    coalesced.OwnerId
                )
                : null;

            return new CronRecoveryOutcomeSnapshot(
                run,
                result.SkippedOccurrenceCount,
                result.ReconciledThroughUtc,
                result.NextDueUtc
            );
        }

        public async Task<IReadOnlyList<CronOccurrenceRowSnapshot>> ReadOccurrencesAsync(
            CancellationToken cancellationToken
        )
        {
            var stored = await provider.GetAllCronJobOccurrencesAsync(
                x => x.CronJobId == definition.Id,
                cancellationToken
            );

            return stored
                .Select(x => new CronOccurrenceRowSnapshot(
                    x.Id,
                    x.ExecutionTime,
                    x.Status.ToString(),
                    x.Disposition.ToString(),
                    x.OwnerId,
                    x.RecoveredFromUtc
                ))
                .ToArray();
        }

        public async Task<(DateTime ReconciledThroughUtc, DateTime NextDueUtc)> ReadSchedulePositionAsync(
            CancellationToken cancellationToken
        )
        {
            var stored = await provider.GetCronJobByIdAsync(definition.Id, cancellationToken);
            stored.Should().NotBeNull("the seeded cron definition must exist");

            return (stored!.ReconciledThroughUtc, stored.NextDueUtc);
        }
    }
}
