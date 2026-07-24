// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Abstractions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Internal;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

#pragma warning disable IDE0130 // Provider implementation intentionally lives in the shared Jobs infrastructure namespace.
#pragma warning disable RCS1015 // SQL parameter names intentionally match lowercase placeholders in the command text.
namespace Headless.Jobs;

internal sealed class PostgreSqlJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>(
    IDbContextFactory<TDbContext> dbContextFactory,
    TimeProvider timeProvider,
    [FromKeyedServices(SequentialGuidType.Version7)] IGuidGenerator guidGenerator,
    IJobsOwnerIdentity ownerIdentity,
    SchedulerOptionsBuilder optionsBuilder
) : IJobsClaimStrategy<TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly TimeSpan _leaseDuration = optionsBuilder.LeaseDuration;

    // R12/KTD2: the maximum number of nodes on a root-to-leaf path the tree claim leases (root = depth 1). A timed
    // descendant is a boundary — not descended into, claimed independently (U5).
    private readonly int _maxChainDepth = optionsBuilder.MaxChainDepth;

    public async IAsyncEnumerable<TimeJobEntity> ClaimTimeJobsAsync(
        TimeJobEntity[] timeJobs,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (!ownerIdentity.TryGetStampOwner(out var owner) || timeJobs.Length == 0)
        {
            yield break;
        }

        var batch =
            timeJobs.Length <= JobsClaimStrategyDefaults.MaxCandidatePageSize
                ? timeJobs
                : [.. timeJobs.Take(JobsClaimStrategyDefaults.MaxCandidatePageSize)];
        ClaimResult claim;
        Guid[] leasedDescendantIds;

        await using (
            var claimTransaction = await JobsClaimTransaction<TDbContext>.CreateAsync(
                dbContextFactory,
                cancellationToken
            )
        )
        {
            var dbContext = claimTransaction.DbContext;
            var transaction = claimTransaction.Transaction;
            var mapping = TimeJobRelationalMapping.Create<TDbContext, TTimeJob>(dbContext);
            claim = await _ClaimRootsAsync(
                    dbContext,
                    transaction,
                    mapping,
                    _BuildDirectCandidates(batch, mapping),
                    owner,
                    _leaseDuration,
                    cancellationToken,
                    [
                        .. batch.SelectMany(
                            (job, index) =>
                                new NpgsqlParameter[]
                                {
                                    new(_ParameterName("id", index), job.Id),
                                    new(_ParameterName("updatedAt", index), job.UpdatedAt),
                                }
                        ),
                    ]
                )
                .ConfigureAwait(false);

            leasedDescendantIds = await _StampDescendantsAsync(
                    dbContext,
                    transaction,
                    mapping,
                    claim.Ids,
                    owner,
                    claim.ClaimedAt,
                    _leaseDuration,
                    _maxChainDepth,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await claimTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        // KTD2: the peek-hydrated tree may include non-idle nodes (and their tails) the claim did not lease; prune to
        // the claimed set (root + leased non-timed descendants) so nothing runs unclaimed — parity with the CAS path.
        var claimedIds = leasedDescendantIds.ToHashSet();
        var won = claim.Ids.ToHashSet();
        foreach (var timeJob in timeJobs)
        {
            if (!won.Contains(timeJob.Id))
            {
                continue;
            }

            timeJob.OwnerId = owner;
            timeJob.LockedUntil = claim.ClaimedAt.Add(_leaseDuration);
            timeJob.UpdatedAt = claim.ClaimedAt;
            timeJob.Status = JobStatus.Queued;
            TimeJobSubtreeOperations.PruneToClaimedSet(timeJob, claimedIds);
            yield return timeJob;
        }
    }

    public async IAsyncEnumerable<TimeJobEntity> ClaimTimedOutTimeJobsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (!ownerIdentity.TryGetStampOwner(out var owner))
        {
            yield break;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        TimeJobEntity[] claimed;
        ClaimResult claim;
        Guid[] leasedDescendantIds;

        await using (
            var claimTransaction = await JobsClaimTransaction<TDbContext>.CreateAsync(
                dbContextFactory,
                cancellationToken
            )
        )
        {
            var dbContext = claimTransaction.DbContext;
            var transaction = claimTransaction.Transaction;
            var mapping = TimeJobRelationalMapping.Create<TDbContext, TTimeJob>(dbContext);
            // U5/KTD3: the fallback selects timed rows directly, so the parent gate is mirrored in its WHERE clause —
            // a timed descendant is a candidate only once its parent reached its matching terminal state.
            var candidates = $"""
                SELECT root.{mapping.Id}
                FROM {mapping.Table} AS root, claim_clock
                WHERE root.{mapping.ExecutionTime} IS NOT NULL
                  AND root.{mapping.ExecutionTime} <= @fallbackThreshold
                  AND (root.{mapping.Status} = @idle
                       OR (root.{mapping.Status} = @queued
                           AND (root.{mapping.LockedUntil} IS NULL
                                OR (root.{mapping.LockedUntil} <= claim_clock.now
                                    AND root.{mapping.OnNodeDeath} = @retry))))
                  {TimedChildGateSql.Build(mapping, "root")}
                ORDER BY root.{mapping.ExecutionTime}, root.{mapping.Id}
                LIMIT {JobsClaimStrategyDefaults.MaxClaimBatchSize}
                FOR UPDATE SKIP LOCKED
                """;
            claim = await _ClaimRootsAsync(
                    dbContext,
                    transaction,
                    mapping,
                    candidates,
                    owner,
                    _leaseDuration,
                    cancellationToken,
                    new NpgsqlParameter("fallbackThreshold", now.AddSeconds(-1)),
                    new NpgsqlParameter("idle", nameof(JobStatus.Idle)),
                    new NpgsqlParameter("queued", nameof(JobStatus.Queued)),
                    new NpgsqlParameter("retry", nameof(NodeDeathPolicy.Retry))
                )
                .ConfigureAwait(false);

            leasedDescendantIds = await _StampDescendantsAsync(
                    dbContext,
                    transaction,
                    mapping,
                    claim.Ids,
                    owner,
                    claim.ClaimedAt,
                    _leaseDuration,
                    _maxChainDepth,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await claimTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (claim.Ids.Length == 0)
        {
            claimed = [];
        }
        else
        {
            await using var dbContext = await dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            // R12/KTD2: reload the claimed roots flat and rebuild their non-timed subtree to MaxChainDepth in memory (a
            // recursive .Select is not EF-translatable), then prune to the claim's leased set so deep leased nodes are
            // returned and non-idle tails are dropped. Runs AFTER the claim transaction commits (E2) so this
            // multi-round-trip hydration no longer holds the claim's exclusive row locks; a fresh dbContext reads the
            // now-committed rows, mirroring the SqlServer sibling.
            var roots = await dbContext
                .Set<TTimeJob>()
                .AsNoTracking()
                .Where(x => claim.Ids.Contains(x.Id) && x.OwnerId == owner)
                .Select(MappingExtensions.ForFlatTimeJob<TTimeJob>())
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            await MappingExtensions
                .AttachNonTimedDescendantsAsync(
                    dbContext.Set<TTimeJob>().AsNoTracking(),
                    roots,
                    _maxChainDepth,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var claimedIds = leasedDescendantIds.ToHashSet();
            foreach (var root in roots)
            {
                TimeJobSubtreeOperations.PruneToClaimedSet(root, claimedIds);
            }

            claimed = roots;
        }

        foreach (var timeJob in claimed)
        {
            timeJob.OwnerId = owner;
            timeJob.Status = JobStatus.Queued;
            yield return timeJob;
        }
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimCronJobOccurrencesAsync(
        (DateTime Key, JobManagerDispatchContext[] Items) cronJobOccurrences,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (!ownerIdentity.TryGetStampOwner(out var owner) || cronJobOccurrences.Items.Length == 0)
        {
            yield break;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var lockedUntil = now.Add(_leaseDuration);
        var claimed = new List<CronJobOccurrenceEntity<TCronJob>>();

        await using (
            var claimTransaction = await JobsClaimTransaction<TDbContext>.CreateAsync(
                dbContextFactory,
                cancellationToken
            )
        )
        {
            var dbContext = claimTransaction.DbContext;
            var transaction = claimTransaction.Transaction;
            var mapping = CronOccurrenceRelationalMapping.Create<TDbContext, TCronJob>(dbContext);
            var definitionMapping = CronDefinitionRelationalMapping.Create<TDbContext, TCronJob>(dbContext);
            foreach (var item in cronJobOccurrences.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    !await _LockActiveCronDefinitionAsync(transaction, definitionMapping, item, cancellationToken)
                        .ConfigureAwait(false)
                )
                {
                    continue;
                }

                var occurrence = item.NextCronOccurrence is null
                    ? await _InsertCronOccurrenceAsync(
                            dbContext,
                            transaction,
                            mapping,
                            item,
                            cronJobOccurrences.Key,
                            owner,
                            now,
                            lockedUntil,
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                    : await _ClaimExistingCronOccurrenceAsync(
                            dbContext,
                            transaction,
                            mapping,
                            item,
                            cronJobOccurrences.Key,
                            owner,
                            now,
                            lockedUntil,
                            cancellationToken
                        )
                        .ConfigureAwait(false);

                if (occurrence is not null)
                {
                    claimed.Add(occurrence);
                }
            }

            if (claimed.Count > 0)
            {
                var refreshedAt = await _RefreshCronOccurrenceLeasesAsync(
                        dbContext,
                        transaction,
                        mapping,
                        [.. claimed.Select(x => x.Id)],
                        owner,
                        _leaseDuration,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                foreach (var occurrence in claimed)
                {
                    occurrence.UpdatedAt = refreshedAt;
                    occurrence.LockedUntil = refreshedAt.Add(_leaseDuration);
                }
            }

            await claimTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var occurrence in claimed)
        {
            occurrence.OwnerId = owner;
            occurrence.Status = JobStatus.Queued;
            yield return occurrence;
        }
    }

    private static async Task<bool> _LockActiveCronDefinitionAsync(
        IDbContextTransaction transaction,
        CronDefinitionRelationalMapping mapping,
        JobManagerDispatchContext item,
        CancellationToken cancellationToken
    )
    {
        var connection = (NpgsqlConnection)transaction.GetDbTransaction().Connection!;
#pragma warning disable CA2100 // SQL identifiers are provider-delimited EF metadata; runtime values are parameters.
        await using var command = new NpgsqlCommand(
            $"""
            SELECT 1
            FROM {mapping.Table}
            WHERE {mapping.Id} = @id
              AND {mapping.IsPaused} = FALSE
              AND {mapping.ScheduleRevision} = @scheduleRevision
            FOR UPDATE
            """,
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction()
        );
#pragma warning restore CA2100
        command.Parameters.Add(new NpgsqlParameter<Guid>("id", item.Id));
        command.Parameters.Add(new NpgsqlParameter<long>("scheduleRevision", item.ScheduleRevision));

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimTimedOutCronJobOccurrencesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (!ownerIdentity.TryGetStampOwner(out var owner))
        {
            yield break;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var lockedUntil = now.Add(_leaseDuration);
        CronJobOccurrenceEntity<TCronJob>[] claimed;

        await using (
            var claimTransaction = await JobsClaimTransaction<TDbContext>.CreateAsync(
                dbContextFactory,
                cancellationToken
            )
        )
        {
            var dbContext = claimTransaction.DbContext;
            var transaction = claimTransaction.Transaction;
            var mapping = CronOccurrenceRelationalMapping.Create<TDbContext, TCronJob>(dbContext);
            var wonIds = await _ClaimFallbackCronOccurrencesAsync(
                    dbContext,
                    transaction,
                    mapping,
                    owner,
                    now,
                    lockedUntil,
                    cancellationToken
                )
                .ConfigureAwait(false);

            claimed = await dbContext
                .Set<CronJobOccurrenceEntity<TCronJob>>()
                .AsNoTracking()
                .Where(x => wonIds.Contains(x.Id) && x.OwnerId == owner)
                .Include(x => x.CronJob)
                .Select(MappingExtensions.ForQueueCronJobOccurrence<CronJobOccurrenceEntity<TCronJob>, TCronJob>())
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            await claimTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var occurrence in claimed)
        {
            yield return occurrence;
        }
    }

    private static async Task<DateTime> _RefreshCronOccurrenceLeasesAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        CronOccurrenceRelationalMapping mapping,
        Guid[] occurrenceIds,
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(dbContext, transaction);

#pragma warning disable CA2100 // SQL structure contains only provider-delimited EF metadata identifiers and fixed clauses;
        command.CommandText = $"""
            WITH claim_clock AS MATERIALIZED (
                SELECT clock_timestamp() AS now
            )
            UPDATE {mapping.Table}
            SET {mapping.LockedUntil} = claim_clock.now + (@leaseSeconds * INTERVAL '1 second'),
                {mapping.UpdatedAt} = claim_clock.now
            FROM claim_clock
            WHERE {mapping.Id} = ANY(@occurrenceIds)
              AND {mapping.OwnerId} = @owner
            RETURNING claim_clock.now;
            """;
#pragma warning restore CA2100

        command.Parameters.Add(new NpgsqlParameter("leaseSeconds", leaseDuration.TotalSeconds));
        command.Parameters.Add(new NpgsqlParameter("occurrenceIds", occurrenceIds) { DataTypeName = "uuid[]" });
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        return (DateTime)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private async Task<CronJobOccurrenceEntity<TCronJob>?> _InsertCronOccurrenceAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        CronOccurrenceRelationalMapping mapping,
        JobManagerDispatchContext item,
        DateTime executionTime,
        string owner,
        DateTime now,
        DateTime lockedUntil,
        CancellationToken cancellationToken
    )
    {
        var id = guidGenerator.Create();
        await using var command = _CreateCommand(dbContext, transaction);
#pragma warning disable CA2100
        command.CommandText = $"""
            WITH claim_clock AS MATERIALIZED (
                SELECT clock_timestamp() AS now
            )
            INSERT INTO {mapping.Table}
                ({mapping.Id}, {mapping.Status}, {mapping.OwnerId}, {mapping.ExecutionTime}, {mapping.CronJobId},
                 {mapping.LockedUntil}, {mapping.OnNodeDeath}, {mapping.ElapsedTime}, {mapping.RetryCount},
                 {mapping.CreatedAt}, {mapping.UpdatedAt})
            SELECT
                @id, @status, @owner, @executionTime, @cronJobId,
                claim_clock.now + (@leaseSeconds * INTERVAL '1 second'), @onNodeDeath,
                @elapsedTime, @retryCount, claim_clock.now, claim_clock.now
            FROM claim_clock
            ON CONFLICT ({mapping.ExecutionTime}, {mapping.CronJobId})
                WHERE {mapping.Status} IN ('Idle', 'Queued', 'InProgress')
                DO NOTHING
            RETURNING {mapping.Id};
            """;
#pragma warning restore CA2100
        command.Parameters.Add(new NpgsqlParameter("id", id));
        command.Parameters.Add(new NpgsqlParameter("status", nameof(JobStatus.Queued)));
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        command.Parameters.Add(new NpgsqlParameter("executionTime", executionTime));
        command.Parameters.Add(new NpgsqlParameter("cronJobId", item.Id));
        command.Parameters.Add(new NpgsqlParameter("leaseSeconds", (lockedUntil - now).TotalSeconds));
        command.Parameters.Add(new NpgsqlParameter("onNodeDeath", item.OnNodeDeath.ToString()));
        command.Parameters.Add(new NpgsqlParameter("elapsedTime", NpgsqlDbType.Bigint) { Value = 0L });
        command.Parameters.Add(new NpgsqlParameter("retryCount", NpgsqlDbType.Integer) { Value = 0 });
        var inserted = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return inserted is Guid
            ? new CronJobOccurrenceEntity<TCronJob>
            {
                Id = id,
                Status = JobStatus.Queued,
                OwnerId = owner,
                ExecutionTime = executionTime,
                CronJobId = item.Id,
                LockedUntil = lockedUntil,
                OnNodeDeath = item.OnNodeDeath,
                CreatedAt = now,
                UpdatedAt = now,
                CronJob = MappingExtensions.ProjectCronJob<TCronJob>(item, owner),
            }
            : null;
    }

    private static async Task<CronJobOccurrenceEntity<TCronJob>?> _ClaimExistingCronOccurrenceAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        CronOccurrenceRelationalMapping mapping,
        JobManagerDispatchContext item,
        DateTime executionTime,
        string owner,
        DateTime now,
        DateTime lockedUntil,
        CancellationToken cancellationToken
    )
    {
        var occurrence = item.NextCronOccurrence!;
        await using var command = _CreateCommand(dbContext, transaction);
#pragma warning disable CA2100
        command.CommandText = $"""
            WITH claim_clock AS MATERIALIZED (
                SELECT clock_timestamp() AS now
            ), candidate AS (
                SELECT occurrence.{mapping.Id}
                FROM {mapping.Table} AS occurrence, claim_clock
                WHERE occurrence.{mapping.Id} = @id
                  AND occurrence.{mapping.ExecutionTime} = @executionTime
                  AND (occurrence.{mapping.Status} = @idle OR occurrence.{mapping.Status} = @queued)
                  AND (occurrence.{mapping.OwnerId} = @owner
                       OR occurrence.{mapping.LockedUntil} IS NULL
                       OR (occurrence.{mapping.LockedUntil} <= claim_clock.now
                           AND occurrence.{mapping.OnNodeDeath} = @retry))
                FOR UPDATE SKIP LOCKED
            )
            UPDATE {mapping.Table} AS occurrence
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = claim_clock.now + (@leaseSeconds * INTERVAL '1 second'),
                {mapping.UpdatedAt} = claim_clock.now,
                {mapping.Status} = @queued,
                {mapping.OnNodeDeath} = @onNodeDeath
            FROM candidate, claim_clock
            WHERE occurrence.{mapping.Id} = candidate.{mapping.Id}
            RETURNING occurrence.{mapping.Id};
            """;
#pragma warning restore CA2100
        command.Parameters.Add(new NpgsqlParameter("id", occurrence.Id));
        command.Parameters.Add(new NpgsqlParameter("executionTime", executionTime));
        command.Parameters.Add(new NpgsqlParameter("idle", nameof(JobStatus.Idle)));
        command.Parameters.Add(new NpgsqlParameter("queued", nameof(JobStatus.Queued)));
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        command.Parameters.Add(new NpgsqlParameter("retry", nameof(NodeDeathPolicy.Retry)));
        command.Parameters.Add(new NpgsqlParameter("leaseSeconds", (lockedUntil - now).TotalSeconds));
        command.Parameters.Add(new NpgsqlParameter("onNodeDeath", item.OnNodeDeath.ToString()));
        var claimed = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return claimed is Guid
            ? new CronJobOccurrenceEntity<TCronJob>
            {
                Id = occurrence.Id,
                CronJobId = item.Id,
                ExecutionTime = executionTime,
                Status = JobStatus.Queued,
                OwnerId = owner,
                LockedUntil = lockedUntil,
                OnNodeDeath = item.OnNodeDeath,
                UpdatedAt = now,
                CreatedAt = occurrence.CreatedAt,
                CronJob = MappingExtensions.ProjectCronJob<TCronJob>(item, owner),
            }
            : null;
    }

    private static async Task<Guid[]> _ClaimFallbackCronOccurrencesAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        CronOccurrenceRelationalMapping mapping,
        string owner,
        DateTime now,
        DateTime lockedUntil,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(dbContext, transaction);

#pragma warning disable CA2100 // SQL structure contains only provider-delimited EF metadata identifiers and fixed clauses; every runtime value remains a command parameter.
        command.CommandText = $"""
            WITH claim_clock AS MATERIALIZED (
                SELECT clock_timestamp() AS now
            ), candidates AS (
                SELECT occurrence.{mapping.Id}
                FROM {mapping.Table} AS occurrence, claim_clock
                WHERE occurrence.{mapping.ExecutionTime} <= @fallbackThreshold
                  AND (occurrence.{mapping.Status} = @idle
                       OR (occurrence.{mapping.Status} = @queued
                           AND (occurrence.{mapping.LockedUntil} IS NULL
                                OR (occurrence.{mapping.LockedUntil} <= claim_clock.now
                                    AND occurrence.{mapping.OnNodeDeath} = @retry))))
                ORDER BY occurrence.{mapping.ExecutionTime}, occurrence.{mapping.Id}
                LIMIT {JobsClaimStrategyDefaults.MaxClaimBatchSize}
                FOR UPDATE SKIP LOCKED
            )
            UPDATE {mapping.Table} AS occurrence
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = claim_clock.now + (@leaseSeconds * INTERVAL '1 second'),
                {mapping.UpdatedAt} = claim_clock.now,
                {mapping.Status} = @queued
            FROM candidates, claim_clock
            WHERE occurrence.{mapping.Id} = candidates.{mapping.Id}
            RETURNING occurrence.{mapping.Id};
            """;
#pragma warning restore CA2100

        command.Parameters.Add(new NpgsqlParameter("fallbackThreshold", now.AddSeconds(-1)));
        command.Parameters.Add(new NpgsqlParameter("idle", nameof(JobStatus.Idle)));
        command.Parameters.Add(new NpgsqlParameter("queued", nameof(JobStatus.Queued)));
        command.Parameters.Add(new NpgsqlParameter("retry", nameof(NodeDeathPolicy.Retry)));
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        command.Parameters.Add(new NpgsqlParameter("leaseSeconds", (lockedUntil - now).TotalSeconds));

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetGuid(0));
        }

        return [.. ids];
    }

    private static string _BuildDirectCandidates(TimeJobEntity[] timeJobs, TimeJobRelationalMapping mapping)
    {
        var values = string.Join(
            ", ",
            timeJobs.Select((_, index) => $"(@{_ParameterName("id", index)}, @{_ParameterName("updatedAt", index)})")
        );
        return $"""
            SELECT root.{mapping.Id}
            FROM {mapping.Table} AS root
            INNER JOIN (VALUES {values}) AS requested(id, updated_at)
                ON requested.id = root.{mapping.Id} AND requested.updated_at = root.{mapping.UpdatedAt}
            ORDER BY root.{mapping.ExecutionTime} NULLS FIRST, root.{mapping.Id}
            LIMIT {JobsClaimStrategyDefaults.MaxClaimBatchSize}
            FOR UPDATE OF root SKIP LOCKED
            """;
    }

    private static async Task<ClaimResult> _ClaimRootsAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        TimeJobRelationalMapping mapping,
        string candidateSql,
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] candidateParameters
    )
    {
        await using var command = _CreateCommand(dbContext, transaction);
        // SQL structure contains only provider-delimited EF metadata identifiers and fixed clauses;
        // every runtime value remains a command parameter.
#pragma warning disable CA2100
        command.CommandText = $"""
            WITH claim_clock AS MATERIALIZED (
                SELECT clock_timestamp() AS now
            ), candidates AS (
                {candidateSql}
            )
            UPDATE {mapping.Table} AS job
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = claim_clock.now + (@leaseSeconds * INTERVAL '1 second'),
                {mapping.UpdatedAt} = claim_clock.now,
                {mapping.Status} = @queuedStatus
            FROM candidates, claim_clock
            WHERE job.{mapping.Id} = candidates.{mapping.Id}
            RETURNING job.{mapping.Id}, claim_clock.now;
            """;
#pragma warning restore CA2100
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        command.Parameters.Add(new NpgsqlParameter("leaseSeconds", leaseDuration.TotalSeconds));
        command.Parameters.Add(new NpgsqlParameter("queuedStatus", nameof(JobStatus.Queued)));
        command.Parameters.AddRange(candidateParameters);

        var ids = new List<Guid>();
        DateTime? claimedAt = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetGuid(0));
            claimedAt ??= reader.GetDateTime(1);
        }

        return new ClaimResult([.. ids], claimedAt ?? default);
    }

    private static async Task<Guid[]> _StampDescendantsAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        TimeJobRelationalMapping mapping,
        Guid[] rootIds,
        string owner,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        int maxChainDepth,
        CancellationToken cancellationToken
    )
    {
        if (rootIds.Length == 0)
        {
            return [];
        }

        await using var command = _CreateCommand(dbContext, transaction);
        // R12/KTD2: bounded WITH RECURSIVE walk that leases the non-timed idle subtree down to maxChainDepth (root =
        // depth 1, so direct children are depth 2). Mirrors the generic-EF frontier claim: descend only THROUGH idle
        // non-timed nodes, so a subtree below a non-idle node (terminalized/running) or a timed boundary (claimed
        // independently in U5) is never leased. Descendants stay Idle — only owner/lease/updated-at are stamped, in the
        // same transacted statement as today. RETURNING the leased ids lets the caller prune the hydrated tree to the
        // claimed set (U3 frontier discipline). SQL structure contains only provider-delimited EF metadata identifiers
        // and fixed clauses; every runtime value remains a command parameter.
#pragma warning disable CA2100
        command.CommandText = $"""
            WITH RECURSIVE descendants (node_id, depth) AS (
                SELECT child.{mapping.Id}, 2
                FROM {mapping.Table} AS child
                WHERE child.{mapping.ParentId} = ANY(@rootIds)
                  AND child.{mapping.Status} = @idle
                  AND child.{mapping.ExecutionTime} IS NULL
                  AND @maxDepth >= 2
                UNION ALL
                SELECT child.{mapping.Id}, descendants.depth + 1
                FROM {mapping.Table} AS child
                INNER JOIN descendants ON descendants.node_id = child.{mapping.ParentId}
                WHERE descendants.depth < @maxDepth
                  AND child.{mapping.Status} = @idle
                  AND child.{mapping.ExecutionTime} IS NULL
            )
            UPDATE {mapping.Table} AS job
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = @claimedAt + (@leaseSeconds * INTERVAL '1 second'),
                {mapping.UpdatedAt} = @claimedAt
            FROM descendants
            WHERE job.{mapping.Id} = descendants.node_id AND job.{mapping.Status} = @idle
            RETURNING job.{mapping.Id};
            """;
#pragma warning restore CA2100
        command.Parameters.Add(new NpgsqlParameter("rootIds", rootIds) { DataTypeName = "uuid[]" });
        command.Parameters.Add(new NpgsqlParameter("idle", nameof(JobStatus.Idle)));
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        command.Parameters.Add(new NpgsqlParameter("claimedAt", claimedAt));
        command.Parameters.Add(new NpgsqlParameter("leaseSeconds", leaseDuration.TotalSeconds));
        command.Parameters.Add(new NpgsqlParameter("maxDepth", NpgsqlDbType.Integer) { Value = maxChainDepth });

        var leasedIds = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            leasedIds.Add(reader.GetGuid(0));
        }

        return [.. leasedIds];
    }

    private readonly record struct ClaimResult(Guid[] Ids, DateTime ClaimedAt);

    private static NpgsqlCommand _CreateCommand(TDbContext dbContext, IDbContextTransaction transaction)
    {
        var connection =
            dbContext.Database.GetDbConnection() as NpgsqlConnection
            ?? throw new InvalidOperationException("PostgreSQL Jobs claims require an Npgsql connection.");
        return new NpgsqlCommand
        {
            Connection = connection,
            Transaction = (NpgsqlTransaction)transaction.GetDbTransaction(),
        };
    }

    private static string _ParameterName(string prefix, int index)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}{index}");
    }
}
