// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Headless.Checks;
using Headless.Fencing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Headless.Jobs.Infrastructure;

// Dashboard reads and Operation scope reads. Each is one query per table; the members that span both tables are the
// interface's default methods, which merge these. Eligibility comes from JobRequeueRules, the same predicates the
// requeue writes use.
internal sealed partial class JobsEfCorePersistenceProvider<TDbContext, TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private static readonly MethodInfo _GuidCompareTo = typeof(Guid).GetMethod(nameof(Guid.CompareTo), [typeof(Guid)])!;

    private static readonly MethodInfo _StringCompare = typeof(string).GetMethod(
        nameof(string.Compare),
        [typeof(string), typeof(string)]
    )!;

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

        if (kind == JobRunKind.TimeJob)
        {
            var rows = _Page(
                _TimeJobRuns(dbContext, query),
                query,
                after,
                static x => x.ExecutionTime ?? JobRunPosition.Unscheduled,
                static x => x.Function,
                static x => x.Status,
                static x => x.Id
            );

            return await _ProjectTimeJobs(rows.Take(maxCount)).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        var occurrences = _Page(
            _OccurrenceRuns(dbContext, query),
            query,
            after,
            static x => x.ExecutionTime,
            static x => x.Function,
            static x => x.Status,
            static x => x.Id
        );

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

        if (_TextPattern(query) is { } pattern)
        {
#pragma warning disable MA0011 // False positive: ToLower runs in the store as LOWER, where no .NET culture applies.
            rows = rows.Where(x =>
                EF.Functions.Like(x.Function.ToLower(), pattern, SqlLikePattern.EscapeCharacter)
                || EF.Functions.Like(x.Id.ToString().ToLower(), pattern, SqlLikePattern.EscapeCharacter)
                || (
                    x.BusinessKey != null
                    && EF.Functions.Like(x.BusinessKey.ToLower(), pattern, SqlLikePattern.EscapeCharacter)
                )
                || (
                    x.Description != null
                    && EF.Functions.Like(x.Description.ToLower(), pattern, SqlLikePattern.EscapeCharacter)
                )
            );
#pragma warning restore MA0011
        }

        if (query.ActionableFailuresOnly)
        {
            rows = rows.Where(JobRequeueRules.ActionableTimeJobFailure(jobs));
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

        if (_TextPattern(query) is { } pattern)
        {
#pragma warning disable MA0011 // False positive: ToLower runs in the store as LOWER, where no .NET culture applies.
            rows = rows.Where(x =>
                EF.Functions.Like(x.Function.ToLower(), pattern, SqlLikePattern.EscapeCharacter)
                || EF.Functions.Like(x.Id.ToString().ToLower(), pattern, SqlLikePattern.EscapeCharacter)
            );
#pragma warning restore MA0011
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

    // The search is literal, so its % and _ are escaped, and case-insensitive on every collation, so both sides are
    // lowered: PostgreSQL's LIKE is case-sensitive, and SQL Server prints identifiers in upper case.
    private static string? _TextPattern(JobRunQuery query)
    {
        return string.IsNullOrEmpty(query.Text) ? null : SqlLikePattern.Contains(query.Text.ToLowerInvariant());
    }

    // Orders one table by the query's sort key and then the identifier, and starts after the cursor's position. The
    // keyset predicate compares the same expression the order sorts on, in the store's own collation and identifier
    // order, so a page boundary never repeats or skips a row. Values reach the store as parameters.
    private static IQueryable<TRow> _Page<TRow>(
        IQueryable<TRow> rows,
        JobRunQuery query,
        JobRunPosition? after,
        Expression<Func<TRow, DateTime>> executionTime,
        Expression<Func<TRow, string>> function,
        Expression<Func<TRow, JobStatus>> status,
        Expression<Func<TRow, Guid>> id
    )
    {
        var row = executionTime.Parameters[0];
        var key = query.SortBy switch
        {
            JobRunSortKey.ExecutionTime => executionTime.Body,
            JobRunSortKey.Function => _Rebind(function, row),
            JobRunSortKey.Status => _StatusRank(_Rebind(status, row)),
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.SortBy, "Unknown sort key."),
        };
        var idKey = _Rebind(id, row);
        var descending = query.Descending;

        if (after is { } position)
        {
            var value = query.SortBy switch
            {
                JobRunSortKey.ExecutionTime => _Parameter(
                    DateTime.SpecifyKind(position.ExecutionTime, DateTimeKind.Utc)
                ),
                JobRunSortKey.Function => _Parameter(position.Function),
                _ => _Parameter((int)position.Status),
            };
            var idOrder = Expression.Call(idKey, _GuidCompareTo, _Parameter(position.Id));
            var past = Expression.OrElse(
                _Past(key, value, descending),
                Expression.AndAlso(Expression.Equal(key, value), _PastZero(idOrder, descending))
            );
            rows = rows.Where(Expression.Lambda<Func<TRow, bool>>(past, row));
        }

        return query.SortBy switch
        {
            JobRunSortKey.Function => _Order(rows, Expression.Lambda<Func<TRow, string>>(key, row), id, descending),
            JobRunSortKey.Status => _Order(rows, Expression.Lambda<Func<TRow, int>>(key, row), id, descending),
            _ => _Order(rows, Expression.Lambda<Func<TRow, DateTime>>(key, row), id, descending),
        };
    }

    private static IQueryable<TRow> _Order<TRow, TKey>(
        IQueryable<TRow> rows,
        Expression<Func<TRow, TKey>> key,
        Expression<Func<TRow, Guid>> id,
        bool descending
    )
    {
        return descending ? rows.OrderByDescending(key).ThenByDescending(id) : rows.OrderBy(key).ThenBy(id);
    }

    // Strings have no > operator in an expression tree; string.Compare translates to the store's comparison.
    private static BinaryExpression _Past(Expression key, Expression value, bool descending)
    {
        return key.Type == typeof(string) ? _PastZero(Expression.Call(_StringCompare, key, value), descending)
            : descending ? Expression.LessThan(key, value)
            : Expression.GreaterThan(key, value);
    }

    private static BinaryExpression _PastZero(Expression order, bool descending)
    {
        var zero = Expression.Constant(0);

        return descending ? Expression.LessThan(order, zero) : Expression.GreaterThan(order, zero);
    }

    // Status is stored by name, so the column's own order is alphabetical; a CASE over the values sorts it in the
    // lifecycle order the in-memory store and the merge use, on every provider alike.
    private static Expression _StatusRank(Expression status)
    {
        var asInt = Expression.Convert(status, typeof(int));
        Expression rank = Expression.Constant(-1);

        foreach (var value in Enum.GetValues<JobStatus>().Reverse())
        {
            var number = Expression.Constant((int)value);
            rank = Expression.Condition(Expression.Equal(asInt, number), number, rank);
        }

        return rank;
    }

    private static Expression _Rebind(LambdaExpression lambda, ParameterExpression row)
    {
        return ReplacingExpressionVisitor.Replace(lambda.Parameters[0], row, lambda.Body);
    }

    // A member of a captured object, which the store sends as a parameter, so one query plan serves every page.
    private static MemberExpression _Parameter<TValue>(TValue value)
    {
        return Expression.Field(Expression.Constant(new StrongBox<TValue>(value)), nameof(StrongBox<>.Value));
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
            OwnerId = x.Lease.Holder,
            IsLeased = x.Lease.State == RowLeaseState.Active,
            ExceptionMessage = x.ExceptionMessage,
            FailureFingerprint = x.FailureFingerprint,
            SkippedReason = x.SkippedReason,
            ProgressPercent = x.ProgressPercent,
            ProgressMessage = x.ProgressMessage,
            ProgressUpdatedAt = x.ProgressUpdatedAt,
            Retries = x.Retries,
            RetryIntervals = x.RetryIntervals,
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
            OwnerId = x.Lease.Holder,
            IsLeased = x.Lease.State == RowLeaseState.Active,
            ExceptionMessage = x.ExceptionMessage,
            FailureFingerprint = x.FailureFingerprint,
            SkippedReason = x.SkippedReason,
            ProgressPercent = x.ProgressPercent,
            ProgressMessage = x.ProgressMessage,
            ProgressUpdatedAt = x.ProgressUpdatedAt,
            // An occurrence retries on its cron job's settings; it stores none of its own.
            Retries = x.CronJob.Retries,
            RetryIntervals = x.CronJob.RetryIntervals,
            CorrelationId = x.CorrelationId,
            CausationId = x.CausationId,
            TraceParent = x.TraceParent,
            TenantId = x.TenantId,
            CronJobId = x.CronJobId,
        });
    }

    public async Task<IReadOnlyList<JobRunBucket>> CountJobRunBucketRowsAsync(
        JobRunKind kind,
        JobRunQuery query,
        IReadOnlyList<DateTime> boundaries,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsNotNull(boundaries);

        var edges = boundaries.Select(static edge => DateTime.SpecifyKind(edge, DateTimeKind.Utc)).ToArray();

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows =
            kind == JobRunKind.TimeJob
                ? await _CountBucketsAsync(
                        _TimeJobRuns(dbContext, query),
                        static x => x.ExecutionTime,
                        static x => x.Status,
                        edges,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
                : await _CountBucketsAsync(
                        _OccurrenceRuns(dbContext, query),
                        static x => (DateTime?)x.ExecutionTime,
                        static x => x.Status,
                        edges,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

        var counts = new Dictionary<JobStatus, int>[edges.Length - 1];

        for (var index = 0; index < counts.Length; index++)
        {
            counts[index] = [];
        }

        foreach (var row in rows)
        {
            counts[row.Bucket][row.Status] = row.Count;
        }

        return
        [
            .. counts.Select(
                (bucket, index) =>
                    new JobRunBucket
                    {
                        Start = edges[index],
                        End = edges[index + 1],
                        Counts = bucket,
                    }
            ),
        ];
    }

    public async Task<IReadOnlyDictionary<string, int>> CountInProgressJobRunRowsByOwnerAsync(
        JobRunKind kind,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows =
            kind == JobRunKind.TimeJob
                ? await dbContext
                    .Set<TTimeJob>()
                    .AsNoTracking()
                    .Where(x => x.Status == JobStatus.InProgress && x.Lease.Holder != null)
                    .GroupBy(x => x.Lease.Holder!)
                    .Select(group => new OwnerCount { Owner = group.Key, Count = group.Count() })
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false)
                : await dbContext
                    .Set<CronJobOccurrenceEntity<TCronJob>>()
                    .AsNoTracking()
                    .Where(x => x.Status == JobStatus.InProgress && x.Lease.Holder != null)
                    .GroupBy(x => x.Lease.Holder!)
                    .Select(group => new OwnerCount { Owner = group.Key, Count = group.Count() })
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

        return rows.ToDictionary(static row => row.Owner, static row => row.Count, StringComparer.Ordinal);
    }

    // One grouped read per table: each row's bucket is a CASE over the edges, computed in an inner projection so the
    // store groups by the projected column instead of repeating the parameterized expression in GROUP BY.
    private static async Task<BucketCount[]> _CountBucketsAsync<TRow>(
        IQueryable<TRow> rows,
        Expression<Func<TRow, DateTime?>> executionTime,
        Expression<Func<TRow, JobStatus>> status,
        DateTime[] edges,
        CancellationToken cancellationToken
    )
    {
        var row = Expression.Parameter(typeof(TRow), "x");
        var at = _Rebind(executionTime, row);
        var inRange = Expression.Lambda<Func<TRow, bool>>(
            Expression.AndAlso(
                Expression.GreaterThanOrEqual(at, _Parameter<DateTime?>(edges[0])),
                Expression.LessThan(at, _Parameter<DateTime?>(edges[^1]))
            ),
            row
        );

        Expression bucket = Expression.Constant(edges.Length - 2);

        for (var index = edges.Length - 2; index >= 1; index--)
        {
            bucket = Expression.Condition(
                Expression.LessThan(at, _Parameter<DateTime?>(edges[index])),
                Expression.Constant(index - 1),
                bucket
            );
        }

        var bucketed = Expression.Lambda<Func<TRow, BucketedRun>>(
            Expression.MemberInit(
                Expression.New(typeof(BucketedRun)),
                Expression.Bind(
                    typeof(BucketedRun).GetProperty(
                        nameof(BucketedRun.Bucket),
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                    )!,
                    bucket
                ),
                Expression.Bind(
                    typeof(BucketedRun).GetProperty(
                        nameof(BucketedRun.Status),
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                    )!,
                    _Rebind(status, row)
                )
            ),
            row
        );

        return await rows.Where(inRange)
            .Select(bucketed)
            .GroupBy(x => new { x.Bucket, x.Status })
            .Select(group => new BucketCount
            {
                Bucket = group.Key.Bucket,
                Status = group.Key.Status,
                Count = group.Count(),
            })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class BucketedRun
    {
        public int Bucket { get; init; }

        public JobStatus Status { get; init; }
    }

    private sealed class BucketCount
    {
        public int Bucket { get; init; }

        public JobStatus Status { get; init; }

        public int Count { get; init; }
    }

    private sealed class OwnerCount
    {
        public required string Owner { get; init; }

        public int Count { get; init; }
    }
}
