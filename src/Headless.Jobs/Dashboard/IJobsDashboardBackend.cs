// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// The Jobs reads and writes a dashboard module needs, closed over the host's entity types, so a module registered
/// without type arguments reaches the store and managers <c>AddHeadlessJobs&lt;TTimeJob, TCronJob&gt;</c> configured.
/// </summary>
/// <remarks>
/// <c>AddHeadlessJobs</c> registers one instance per host. Every member delegates to the host's persistence provider,
/// managers, scheduler, function registry, or node membership; none keeps state of its own.
/// </remarks>
internal interface IJobsDashboardBackend
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

    /// <summary>
    /// Requeues a run, only while its change marker equals <paramref name="expectedChangedAt"/> when one is given, and
    /// wakes this node's scheduler when the row moved.
    /// </summary>
    Task<JobRequeueOutcome> RequeueAsync(
        JobRunKind kind,
        Guid id,
        DateTime? expectedChangedAt,
        CancellationToken cancellationToken
    );

    /// <summary>Requests cancellation of a time job; <see langword="false"/> when it is finished or already requested.</summary>
    Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Deletes a run that is not running, only while its change marker equals <paramref name="expectedChangedAt"/>.</summary>
    Task<bool> DeleteAsync(JobRunKind kind, Guid id, DateTime expectedChangedAt, CancellationToken cancellationToken);

    Task<bool> PauseCronJobAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ResumeCronJobAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Adds an occurrence of the cron job due now and dispatches it on this node when it can claim it.</summary>
    /// <returns>The new occurrence's id.</returns>
    Task<Guid> RunCronJobNowAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Changes a cron job's expression and zone; the error message when the manager refused the edit.</summary>
    Task<string?> UpdateCronScheduleAsync(
        Guid id,
        string expression,
        string? timeZoneId,
        CancellationToken cancellationToken
    );

    /// <summary>Adds a time job, or a chain when the draft has children.</summary>
    /// <returns>The root's id.</returns>
    /// <exception cref="ArgumentException">The draft names no registered function, or its request does not bind.</exception>
    Task<Guid> AddTimeJobAsync(JobsDashboardTimeJobDraft draft, CancellationToken cancellationToken);

    /// <summary>Adds a cron job.</summary>
    /// <returns>The cron job's id.</returns>
    /// <exception cref="ArgumentException">The draft names no registered function, or its request does not bind.</exception>
    Task<Guid> AddCronJobAsync(JobsDashboardCronJobDraft draft, CancellationToken cancellationToken);
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
internal sealed record JobsDashboardNode(
    string Identity,
    string State,
    string? Role,
    IReadOnlyList<string>? ClaimedFunctions
);

/// <summary>A run's stored request: its JSON, and why the host cannot bind it, when it cannot.</summary>
internal sealed record JobsDashboardRequest(string Json, string? Unsupported);

/// <summary>A time job, or the root of a chain, an operator is creating.</summary>
internal sealed record JobsDashboardTimeJobDraft
{
    public required string Function { get; init; }

    /// <summary>The due time (UTC), or <see langword="null"/> to run now, or after its parent for a chain step.</summary>
    public DateTime? ExecutionTime { get; init; }

    public string? RequestJson { get; init; }

    public string? Description { get; init; }

    public int Retries { get; init; }

    public int[]? RetryIntervals { get; init; }

    /// <summary>When a chain step runs after its parent; ignored on the root.</summary>
    public RunCondition? RunCondition { get; init; }

    public IReadOnlyList<JobsDashboardTimeJobDraft> Children { get; init; } = [];
}

/// <summary>A cron job an operator is creating.</summary>
internal sealed record JobsDashboardCronJobDraft
{
    public required string Function { get; init; }

    public required string Expression { get; init; }

    public string? TimeZoneId { get; init; }

    public string? RequestJson { get; init; }

    public string? Description { get; init; }
}
