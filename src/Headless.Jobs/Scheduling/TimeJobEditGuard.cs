// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// The rule every store applies to an ordinary time-job edit (<c>UpdateAsync</c>, <c>UpdateBatchAsync</c>, the
/// dashboard). An edit is a whole-row write from a copy the caller read earlier, so it may only land on a row no node
/// has started: otherwise the stale copy would overwrite what the executing node is writing.
/// </summary>
internal static class TimeJobEditGuard
{
    /// <summary>
    /// The columns the scheduler and the executing node own. An edit never writes them from the caller's copy; the
    /// store sets the release values itself.
    /// </summary>
    internal static readonly string[] RuntimeProperties =
    [
        nameof(TimeJobEntity.Status),
        nameof(TimeJobEntity.OwnerId),
        nameof(TimeJobEntity.LockedUntil),
        nameof(TimeJobEntity.RetryCount),
        nameof(TimeJobEntity.UpdatedAt),
        nameof(TimeJobEntity.CreatedAt),
        nameof(TimeJobEntity.ExecutedAt),
        nameof(TimeJobEntity.ElapsedTime),
        nameof(TimeJobEntity.ExceptionMessage),
        nameof(TimeJobEntity.SkippedReason),
        nameof(TimeJobEntity.ProgressPercent),
        nameof(TimeJobEntity.ProgressMessage),
        nameof(TimeJobEntity.ProgressUpdatedAt),
        nameof(TimeJobEntity.CancelRequested),
    ];

    /// <summary>
    /// Whether a row passed to the edit may be written. An idle unclaimed row is free. A queued row is accepted because
    /// the scheduler claims the next-due job up to a day early, and rescheduling that job must keep working; the edit
    /// releases the claim in the same write, so the claiming node drops it at admission. An idle row that carries an
    /// owner is a descendant inside a claimed tree, and anything running or finished belongs to its run.
    /// </summary>
    internal static bool IsEditable(JobStatus status, string? ownerId) =>
        (status == JobStatus.Idle && ownerId is null) || status == JobStatus.Queued;

    /// <summary>
    /// Whether an existing descendant carried in an edited root's graph may be written. Reading a pre-claimed root
    /// loads its children, which the claim stamped idle with the root's owner; the edit releases them with the root.
    /// A descendant owned by anyone else, or no longer idle, belongs to other work.
    /// </summary>
    internal static bool IsCarriedDescendantEditable(JobStatus status, string? ownerId, string? rootOwnerBeforeEdit) =>
        status == JobStatus.Idle
        && (ownerId is null || string.Equals(ownerId, rootOwnerBeforeEdit, StringComparison.Ordinal));

    internal static InvalidOperationException Refused() =>
        new(
            "The job cannot be edited: only a job that is idle and unclaimed, or queued and not yet started, can be "
                + "edited. The job, or a descendant in its graph, is running, finished, or part of a claimed chain; "
                + "a failed job goes back to the queue through IJobScheduler.RequeueAsync."
        );
}
