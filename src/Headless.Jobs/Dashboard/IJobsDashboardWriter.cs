// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// The Jobs writes a dashboard module makes: creates, edits, deletes, cancels, requeues, pauses, and resumes. Each one
/// goes through the host's managers or store with the same rules the application's own calls follow.
/// </summary>
/// <remarks>
/// <c>AddHeadlessJobs</c> registers one backend per host as both this and <see cref="IJobsDashboardReader"/>, so a
/// component that only reads never holds a way to change a job.
/// </remarks>
internal interface IJobsDashboardWriter
{
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

    /// <summary>
    /// Edits an idle or queued time job through the time job manager; a field the edit leaves unset keeps its stored
    /// value.
    /// </summary>
    /// <returns>Why the edit did not apply, or <see langword="null"/> when it did.</returns>
    Task<string?> EditTimeJobAsync(Guid id, JobsDashboardTimeJobEdit edit, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new standalone time job carrying a stored time job's function, request, retries, intervals,
    /// description, and tenant, with no business key, due at <paramref name="executionTime"/> or now.
    /// </summary>
    /// <returns>The new job's id, or <see langword="null"/> when the source no longer exists.</returns>
    Task<Guid?> DuplicateTimeJobAsync(Guid id, DateTime? executionTime, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a running time job whose cancel was requested, only while it is still running with the request
    /// standing. The running code is not stopped.
    /// </summary>
    Task<bool> ForceDeleteTimeJobAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Deletes a cron job and its stored occurrences, only while none of them is running.</summary>
    /// <returns>
    /// The occurrences removed with it, or <see langword="null"/> when it is gone or an occurrence is running.
    /// </returns>
    Task<int?> DeleteCronJobAsync(Guid id, CancellationToken cancellationToken);

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

    public int Retries { get; init; }

    public int[]? RetryIntervals { get; init; }
}

/// <summary>The fields of a time job an operator changes; each one left unset keeps its stored value.</summary>
internal sealed record JobsDashboardTimeJobEdit
{
    /// <summary>The new due time (UTC).</summary>
    public DateTime? ExecutionTime { get; init; }

    /// <summary>Whether <see cref="RequestJson"/> replaces the stored request, so an empty one clears it.</summary>
    public bool ReplacesRequest { get; init; }

    public string? RequestJson { get; init; }

    public int? Retries { get; init; }

    public int[]? RetryIntervals { get; init; }
}
