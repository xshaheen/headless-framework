// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Jobs.Provider;

// Dashboard reads and the compare-and-set writes Operations apply, over the in-memory rows. Each read takes a snapshot
// of the row dictionaries and filters it with the same rules the relational store writes into its queries.
internal sealed partial class JobsInMemoryPersistenceProvider<TTimeJob, TCronJob>
{
    public Task<IReadOnlyList<JobRunSummary>> ReadJobRunRowsAsync(
        JobRunKind kind,
        JobRunQuery query,
        JobRunPosition? after,
        int maxCount,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsPositive(maxCount);
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<JobRunSummary> rows = _RunsOf(kind, query);
        var sortBy = query.SortBy;
        var descending = query.Descending;

        if (after is { } position)
        {
            rows = rows.Where(run =>
            {
                var current = JobRunPosition.Of(run);
                var order = current.CompareKey(sortBy, position);

                if (order == 0)
                {
                    order = run.Id.CompareTo(position.Id);
                }

                return descending ? order < 0 : order > 0;
            });
        }

        int compare(JobRunSummary left, JobRunSummary right)
        {
            var order = JobRunPosition.Of(left).CompareKey(sortBy, JobRunPosition.Of(right));

            return order != 0 ? order : left.Id.CompareTo(right.Id);
        }

        var ordered = descending
            ? rows.Order(Comparer<JobRunSummary>.Create((left, right) => compare(right, left)))
            : rows.Order(Comparer<JobRunSummary>.Create(compare));

        return Task.FromResult<IReadOnlyList<JobRunSummary>>([.. ordered.Take(maxCount)]);
    }

