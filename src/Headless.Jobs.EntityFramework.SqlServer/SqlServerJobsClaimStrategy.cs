// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

#pragma warning disable IDE0130 // Provider implementation intentionally lives in the shared Jobs infrastructure namespace.
#pragma warning disable RCS1015 // SQL parameter names intentionally match lowercase placeholders in the command text.
namespace Headless.Jobs.Infrastructure;

internal sealed class SqlServerJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>(
    IDbContextFactory<TDbContext> dbContextFactory,
    TimeProvider timeProvider,
    IJobsOwnerIdentity ownerIdentity,
    SchedulerOptionsBuilder optionsBuilder
) : IJobsClaimStrategy<TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly TimeSpan _leaseDuration = optionsBuilder.LeaseDuration;
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

        ClaimResult claim;

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
            var readPastHints = await _GetReadPastHintsAsync(cancellationToken).ConfigureAwait(false);
            var batch =
                timeJobs.Length <= JobsClaimStrategyDefaults.MaxCandidatePageSize
                    ? timeJobs
                    : [.. timeJobs.Take(JobsClaimStrategyDefaults.MaxCandidatePageSize)];
            claim = await _ClaimRootsAsync(
                    dbContext,
                    transaction,
                    mapping,
                    _BuildDirectCandidates(batch, mapping, readPastHints),
                    owner,
                    _leaseDuration,
                    cancellationToken,
                    [
                        .. batch.SelectMany(
                            (job, index) =>
                                new SqlParameter[]
                                {
                                    new(_ParameterName("id", index), job.Id),
                                    _DateTimeParameter(_ParameterName("updatedAt", index), job.UpdatedAt),
                                }
                        ),
                    ]
                )
                .ConfigureAwait(false);

            await _StampDescendantsAsync(
                    dbContext,
                    transaction,
                    mapping,
                    claim.Ids,
                    owner,
                    claim.ClaimedAt,
                    _leaseDuration,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await claimTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

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
            var readPastHints = await _GetReadPastHintsAsync(cancellationToken).ConfigureAwait(false);
            var candidates = $"""
                SELECT TOP ({JobsClaimStrategyDefaults.MaxClaimBatchSize}) root.{mapping.Id}
                FROM {mapping.Table} AS root WITH ({readPastHints})
                WHERE root.{mapping.ExecutionTime} IS NOT NULL
                  AND root.{mapping.ExecutionTime} <= @fallbackThreshold
                  AND (root.{mapping.Status} = @idle
                       OR (root.{mapping.Status} = @queued
                           AND (root.{mapping.LockedUntil} IS NULL
                                OR (root.{mapping.LockedUntil} <= @claimNow
                                    AND root.{mapping.OnNodeDeath} = @retry))))
                ORDER BY root.{mapping.ExecutionTime}, root.{mapping.Id}
                """;
            claim = await _ClaimRootsAsync(
                    dbContext,
                    transaction,
                    mapping,
                    candidates,
                    owner,
                    _leaseDuration,
                    cancellationToken,
                    _DateTimeParameter("fallbackThreshold", now.AddSeconds(-1)),
                    new SqlParameter("idle", JobStatus.Idle.ToString()),
                    new SqlParameter("queued", JobStatus.Queued.ToString()),
                    new SqlParameter("retry", NodeDeathPolicy.Retry.ToString())
                )
                .ConfigureAwait(false);

            await _StampDescendantsAsync(
                    dbContext,
                    transaction,
                    mapping,
                    claim.Ids,
                    owner,
                    claim.ClaimedAt,
                    _leaseDuration,
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
            claimed = await dbContext
                .Set<TTimeJob>()
                .AsNoTracking()
                .Where(x => claim.Ids.Contains(x.Id) && x.OwnerId == owner)
                .Include(x => x.Children.Where(y => y.ExecutionTime == null))
                .Select(MappingExtensions.ForQueueTimeJobs<TTimeJob>())
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
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
            var readPastHints = await _GetReadPastHintsAsync(cancellationToken).ConfigureAwait(false);
            foreach (var item in cronJobOccurrences.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                            readPastHints,
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
        Guid[] wonIds;

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
            var readPastHints = await _GetReadPastHintsAsync(cancellationToken).ConfigureAwait(false);
            wonIds = await _ClaimFallbackCronOccurrencesAsync(
                    dbContext,
                    transaction,
                    mapping,
                    owner,
                    now,
                    lockedUntil,
                    readPastHints,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await claimTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

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
#pragma warning disable CA2100
        command.CommandText = $"""
            DECLARE @claimNow datetime2(7) = SYSUTCDATETIME();

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
        return (DateTime)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task<CronJobOccurrenceEntity<TCronJob>?> _InsertCronOccurrenceAsync(
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
        var id = Guid.NewGuid();
        await using var command = _CreateCommand(dbContext, transaction);
#pragma warning disable CA2100
        command.CommandText = $"""
            DECLARE @claimNow datetime2(7) = SYSUTCDATETIME();

            INSERT INTO {mapping.Table}
                ({mapping.Id}, {mapping.Status}, {mapping.OwnerId}, {mapping.ExecutionTime}, {mapping.CronJobId},
                 {mapping.LockedUntil}, {mapping.OnNodeDeath}, {mapping.ElapsedTime}, {mapping.RetryCount},
                 {mapping.CreatedAt}, {mapping.UpdatedAt})
            OUTPUT inserted.{mapping.Id}
            SELECT
                @id, @status, @owner, @executionTime, @cronJobId,
                {_LeaseDeadlineSql("@claimNow")}, @onNodeDeath, @elapsedTime, @retryCount,
                @claimNow, @claimNow
            WHERE NOT EXISTS (
                SELECT 1
                FROM {mapping.Table} WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                WHERE {mapping.ExecutionTime} = @executionTime AND {mapping.CronJobId} = @cronJobId
            );
            """;
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("id", id));
        command.Parameters.Add(new SqlParameter("status", JobStatus.Queued.ToString()));
        command.Parameters.Add(new SqlParameter("owner", owner));
        command.Parameters.Add(_DateTimeParameter("executionTime", executionTime));
        command.Parameters.Add(new SqlParameter("cronJobId", item.Id));
        _AddLeaseDurationParameters(command, lockedUntil - now);
        command.Parameters.Add(new SqlParameter("onNodeDeath", item.OnNodeDeath.ToString()));
        command.Parameters.Add(new SqlParameter("elapsedTime", SqlDbType.BigInt) { Value = 0L });
        command.Parameters.Add(new SqlParameter("retryCount", SqlDbType.Int) { Value = 0 });

        object? inserted;
        try
        {
            inserted = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
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
        DateTime now,
        DateTime lockedUntil,
        string readPastHints,
        CancellationToken cancellationToken
    )
    {
        var occurrence = item.NextCronOccurrence!;
        await using var command = _CreateCommand(dbContext, transaction);
#pragma warning disable CA2100
        command.CommandText = $"""
            DECLARE @claimNow datetime2(7) = SYSUTCDATETIME();

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
            OUTPUT inserted.{mapping.Id}
            FROM {mapping.Table} AS occurrence
            INNER JOIN candidate ON occurrence.{mapping.Id} = candidate.{mapping.Id};
            """;
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("id", occurrence.Id));
        command.Parameters.Add(_DateTimeParameter("executionTime", executionTime));
        command.Parameters.Add(new SqlParameter("idle", JobStatus.Idle.ToString()));
        command.Parameters.Add(new SqlParameter("queued", JobStatus.Queued.ToString()));
        command.Parameters.Add(new SqlParameter("owner", owner));
        command.Parameters.Add(new SqlParameter("retry", NodeDeathPolicy.Retry.ToString()));
        _AddLeaseDurationParameters(command, lockedUntil - now);
        command.Parameters.Add(new SqlParameter("onNodeDeath", item.OnNodeDeath.ToString()));
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
        string readPastHints,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(dbContext, transaction);
#pragma warning disable CA2100
        command.CommandText = $"""
            DECLARE @claimNow datetime2(7) = SYSUTCDATETIME();

            WITH candidates AS (
                SELECT TOP ({JobsClaimStrategyDefaults.MaxClaimBatchSize}) occurrence.{mapping.Id}
                FROM {mapping.Table} AS occurrence WITH ({readPastHints})
                WHERE occurrence.{mapping.ExecutionTime} <= @fallbackThreshold
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
        command.Parameters.Add(_DateTimeParameter("fallbackThreshold", now.AddSeconds(-1)));
        command.Parameters.Add(new SqlParameter("idle", JobStatus.Idle.ToString()));
        command.Parameters.Add(new SqlParameter("queued", JobStatus.Queued.ToString()));
        command.Parameters.Add(new SqlParameter("retry", NodeDeathPolicy.Retry.ToString()));
        command.Parameters.Add(new SqlParameter("owner", owner));
        _AddLeaseDurationParameters(command, lockedUntil - now);

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
            DECLARE @claimNow datetime2(7) = SYSUTCDATETIME();

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
        command.Parameters.Add(new SqlParameter("queuedStatus", JobStatus.Queued.ToString()));
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

    private static async Task _StampDescendantsAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        TimeJobRelationalMapping mapping,
        Guid[] rootIds,
        string owner,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    )
    {
        if (rootIds.Length == 0)
        {
            return;
        }

        await using var command = _CreateCommand(dbContext, transaction);
        var rootValues = string.Join(", ", rootIds.Select((_, index) => $"(@{_ParameterName("rootId", index)})"));
        // SQL structure contains only provider-delimited EF metadata identifiers and fixed clauses;
        // every runtime value remains a command parameter.
#pragma warning disable CA2100
        command.CommandText = $"""
            WITH direct_children AS (
                SELECT child.{mapping.Id}
                FROM {mapping.Table} AS child
                INNER JOIN (VALUES {rootValues}) AS roots(id) ON roots.id = child.{mapping.ParentId}
            ), descendants AS (
                SELECT {mapping.Id} FROM direct_children
                UNION ALL
                SELECT grandchild.{mapping.Id}
                FROM {mapping.Table} AS grandchild
                INNER JOIN direct_children ON direct_children.{mapping.Id} = grandchild.{mapping.ParentId}
            )
            UPDATE job
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = {_LeaseDeadlineSql("@claimedAt")},
                {mapping.UpdatedAt} = @claimedAt
            FROM {mapping.Table} AS job
            INNER JOIN descendants ON job.{mapping.Id} = descendants.{mapping.Id}
            WHERE job.{mapping.Status} = @idle;
            """;
#pragma warning restore CA2100
        for (var index = 0; index < rootIds.Length; index++)
        {
            command.Parameters.Add(new SqlParameter(_ParameterName("rootId", index), rootIds[index]));
        }
        command.Parameters.Add(new SqlParameter("idle", JobStatus.Idle.ToString()));
        command.Parameters.Add(new SqlParameter("owner", owner));
        command.Parameters.Add(_DateTimeParameter("claimedAt", claimedAt));
        _AddLeaseDurationParameters(command, leaseDuration);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct ClaimResult(Guid[] Ids, DateTime ClaimedAt);

    private static SqlCommand _CreateCommand(TDbContext dbContext, IDbContextTransaction transaction)
    {
        var connection =
            dbContext.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException(
                "SQL Server Jobs claims require a Microsoft.Data.SqlClient connection."
            );
        return new SqlCommand { Connection = connection, Transaction = (SqlTransaction)transaction.GetDbTransaction() };
    }

    private static SqlParameter _DateTimeParameter(string name, DateTime value) =>
        new(name, SqlDbType.DateTime2) { Value = value };

    private static string _LeaseDeadlineSql(string start) =>
        "DATEADD(nanosecond, @leaseNanoseconds, "
        + "DATEADD(second, @leaseWholeSeconds, "
        + $"DATEADD(day, @leaseDays, {start})))";

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

    private static string _ParameterName(string prefix, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}{index}");

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

    internal static string GetReadPastHints(bool readCommittedSnapshotEnabled) =>
        readCommittedSnapshotEnabled ? "UPDLOCK, READPAST, ROWLOCK, READCOMMITTEDLOCK" : "UPDLOCK, READPAST, ROWLOCK";
}
