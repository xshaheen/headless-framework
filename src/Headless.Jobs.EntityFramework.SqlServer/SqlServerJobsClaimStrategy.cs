// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
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
    private const int _MaxDirectClaimBatchSize = 1000;
    private readonly TimeSpan _leaseDuration = optionsBuilder.LeaseDuration;

    public async IAsyncEnumerable<TimeJobEntity> ClaimTimeJobsAsync(
        TimeJobEntity[] timeJobs,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (!ownerIdentity.TryGetStampOwner(out var owner) || timeJobs.Length == 0)
        {
            yield break;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var lockedUntil = now.Add(_leaseDuration);
        Guid[] wonIds;

        await using (
            var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false)
        )
        await using (
            var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
        )
        {
            var mapping = TimeJobRelationalMapping.Create<TDbContext, TTimeJob>(dbContext);
            var readPastHints = await _GetReadPastHintsAsync(dbContext, transaction, cancellationToken)
                .ConfigureAwait(false);
            var wonIdList = new List<Guid>(timeJobs.Length);
            foreach (var batch in timeJobs.Chunk(_MaxDirectClaimBatchSize))
            {
                var batchWonIds = await _ClaimRootsAsync(
                        dbContext,
                        transaction,
                        mapping,
                        _BuildDirectCandidates(batch, mapping, readPastHints),
                        owner,
                        now,
                        lockedUntil,
                        cancellationToken,
                        batch
                            .SelectMany(
                                (job, index) =>
                                    new SqlParameter[]
                                    {
                                        new(_ParameterName("id", index), job.Id),
                                        _DateTimeParameter(_ParameterName("updatedAt", index), job.UpdatedAt),
                                    }
                            )
                            .ToArray()
                    )
                    .ConfigureAwait(false);
                wonIdList.AddRange(batchWonIds);
            }

            wonIds = wonIdList.ToArray();

            await _StampDescendantsAsync(
                    dbContext,
                    transaction,
                    mapping,
                    wonIds,
                    owner,
                    now,
                    lockedUntil,
                    cancellationToken
                )
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var won = wonIds.ToHashSet();
        foreach (var timeJob in timeJobs)
        {
            if (!won.Contains(timeJob.Id))
            {
                continue;
            }

            timeJob.OwnerId = owner;
            timeJob.LockedUntil = lockedUntil;
            timeJob.UpdatedAt = now;
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
        var lockedUntil = now.Add(_leaseDuration);
        TimeJobEntity[] claimed;

        await using (
            var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false)
        )
        await using (
            var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
        )
        {
            var mapping = TimeJobRelationalMapping.Create<TDbContext, TTimeJob>(dbContext);
            var readPastHints = await _GetReadPastHintsAsync(dbContext, transaction, cancellationToken)
                .ConfigureAwait(false);
            var candidates = $"""
                SELECT TOP (2147483647) root.{mapping.Id}
                FROM {mapping.Table} AS root WITH ({readPastHints})
                WHERE root.{mapping.ExecutionTime} IS NOT NULL
                  AND root.{mapping.ExecutionTime} <= @fallbackThreshold
                  AND (root.{mapping.Status} = @idle
                       OR (root.{mapping.Status} = @queued
                           AND (root.{mapping.LockedUntil} IS NULL
                                OR (root.{mapping.LockedUntil} <= @now AND root.{mapping.OnNodeDeath} = @retry))))
                ORDER BY root.{mapping.ExecutionTime}, root.{mapping.Id}
                """;
            var wonIds = await _ClaimRootsAsync(
                    dbContext,
                    transaction,
                    mapping,
                    candidates,
                    owner,
                    now,
                    lockedUntil,
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
                    wonIds,
                    owner,
                    now,
                    lockedUntil,
                    cancellationToken
                )
                .ConfigureAwait(false);

            claimed = await dbContext
                .Set<TTimeJob>()
                .AsNoTracking()
                .Where(x => wonIds.Contains(x.Id) && x.OwnerId == owner && x.UpdatedAt == now)
                .Include(x => x.Children.Where(y => y.ExecutionTime == null))
                .Select(MappingExtensions.ForQueueTimeJobs<TTimeJob>())
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        foreach (var timeJob in claimed)
        {
            timeJob.OwnerId = owner;
            timeJob.LockedUntil = lockedUntil;
            timeJob.UpdatedAt = now;
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
            var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false)
        )
        await using (
            var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
        )
        {
            var mapping = CronOccurrenceRelationalMapping.Create<TDbContext, TCronJob>(dbContext);
            var readPastHints = await _GetReadPastHintsAsync(dbContext, transaction, cancellationToken)
                .ConfigureAwait(false);
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

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        foreach (var occurrence in claimed)
        {
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

        await using (
            var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false)
        )
        await using (
            var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
        )
        {
            var mapping = CronOccurrenceRelationalMapping.Create<TDbContext, TCronJob>(dbContext);
            var readPastHints = await _GetReadPastHintsAsync(dbContext, transaction, cancellationToken)
                .ConfigureAwait(false);
            var wonIds = await _ClaimFallbackCronOccurrencesAsync(
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

            claimed = await dbContext
                .Set<CronJobOccurrenceEntity<TCronJob>>()
                .AsNoTracking()
                .Where(x => wonIds.Contains(x.Id) && x.OwnerId == owner && x.UpdatedAt == now)
                .Include(x => x.CronJob)
                .Select(MappingExtensions.ForQueueCronJobOccurrence<CronJobOccurrenceEntity<TCronJob>, TCronJob>())
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        foreach (var occurrence in claimed)
        {
            occurrence.OwnerId = owner;
            occurrence.LockedUntil = lockedUntil;
            occurrence.UpdatedAt = now;
            occurrence.Status = JobStatus.Queued;
            yield return occurrence;
        }
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
            INSERT INTO {mapping.Table}
                ({mapping.Id}, {mapping.Status}, {mapping.OwnerId}, {mapping.ExecutionTime}, {mapping.CronJobId},
                 {mapping.LockedUntil}, {mapping.OnNodeDeath}, {mapping.ElapsedTime}, {mapping.RetryCount},
                 {mapping.CreatedAt}, {mapping.UpdatedAt})
            OUTPUT inserted.{mapping.Id}
            SELECT
                @id, @status, @owner, @executionTime, @cronJobId,
                @lockedUntil, @onNodeDeath, @elapsedTime, @retryCount, @now, @now
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
        command.Parameters.Add(_DateTimeParameter("lockedUntil", lockedUntil));
        command.Parameters.Add(new SqlParameter("onNodeDeath", item.OnNodeDeath.ToString()));
        command.Parameters.Add(new SqlParameter("elapsedTime", SqlDbType.BigInt) { Value = 0L });
        command.Parameters.Add(new SqlParameter("retryCount", SqlDbType.Int) { Value = 0 });
        command.Parameters.Add(_DateTimeParameter("now", now));

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
            WITH candidate AS (
                SELECT TOP (2147483647) occurrence.{mapping.Id}
                FROM {mapping.Table} AS occurrence WITH ({readPastHints})
                WHERE occurrence.{mapping.Id} = @id
                  AND occurrence.{mapping.ExecutionTime} = @executionTime
                  AND (occurrence.{mapping.Status} = @idle OR occurrence.{mapping.Status} = @queued)
                  AND (occurrence.{mapping.OwnerId} = @owner
                       OR occurrence.{mapping.LockedUntil} IS NULL
                       OR (occurrence.{mapping.LockedUntil} <= @now AND occurrence.{mapping.OnNodeDeath} = @retry))
            )
            UPDATE occurrence
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = @lockedUntil,
                {mapping.UpdatedAt} = @now,
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
        command.Parameters.Add(_DateTimeParameter("now", now));
        command.Parameters.Add(new SqlParameter("retry", NodeDeathPolicy.Retry.ToString()));
        command.Parameters.Add(_DateTimeParameter("lockedUntil", lockedUntil));
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
            WITH candidates AS (
                SELECT TOP (2147483647) occurrence.{mapping.Id}
                FROM {mapping.Table} AS occurrence WITH ({readPastHints})
                WHERE occurrence.{mapping.ExecutionTime} <= @fallbackThreshold
                  AND (occurrence.{mapping.Status} = @idle
                       OR (occurrence.{mapping.Status} = @queued
                           AND (occurrence.{mapping.LockedUntil} IS NULL
                                OR (occurrence.{mapping.LockedUntil} <= @now
                                    AND occurrence.{mapping.OnNodeDeath} = @retry))))
                ORDER BY occurrence.{mapping.ExecutionTime}, occurrence.{mapping.Id}
            )
            UPDATE occurrence
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = @lockedUntil,
                {mapping.UpdatedAt} = @now,
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
        command.Parameters.Add(_DateTimeParameter("lockedUntil", lockedUntil));
        command.Parameters.Add(_DateTimeParameter("now", now));

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids.ToArray();
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
            SELECT TOP (2147483647) root.{mapping.Id}
            FROM {mapping.Table} AS root WITH ({readPastHints})
            INNER JOIN (VALUES {values}) AS requested(id, updated_at)
                ON requested.id = root.{mapping.Id} AND requested.updated_at = root.{mapping.UpdatedAt}
            ORDER BY CASE WHEN root.{mapping.ExecutionTime} IS NULL THEN 0 ELSE 1 END,
                     root.{mapping.ExecutionTime}, root.{mapping.Id}
            """;
    }

    private static async Task<Guid[]> _ClaimRootsAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        TimeJobRelationalMapping mapping,
        string candidateSql,
        string owner,
        DateTime now,
        DateTime lockedUntil,
        CancellationToken cancellationToken,
        params SqlParameter[] candidateParameters
    )
    {
        await using var command = _CreateCommand(dbContext, transaction);
        // SQL structure contains only provider-delimited EF metadata identifiers and fixed clauses;
        // every runtime value remains a command parameter.
#pragma warning disable CA2100
        command.CommandText = $"""
            WITH candidates AS (
                {candidateSql}
            )
            UPDATE job
            SET {mapping.OwnerId} = @owner,
                {mapping.LockedUntil} = @lockedUntil,
                {mapping.UpdatedAt} = @now,
                {mapping.Status} = @queuedStatus
            OUTPUT inserted.{mapping.Id}
            FROM {mapping.Table} AS job
            INNER JOIN candidates ON job.{mapping.Id} = candidates.{mapping.Id};
            """;
#pragma warning restore CA2100
        command.Parameters.Add(new SqlParameter("owner", owner));
        command.Parameters.Add(_DateTimeParameter("lockedUntil", lockedUntil));
        command.Parameters.Add(_DateTimeParameter("now", now));
        command.Parameters.Add(new SqlParameter("queuedStatus", JobStatus.Queued.ToString()));
        command.Parameters.AddRange(candidateParameters);

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids.ToArray();
    }

    private static async Task _StampDescendantsAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        TimeJobRelationalMapping mapping,
        Guid[] rootIds,
        string owner,
        DateTime now,
        DateTime lockedUntil,
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
                {mapping.LockedUntil} = @lockedUntil,
                {mapping.UpdatedAt} = @now
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
        command.Parameters.Add(_DateTimeParameter("lockedUntil", lockedUntil));
        command.Parameters.Add(_DateTimeParameter("now", now));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

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

    private static string _ParameterName(string prefix, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}{index}");

    private static async Task<string> _GetReadPastHintsAsync(
        TDbContext dbContext,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(dbContext, transaction);
        command.CommandText = "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID();";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return GetReadPastHints(result is true);
    }

    internal static string GetReadPastHints(bool readCommittedSnapshotEnabled) =>
        readCommittedSnapshotEnabled ? "UPDLOCK, READPAST, ROWLOCK, READCOMMITTEDLOCK" : "UPDLOCK, READPAST, ROWLOCK";
}