    public Task<int> CountJobRunRowsAsync(
        JobRunKind kind,
        JobRunQuery query,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_RunsOf(kind, query).Take(limit).Count());
    }

    public Task<JobRunSummary?> GetJobRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_timeJobs.TryGetValue(id, out var job))
        {
            return Task.FromResult<JobRunSummary?>(_Summary(job));
        }

        return Task.FromResult(_cronOccurrences.TryGetValue(id, out var occurrence) ? _Summary(occurrence) : null);
    }

    public Task<CronJobOccurrenceEntity<TCronJob>?> GetCronJobOccurrenceByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            _cronOccurrences.TryGetValue(id, out var occurrence) ? _CloneCronOccurrence(occurrence) : null
        );
    }

    public Task<IReadOnlyList<JobFailureGroup>> ReadJobFailureGroupRowsAsync(
        JobRunKind kind,
        DateTimeOffset recentSince,
        string? function,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var failures = _RunsOf(kind, new JobRunQuery { ActionableFailuresOnly = true })
            .Where(run => function is null || string.Equals(run.Function, function, StringComparison.Ordinal))
            .Select(run => (run.Function, run.FailureFingerprint, FailedAt: _FailedAt(run.Kind, run.Id)));

        IReadOnlyList<JobFailureGroup> groups =
        [
            .. failures
                .GroupBy(static failure => (failure.Function, failure.FailureFingerprint))
                .Select(group => new JobFailureGroup
                {
                    Function = group.Key.Function,
                    FailureFingerprint = group.Key.FailureFingerprint,
                    Count = group.Count(),
                    FirstFailedAt = group.Min(static failure => failure.FailedAt),
                    LastFailedAt = group.Max(static failure => failure.FailedAt),
                    RecentCount = group.Count(failure => failure.FailedAt >= recentSince),
                }),
        ];

        return Task.FromResult(groups);
    }

    public Task<IReadOnlyList<JobOperationTarget>> ReadJobOperationTargetsAsync(
        JobRunKind kind,
        JobOperationScope scope,
        Guid? afterId,
        int maxCount,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(scope);
        Argument.IsPositive(maxCount);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<JobOperationTarget> targets =
        [
            .. _ScopeOf(kind, scope)
                .Where(run => afterId is not { } last || run.Id.CompareTo(last) > 0)
                .OrderBy(static run => run.Id)
                .Take(maxCount)
                .Select(static run => new JobOperationTarget(run.Kind, run.Id, run.ChangedAt)),
        ];

        return Task.FromResult(targets);
    }

    public Task<int> CountJobOperationTargetsAsync(
        JobRunKind kind,
        JobOperationScope scope,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(scope);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_ScopeOf(kind, scope).Take(limit).Count());
    }

    public Task<JobRequeueOutcome> RequeueTimeJobIfUnchangedAsync(
        Guid jobId,
        DateTime expectedChangedAt,
        string? traceParent,
        CancellationToken cancellationToken = default
    )
    {
        return _RequeueTimeJobAsync(jobId, expectedChangedAt, traceParent, cancellationToken);
    }

    public Task<JobRequeueOutcome> RequeueCronJobOccurrenceIfUnchangedAsync(
        Guid occurrenceId,
        DateTime expectedChangedAt,
        string? traceParent,
        CancellationToken cancellationToken = default
    )
    {
        return _RequeueCronJobOccurrenceAsync(occurrenceId, expectedChangedAt, traceParent, cancellationToken);
    }

    public Task<bool> RemoveTimeJobIfUnchangedAsync(
        Guid jobId,
        DateTime expectedChangedAt,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The keyed-operations lock is the one every time-job delete and requeue holds, so the check below stays true
        // until the delete, which takes the same reentrant lock, removes the row.
        lock (_keyedOperations)
        {
            if (
                !_timeJobs.TryGetValue(jobId, out var job)
                || job.ChangedAt != expectedChangedAt
                || job.Status == JobStatus.InProgress
            )
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(_RemoveTimeJobs([jobId]) > 0);
        }
    }

    public Task<bool> RemoveCronJobOccurrenceIfUnchangedAsync(
        Guid occurrenceId,
        DateTime expectedChangedAt,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (
            !_cronOccurrences.TryGetValue(occurrenceId, out var occurrence)
            || occurrence.ChangedAt != expectedChangedAt
            || occurrence.Status == JobStatus.InProgress
        )
        {
            return Task.FromResult(false);
        }

        // Removes only the row the check read: a write since then swapped in another instance.
        if (
            !_cronOccurrences.TryRemove(
                new KeyValuePair<Guid, CronJobOccurrenceEntity<TCronJob>>(occurrenceId, occurrence)
            )
        )
        {
            return Task.FromResult(false);
        }

        _RemoveAttempts(_occurrenceAttempts, occurrenceId);

        return Task.FromResult(true);
    }

    public Task<JobRunWindowFigures> CountJobRunWindowAsync(
        JobRunKind kind,
        DateTime from,
        DateTime to,
        TimeSpan lateAfter,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lower = new DateTimeOffset(DateTime.SpecifyKind(from, DateTimeKind.Utc));
        var upper = new DateTimeOffset(DateTime.SpecifyKind(to, DateTimeKind.Utc));
        var runs = _RunsOf(kind, new JobRunQuery()).ToArray();
        var finished = runs.Where(run => run.ExecutedAt >= lower && run.ExecutedAt < upper).ToArray();
        var late = 0;

        foreach (var run in runs)
        {
            if (
                run.ExecutionTime is { } due
                && _FirstAttemptStart(kind, run.Id) is { } started
                && started >= from
                && started < to
                && started > due + lateAfter
            )
            {
                late++;
            }
        }

        return Task.FromResult(
            new JobRunWindowFigures(
                finished.Count(static run => JobRequeueRules.IsSuccess(run.Status)),
                finished.Count(static run => run.Status == JobStatus.Failed),
                late
            )
        );
    }

    public Task<IReadOnlyList<JobFunctionStats>> ReadJobFunctionStatsRowsAsync(
        JobRunKind kind,
        DateTimeOffset since,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<JobFunctionStats> stats =
        [
            .. _RunsOf(kind, new JobRunQuery())
                .GroupBy(static run => run.Function, StringComparer.Ordinal)
                .Select(group => new JobFunctionStats
                {
                    Function = group.Key,
                    Succeeded = group.Count(run => run.ExecutedAt >= since && JobRequeueRules.IsSuccess(run.Status)),
                    Failed = group.Count(run => run.ExecutedAt >= since && run.Status == JobStatus.Failed),
                    Running = group.Count(static run => run.Status == JobStatus.InProgress),
                    Waiting = group.Count(static run => run.Status is JobStatus.Idle or JobStatus.Queued),
                    LastExecutedAt = group.Max(static run => run.ExecutedAt),
                }),
        ];

        return Task.FromResult(stats);
    }

    private JobRunSummary[] _RunsOf(JobRunKind kind, JobRunQuery query)
    {
        return kind switch
        {
            JobRunKind.TimeJob =>
            [
                .. _timeJobs.Values.Where(job => _MatchesTimeJob(job, query)).Select(static job => _Summary(job)),
            ],
            JobRunKind.CronJobOccurrence =>
            [
                .. _cronOccurrences
                    .Values.Where(occurrence => _MatchesOccurrence(occurrence, query))
                    .Select(occurrence => _Summary(occurrence)),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown run kind."),
        };
    }

    private IEnumerable<JobRunSummary> _ScopeOf(JobRunKind kind, JobOperationScope scope)
    {
        IEnumerable<JobRunSummary> runs = _RunsOf(kind, scope.Query);

        return (kind, scope.Kind) switch
        {
            (JobRunKind.TimeJob, JobOperationKind.Requeue) => runs.Where(run =>
                run.ChangedAt <= scope.HighWaterMark
                && JobRequeueRules.RefuseTimeJob(
                    run.Status,
                    _IsChainMember(run.Id, run.ParentId),
                    run.BusinessKey,
                    _IsCurrent(run)
                )
                    is null
            ),
            (JobRunKind.TimeJob, JobOperationKind.Cancel) => runs.Where(run =>
                run.CreatedAt <= scope.ConfirmedAt
                && run.Status is JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress
                && !run.CancelRequested
            ),
            (JobRunKind.TimeJob, JobOperationKind.Delete) => runs.Where(run =>
                run.ChangedAt <= scope.HighWaterMark && run.Status != JobStatus.InProgress && run.BusinessKey is null
            ),
            (JobRunKind.CronJobOccurrence, JobOperationKind.Requeue) => runs.Where(run =>
                run.ChangedAt <= scope.HighWaterMark && JobRequeueRules.RefuseCronJobOccurrence(run.Status) is null
            ),
            (JobRunKind.CronJobOccurrence, JobOperationKind.Delete) => runs.Where(run =>
                run.ChangedAt <= scope.HighWaterMark && run.Status != JobStatus.InProgress
            ),
            _ => [],
        };
    }

    private bool _MatchesTimeJob(TTimeJob job, JobRunQuery query)
    {
        return query.IncludesTimeJobs
            && _MatchesCommon(
                job.Function,
                job.Status,
                job.ExecutionTime,
                job.CorrelationId,
                job.FailureFingerprint,
                query
            )
            && _MatchesText(query.Text, job.Function, job.Id, job.BusinessKey, job.Description)
            && (
                !query.ActionableFailuresOnly
                || JobRequeueRules.IsActionableTimeJobFailure(
                    job.Status,
                    _IsChainMember(job.Id, job.ParentId),
                    job.BusinessKey,
                    job.IsCurrentGeneration
                )
            );
    }

    private bool _MatchesOccurrence(CronJobOccurrenceEntity<TCronJob> occurrence, JobRunQuery query)
    {
        return query.IncludesCronJobOccurrences
            && (query.CronJobId is not { } cronJobId || occurrence.CronJobId == cronJobId)
            && _MatchesCommon(
                occurrence.Function,
                occurrence.Status,
                occurrence.ExecutionTime,
                occurrence.CorrelationId,
                occurrence.FailureFingerprint,
                query
            )
            && _MatchesText(query.Text, occurrence.Function, occurrence.Id, businessKey: null, description: null)
            && (
                !query.ActionableFailuresOnly
                || JobRequeueRules.IsActionableCronJobOccurrenceFailure(
                    occurrence.Status,
                    _cronOccurrences.Values.Any(later =>
                        later.CronJobId == occurrence.CronJobId
                        && later.ExecutionTime > occurrence.ExecutionTime
                        && JobRequeueRules.IsSuccess(later.Status)
                    )
                )
            );
    }

    private static bool _MatchesCommon(
        string function,
        JobStatus status,
        DateTime? executionTime,
        string? correlationId,
        string? failureFingerprint,
        JobRunQuery query
    )
    {
        return (query.Functions is not { Count: > 0 } functions || functions.Contains(function, StringComparer.Ordinal))
            && (query.Statuses is not { Count: > 0 } statuses || statuses.Contains(status))
            && (query.ExecutionFrom is not { } from || executionTime >= from)
            && (query.ExecutionBefore is not { } before || executionTime < before)
            && (
                query.CorrelationId is not { } correlation
                || string.Equals(correlationId, correlation, StringComparison.Ordinal)
            )
            && (
                query.FailureFingerprint is not { } fingerprint
                || string.Equals(failureFingerprint, fingerprint, StringComparison.Ordinal)
            );
    }

    private static bool _MatchesText(string? text, string function, Guid id, string? businessKey, string? description)
    {
        return string.IsNullOrEmpty(text)
            || function.Contains(text, StringComparison.OrdinalIgnoreCase)
            || id.ToString("D").Contains(text, StringComparison.OrdinalIgnoreCase)
            || businessKey?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
            || description?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
    }

    private bool _IsChainMember(Guid id, Guid? parentId)
    {
        return parentId is not null || _GetChildrenIds(id).Length != 0;
    }

    private bool? _IsCurrent(JobRunSummary run)
    {
        return _timeJobs.TryGetValue(run.Id, out var job) ? job.IsCurrentGeneration : null;
    }

    private DateTimeOffset _FailedAt(JobRunKind kind, Guid id)
    {
        if (kind == JobRunKind.TimeJob && _timeJobs.TryGetValue(id, out var job))
        {
            return job.ExecutedAt ?? job.UpdatedAt;
        }

        return _cronOccurrences.TryGetValue(id, out var occurrence)
            ? occurrence.ExecutedAt ?? occurrence.UpdatedAt
            : DateTimeOffset.MinValue;
    }

    private DateTime? _FirstAttemptStart(JobRunKind kind, Guid runId)
    {
        lock (_attemptsLock)
        {
            IEnumerable<JobAttemptEntity>? attempts =
                kind == JobRunKind.TimeJob
                    ? _timeJobAttempts.GetValueOrDefault(runId)
                    : _occurrenceAttempts.GetValueOrDefault(runId);

            return attempts?.Min(static attempt => attempt.StartedAt);
        }
    }

    private static JobRunSummary _Summary(TTimeJob job)
    {
        return new JobRunSummary
        {
            Kind = JobRunKind.TimeJob,
            Id = job.Id,
            Function = job.Function,
            ContractVersion = job.ContractVersion,
            Status = job.Status,
            ExecutionTime = job.ExecutionTime,
            CreatedAt = job.CreatedAt,
            ChangedAt = job.ChangedAt,
            ExecutedAt = job.ExecutedAt,
            ElapsedTime = job.ElapsedTime,
            RetryCount = job.RetryCount,
            OwnerId = job.OwnerId,
            ExceptionMessage = job.ExceptionMessage,
            FailureFingerprint = job.FailureFingerprint,
            SkippedReason = job.SkippedReason,
            ProgressPercent = job.ProgressPercent,
            ProgressMessage = job.ProgressMessage,
            ProgressUpdatedAt = job.ProgressUpdatedAt,
            Retries = job.Retries,
            RetryIntervals = job.RetryIntervals?.ToArray(),
            CorrelationId = job.CorrelationId,
            CausationId = job.CausationId,
            TraceParent = job.TraceParent,
            TenantId = job.TenantId,
            Description = job.Description,
            ParentId = job.ParentId,
            BusinessKey = job.BusinessKey,
            CancelRequested = job.CancelRequested,
        };
    }

    private JobRunSummary _Summary(CronJobOccurrenceEntity<TCronJob> occurrence)
    {
        var cronJob = _cronJobs.GetValueOrDefault(occurrence.CronJobId);

        return new JobRunSummary
        {
            Kind = JobRunKind.CronJobOccurrence,
            Id = occurrence.Id,
            Function = occurrence.Function,
            ContractVersion = occurrence.ContractVersion,
            Status = occurrence.Status,
            ExecutionTime = occurrence.ExecutionTime,
            CreatedAt = occurrence.CreatedAt,
            ChangedAt = occurrence.ChangedAt,
            ExecutedAt = occurrence.ExecutedAt,
            ElapsedTime = occurrence.ElapsedTime,
            RetryCount = occurrence.RetryCount,
            OwnerId = occurrence.OwnerId,
            ExceptionMessage = occurrence.ExceptionMessage,
            FailureFingerprint = occurrence.FailureFingerprint,
            SkippedReason = occurrence.SkippedReason,
            ProgressPercent = occurrence.ProgressPercent,
            ProgressMessage = occurrence.ProgressMessage,
            ProgressUpdatedAt = occurrence.ProgressUpdatedAt,
            // An occurrence retries on its cron job's settings; it stores none of its own.
            Retries = cronJob?.Retries ?? 0,
            RetryIntervals = cronJob?.RetryIntervals?.ToArray(),
            CorrelationId = occurrence.CorrelationId,
            CausationId = occurrence.CausationId,
            TraceParent = occurrence.TraceParent,
            TenantId = occurrence.TenantId,
            CronJobId = occurrence.CronJobId,
        };
    }

    public Task<IReadOnlyList<JobRunBucket>> CountJobRunBucketRowsAsync(
        JobRunKind kind,
        JobRunQuery query,
        IReadOnlyList<DateTime> boundaries,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsNotNull(boundaries);
        cancellationToken.ThrowIfCancellationRequested();

        var edges = boundaries.Select(static edge => DateTime.SpecifyKind(edge, DateTimeKind.Utc)).ToArray();
        var counts = new Dictionary<JobStatus, int>[edges.Length - 1];

        for (var index = 0; index < counts.Length; index++)
        {
            counts[index] = [];
        }

        foreach (var run in _RunsOf(kind, query))
        {
            if (run.ExecutionTime is not { } at || at < edges[0] || at >= edges[^1])
            {
                continue;
            }

            // BinarySearch returns the edge's index on a hit, else the complement of the next edge's index; either
            // way the bucket is the last edge at or before the execution time.
            var found = Array.BinarySearch(edges, DateTime.SpecifyKind(at, DateTimeKind.Utc));
            var bucket = found >= 0 ? found : ~found - 1;
            counts[bucket][run.Status] = counts[bucket].GetValueOrDefault(run.Status) + 1;
        }

        IReadOnlyList<JobRunBucket> buckets =
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

        return Task.FromResult(buckets);
    }

    public Task<IReadOnlyDictionary<string, int>> CountInProgressJobRunRowsByOwnerAsync(
        JobRunKind kind,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyDictionary<string, int> counts = _RunsOf(kind, new JobRunQuery { Statuses = [JobStatus.InProgress] })
            .Where(static run => run.OwnerId is not null)
            .GroupBy(static run => run.OwnerId!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        return Task.FromResult(counts);
    }
}
