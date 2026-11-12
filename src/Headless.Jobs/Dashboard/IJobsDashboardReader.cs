// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// The Jobs reads a dashboard module needs, closed over the host's entity types, so a module registered without type
/// arguments reaches the store, scheduler, and function registry <c>AddHeadlessJobs&lt;TTimeJob, TCronJob&gt;</c>
/// configured. <see cref="IJobsDashboardWriter"/> holds every call that changes state.
/// </summary>
/// <remarks>
/// <c>AddHeadlessJobs</c> registers one backend per host as both this and <see cref="IJobsDashboardWriter"/>. Every
/// member delegates to the host's persistence provider, scheduler, function registry, or node membership; none keeps
/// state of its own.
/// </remarks>
internal interface IJobsDashboardReader
{
    /// <summary>Gets the zone a cron job with no zone of its own fires in.</summary>
    TimeZoneInfo SchedulerTimeZone { get; }

    /// <summary>
    /// Gets how long after it was due a run may start before it counts as late: the fallback interval, the longest a
    /// due run waits for a node to claim it when no wake-up reaches the scheduler.
    /// </summary>
    TimeSpan LateAfter { get; }

    /// <summary>
    /// Returns the id of a time job, cron job, or cron occurrence row the job managers handed to a notification, or
    /// <see langword="null"/> for any other object.
    /// </summary>
    Guid? RowIdOf(object row);

    /// <summary>Gets the functions this host registers, ordered by name.</summary>
    IReadOnlyList<JobsDashboardFunction> Functions { get; }

    Task<JobRunPage> ListRunsAsync(
        JobRunQuery query,
        JobRunCursor? after,
        int pageSize,
        CancellationToken cancellationToken
    );

    Task<JobRunCount> CountRunsAsync(JobRunQuery query, CancellationToken cancellationToken);

    /// <summary>Returns the run, a time job or a cron occurrence, or <see langword="null"/> when none has the id.</summary>
    Task<JobRunSummary?> GetRunAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Returns the ids of a time job's children; empty for a cron occurrence or a job with none.</summary>
    Task<IReadOnlyList<Guid>> GetChildIdsAsync(Guid timeJobId, CancellationToken cancellationToken);

    Task<IReadOnlyList<JobAttemptEntity>> GetAttemptsAsync(
        JobRunKind kind,
        Guid id,
        CancellationToken cancellationToken
    );

    /// <summary>Returns a run's stored request as JSON, or why it cannot be read; <see langword="null"/> when it has none.</summary>
    Task<JobsDashboardRequest?> GetRequestAsync(JobRunKind kind, Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<JobFailureGroup>> GetFailureGroupsAsync(
        DateTimeOffset recentSince,
        CancellationToken cancellationToken
    );

    Task<JobOverviewFigures> GetOverviewFiguresAsync(DateTime now, CancellationToken cancellationToken);

    Task<IReadOnlyList<JobFunctionStats>> GetFunctionStatsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken
    );

    Task<CronJobEntity?> GetCronJobAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CronJobEntity>> GetCronJobsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns each cron job's last run by cron job id, in one store read; a cron job that never ran has no entry.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, CronJobLastRun>> GetCronJobLastRunsAsync(CancellationToken cancellationToken);

    /// <summary>Previews a stored or draft schedule in the zone the scheduler would use for it.</summary>
    CronSchedulePreview Preview(string expression, string? timeZoneId, DateTime fromUtc, int count);

    /// <summary>
    /// Returns the live nodes and the functions each one's run filter claims. Without node membership this host is
    /// the only node.
    /// </summary>
    Task<IReadOnlyList<JobsDashboardNode>> GetNodesAsync(CancellationToken cancellationToken);

    Task<DateTime?> GetLatestChangeMarkerAsync(CancellationToken cancellationToken);

    Task<JobChangeCursor> GetChangeStartCursorAsync(CancellationToken cancellationToken);

    Task<JobChangePage> ReadChangesAsync(
        JobChangeCursor cursor,
        int pageSize,
        TimeSpan overlapWindow,
        CancellationToken cancellationToken
    );

    Task<JobRunCount> CountOperationScopeAsync(JobOperationScope scope, int cap, CancellationToken cancellationToken);

    Task<JobOperationChunk> ReadOperationScopeAsync(
        JobOperationScope scope,
        JobOperationCursor? after,
        int chunkSize,
        CancellationToken cancellationToken
    );

    /// <summary>Counts per status the runs <paramref name="query"/> matches in each bucket, by execution time.</summary>
    Task<IReadOnlyList<JobRunBucket>> CountRunsByBucketAsync(
        JobRunQuery query,
        IReadOnlyList<DateTime> boundaries,
        CancellationToken cancellationToken
    );

    /// <summary>Counts the running runs per owner tag, as <see cref="JobsDashboardNode.Owner"/> names a node.</summary>
    Task<IReadOnlyDictionary<string, JobRunCount>> CountRunningByOwnerAsync(CancellationToken cancellationToken);
}

/// <summary>A registered function as the dashboard shows it.</summary>
internal sealed record JobsDashboardFunction(
    string Name,
    string ContractVersion,
    JobPriority Priority,
    int MaxConcurrency,
    string? RequestType,
    string? CronExpression
);

/// <summary>A live node and the functions its run filter claims, or <see langword="null"/> when it published none.</summary>
/// <param name="Owner">The owner tag the node stamps on the rows it claims.</param>
/// <param name="IsLocal">Whether the node is this host, the one serving the request.</param>
/// <param name="MaxConcurrency">The node's host max concurrency, or <see langword="null"/> when it published none.</param>
/// <param name="LastHeartbeatAt">
/// The store-clock time of the node's latest accepted heartbeat, or <see langword="null"/> without node membership.
/// </param>
internal sealed record JobsDashboardNode(
    string Identity,
    string State,
    string? Role,
    IReadOnlyList<string>? ClaimedFunctions,
    string Owner,
    bool IsLocal,
    int? MaxConcurrency,
    DateTimeOffset? LastHeartbeatAt
);

/// <summary>A run's stored request: its JSON, and why the host cannot bind it, when it cannot.</summary>
internal sealed record JobsDashboardRequest(string Json, string? Unsupported);
