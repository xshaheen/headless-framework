// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
using Headless.Sql.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

#pragma warning disable IDE0130 // Provider implementation intentionally lives in the shared Jobs infrastructure namespace.
#pragma warning disable RCS1015 // SQL parameter names intentionally match lowercase placeholders in the command text.
namespace Headless.Jobs;

internal sealed class PostgreSqlJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>(
    IDbContextFactory<TDbContext> dbContextFactory,
    TimeProvider timeProvider,
    [FromKeyedServices(SetupPostgreSqlJobsEntityFramework.GuidGeneratorKey)] IGuidGenerator guidGenerator,
    IJobsOwnerIdentity ownerIdentity,
    SchedulerOptionsBuilder optionsBuilder,
    ILogger<PostgreSqlJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>> logger,
    JobsRunFilter? runFilter = null
) : IJobsClaimStrategy<TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
#pragma warning disable RCS1158 // Static member in generic type should use a type parameter
    private static readonly SqlColumnType[] _DirectCandidateTypes = [SqlColumnType.Guid, SqlColumnType.Timestamp];

    // Function names are matched as the column stores them, in the database's default collation.
    private static readonly SqlColumnType _FunctionType = SqlColumnType.Text(0);
#pragma warning restore RCS1158
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

        var batch =
            timeJobs.Length <= JobsClaimStrategyDefaults.MaxCandidatePageSize
                ? timeJobs
                : [.. timeJobs.Take(JobsClaimStrategyDefaults.MaxCandidatePageSize)];
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
                    var attemptClaim = await _ClaimRootsAsync(
                            dbContext,
                            transaction,
                            mapping,
                            _DirectCandidateFilter(mapping) + _RunnableClause(_runFilter, mapping.Function),
                            // Unscheduled roots first, as the CAS path visits them; PostgreSQL sorts NULL last by
                            // default.
                            [$"{mapping.ExecutionTime} NULLS FIRST", mapping.Id],
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
                          AND {mapping.ExecutionTime} <= {SqlDialectTokens.Now} - INTERVAL '1 second'
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
                                new NpgsqlParameter("idle", nameof(JobStatus.Idle)),
                                new NpgsqlParameter("queued", nameof(JobStatus.Queued)),
                                new NpgsqlParameter("retry", nameof(NodeDeathPolicy.Retry)),
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
        var connection = (NpgsqlConnection)transaction.GetDbTransaction().Connection!;
#pragma warning disable CA2100 // SQL identifiers are provider-delimited EF metadata; runtime values are parameters.
        await using var command = new NpgsqlCommand(
            $"""
            SELECT 1
            FROM {mapping.Table}
            WHERE {mapping.Id} = @id
              AND {mapping.IsPaused} = FALSE
              AND {mapping.ScheduleRevision} = @scheduleRevision
              {_RunnableClause(runFilter, mapping.Function)}
            FOR UPDATE
            """,
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction()
        );
#pragma warning restore CA2100
        command.Parameters.Add(new NpgsqlParameter<Guid>("id", item.Id));
        command.Parameters.Add(new NpgsqlParameter<long>("scheduleRevision", item.ScheduleRevision));
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

        var claimed = await _ExecuteWithRetryAsync(
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
                    var wonIds = await _ClaimFallbackCronOccurrencesAsync(
                            dbContext,
                            transaction,
                            mapping,
                            _runFilter,
                            owner,
                            _leaseDuration,
                            ct
                        )
                        .ConfigureAwait(false);

                    var attemptClaimed = await dbContext
                        .Set<CronJobOccurrenceEntity<TCronJob>>()
                        .AsNoTracking()
                        .Where(x => wonIds.Contains(x.Id) && x.OwnerId == owner)
                        .Include(x => x.CronJob)
                        .Select(
                            MappingExtensions.ForQueueCronJobOccurrence<CronJobOccurrenceEntity<TCronJob>, TCronJob>()
                        )
                        .ToArrayAsync(ct)
                        .ConfigureAwait(false);
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
        var dialect = PostgreSqlDialect.Instance;
        await using var command = _CreateCommand(dbContext, transaction);

#pragma warning disable CA2100 // SQL structure contains only provider-delimited EF metadata identifiers and fixed clauses;
        command.CommandText = dialect.Render(
            new SqlClockedStatement(
                $"""
                UPDATE {mapping.Table}
                SET {mapping.LockedUntil} = {dialect.ShiftByDuration(SqlDialectTokens.Now, "lease")},
                    {mapping.UpdatedAt} = {SqlDialectTokens.Now}
                WHERE {dialect.InList(mapping.Id, "occurrenceIds", SqlColumnType.Guid)}
                  AND {mapping.OwnerId} = @owner
                RETURNING {mapping.UpdatedAt};
                """
            )
        );
#pragma warning restore CA2100

        dialect.AddDuration(command, "lease", leaseDuration);
        dialect.AddListParameter(command, "occurrenceIds", SqlColumnType.Guid, occurrenceIds);
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
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
        var dialect = PostgreSqlDialect.Instance;
        var id = guidGenerator.Create();
        var snapshot = new CronJobOccurrenceEntity<TCronJob> { Id = id };
        snapshot.SnapshotContract(definition);
        await using var command = _CreateCommand(dbContext, transaction);
        // Hand-written rather than SqlInsertIfAbsent: the guard is the occupied-instant accounting predicate over
        // (CronJobId, ExecutionTime), not a key, and the conflict target is a partial unique index.
#pragma warning disable CA2100
        command.CommandText = dialect.Render(
            new SqlClockedStatement(
                $"""
                INSERT INTO {mapping.Table}
                    ({mapping.Id}, {mapping.Status}, {mapping.OwnerId}, {mapping.ExecutionTime}, {mapping.CronJobId},
                     {mapping.LockedUntil}, {mapping.OnNodeDeath}, {mapping.ElapsedTime}, {mapping.RetryCount},
                     {mapping.CreatedAt}, {mapping.UpdatedAt}, {mapping.Disposition},
                     {mapping.Function}, {mapping.ContractVersion}, {mapping.Request}, {mapping.CorrelationId}, {mapping.CausationId})
                SELECT
                    @id, @status, @owner, @executionTime, @cronJobId,
                    {dialect.ShiftByDuration(SqlDialectTokens.Now, "lease")}, @onNodeDeath,
                    @elapsedTime, @retryCount, {SqlDialectTokens.Now}, {SqlDialectTokens.Now}, @disposition,
                    @function, @contractVersion, @request, @correlationId, @causationId
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM {mapping.Table}
                    WHERE {mapping.CronJobId} = @cronJobId AND {mapping.ExecutionTime} = @executionTime
                      AND {mapping.AccountsForInstantPredicate("@unaccountedStatus", "@unaccountedDisposition")}
                )
                ON CONFLICT ({mapping.ExecutionTime}, {mapping.CronJobId})
                    WHERE {mapping.Status} IN ('Idle', 'Queued', 'InProgress')
                    DO NOTHING
                RETURNING {mapping.Id};
                """
            )
        );
#pragma warning restore CA2100
        command.Parameters.Add(new NpgsqlParameter("id", id));
        command.Parameters.Add(
            new NpgsqlParameter("function", NpgsqlDbType.Text) { Value = (object?)snapshot.Function ?? DBNull.Value }
        );
        command.Parameters.Add(
            new NpgsqlParameter("contractVersion", NpgsqlDbType.Text)
            {
                Value = (object?)snapshot.ContractVersion ?? DBNull.Value,
            }
        );
        command.Parameters.Add(
            new NpgsqlParameter("request", NpgsqlDbType.Bytea) { Value = (object?)snapshot.Request ?? DBNull.Value }
        );
        command.Parameters.Add(
            new NpgsqlParameter("correlationId", NpgsqlDbType.Text)
            {
                Value = (object?)snapshot.CorrelationId ?? DBNull.Value,
            }
        );
        command.Parameters.Add(
            new NpgsqlParameter("causationId", NpgsqlDbType.Text)
            {
                Value = (object?)snapshot.CausationId ?? DBNull.Value,
            }
        );
        command.Parameters.Add(new NpgsqlParameter("status", nameof(JobStatus.Queued)));
        // This is the occupied-instant ACCOUNTING matrix, not the live-only filter this statement used to carry. A
        // terminal row at the instant means the instant ran (or was deliberately retired) and must not fire again;
        // only the seeding migration's ReplacementOwed retirement still owes one, and only it falls through. The
        // predicate and its two literals come from CronOccurrenceAccounting via the mapping, so this SQL cannot
        // drift from the LINQ providers. ON CONFLICT stays: it arbitrates the concurrent-live race the NOT EXISTS
        // read (unlocked under READ COMMITTED) cannot see, and every row starts live, so a row that turns terminal
        // between the two was serialized by the filtered unique index first.
        command.Parameters.Add(
            new NpgsqlParameter("unaccountedStatus", CronOccurrenceRelationalMapping.UnaccountedStatusValue)
        );
        command.Parameters.Add(
            new NpgsqlParameter("unaccountedDisposition", CronOccurrenceRelationalMapping.UnaccountedDispositionValue)
        );
        command.Parameters.Add(new NpgsqlParameter("disposition", nameof(CronOccurrenceDisposition.Accounted)));
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        command.Parameters.Add(new NpgsqlParameter("executionTime", executionTime));
        command.Parameters.Add(new NpgsqlParameter("cronJobId", item.Id));
        dialect.AddDuration(command, "lease", leaseDuration);
        command.Parameters.Add(new NpgsqlParameter("onNodeDeath", item.OnNodeDeath.ToString()));
        command.Parameters.Add(new NpgsqlParameter("elapsedTime", NpgsqlDbType.Bigint) { Value = 0L });
        command.Parameters.Add(new NpgsqlParameter("retryCount", NpgsqlDbType.Integer) { Value = 0 });
        var inserted = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
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
        var dialect = PostgreSqlDialect.Instance;
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
        command.Parameters.Add(new NpgsqlParameter("id", occurrence.Id));
        command.Parameters.Add(new NpgsqlParameter("executionTime", executionTime));
        command.Parameters.Add(new NpgsqlParameter("idle", nameof(JobStatus.Idle)));
        command.Parameters.Add(new NpgsqlParameter("queued", nameof(JobStatus.Queued)));
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        command.Parameters.Add(new NpgsqlParameter("retry", nameof(NodeDeathPolicy.Retry)));
        dialect.AddDuration(command, "lease", leaseDuration);
        command.Parameters.Add(new NpgsqlParameter("onNodeDeath", item.OnNodeDeath.ToString()));
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
        var dialect = PostgreSqlDialect.Instance;
        await using var command = _CreateCommand(dbContext, transaction);

#pragma warning disable CA2100 // SQL structure contains only provider-delimited EF metadata identifiers and fixed clauses; every runtime value remains a command parameter.
        command.CommandText = dialect.Render(
            new SqlClaimNext(
                mapping.Table,
                [mapping.Id],
                $"""
                {mapping.ExecutionTime} <= {SqlDialectTokens.Now} - INTERVAL '1 second'
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

        command.Parameters.Add(new NpgsqlParameter("idle", nameof(JobStatus.Idle)));
        command.Parameters.Add(new NpgsqlParameter("queued", nameof(JobStatus.Queued)));
        command.Parameters.Add(new NpgsqlParameter("retry", nameof(NodeDeathPolicy.Retry)));
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
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
        return PostgreSqlDialect.Instance.InTuples([mapping.Id, mapping.UpdatedAt], "requested", _DirectCandidateTypes);
    }

    private static DbParameter[] _DirectCandidateParameters(TimeJobEntity[] timeJobs)
    {
        return
        [
            .. PostgreSqlDialect.Instance.CreateTupleListParameters(
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
        var dialect = PostgreSqlDialect.Instance;
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
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        dialect.AddDuration(command, "lease", leaseDuration);
        command.Parameters.Add(new NpgsqlParameter("queuedStatus", nameof(JobStatus.Queued)));
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

        var dialect = PostgreSqlDialect.Instance;
        await using var command = _CreateCommand(dbContext, transaction);
        // Bounded WITH RECURSIVE walk that leases the non-timed idle subtree down to maxChainDepth (root =
        // depth 1, so direct children are depth 2). Mirrors the generic-EF frontier claim: descend only THROUGH idle
        // non-timed nodes, so a subtree below a non-idle node (terminalized/running) or a timed boundary (claimed
        // independently) is never leased. Descendants stay Idle — only owner/lease/updated-at are stamped, in the
        // same transacted statement as today. RETURNING the leased ids lets the caller prune the hydrated tree to the
        // claimed set (frontier discipline). No kit shape walks a tree, so the statement stays hand-written; the
        // stamp copies the root's claim instant instead of reading a clock. SQL structure contains only
        // provider-delimited EF metadata identifiers and fixed clauses; every runtime value remains a command
        // parameter.
#pragma warning disable CA2100
        command.CommandText = $"""
            WITH RECURSIVE descendants (node_id, depth) AS (
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
            UPDATE {mapping.Table} AS job
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = {dialect.ShiftByDuration("@claimedAt", "lease")},
                {mapping.UpdatedAt} = @claimedAt
            FROM descendants
            WHERE job.{mapping.Id} = descendants.node_id AND job.{mapping.Status} = @idle
            RETURNING job.{mapping.Id};
            """;
#pragma warning restore CA2100
        dialect.AddListParameter(command, "rootIds", SqlColumnType.Guid, rootIds);
        command.Parameters.Add(new NpgsqlParameter("idle", nameof(JobStatus.Idle)));
        command.Parameters.Add(new NpgsqlParameter("owner", owner));
        command.Parameters.Add(new NpgsqlParameter("claimedAt", NpgsqlDbType.TimestampTz) { Value = claimedAt });
        dialect.AddDuration(command, "lease", leaseDuration);
        command.Parameters.Add(new NpgsqlParameter("maxDepth", NpgsqlDbType.Integer) { Value = maxChainDepth });

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

    // Empty on an unfiltered host, so its statements keep claiming rows of every function.
    private static string _RunnableClause(JobsRunFilter runFilter, string functionColumn) =>
        runFilter.IsFiltered
            ? " AND " + PostgreSqlDialect.Instance.InList(functionColumn, "runnableFunctions", _FunctionType)
            : string.Empty;

    private static DbParameter[] _RunnableParameters(JobsRunFilter runFilter) =>
        runFilter.RunnableFunctions is { } runnable
            ? [PostgreSqlDialect.Instance.CreateListParameter("runnableFunctions", _FunctionType, runnable)]
            : [];

    private static NpgsqlParameter<int> _BatchSizeParameter()
    {
        return new NpgsqlParameter<int>("batchSize", JobsClaimStrategyDefaults.MaxClaimBatchSize);
    }
}
