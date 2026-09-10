// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Runtime.CompilerServices;
using Headless.Abstractions;
using Headless.Constants;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Internal;
using Headless.Jobs.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

#pragma warning disable IDE0130 // Provider implementation intentionally lives in the shared Jobs infrastructure namespace.
#pragma warning disable RCS1015 // SQL parameter names intentionally match lowercase placeholders in the command text.
namespace Headless.Jobs;

internal sealed class SqlServerJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>(
    IDbContextFactory<TDbContext> dbContextFactory,
    TimeProvider timeProvider,
    [FromKeyedServices(SetupSqlServerJobsEntityFramework.GuidGeneratorKey)] IGuidGenerator guidGenerator,
    IJobsOwnerIdentity ownerIdentity,
    SchedulerOptionsBuilder optionsBuilder,
    ILogger<SqlServerJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>> logger
) : IJobsClaimStrategy<TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private const int _MaxDeadlockRetryAttempts = 2;
    private readonly ResiliencePipeline _deadlockRetryPipeline = _BuildDeadlockRetryPipeline(timeProvider, logger);
    private readonly TimeSpan _leaseDuration = optionsBuilder.LeaseDuration;

    // R12/KTD2: the maximum number of nodes on a root-to-leaf path the tree claim leases (root = depth 1). A timed
    // descendant is a boundary — not descended into, claimed independently (U5).
    private readonly int _maxChainDepth = optionsBuilder.MaxChainDepth;
    private readonly Lock _readPastHintsLock = new();
    private Task<string>? _readPastHintsTask;
    private int _readPastHintsProbeCount;

    internal int ReadPastHintsProbeCount => Volatile.Read(ref _readPastHintsProbeCount);

    public async IAsyncEnumerable<TimeJobEntity> ClaimTimeJobsAsync(
        TimeJobEntity[] timeJobs,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (!ownerIdentity.TryGetStampOwner(out var owner) || timeJobs.Length == 0)
        {
            yield break;
        }

        var (claim, leasedDescendantIds) = await _ExecuteWithDeadlockRetryAsync(
                async ct =>
                {
                    await using var claimTransaction = await JobsClaimTransaction<TDbContext>.CreateAsync(
                        dbContextFactory,
                        ct
                    );
                    var dbContext = claimTransaction.DbContext;
                    var transaction = claimTransaction.Transaction;
                    var mapping = TimeJobRelationalMapping.Create<TDbContext, TTimeJob>(dbContext);
                    var readPastHints = await _GetReadPastHintsAsync(ct).ConfigureAwait(false);
                    var batch =
                        timeJobs.Length <= JobsClaimStrategyDefaults.MaxCandidatePageSize
                            ? timeJobs
                            : [.. timeJobs.Take(JobsClaimStrategyDefaults.MaxCandidatePageSize)];
                    var attemptClaim = await _ClaimRootsAsync(
                            dbContext,
                            transaction,
                            mapping,
                            _BuildDirectCandidates(batch, mapping, readPastHints),
                            owner,
                            _leaseDuration,
                            ct,
                            [
                                .. batch.SelectMany(
                                    (job, index) =>
                                        new[]
                                        {
                                            new(_ParameterName("id", index), job.Id),
                                            _DateTimeOffsetParameter(_ParameterName("updatedAt", index), job.UpdatedAt),
                                        }
                                ),
                            ]
                        )
                        .ConfigureAwait(false);

                    var attemptLeasedDescendantIds = await _StampDescendantsAsync(
                            dbContext,
                            transaction,
                            mapping,
                            attemptClaim.Ids,
                            owner,
                            attemptClaim.ClaimedAt,
                            _leaseDuration,
                            _maxChainDepth,
                            ct
                        )
                        .ConfigureAwait(false);
                    await claimTransaction.CommitAsync(ct).ConfigureAwait(false);

                    return (attemptClaim, attemptLeasedDescendantIds);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

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
            timeJob.LockedUntil = claim.ClaimedAt.UtcDateTime.Add(_leaseDuration);
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

        TimeJobEntity[] claimed;
        var (claim, leasedDescendantIds) = await _ExecuteWithDeadlockRetryAsync(
                async ct =>
                {
                    await using var claimTransaction = await JobsClaimTransaction<TDbContext>.CreateAsync(
                        dbContextFactory,
                        ct
                    );
                    var dbContext = claimTransaction.DbContext;
                    var transaction = claimTransaction.Transaction;
                    var mapping = TimeJobRelationalMapping.Create<TDbContext, TTimeJob>(dbContext);
                    var readPastHints = await _GetReadPastHintsAsync(ct).ConfigureAwait(false);
                    // U5/KTD3: the fallback selects timed rows directly, so the parent gate is mirrored in its WHERE
                    // clause — a timed descendant is a candidate only once its parent reached its matching terminal
                    // state.
                    var candidates = $"""
                        SELECT TOP ({JobsClaimStrategyDefaults.MaxClaimBatchSize}) root.{mapping.Id}
                        FROM {mapping.Table} AS root WITH ({readPastHints})
                        WHERE root.{mapping.ExecutionTime} IS NOT NULL
                          AND root.{mapping.ExecutionTime} <= DATEADD(second, -1, @claimNow)
                          AND (root.{mapping.Status} = @idle
                               OR (root.{mapping.Status} = @queued
                                   AND (root.{mapping.LockedUntil} IS NULL
                                        OR (root.{mapping.LockedUntil} <= @claimNow
                                            AND root.{mapping.OnNodeDeath} = @retry))))
                          {TimedChildGateSql.Build(mapping, "root")}
                        ORDER BY root.{mapping.ExecutionTime}, root.{mapping.Id}
                        """;
                    var attemptClaim = await _ClaimRootsAsync(
                            dbContext,
                            transaction,
                            mapping,
                            candidates,
                            owner,
                            _leaseDuration,
                            ct,
                            new SqlParameter("idle", nameof(JobStatus.Idle)),
                            new SqlParameter("queued", nameof(JobStatus.Queued)),
                            new SqlParameter("retry", nameof(NodeDeathPolicy.Retry))
                        )
                        .ConfigureAwait(false);

                    var attemptLeasedDescendantIds = await _StampDescendantsAsync(
                            dbContext,
                            transaction,
                            mapping,
                            attemptClaim.Ids,
                            owner,
                            attemptClaim.ClaimedAt,
                            _leaseDuration,
                            _maxChainDepth,
                            ct
                        )
                        .ConfigureAwait(false);
                    await claimTransaction.CommitAsync(ct).ConfigureAwait(false);

                    return (attemptClaim, attemptLeasedDescendantIds);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

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
            // returned and non-idle tails are dropped — replacing a fixed-depth nested projection.
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

        var now = timeProvider.GetUtcNow();
        var lockedUntil = now.UtcDateTime.Add(_leaseDuration);
        var claimed = await _ExecuteWithDeadlockRetryAsync(
                async ct =>
                {
                    var attemptClaimed = new List<CronJobOccurrenceEntity<TCronJob>>();
                    await using var claimTransaction = await JobsClaimTransaction<TDbContext>.CreateAsync(
                        dbContextFactory,
                        ct
                    );
                    var dbContext = claimTransaction.DbContext;
                    var transaction = claimTransaction.Transaction;
                    var mapping = CronOccurrenceRelationalMapping.Create<TDbContext, TCronJob>(dbContext);
                    var definitionMapping = CronDefinitionRelationalMapping.Create<TDbContext, TCronJob>(dbContext);
                    var readPastHints = await _GetReadPastHintsAsync(ct).ConfigureAwait(false);
                    foreach (var item in cronJobOccurrences.Items)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (
                            !await _LockActiveCronDefinitionAsync(transaction, definitionMapping, item, ct)
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
                                    ct
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
                                    readPastHints,
                                    ct
                                )
                                .ConfigureAwait(false);

                        if (occurrence is not null)
                        {
                            attemptClaimed.Add(occurrence);
                        }
                    }

                    if (attemptClaimed.Count > 0)
                    {
                        var refreshedAt = await _RefreshCronOccurrenceLeasesAsync(
                                dbContext,
                                transaction,
                                mapping,
                                [.. attemptClaimed.Select(x => x.Id)],
                                owner,
                                _leaseDuration,
                                ct
                            )
                            .ConfigureAwait(false);

                        foreach (var occurrence in attemptClaimed)
                        {
                            occurrence.UpdatedAt = refreshedAt;
                            occurrence.LockedUntil = refreshedAt.UtcDateTime.Add(_leaseDuration);
                        }
                    }

                    await claimTransaction.CommitAsync(ct).ConfigureAwait(false);
                    return attemptClaimed;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

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
        var connection = (SqlConnection)transaction.GetDbTransaction().Connection!;
#pragma warning disable CA2100 // SQL identifiers are provider-delimited EF metadata; runtime values are parameters.
        await using var command = new SqlCommand(
            $"""
            SELECT 1
            FROM {mapping.Table} WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE {mapping.Id} = @id
              AND {mapping.IsPaused} = 0
              AND {mapping.ScheduleRevision} = @scheduleRevision
            """,
            connection,
            (SqlTransaction)transaction.GetDbTransaction()
        );
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("id", SqlDbType.UniqueIdentifier) { Value = item.Id });
        command.Parameters.Add(
            new SqlParameter("scheduleRevision", SqlDbType.BigInt) { Value = item.ScheduleRevision }
        );

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

        var now = timeProvider.GetUtcNow();
        var lockedUntil = now.UtcDateTime.Add(_leaseDuration);
        CronJobOccurrenceEntity<TCronJob>[] claimed;
        var wonIds = await _ExecuteWithDeadlockRetryAsync(
                async ct =>
                {
                    await using var claimTransaction = await JobsClaimTransaction<TDbContext>.CreateAsync(
                        dbContextFactory,
                        ct
                    );
                    var dbContext = claimTransaction.DbContext;
                    var transaction = claimTransaction.Transaction;
                    var mapping = CronOccurrenceRelationalMapping.Create<TDbContext, TCronJob>(dbContext);
                    var readPastHints = await _GetReadPastHintsAsync(ct).ConfigureAwait(false);
                    var attemptWonIds = await _ClaimFallbackCronOccurrencesAsync(
                            dbContext,
                            transaction,
                            mapping,
                            owner,
                            now,
                            lockedUntil,
                            readPastHints,
                            ct
                        )
                        .ConfigureAwait(false);
                    await claimTransaction.CommitAsync(ct).ConfigureAwait(false);
                    return attemptWonIds;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        if (wonIds.Length == 0)
        {
            claimed = [];
        }
        else
        {
            await using var dbContext = await dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            claimed = await dbContext
                .Set<CronJobOccurrenceEntity<TCronJob>>()
                .AsNoTracking()
                .Where(x => wonIds.Contains(x.Id) && x.OwnerId == owner)
                .Include(x => x.CronJob)
                .Select(MappingExtensions.ForQueueCronJobOccurrence<CronJobOccurrenceEntity<TCronJob>, TCronJob>())
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var occurrence in claimed)
        {
            yield return occurrence;
        }
    }

    private static async Task<DateTimeOffset> _RefreshCronOccurrenceLeasesAsync(
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
#pragma warning disable CA2100
        command.CommandText = $"""
            DECLARE @claimNow datetimeoffset(7) = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

            UPDATE occurrence
            SET {mapping.LockedUntil} = {_LeaseDeadlineSql("@claimNow")},
                {mapping.UpdatedAt} = @claimNow
            OUTPUT @claimNow
            FROM {mapping.Table} AS occurrence
            INNER JOIN OPENJSON(@occurrenceIds) AS claimed
                ON occurrence.{mapping.Id} = TRY_CONVERT(uniqueidentifier, claimed.[value])
            WHERE occurrence.{mapping.OwnerId} = @owner;
            """;
#pragma warning restore CA2100
        _AddLeaseDurationParameters(command, leaseDuration);
        command.Parameters.Add(new SqlParameter("occurrenceIds", JsonSerializer.Serialize(occurrenceIds)));
        command.Parameters.Add(new SqlParameter("owner", owner));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The database did not return the refreshed claim clock.");
        }

        return await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CronJobOccurrenceEntity<TCronJob>?> _InsertCronOccurrenceAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        CronOccurrenceRelationalMapping mapping,
        JobManagerDispatchContext item,
        DateTime executionTime,
        string owner,
        DateTimeOffset now,
        DateTime lockedUntil,
        CancellationToken cancellationToken
    )
    {
        var id = guidGenerator.Create();
        await using var command = _CreateCommand(dbContext, transaction);
#pragma warning disable CA2100
        command.CommandText = $"""
            DECLARE @claimNow datetimeoffset(7) = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

            INSERT INTO {mapping.Table}
                ({mapping.Id}, {mapping.Status}, {mapping.OwnerId}, {mapping.ExecutionTime}, {mapping.CronJobId},
                 {mapping.LockedUntil}, {mapping.OnNodeDeath}, {mapping.ElapsedTime}, {mapping.RetryCount},
                 {mapping.CreatedAt}, {mapping.UpdatedAt}, {mapping.Disposition})
            OUTPUT inserted.{mapping.Id}
            SELECT
                @id, @status, @owner, @executionTime, @cronJobId,
                {_LeaseDeadlineSql("@claimNow")}, @onNodeDeath, @elapsedTime, @retryCount,
                @claimNow, @claimNow, @disposition
            WHERE NOT EXISTS (
                SELECT 1
                FROM {mapping.Table} WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                WHERE {mapping.ExecutionTime} = @executionTime AND {mapping.CronJobId} = @cronJobId
                  AND {mapping.AccountsForInstantPredicate("@unaccountedStatus", "@unaccountedDisposition")}
            );
            """;
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("id", id));
        command.Parameters.Add(new SqlParameter("status", nameof(JobStatus.Queued)));
        // KTD1: a row that ACCOUNTS for the instant blocks the insert — every live status, every terminal status,
        // and any status this binary does not recognize (the predicate is a negation, so unknown values fall on the
        // suppressing side). The single exception is the seeding migration's ReplacementOwed retirement, which
        // retired the row without creating a replacement and therefore still owes the fire. Predicate and literals
        // come from CronOccurrenceAccounting via the mapping, so this SQL cannot drift from the LINQ providers or
        // from the PostgreSQL sibling. The lock hints stay: they are what make the read-then-insert atomic here.
        command.Parameters.Add(
            new SqlParameter("unaccountedStatus", CronOccurrenceRelationalMapping.UnaccountedStatusValue)
        );
        command.Parameters.Add(
            new SqlParameter("unaccountedDisposition", CronOccurrenceRelationalMapping.UnaccountedDispositionValue)
        );
        command.Parameters.Add(new SqlParameter("disposition", nameof(CronOccurrenceDisposition.Accounted)));
        command.Parameters.Add(new SqlParameter("owner", owner));
        command.Parameters.Add(_DateTimeParameter("executionTime", executionTime));
        command.Parameters.Add(new SqlParameter("cronJobId", item.Id));
        _AddLeaseDurationParameters(command, lockedUntil - now.UtcDateTime);
        command.Parameters.Add(new SqlParameter("onNodeDeath", item.OnNodeDeath.ToString()));
        command.Parameters.Add(new SqlParameter("elapsedTime", SqlDbType.BigInt) { Value = 0L });
        command.Parameters.Add(new SqlParameter("retryCount", SqlDbType.Int) { Value = 0 });

        object? inserted;
        try
        {
            inserted = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
            when (ex.Number
                    is SqlErrorCodes.SqlServer.DuplicateKeyUniqueIndex
                        or SqlErrorCodes.SqlServer.DuplicateKeyUniqueConstraint
            )
        {
            return null;
        }
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
        DateTimeOffset now,
        DateTime lockedUntil,
        string readPastHints,
        CancellationToken cancellationToken
    )
    {
        var occurrence = item.NextCronOccurrence!;
        await using var command = _CreateCommand(dbContext, transaction);
#pragma warning disable CA2100
        command.CommandText = $"""
            DECLARE @claimNow datetimeoffset(7) = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

            WITH candidate AS (
                SELECT TOP ({JobsClaimStrategyDefaults.MaxClaimBatchSize}) occurrence.{mapping.Id}
                FROM {mapping.Table} AS occurrence WITH ({readPastHints})
                WHERE occurrence.{mapping.Id} = @id
                  AND occurrence.{mapping.ExecutionTime} = @executionTime
                  AND (occurrence.{mapping.Status} = @idle OR occurrence.{mapping.Status} = @queued)
                  AND (occurrence.{mapping.OwnerId} = @owner
                       OR occurrence.{mapping.LockedUntil} IS NULL
                       OR (occurrence.{mapping.LockedUntil} <= @claimNow
                           AND occurrence.{mapping.OnNodeDeath} = @retry))
            )
            UPDATE occurrence
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = {_LeaseDeadlineSql("@claimNow")},
                {mapping.UpdatedAt} = @claimNow,
                {mapping.Status} = @queued,
                {mapping.OnNodeDeath} = @onNodeDeath
            OUTPUT inserted.{mapping.Id}, inserted.{mapping.RecoveredFromUtc}
            FROM {mapping.Table} AS occurrence
            INNER JOIN candidate ON occurrence.{mapping.Id} = candidate.{mapping.Id};
            """;
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("id", occurrence.Id));
        command.Parameters.Add(_DateTimeParameter("executionTime", executionTime));
        command.Parameters.Add(new SqlParameter("idle", nameof(JobStatus.Idle)));
        command.Parameters.Add(new SqlParameter("queued", nameof(JobStatus.Queued)));
        command.Parameters.Add(new SqlParameter("owner", owner));
        command.Parameters.Add(new SqlParameter("retry", nameof(NodeDeathPolicy.Retry)));
        _AddLeaseDurationParameters(command, lockedUntil - now.UtcDateTime);
        command.Parameters.Add(new SqlParameter("onNodeDeath", item.OnNodeDeath.ToString()));
        // R23: read the recovery stamp back out of the row rather than trusting the dispatch context to carry it. The
        // durable row is the only authority for what a coalesced run stands for, and a caller that reconstructed the
        // context from an id alone would otherwise silently demote it to an ordinary run.
        DateTime? claimedRecoveredFrom = null;
        var claimed = await _ReadClaimedIdAsync(command, x => claimedRecoveredFrom = x, cancellationToken)
            .ConfigureAwait(false);

        return claimed is not null
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
                RecoveredFromUtc = claimedRecoveredFrom,
                CronJob = MappingExtensions.ProjectCronJob<TCronJob>(item, owner),
            }
            : null;
    }

    private static async Task<Guid[]> _ClaimFallbackCronOccurrencesAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        CronOccurrenceRelationalMapping mapping,
        string owner,
        DateTimeOffset now,
        DateTime lockedUntil,
        string readPastHints,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(dbContext, transaction);
#pragma warning disable CA2100
        command.CommandText = $"""
            DECLARE @claimNow datetimeoffset(7) = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

            WITH candidates AS (
                SELECT TOP ({JobsClaimStrategyDefaults.MaxClaimBatchSize}) occurrence.{mapping.Id}
                FROM {mapping.Table} AS occurrence WITH ({readPastHints})
                WHERE occurrence.{mapping.ExecutionTime} <= DATEADD(second, -1, @claimNow)
                  AND (occurrence.{mapping.Status} = @idle
                       OR (occurrence.{mapping.Status} = @queued
                           AND (occurrence.{mapping.LockedUntil} IS NULL
                                OR (occurrence.{mapping.LockedUntil} <= @claimNow
                                    AND occurrence.{mapping.OnNodeDeath} = @retry))))
                ORDER BY occurrence.{mapping.ExecutionTime}, occurrence.{mapping.Id}
            )
            UPDATE occurrence
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = {_LeaseDeadlineSql("@claimNow")},
                {mapping.UpdatedAt} = @claimNow,
                {mapping.Status} = @queued
            OUTPUT inserted.{mapping.Id}
            FROM {mapping.Table} AS occurrence
            INNER JOIN candidates ON occurrence.{mapping.Id} = candidates.{mapping.Id};
            """;
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("idle", nameof(JobStatus.Idle)));
        command.Parameters.Add(new SqlParameter("queued", nameof(JobStatus.Queued)));
        command.Parameters.Add(new SqlParameter("retry", nameof(NodeDeathPolicy.Retry)));
        command.Parameters.Add(new SqlParameter("owner", owner));
        _AddLeaseDurationParameters(command, lockedUntil - now.UtcDateTime);

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetGuid(0));
        }

        return [.. ids];
    }

    private static string _BuildDirectCandidates(
        TimeJobEntity[] timeJobs,
        TimeJobRelationalMapping mapping,
        string readPastHints
    )
    {
        var values = string.Join(
            ", ",
            timeJobs.Select((_, index) => $"(@{_ParameterName("id", index)}, @{_ParameterName("updatedAt", index)})")
        );
        return $"""
            SELECT TOP ({JobsClaimStrategyDefaults.MaxClaimBatchSize}) root.{mapping.Id}
            FROM {mapping.Table} AS root WITH ({readPastHints})
            INNER JOIN (VALUES {values}) AS requested(id, updated_at)
                ON requested.id = root.{mapping.Id} AND requested.updated_at = root.{mapping.UpdatedAt}
            ORDER BY CASE WHEN root.{mapping.ExecutionTime} IS NULL THEN 0 ELSE 1 END,
                     root.{mapping.ExecutionTime}, root.{mapping.Id}
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
        params SqlParameter[] candidateParameters
    )
    {
        await using var command = _CreateCommand(dbContext, transaction);
        // SQL structure contains only provider-delimited EF metadata identifiers and fixed clauses;
        // every runtime value remains a command parameter.
#pragma warning disable CA2100
        command.CommandText = $"""
            DECLARE @claimNow datetimeoffset(7) = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');

            WITH candidates AS (
                {candidateSql}
            )
            UPDATE job
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = {_LeaseDeadlineSql("@claimNow")},
                {mapping.UpdatedAt} = @claimNow,
                {mapping.Status} = @queuedStatus
            OUTPUT inserted.{mapping.Id}, @claimNow
            FROM {mapping.Table} AS job
            INNER JOIN candidates ON job.{mapping.Id} = candidates.{mapping.Id};
            """;
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("owner", owner));
        _AddLeaseDurationParameters(command, leaseDuration);
        command.Parameters.Add(new SqlParameter("queuedStatus", nameof(JobStatus.Queued)));
        command.Parameters.AddRange(candidateParameters);

        var ids = new List<Guid>();
        DateTimeOffset? claimedAt = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetGuid(0));
            claimedAt ??= await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken).ConfigureAwait(false);
        }

        return new ClaimResult([.. ids], claimedAt ?? default);
    }

    private static async Task<Guid[]> _StampDescendantsAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        TimeJobRelationalMapping mapping,
        Guid[] rootIds,
        string owner,
        DateTimeOffset claimedAt,
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
        var rootValues = string.Join(", ", rootIds.Select((_, index) => $"(@{_ParameterName("rootId", index)})"));
        // R12/KTD2: bounded recursive CTE that leases the non-timed idle subtree down to maxChainDepth (root = depth 1,
        // so direct children are depth 2). Mirrors the generic-EF frontier claim: descend only THROUGH idle non-timed
        // nodes, so a subtree below a non-idle node (terminalized/running) or a timed boundary (claimed independently in
        // U5) is never leased. Descendants stay Idle — only owner/lease/updated-at are stamped, in the same transacted
        // statement as today. OUTPUT returns the leased ids so the caller prunes the hydrated tree to the claimed set
        // (U3 frontier discipline). MAXRECURSION is sized from maxChainDepth (bounded by JobChain.MaxStructuralDepth =
        // 64, well under the 32767 ceiling). SQL structure contains only provider-delimited EF metadata identifiers and
        // fixed clauses; every runtime value remains a command parameter.
#pragma warning disable CA2100
        command.CommandText = $"""
            WITH descendants (node_id, depth) AS (
                SELECT child.{mapping.Id}, 2
                FROM {mapping.Table} AS child
                INNER JOIN (VALUES {rootValues}) AS roots(id) ON roots.id = child.{mapping.ParentId}
                WHERE child.{mapping.Status} = @idle
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
            UPDATE job
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = {_LeaseDeadlineSql("@claimedAt")},
                {mapping.UpdatedAt} = @claimedAt
            OUTPUT inserted.{mapping.Id}
            FROM {mapping.Table} AS job
            INNER JOIN descendants ON job.{mapping.Id} = descendants.node_id
            WHERE job.{mapping.Status} = @idle
            OPTION (MAXRECURSION {maxChainDepth.ToString(CultureInfo.InvariantCulture)});
            """;
#pragma warning restore CA2100
        for (var index = 0; index < rootIds.Length; index++)
        {
            command.Parameters.Add(new SqlParameter(_ParameterName("rootId", index), rootIds[index]));
        }
        command.Parameters.Add(new SqlParameter("idle", nameof(JobStatus.Idle)));
        command.Parameters.Add(new SqlParameter("owner", owner));
        command.Parameters.Add(_DateTimeOffsetParameter("claimedAt", claimedAt));
        command.Parameters.Add(new SqlParameter("maxDepth", SqlDbType.Int) { Value = maxChainDepth });
        _AddLeaseDurationParameters(command, leaseDuration);

        var leasedIds = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            leasedIds.Add(reader.GetGuid(0));
        }

        return [.. leasedIds];
    }

    private async Task<TResult> _ExecuteWithDeadlockRetryAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken
    )
    {
        // SQL Server has rolled the victim transaction back before surfacing 1205. Retrying the whole scope
        // preserves the root/descendant and definition/occurrence atomicity boundaries.
        return await _deadlockRetryPipeline
            .ExecuteAsync(static async (state, ct) => await state(ct).ConfigureAwait(false), action, cancellationToken)
            .ConfigureAwait(false);
    }

    private static ResiliencePipeline _BuildDeadlockRetryPipeline(TimeProvider timeProvider, ILogger logger)
    {
        // Jittered exponential backoff (mirroring the Coordination SQL Server membership store): retrying the losing
        // scope immediately lets two nodes deadlocking on the same rows livelock into repeated mutual victimization.
        return new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddRetry(
                new RetryStrategyOptions
                {
                    ShouldHandle = static args => new ValueTask<bool>(
                        args.Outcome.Exception is SqlException { Number: SqlErrorCodes.SqlServer.DeadlockVictim }
                    ),
                    MaxRetryAttempts = _MaxDeadlockRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(50),
                    MaxDelay = TimeSpan.FromMilliseconds(500),
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        logger.LogJobsClaimDeadlockRetry(
                            args.AttemptNumber + 2,
                            _MaxDeadlockRetryAttempts + 1,
                            args.RetryDelay,
                            args.Outcome.Exception
                        );

                        return default;
                    },
                }
            )
            .Build();
    }

    private readonly record struct ClaimResult(Guid[] Ids, DateTimeOffset ClaimedAt);

    private static SqlCommand _CreateCommand(TDbContext dbContext, IDbContextTransaction transaction)
    {
        var connection =
            dbContext.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException(
                "SQL Server Jobs claims require a Microsoft.Data.SqlClient connection."
            );
        return new SqlCommand { Connection = connection, Transaction = (SqlTransaction)transaction.GetDbTransaction() };
    }

    private static SqlParameter _DateTimeParameter(string name, DateTime value)
    {
        return new(name, SqlDbType.DateTime2) { Value = value };
    }

    private static SqlParameter _DateTimeOffsetParameter(string name, DateTimeOffset value)
    {
        return new(name, SqlDbType.DateTimeOffset) { Value = value };
    }

    private static string _LeaseDeadlineSql(string start)
    {
        return "DATEADD(nanosecond, @leaseNanoseconds, "
            + "DATEADD(second, @leaseWholeSeconds, "
            + $"DATEADD(day, @leaseDays, {start})))";
    }

    private static void _AddLeaseDurationParameters(SqlCommand command, TimeSpan leaseDuration)
    {
        var leaseDays = checked((int)(leaseDuration.Ticks / TimeSpan.TicksPerDay));
        var ticksWithinDay = leaseDuration.Ticks % TimeSpan.TicksPerDay;
        var leaseWholeSeconds = checked((int)(ticksWithinDay / TimeSpan.TicksPerSecond));
        var leaseNanoseconds = checked((int)(ticksWithinDay % TimeSpan.TicksPerSecond * 100));

        command.Parameters.Add(new SqlParameter("leaseDays", SqlDbType.Int) { Value = leaseDays });
        command.Parameters.Add(new SqlParameter("leaseWholeSeconds", SqlDbType.Int) { Value = leaseWholeSeconds });
        command.Parameters.Add(new SqlParameter("leaseNanoseconds", SqlDbType.Int) { Value = leaseNanoseconds });
    }

    private static string _ParameterName(string prefix, int index)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}{index}");
    }

    private async Task<string> _GetReadPastHintsAsync(CancellationToken cancellationToken)
    {
        Task<string> probe;
        lock (_readPastHintsLock)
        {
            probe = _readPastHintsTask ??= _ProbeReadPastHintsAsync();
        }

        try
        {
            return await probe.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (probe.IsFaulted || probe.IsCanceled)
        {
            lock (_readPastHintsLock)
            {
                if (ReferenceEquals(_readPastHintsTask, probe))
                {
                    _readPastHintsTask = null;
                }
            }

            throw;
        }
    }

    private async Task<string> _ProbeReadPastHintsAsync()
    {
        Interlocked.Increment(ref _readPastHintsProbeCount);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var connection =
            dbContext.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException(
                "SQL Server Jobs claims require a Microsoft.Data.SqlClient connection."
            );
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID();";
        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return GetReadPastHints(result is true);
    }

#pragma warning disable RCS1158 // Static member in generic type should use a type parameter
    internal static string GetReadPastHints(bool readCommittedSnapshotEnabled)
#pragma warning restore RCS1158
    {
        return readCommittedSnapshotEnabled
            ? "UPDLOCK, READPAST, ROWLOCK, READCOMMITTEDLOCK"
            : "UPDLOCK, READPAST, ROWLOCK";
    }

    /// <summary>
    /// Reads the claim's RETURNING row: the claimed id plus the durable recovery stamp. Replaces ExecuteScalar so the
    /// stamp leaves the store with the claim rather than being reconstructed by the caller (R23).
    /// </summary>
    private static async Task<Guid?> _ReadClaimedIdAsync(
        SqlCommand command,
        Action<DateTime?> onRecoveredFrom,
        CancellationToken cancellationToken
    )
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var id = reader.GetGuid(0);
        var isNull = await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false);
        onRecoveredFrom(isNull ? null : DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));

        return id;
    }
}

internal static partial class SqlServerJobsClaimStrategyLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "JobsClaimDeadlockRetry",
        Level = LogLevel.Warning,
        Message = "SQL Server Jobs claim hit deadlock victim error 1205; retrying attempt {AttemptNumber}/{MaxAttempts} after {Delay}."
    )]
    public static partial void LogJobsClaimDeadlockRetry(
        this ILogger logger,
        int attemptNumber,
        int maxAttempts,
        TimeSpan delay,
        Exception? exception
    );
}
