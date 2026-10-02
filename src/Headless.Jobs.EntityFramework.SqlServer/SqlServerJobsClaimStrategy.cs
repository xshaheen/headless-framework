// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Headless.Abstractions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Internal;
using Headless.Jobs.Models;
using Headless.Sql;
using Headless.Sql.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs;

internal sealed class SqlServerJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>(
    IDbContextFactory<TDbContext> dbContextFactory,
    TimeProvider timeProvider,
    [FromKeyedServices(SetupSqlServerJobsEntityFramework.GuidGeneratorKey)] IGuidGenerator guidGenerator,
    IJobsOwnerIdentity ownerIdentity,
    SchedulerOptionsBuilder optionsBuilder,
    ILogger<SqlServerJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>> logger,
    JobsRunFilter? runFilter = null
) : IJobsClaimStrategy<TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private static readonly SqlColumnType[] _DirectCandidateTypes = [SqlColumnType.Guid, SqlColumnType.Timestamp];

    // Function names are matched as the column stores them, in the database's default collation.
    private static readonly SqlColumnType _FunctionType = SqlColumnType.Text(0);
    private readonly TimeSpan _leaseDuration = optionsBuilder.LeaseDuration;

    // The maximum number of nodes on a root-to-leaf path the tree claim leases (root = depth 1). A timed
    // descendant is a boundary — not descended into, claimed independently.
    private readonly int _maxChainDepth = optionsBuilder.MaxChainDepth;

    // Gates every root claim, including the direct claim whose candidates a filtered peek already chose, so a claim
    // never depends on its caller having filtered.
    private readonly JobsRunFilter _runFilter = runFilter ?? JobsRunFilter.All;

    public async IAsyncEnumerable<TimeJobEntity> ClaimTimeJobsAsync(
        TimeJobEntity[] timeJobs,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (!ownerIdentity.TryGetStampOwner(out var owner) || timeJobs.Length == 0)
        {
            yield break;
        }

        var (claim, leasedDescendantIds) = await _ExecuteWithRetryAsync(
                JobsClaimRetry.ClaimTimeJobs,
                async (attempt, ct) =>
                {
                    await using var claimTransaction = await JobsClaimTransaction<TDbContext>.CreateAsync(
                        dbContextFactory,
                        attempt,
                        ct
                    );
                    var dbContext = claimTransaction.DbContext;
                    var transaction = claimTransaction.Transaction;
                    var mapping = TimeJobRelationalMapping.Create<TDbContext, TTimeJob>(dbContext);
                    var batch =
                        timeJobs.Length <= JobsClaimStrategyDefaults.MaxCandidatePageSize
                            ? timeJobs
                            : [.. timeJobs.Take(JobsClaimStrategyDefaults.MaxCandidatePageSize)];
                    var attemptClaim = await _ClaimRootsAsync(
                            dbContext,
                            transaction,
                            mapping,
                            _DirectCandidateFilter(mapping) + _RunnableClause(_runFilter, mapping.Function),
                            // Unscheduled roots first, as the CAS path visits them; SQL Server sorts NULL first.
                            [mapping.ExecutionTime, mapping.Id],
                            owner,
                            _leaseDuration,
                            ct,
                            [.. _DirectCandidateParameters(batch), .. _RunnableParameters(_runFilter)]
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

        // The peek-hydrated tree may include non-idle nodes (and their tails) the claim did not lease; prune to
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
        var (claim, leasedDescendantIds) = await _ExecuteWithRetryAsync(
                JobsClaimRetry.ClaimTimedOutTimeJobs,
                async (attempt, ct) =>
                {
                    await using var claimTransaction = await JobsClaimTransaction<TDbContext>.CreateAsync(
                        dbContextFactory,
                        attempt,
                        ct
                    );
                    var dbContext = claimTransaction.DbContext;
                    var transaction = claimTransaction.Transaction;
                    var mapping = TimeJobRelationalMapping.Create<TDbContext, TTimeJob>(dbContext);
                    // The fallback selects timed rows directly, so the parent gate is mirrored in its WHERE
                    // clause — a timed descendant is a candidate only once its parent reached its matching terminal
                    // state.
                    var filter = $"""
                        {mapping.ExecutionTime} IS NOT NULL
                          AND {mapping.ExecutionTime} <= DATEADD(second, -1, {SqlDialectTokens.Now})
                          AND ({mapping.Status} = @idle
                               OR ({mapping.Status} = @queued
                                   AND ({mapping.LockedUntil} IS NULL
                                        OR ({mapping.LockedUntil} <= {SqlDialectTokens.Now}
                                            AND {mapping.OnNodeDeath} = @retry))))
                          {TimedChildGateSql.Build(mapping)}
                          {_RunnableClause(_runFilter, mapping.Function)}
                        """;
                    var attemptClaim = await _ClaimRootsAsync(
                            dbContext,
                            transaction,
                            mapping,
                            filter,
                            [mapping.ExecutionTime, mapping.Id],
                            owner,
                            _leaseDuration,
                            ct,
                            [
                                new SqlParameter("idle", nameof(JobStatus.Idle)),
                                new SqlParameter("queued", nameof(JobStatus.Queued)),
                                new SqlParameter("retry", nameof(NodeDeathPolicy.Retry)),
                                .. _RunnableParameters(_runFilter),
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

        if (claim.Ids.Length == 0)
        {
            claimed = [];
        }
        else
        {
            await using var dbContext = await dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            // Reload the claimed roots flat and rebuild their non-timed subtree to MaxChainDepth in memory (a
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

        var claimed = await _ExecuteWithRetryAsync(
                JobsClaimRetry.ClaimCronJobOccurrences,
                async (attempt, ct) =>
                {
                    CronJobOccurrenceEntity<TCronJob>[] attemptClaimed = [];
                    await using var claimTransaction = await JobsClaimTransaction<TDbContext>.CreateAsync(
                        dbContextFactory,
                        attempt,
                        ct
                    );
                    var dbContext = claimTransaction.DbContext;
                    var transaction = claimTransaction.Transaction;
                    var mapping = CronOccurrenceRelationalMapping.Create<TDbContext, TCronJob>(dbContext);
                    var definitionMapping = CronDefinitionRelationalMapping.Create<TDbContext, TCronJob>(dbContext);
                    var activeItems = new HashSet<JobManagerDispatchContext>();
                    // All batches take definition locks in the same order, even when dispatch order differs.
                    foreach (var item in cronJobOccurrences.Items.OrderBy(x => x.Id))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (
                            !await _LockActiveCronDefinitionAsync(transaction, definitionMapping, item, _runFilter, ct)
                                .ConfigureAwait(false)
                        )
                        {
                            continue;
                        }

                        activeItems.Add(item);
                    }

                    // Read snapshots only after locking definitions so edits cannot race the batch read and insert.
                    var definitionIds = activeItems
                        .Where(x => x.NextCronOccurrence is null)
                        .Select(x => x.Id)
                        .ToArray();
                    var definitions =
                        definitionIds.Length == 0
                            ? []
                            : await dbContext
                                .Set<TCronJob>()
                                .AsNoTracking()
                                .Where(x => definitionIds.Contains(x.Id))
                                .ToDictionaryAsync(x => x.Id, ct)
                                .ConfigureAwait(false);
                    var claimedIds = new List<Guid>();
                    foreach (var item in cronJobOccurrences.Items.Where(activeItems.Contains))
                    {
                        var occurrenceId = item.NextCronOccurrence is null
                            ? await _InsertCronOccurrenceAsync(
                                    dbContext,
                                    transaction,
                                    mapping,
                                    item,
                                    definitions[item.Id],
                                    cronJobOccurrences.Key,
                                    owner,
                                    _leaseDuration,
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
                                    _leaseDuration,
                                    ct
                                )
                                .ConfigureAwait(false);

                        if (occurrenceId is { } id)
                        {
                            claimedIds.Add(id);
                        }
                    }

                    if (claimedIds.Count > 0)
                    {
                        await _RefreshCronOccurrenceLeasesAsync(
                                dbContext,
                                transaction,
                                mapping,
                                [.. claimedIds],
                                owner,
                                _leaseDuration,
                                ct
                            )
                            .ConfigureAwait(false);

                        // Hydrate once after the final write, preserving stored contracts, retry state and clock precision.
                        var occurrences = await dbContext
                            .Set<CronJobOccurrenceEntity<TCronJob>>()
                            .AsNoTracking()
                            .Where(x => claimedIds.Contains(x.Id) && x.OwnerId == owner)
                            .Include(x => x.CronJob)
                            .ToDictionaryAsync(x => x.Id, ct)
                            .ConfigureAwait(false);
                        attemptClaimed = [.. claimedIds.Select(id => occurrences[id])];
                    }

                    await claimTransaction.CommitAsync(ct).ConfigureAwait(false);
                    return attemptClaimed;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        foreach (var occurrence in claimed)
        {
            yield return occurrence;
        }
    }

    private static async Task<bool> _LockActiveCronDefinitionAsync(
        IDbContextTransaction transaction,
        CronDefinitionRelationalMapping mapping,
        JobManagerDispatchContext item,
        JobsRunFilter runFilter,
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
              {_RunnableClause(runFilter, mapping.Function)}
            """,
            connection,
            (SqlTransaction)transaction.GetDbTransaction()
        );
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("id", SqlDbType.UniqueIdentifier) { Value = item.Id });
        command.Parameters.Add(
            new SqlParameter("scheduleRevision", SqlDbType.BigInt) { Value = item.ScheduleRevision }
        );
        command.Parameters.AddRange(_RunnableParameters(runFilter));

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

        CronJobOccurrenceEntity<TCronJob>[] claimed;
        var wonIds = await _ExecuteWithRetryAsync(
                JobsClaimRetry.ClaimTimedOutCronJobOccurrences,
                async (attempt, ct) =>
                {
                    await using var claimTransaction = await JobsClaimTransaction<TDbContext>.CreateAsync(
                        dbContextFactory,
                        attempt,
                        ct
                    );
                    var dbContext = claimTransaction.DbContext;
                    var transaction = claimTransaction.Transaction;
                    var mapping = CronOccurrenceRelationalMapping.Create<TDbContext, TCronJob>(dbContext);
                    var attemptWonIds = await _ClaimFallbackCronOccurrencesAsync(
                            dbContext,
                            transaction,
                            mapping,
                            _runFilter,
                            owner,
                            _leaseDuration,
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

    private static async Task _RefreshCronOccurrenceLeasesAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        CronOccurrenceRelationalMapping mapping,
        Guid[] occurrenceIds,
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    )
    {
        var dialect = SqlServerDialect.Instance;
        await using var command = _CreateCommand(dbContext, transaction);
#pragma warning disable CA2100 // SQL structure contains only provider-delimited EF metadata identifiers and fixed clauses;
        command.CommandText = dialect.Render(
            new SqlClockedStatement(
                $"""
                UPDATE {mapping.Table}
                SET {mapping.LockedUntil} = {dialect.ShiftByDuration(SqlDialectTokens.Now, "lease")},
                    {mapping.UpdatedAt} = {SqlDialectTokens.Now}
                OUTPUT inserted.{mapping.UpdatedAt}
                WHERE {dialect.InList(mapping.Id, "occurrenceIds", SqlColumnType.Guid)}
                  AND {mapping.OwnerId} = @owner;
                """
            )
        );
#pragma warning restore CA2100
        dialect.AddDuration(command, "lease", leaseDuration);
        dialect.AddListParameter(command, "occurrenceIds", SqlColumnType.Guid, occurrenceIds);
        command.Parameters.Add(new SqlParameter("owner", owner));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The database did not return the refreshed claim clock.");
        }
    }

    private async Task<Guid?> _InsertCronOccurrenceAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        CronOccurrenceRelationalMapping mapping,
        JobManagerDispatchContext item,
        TCronJob definition,
        DateTime executionTime,
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    )
    {
        var dialect = SqlServerDialect.Instance;
        var id = guidGenerator.Create();
        var snapshot = new CronJobOccurrenceEntity<TCronJob> { Id = id };
        snapshot.SnapshotContract(definition);
        await using var command = _CreateCommand(dbContext, transaction);
        // Hand-written rather than SqlInsertIfAbsent: the guard is the occupied-instant accounting predicate over
        // (ExecutionTime, CronJobId), not a key.
#pragma warning disable CA2100
        command.CommandText = dialect.Render(
            new SqlClockedStatement(
                $"""
                INSERT INTO {mapping.Table}
                    ({mapping.Id}, {mapping.Status}, {mapping.OwnerId}, {mapping.ExecutionTime}, {mapping.CronJobId},
                     {mapping.LockedUntil}, {mapping.OnNodeDeath}, {mapping.ElapsedTime}, {mapping.RetryCount},
                     {mapping.CreatedAt}, {mapping.UpdatedAt}, {mapping.Disposition},
                     {mapping.Function}, {mapping.ContractVersion}, {mapping.Request}, {mapping.CorrelationId}, {mapping.CausationId})
                OUTPUT inserted.{mapping.Id}
                SELECT
                    @id, @status, @owner, @executionTime, @cronJobId,
                    {dialect.ShiftByDuration(SqlDialectTokens.Now, "lease")}, @onNodeDeath, @elapsedTime, @retryCount,
                    {SqlDialectTokens.Now}, {SqlDialectTokens.Now}, @disposition,
                    @function, @contractVersion, @request, @correlationId, @causationId
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM {mapping.Table} WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                    WHERE {mapping.ExecutionTime} = @executionTime AND {mapping.CronJobId} = @cronJobId
                      AND {mapping.AccountsForInstantPredicate("@unaccountedStatus", "@unaccountedDisposition")}
                );
                """
            )
        );
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("id", id));
        command.Parameters.Add(
            new SqlParameter("function", SqlDbType.NVarChar) { Value = (object?)snapshot.Function ?? DBNull.Value }
        );
        command.Parameters.Add(
            new SqlParameter("contractVersion", SqlDbType.NVarChar)
            {
                Value = (object?)snapshot.ContractVersion ?? DBNull.Value,
            }
        );
        command.Parameters.Add(
            new SqlParameter("request", SqlDbType.VarBinary) { Value = (object?)snapshot.Request ?? DBNull.Value }
        );
        command.Parameters.Add(
            new SqlParameter("correlationId", SqlDbType.NVarChar)
            {
                Value = (object?)snapshot.CorrelationId ?? DBNull.Value,
            }
        );
        command.Parameters.Add(
            new SqlParameter("causationId", SqlDbType.NVarChar)
            {
                Value = (object?)snapshot.CausationId ?? DBNull.Value,
            }
        );
        command.Parameters.Add(new SqlParameter("status", nameof(JobStatus.Queued)));
        // A row that ACCOUNTS for the instant blocks the insert — every live status, every terminal status,
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
        dialect.AddDuration(command, "lease", leaseDuration);
        command.Parameters.Add(new SqlParameter("onNodeDeath", item.OnNodeDeath.ToString()));
        command.Parameters.Add(new SqlParameter("elapsedTime", SqlDbType.BigInt) { Value = 0L });
        command.Parameters.Add(new SqlParameter("retryCount", SqlDbType.Int) { Value = 0 });

        object? inserted;
        try
        {
            inserted = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex) when (dialect.Classify(ex) is SqlErrorKind.UniqueViolation)
        {
            return null;
        }

        return inserted is Guid insertedId ? insertedId : null;
    }

    private static async Task<Guid?> _ClaimExistingCronOccurrenceAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        CronOccurrenceRelationalMapping mapping,
        JobManagerDispatchContext item,
        DateTime executionTime,
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    )
    {
        var dialect = SqlServerDialect.Instance;
        var occurrence = item.NextCronOccurrence!;
        await using var command = _CreateCommand(dbContext, transaction);
#pragma warning disable CA2100
        command.CommandText = dialect.Render(
            new SqlClaimNext(
                mapping.Table,
                [mapping.Id],
                $"""
                {mapping.Id} = @id
                  AND {mapping.ExecutionTime} = @executionTime
                  AND ({mapping.Status} = @idle OR {mapping.Status} = @queued)
                  AND ({mapping.OwnerId} = @owner
                       OR {mapping.LockedUntil} IS NULL
                       OR ({mapping.LockedUntil} <= {SqlDialectTokens.Now}
                           AND {mapping.OnNodeDeath} = @retry))
                """,
                [mapping.Id],
                $"""
                {mapping.OwnerId} = @owner,
                    {mapping.LockedUntil} = {dialect.ShiftByDuration(SqlDialectTokens.Now, "lease")},
                    {mapping.UpdatedAt} = {SqlDialectTokens.Now},
                    {mapping.Status} = @queued,
                    {mapping.OnNodeDeath} = @onNodeDeath
                """,
                [mapping.Id]
            )
        );
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("id", occurrence.Id));
        command.Parameters.Add(_DateTimeParameter("executionTime", executionTime));
        command.Parameters.Add(new SqlParameter("idle", nameof(JobStatus.Idle)));
        command.Parameters.Add(new SqlParameter("queued", nameof(JobStatus.Queued)));
        command.Parameters.Add(new SqlParameter("owner", owner));
        command.Parameters.Add(new SqlParameter("retry", nameof(NodeDeathPolicy.Retry)));
        dialect.AddDuration(command, "lease", leaseDuration);
        command.Parameters.Add(new SqlParameter("onNodeDeath", item.OnNodeDeath.ToString()));
        var claimed = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return claimed is Guid claimedId ? claimedId : null;
    }

    private static async Task<Guid[]> _ClaimFallbackCronOccurrencesAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        CronOccurrenceRelationalMapping mapping,
        JobsRunFilter runFilter,
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    )
    {
        var dialect = SqlServerDialect.Instance;
        await using var command = _CreateCommand(dbContext, transaction);
#pragma warning disable CA2100
        command.CommandText = dialect.Render(
            new SqlClaimNext(
                mapping.Table,
                [mapping.Id],
                $"""
                {mapping.ExecutionTime} <= DATEADD(second, -1, {SqlDialectTokens.Now})
                  AND ({mapping.Status} = @idle
                       OR ({mapping.Status} = @queued
                           AND ({mapping.LockedUntil} IS NULL
                                OR ({mapping.LockedUntil} <= {SqlDialectTokens.Now}
                                    AND {mapping.OnNodeDeath} = @retry))))
                  {_RunnableClause(runFilter, mapping.Function)}
                """,
                [mapping.ExecutionTime, mapping.Id],
                $"""
                {mapping.OwnerId} = @owner,
                    {mapping.LockedUntil} = {dialect.ShiftByDuration(SqlDialectTokens.Now, "lease")},
                    {mapping.UpdatedAt} = {SqlDialectTokens.Now},
                    {mapping.Status} = @queued
                """,
                [mapping.Id],
                BatchSizeParameter: "batchSize"
            )
        );
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("idle", nameof(JobStatus.Idle)));
        command.Parameters.Add(new SqlParameter("queued", nameof(JobStatus.Queued)));
        command.Parameters.Add(new SqlParameter("retry", nameof(NodeDeathPolicy.Retry)));
        command.Parameters.Add(new SqlParameter("owner", owner));
        dialect.AddDuration(command, "lease", leaseDuration);
        command.Parameters.Add(_BatchSizeParameter());
        command.Parameters.AddRange(_RunnableParameters(runFilter));

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetGuid(0));
        }

        return [.. ids];
    }

    // The CAS pairs each id with its own expected UpdatedAt, so the pairs are matched as whole rows: two separate
    // lists would also match an id against another job's stamp. One list parameter carries every pair, so the
    // candidate page size no longer approaches the engine's parameter limit.
    private static string _DirectCandidateFilter(TimeJobRelationalMapping mapping)
    {
        return SqlServerDialect.Instance.InTuples([mapping.Id, mapping.UpdatedAt], "requested", _DirectCandidateTypes);
    }

    private static DbParameter[] _DirectCandidateParameters(TimeJobEntity[] timeJobs)
    {
        return
        [
            .. SqlServerDialect.Instance.CreateTupleListParameters(
                "requested",
                _DirectCandidateTypes,
                [.. timeJobs.Select(static job => (IReadOnlyList<object>)[job.Id, job.UpdatedAt])]
            ),
        ];
    }

    private static async Task<ClaimResult> _ClaimRootsAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        TimeJobRelationalMapping mapping,
        string filter,
        IReadOnlyList<string> orderBy,
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken,
        params DbParameter[] filterParameters
    )
    {
        var dialect = SqlServerDialect.Instance;
        await using var command = _CreateCommand(dbContext, transaction);
        // SQL structure contains only provider-delimited EF metadata identifiers and fixed clauses;
        // every runtime value remains a command parameter. UpdatedAt is returned as the claim instant: the claim sets
        // it to the statement's clock.
#pragma warning disable CA2100
        command.CommandText = dialect.Render(
            new SqlClaimNext(
                mapping.Table,
                [mapping.Id],
                filter,
                orderBy,
                $"""
                {mapping.OwnerId} = @owner,
                    {mapping.LockedUntil} = {dialect.ShiftByDuration(SqlDialectTokens.Now, "lease")},
                    {mapping.UpdatedAt} = {SqlDialectTokens.Now},
                    {mapping.Status} = @queuedStatus
                """,
                [mapping.Id, mapping.UpdatedAt],
                BatchSizeParameter: "batchSize"
            )
        );
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("owner", owner));
        dialect.AddDuration(command, "lease", leaseDuration);
        command.Parameters.Add(new SqlParameter("queuedStatus", nameof(JobStatus.Queued)));
        command.Parameters.Add(_BatchSizeParameter());
        command.Parameters.AddRange(filterParameters);

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

        var dialect = SqlServerDialect.Instance;
        await using var command = _CreateCommand(dbContext, transaction);
        // Bounded recursive CTE that leases the non-timed idle subtree down to maxChainDepth (root = depth 1,
        // so direct children are depth 2). Mirrors the generic-EF frontier claim: descend only THROUGH idle non-timed
        // nodes, so a subtree below a non-idle node (terminalized/running) or a timed boundary (claimed independently)
        // is never leased. Descendants stay Idle — only owner/lease/updated-at are stamped, in the same transacted
        // statement as today. OUTPUT returns the leased ids so the caller prunes the hydrated tree to the claimed set
        // (frontier discipline). No kit shape walks a tree, so the statement stays hand-written; the stamp copies the
        // root's claim instant instead of reading a clock. MAXRECURSION is sized from maxChainDepth (bounded by
        // JobChain.MaxStructuralDepth = 64, well under the 32767 ceiling). SQL structure contains only
        // provider-delimited EF metadata identifiers and fixed clauses; every runtime value remains a command
        // parameter.
#pragma warning disable CA2100
        command.CommandText = $"""
            WITH descendants (node_id, depth) AS (
                SELECT child.{mapping.Id}, 2
                FROM {mapping.Table} AS child
                WHERE {dialect.InList($"child.{mapping.ParentId}", "rootIds", SqlColumnType.Guid)}
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
            UPDATE job
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = {dialect.ShiftByDuration("@claimedAt", "lease")},
                {mapping.UpdatedAt} = @claimedAt
            OUTPUT inserted.{mapping.Id}
            FROM {mapping.Table} AS job
            INNER JOIN descendants ON job.{mapping.Id} = descendants.node_id
            WHERE job.{mapping.Status} = @idle
            OPTION (MAXRECURSION {maxChainDepth.ToString(CultureInfo.InvariantCulture)});
            """;
#pragma warning restore CA2100
        dialect.AddListParameter(command, "rootIds", SqlColumnType.Guid, rootIds);
        command.Parameters.Add(new SqlParameter("idle", nameof(JobStatus.Idle)));
        command.Parameters.Add(new SqlParameter("owner", owner));
        command.Parameters.Add(_DateTimeOffsetParameter("claimedAt", claimedAt));
        command.Parameters.Add(new SqlParameter("maxDepth", SqlDbType.Int) { Value = maxChainDepth });
        dialect.AddDuration(command, "lease", leaseDuration);

        var leasedIds = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            leasedIds.Add(reader.GetGuid(0));
        }

        return [.. leasedIds];
    }

    private Task<TResult> _ExecuteWithRetryAsync<TResult>(
        string operation,
        Func<SqlAutonomousAttempt, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken
    )
    {
        return JobsClaimRetry.RunAsync(operation, action, timeProvider, logger, cancellationToken);
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

    // Empty on an unfiltered host, so its statements keep claiming rows of every function.
    private static string _RunnableClause(JobsRunFilter runFilter, string functionColumn) =>
        runFilter.IsFiltered
            ? " AND " + SqlServerDialect.Instance.InList(functionColumn, "runnableFunctions", _FunctionType)
            : string.Empty;

    private static DbParameter[] _RunnableParameters(JobsRunFilter runFilter) =>
        runFilter.RunnableFunctions is { } runnable
            ? [SqlServerDialect.Instance.CreateListParameter("runnableFunctions", _FunctionType, runnable)]
            : [];

    private static SqlParameter _BatchSizeParameter()
    {
        return new SqlParameter("batchSize", SqlDbType.Int) { Value = JobsClaimStrategyDefaults.MaxClaimBatchSize };
    }

    private static SqlParameter _DateTimeParameter(string name, DateTime value)
    {
        return new(name, SqlDbType.DateTime2) { Value = value };
    }

    private static SqlParameter _DateTimeOffsetParameter(string name, DateTimeOffset value)
    {
        return new(name, SqlDbType.DateTimeOffset) { Value = value };
    }
}
