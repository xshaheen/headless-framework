// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Drives the shared recovery scenarios against a relational backend through <see cref="IJobsCoordinationFixture" />.
/// </summary>
/// <remarks>
/// The store is reset and the host built once per test method, then each scenario gets its own definition — the same
/// shape the occupied-instant matrix uses, because a container reset per scenario would dominate the run time
/// without proving anything extra.
/// </remarks>
public sealed class RelationalCronRecoveryScenarioBackend(IJobsCoordinationFixture fixture, string backendName)
    : ICronRecoveryScenarioBackend
{
    /// <summary>Eight hours of backlog, so the whole grid sits in the past relative to the store clock.</summary>
    private const int _WatermarkOffsetSeconds = -28800;

    /// <summary>Instant 0 of the grid: one hour after the watermark.</summary>
    private const int _FirstInstantOffsetSeconds = -25200;

    /// <summary>Creation stamp of the lowest-ranked seeded row; each further rank is a minute later.</summary>
    private const int _CreatedAtBaseOffsetSeconds = -600;

    private IHost? _host;

    public string BackendName => backendName;

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await fixture.ResetDatabaseAsync(cancellationToken);
        _host = fixture.BuildHost("recovery-scenarios");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(_host, cancellationToken);
    }

    public async Task<ICronRecoveryScenarioWorld> BeginScenarioAsync(
        string scenarioName,
        CancellationToken cancellationToken
    )
    {
        _host.Should().NotBeNull("PrepareAsync must run before any scenario");

        var cronJobId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(
            cronJobId,
            scenarioName,
            "0 0 * * * *",
            NodeDeathPolicy.Retry,
            cancellationToken,
            reconciledThroughOffsetSeconds: _WatermarkOffsetSeconds,
            nextDueOffsetSeconds: _FirstInstantOffsetSeconds
        );

        // Read the position back rather than computing it: the store wrote it with its own clock and its own
        // precision, and every instant on the grid has to match what a query will compare against.
        var seeded = await fixture.ReadCronSchedulePositionAsync(cronJobId, cancellationToken);
        var persistence = _host!.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        return new World(fixture, persistence, cronJobId, seeded.ReconciledThroughUtc, seeded.NextDueUtc);
    }

    public ValueTask DisposeAsync()
    {
        _host?.Dispose();
        _host = null;

        return ValueTask.CompletedTask;
    }

    private sealed class World(
        IJobsCoordinationFixture fixture,
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence,
        Guid cronJobId,
        DateTime observedReconciledThroughUtc,
        DateTime firstInstantUtc
    ) : ICronRecoveryScenarioWorld
    {
        public Guid CronJobId => cronJobId;

        public DateTime ObservedReconciledThroughUtc => observedReconciledThroughUtc;

        public long ScheduleRevision => 0L;

        public DateTime FirstInstantUtc => firstInstantUtc;

        public async Task<Guid> SeedOccurrenceAsync(
            CronRecoverySeedRow row,
            DateTime executionTimeUtc,
            CancellationToken cancellationToken
        )
        {
            var id = Guid.NewGuid();

            // CreatedAt is stamped from an EXPLICIT rank-derived offset, not from the raw store clock: two
            // consecutive inserts can read the same SYSUTCDATETIME(), and the live-first ordering scenario would
            // then be decided by a random Id tiebreak instead of by the rule under test. Measured — the scenario
            // passed under a planner mutated to drop live-first ordering until this became explicit.
            await fixture.SeedCronOccurrenceAsync(
                id,
                cronJobId,
                (int)row.Status,
                row.OwnerId,
                NodeDeathPolicy.Retry,
                row.OwnerId is null ? null : DateTime.UtcNow.AddMinutes(5),
                executionTimeUtc,
                cancellationToken,
                row.SkippedReason,
                row.Disposition,
                row.RawStatus,
                createdAtOffsetSeconds: _CreatedAtBaseOffsetSeconds + (row.CreatedAtRank * 60)
            );

            return id;
        }

        public async Task<CronRecoveryOutcomeSnapshot?> ApplyRecoveryAsync(
            CronRecoveryRequest request,
            CancellationToken cancellationToken
        )
        {
            var result = await persistence.ApplyCronRecoveryAsync(request, cancellationToken);

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
            // Raw SQL, not the entity read: one scenario seeds a status string no binary in this repo writes, and
            // materializing it as an enum would throw on the read instead of exercising the fail-closed rule.
            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT \"Id\", \"ExecutionTime\", \"Status\", \"Disposition\", \"OwnerId\", \"RecoveredFromUtc\" "
                + $"FROM {fixture.QualifiedCronJobOccurrencesTable} WHERE \"CronJobId\" = @cronJobId;";
            JobsCoordinationFixtureExtensions.AddParameter(command, "@cronJobId", cronJobId);

            var rows = new List<CronOccurrenceRowSnapshot>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var hasOwner = !await reader.IsDBNullAsync(4, cancellationToken);
                var hasRecoveryStamp = !await reader.IsDBNullAsync(5, cancellationToken);

                rows.Add(
                    new CronOccurrenceRowSnapshot(
                        reader.GetGuid(0),
                        _Utc(reader.GetDateTime(1)),
                        reader.GetString(2),
                        reader.GetString(3),
                        hasOwner ? reader.GetString(4) : null,
                        hasRecoveryStamp ? _Utc(reader.GetDateTime(5)) : null
                    )
                );
            }

            return rows;
        }

        public async Task<(DateTime ReconciledThroughUtc, DateTime NextDueUtc)> ReadSchedulePositionAsync(
            CancellationToken cancellationToken
        )
        {
            return await fixture.ReadCronSchedulePositionAsync(cronJobId, cancellationToken);
        }

        private static DateTime _Utc(DateTime value)
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }
}
