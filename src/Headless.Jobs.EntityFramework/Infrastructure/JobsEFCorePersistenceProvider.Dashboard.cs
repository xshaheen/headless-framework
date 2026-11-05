// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

// Dashboard reads and Operation scope reads. Each is one query per table; the members that span both tables are the
// interface's default methods, which merge these. Eligibility comes from JobRequeueRules, the same predicates the
// requeue writes use.
internal sealed partial class JobsEfCorePersistenceProvider<TDbContext, TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public async Task<IReadOnlyList<JobRunSummary>> ReadJobRunRowsAsync(
        JobRunKind kind,
        JobRunQuery query,
        JobRunPosition? after,
        int maxCount,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsPositive(maxCount);

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var unscheduled = JobRunPosition.Unscheduled;
        var hasPosition = after is not null;
        var positionAt = DateTime.SpecifyKind(after?.ExecutionTime ?? default, DateTimeKind.Utc);
        var positionId = after?.Id ?? Guid.Empty;

        if (kind == JobRunKind.TimeJob)
        {
            var rows = _TimeJobRuns(dbContext, query);

            if (hasPosition)
            {
                rows = query.NewestFirst
                    ? rows.Where(x =>
                        (x.ExecutionTime ?? unscheduled) < positionAt
                        || ((x.ExecutionTime ?? unscheduled) == positionAt && x.Id.CompareTo(positionId) < 0)
                    )
                    : rows.Where(x =>
                        (x.ExecutionTime ?? unscheduled) > positionAt
                        || ((x.ExecutionTime ?? unscheduled) == positionAt && x.Id.CompareTo(positionId) > 0)
                    );
            }

            rows = query.NewestFirst
                ? rows.OrderByDescending(x => x.ExecutionTime ?? unscheduled).ThenByDescending(x => x.Id)
                : rows.OrderBy(x => x.ExecutionTime ?? unscheduled).ThenBy(x => x.Id);

            return await _ProjectTimeJobs(rows.Take(maxCount)).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        var occurrences = _OccurrenceRuns(dbContext, query);

        if (hasPosition)
        {
            occurrences = query.NewestFirst
                ? occurrences.Where(x =>
                    x.ExecutionTime < positionAt || (x.ExecutionTime == positionAt && x.Id.CompareTo(positionId) < 0)
                )
                : occurrences.Where(x =>
                    x.ExecutionTime > positionAt || (x.ExecutionTime == positionAt && x.Id.CompareTo(positionId) > 0)
                );
        }

        occurrences = query.NewestFirst
            ? occurrences.OrderByDescending(x => x.ExecutionTime).ThenByDescending(x => x.Id)
            : occurrences.OrderBy(x => x.ExecutionTime).ThenBy(x => x.Id);

        return await _ProjectOccurrences(occurrences.Take(maxCount))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountJobRunRowsAsync(
        JobRunKind kind,
        JobRunQuery query,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Take before Count, so the store stops counting at the limit instead of counting every row.
        return kind == JobRunKind.TimeJob
            ? await _TimeJobRuns(dbContext, query).Take(limit).CountAsync(cancellationToken).ConfigureAwait(false)
            : await _OccurrenceRuns(dbContext, query).Take(limit).CountAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobRunSummary?> GetJobRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await _ProjectTimeJobs(dbContext.Set<TTimeJob>().AsNoTracking().Where(x => x.Id == id))
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            ?? await _ProjectOccurrences(
                    dbContext.Set<CronJobOccurrenceEntity<TCronJob>>().AsNoTracking().Where(x => x.Id == id)
                )
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<CronJobOccurrenceEntity<TCronJob>?> GetCronJobOccurrenceByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobFailureGroup>> ReadJobFailureGroupRowsAsync(
        JobRunKind kind,
        DateTimeOffset recentSince,
        string? function,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var actionable = new JobRunQuery
        {
            ActionableFailuresOnly = true,
            Functions = function is null ? null : [function],
        };

        // A run's failure time is when its last execution ended; UpdatedAt stands in for a row stored without it.
        if (kind == JobRunKind.TimeJob)
        {
            return await _TimeJobRuns(dbContext, actionable)
                .GroupBy(x => new { x.Function, x.FailureFingerprint })
                .Select(group => new JobFailureGroup
                {
                    Function = group.Key.Function,
                    FailureFingerprint = group.Key.FailureFingerprint,
                    Count = group.Count(),
                    FirstFailedAt = group.Min(x => x.ExecutedAt ?? x.UpdatedAt),
                    LastFailedAt = group.Max(x => x.ExecutedAt ?? x.UpdatedAt),
                    RecentCount = group.Count(x => (x.ExecutedAt ?? x.UpdatedAt) >= recentSince),
                })
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return await _OccurrenceRuns(dbContext, actionable)
            .GroupBy(x => new { x.Function, x.FailureFingerprint })
            .Select(group => new JobFailureGroup
            {
                Function = group.Key.Function,
                FailureFingerprint = group.Key.FailureFingerprint,
                Count = group.Count(),
                FirstFailedAt = group.Min(x => x.ExecutedAt ?? x.UpdatedAt),
                LastFailedAt = group.Max(x => x.ExecutedAt ?? x.UpdatedAt),
                RecentCount = group.Count(x => (x.ExecutedAt ?? x.UpdatedAt) >= recentSince),
            })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobOperationTarget>> ReadJobOperationTargetsAsync(
        JobRunKind kind,
        JobOperationScope scope,
        Guid? afterId,
        int maxCount,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(scope);
        Argument.IsPositive(maxCount);

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasPosition = afterId is not null;
        var position = afterId ?? Guid.Empty;

        if (kind == JobRunKind.TimeJob)
        {
            var timeJobs = _TimeJobScope(dbContext, scope);

            if (hasPosition)
            {
                timeJobs = timeJobs.Where(x => x.Id.CompareTo(position) > 0);
            }

            return await timeJobs
                .OrderBy(x => x.Id)
                .Take(maxCount)
                .Select(x => new JobOperationTarget(JobRunKind.TimeJob, x.Id, x.ChangedAt))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var occurrences = _OccurrenceScope(dbContext, scope);

        if (hasPosition)
        {
            occurrences = occurrences.Where(x => x.Id.CompareTo(position) > 0);
        }

        return await occurrences
            .OrderBy(x => x.Id)
            .Take(maxCount)
            .Select(x => new JobOperationTarget(JobRunKind.CronJobOccurrence, x.Id, x.ChangedAt))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountJobOperationTargetsAsync(
        JobRunKind kind,
        JobOperationScope scope,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(scope);

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return kind == JobRunKind.TimeJob
            ? await _TimeJobScope(dbContext, scope).Take(limit).CountAsync(cancellationToken).ConfigureAwait(false)
            : await _OccurrenceScope(dbContext, scope).Take(limit).CountAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobRunWindowFigures> CountJobRunWindowAsync(
        JobRunKind kind,
        DateTime from,
        DateTime to,
        TimeSpan lateAfter,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var lowerUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var upperUtc = DateTime.SpecifyKind(to, DateTimeKind.Utc);
        var lower = new DateTimeOffset(lowerUtc);
        var upper = new DateTimeOffset(upperUtc);
        var lateSeconds = lateAfter.TotalSeconds;

        if (kind == JobRunKind.TimeJob)
        {
            var jobs = dbContext.Set<TTimeJob>().AsNoTracking();
            var finished = jobs.Where(x => x.ExecutedAt >= lower && x.ExecutedAt < upper);
            var succeeded = await finished
                .CountAsync(x => x.Status == JobStatus.Succeeded || x.Status == JobStatus.DueDone, cancellationToken)
                .ConfigureAwait(false);
            var failed = await finished
                .CountAsync(x => x.Status == JobStatus.Failed, cancellationToken)
                .ConfigureAwait(false);
            var late = 0;

            if (JobsAttemptLedger.IsMapped<TimeJobAttemptEntity>(dbContext))
            {
                var attempts = dbContext.Set<TimeJobAttemptEntity>();
                late = await jobs.Where(x => x.ExecutionTime != null)
                    .Select(x => new
                    {
                        Due = x.ExecutionTime!.Value,
                        Started = attempts.Where(a => a.RunId == x.Id).Min(a => a.StartedAt),
                    })
                    .CountAsync(
                        run =>
                            run.Started >= lowerUtc
                            && run.Started < upperUtc
                            && run.Started > run.Due.AddSeconds(lateSeconds),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            return new JobRunWindowFigures(succeeded, failed, late);
        }

        var occurrences = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>().AsNoTracking();
        var finishedOccurrences = occurrences.Where(x => x.ExecutedAt >= lower && x.ExecutedAt < upper);
        var occurrencesSucceeded = await finishedOccurrences
            .CountAsync(x => x.Status == JobStatus.Succeeded || x.Status == JobStatus.DueDone, cancellationToken)
            .ConfigureAwait(false);
        var occurrencesFailed = await finishedOccurrences
            .CountAsync(x => x.Status == JobStatus.Failed, cancellationToken)
            .ConfigureAwait(false);
        var occurrencesLate = 0;

        if (JobsAttemptLedger.IsMapped<CronJobOccurrenceAttemptEntity>(dbContext))
        {
            var attempts = dbContext.Set<CronJobOccurrenceAttemptEntity>();
            occurrencesLate = await occurrences
                .Select(x => new
                {
                    Due = x.ExecutionTime,
                    Started = attempts.Where(a => a.RunId == x.Id).Min(a => a.StartedAt),
                })
                .CountAsync(
                    run =>
                        run.Started >= lowerUtc
                        && run.Started < upperUtc
                        && run.Started > run.Due.AddSeconds(lateSeconds),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return new JobRunWindowFigures(occurrencesSucceeded, occurrencesFailed, occurrencesLate);
    }

    public async Task<IReadOnlyList<JobFunctionStats>> ReadJobFunctionStatsRowsAsync(
        JobRunKind kind,
        DateTimeOffset since,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        if (kind == JobRunKind.TimeJob)
        {
            return await dbContext
                .Set<TTimeJob>()
                .AsNoTracking()
                .GroupBy(x => x.Function)
                .Select(group => new JobFunctionStats
                {
                    Function = group.Key,
                    Succeeded = group.Count(x =>
                        x.ExecutedAt >= since && (x.Status == JobStatus.Succeeded || x.Status == JobStatus.DueDone)
                    ),
                    Failed = group.Count(x => x.ExecutedAt >= since && x.Status == JobStatus.Failed),
                    Running = group.Count(x => x.Status == JobStatus.InProgress),
                    Waiting = group.Count(x => x.Status == JobStatus.Idle || x.Status == JobStatus.Queued),
                    LastExecutedAt = group.Max(x => x.ExecutedAt),
                })
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .AsNoTracking()
            .GroupBy(x => x.Function)
            .Select(group => new JobFunctionStats
            {
                Function = group.Key,
                Succeeded = group.Count(x =>
                    x.ExecutedAt >= since && (x.Status == JobStatus.Succeeded || x.Status == JobStatus.DueDone)
                ),
                Failed = group.Count(x => x.ExecutedAt >= since && x.Status == JobStatus.Failed),
                Running = group.Count(x => x.Status == JobStatus.InProgress),
                Waiting = group.Count(x => x.Status == JobStatus.Idle || x.Status == JobStatus.Queued),
                LastExecutedAt = group.Max(x => x.ExecutedAt),
            })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static IQueryable<TTimeJob> _TimeJobRuns(TDbContext dbContext, JobRunQuery query)
    {
        var jobs = dbContext.Set<TTimeJob>();
        var rows = jobs.AsNoTracking();

        if (!query.IncludesTimeJobs)
        {
            return rows.Where(_ => false);
        }

        if (query.Functions is { Count: > 0 } functions)
        {
            var names = functions.ToArray();
            rows = rows.Where(x => names.Contains(x.Function));
        }

        if (query.Statuses is { Count: > 0 } statuses)
        {
            var values = statuses.ToArray();
            rows = rows.Where(x => values.Contains(x.Status));
        }

        if (query.ExecutionFrom is { } from)
        {
            var bound = DateTime.SpecifyKind(from, DateTimeKind.Utc);
            rows = rows.Where(x => x.ExecutionTime >= bound);
        }

        if (query.ExecutionBefore is { } before)
        {
            var bound = DateTime.SpecifyKind(before, DateTimeKind.Utc);
            rows = rows.Where(x => x.ExecutionTime < bound);
        }

        if (query.CorrelationId is { } correlationId)
        {
            rows = rows.Where(x => x.CorrelationId == correlationId);
        }

        if (query.FailureFingerprint is { } fingerprint)
        {
            rows = rows.Where(x => x.FailureFingerprint == fingerprint);
        }

        if (query.ActionableFailuresOnly)
        {
            rows = rows.Where(JobRequeueRules.RequeueableTimeJob(jobs));
        }

        return rows;
    }

    private static IQueryable<CronJobOccurrenceEntity<TCronJob>> _OccurrenceRuns(
        TDbContext dbContext,
        JobRunQuery query
    )
    {
        var occurrences = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>();
        var rows = occurrences.AsNoTracking();

        if (!query.IncludesCronJobOccurrences)
        {
            return rows.Where(_ => false);
        }

        if (query.CronJobId is { } cronJobId)
        {
            rows = rows.Where(x => x.CronJobId == cronJobId);
        }

        if (query.Functions is { Count: > 0 } functions)
        {
            var names = functions.ToArray();
            rows = rows.Where(x => names.Contains(x.Function));
        }

        if (query.Statuses is { Count: > 0 } statuses)
        {
            var values = statuses.ToArray();
            rows = rows.Where(x => values.Contains(x.Status));
        }

        if (query.ExecutionFrom is { } from)
        {
            var bound = DateTime.SpecifyKind(from, DateTimeKind.Utc);
            rows = rows.Where(x => x.ExecutionTime >= bound);
        }

        if (query.ExecutionBefore is { } before)
        {
            var bound = DateTime.SpecifyKind(before, DateTimeKind.Utc);
            rows = rows.Where(x => x.ExecutionTime < bound);
        }

        if (query.CorrelationId is { } correlationId)
        {
            rows = rows.Where(x => x.CorrelationId == correlationId);
        }

        if (query.FailureFingerprint is { } fingerprint)
        {
            rows = rows.Where(x => x.FailureFingerprint == fingerprint);
        }

        if (query.ActionableFailuresOnly)
        {
            rows = rows.Where(JobRequeueRules.ActionableCronJobOccurrenceFailure(occurrences));
        }

        return rows;
    }

    private static IQueryable<TTimeJob> _TimeJobScope(TDbContext dbContext, JobOperationScope scope)
    {
        var rows = _TimeJobRuns(dbContext, scope.Query);
        var highWater = DateTime.SpecifyKind(scope.HighWaterMark, DateTimeKind.Utc);
        var confirmedAt = scope.ConfirmedAt;

        return scope.Kind switch
        {
            JobOperationKind.Requeue => rows.Where(x => x.ChangedAt <= highWater)
                .Where(JobRequeueRules.RequeueableTimeJob(dbContext.Set<TTimeJob>())),
            // A running job keeps advancing its marker as it reports progress, so cancel freezes on creation.
            JobOperationKind.Cancel => rows.Where(x =>
                x.CreatedAt <= confirmedAt
                && (x.Status == JobStatus.Idle || x.Status == JobStatus.Queued || x.Status == JobStatus.InProgress)
                && !x.CancelRequested
            ),
            // Keyed generations are retained, so a delete refuses them.
            JobOperationKind.Delete => rows.Where(x =>
                x.ChangedAt <= highWater && x.Status != JobStatus.InProgress && x.BusinessKey == null
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope.Kind, "Unknown operation kind."),
        };
    }

    private static IQueryable<CronJobOccurrenceEntity<TCronJob>> _OccurrenceScope(
        TDbContext dbContext,
        JobOperationScope scope
    )
    {
        var rows = _OccurrenceRuns(dbContext, scope.Query);
        var highWater = DateTime.SpecifyKind(scope.HighWaterMark, DateTimeKind.Utc);

        return scope.Kind switch
        {
            JobOperationKind.Requeue => rows.Where(x => x.ChangedAt <= highWater)
                .Where(JobRequeueRules.RequeueableCronJobOccurrence<TCronJob>()),
            // Occurrences take no cancel request.
            JobOperationKind.Cancel => rows.Where(_ => false),
            JobOperationKind.Delete => rows.Where(x => x.ChangedAt <= highWater && x.Status != JobStatus.InProgress),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope.Kind, "Unknown operation kind."),
        };
    }

    private static IQueryable<JobRunSummary> _ProjectTimeJobs(IQueryable<TTimeJob> rows)
    {
        return rows.Select(x => new JobRunSummary
        {
            Kind = JobRunKind.TimeJob,
            Id = x.Id,
            Function = x.Function,
            ContractVersion = x.ContractVersion,
            Status = x.Status,
            ExecutionTime = x.ExecutionTime,
            CreatedAt = x.CreatedAt,
            ChangedAt = x.ChangedAt,
            ExecutedAt = x.ExecutedAt,
            ElapsedTime = x.ElapsedTime,
            RetryCount = x.RetryCount,
            OwnerId = x.OwnerId,
            ExceptionMessage = x.ExceptionMessage,
            FailureFingerprint = x.FailureFingerprint,
            SkippedReason = x.SkippedReason,
            ProgressPercent = x.ProgressPercent,
            ProgressMessage = x.ProgressMessage,
            CorrelationId = x.CorrelationId,
            CausationId = x.CausationId,
            TraceParent = x.TraceParent,
            TenantId = x.TenantId,
            Description = x.Description,
            ParentId = x.ParentId,
            BusinessKey = x.BusinessKey,
            CancelRequested = x.CancelRequested,
        });
    }

    private static IQueryable<JobRunSummary> _ProjectOccurrences(IQueryable<CronJobOccurrenceEntity<TCronJob>> rows)
    {
        return rows.Select(x => new JobRunSummary
        {
            Kind = JobRunKind.CronJobOccurrence,
            Id = x.Id,
            Function = x.Function,
            ContractVersion = x.ContractVersion,
            Status = x.Status,
            ExecutionTime = x.ExecutionTime,
            CreatedAt = x.CreatedAt,
            ChangedAt = x.ChangedAt,
            ExecutedAt = x.ExecutedAt,
            ElapsedTime = x.ElapsedTime,
            RetryCount = x.RetryCount,
            OwnerId = x.OwnerId,
            ExceptionMessage = x.ExceptionMessage,
            FailureFingerprint = x.FailureFingerprint,
            SkippedReason = x.SkippedReason,
            ProgressPercent = x.ProgressPercent,
            ProgressMessage = x.ProgressMessage,
            CorrelationId = x.CorrelationId,
            CausationId = x.CausationId,
            TraceParent = x.TraceParent,
            TenantId = x.TenantId,
            CronJobId = x.CronJobId,
        });
    }
}
