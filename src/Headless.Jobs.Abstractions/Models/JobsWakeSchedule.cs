// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Models;

/// <summary>
/// The scheduler's next wake, expressed entirely in the STORE's clock domain: the instant the decision was anchored on
/// and the instant to wake at. The sleep duration is their difference.
/// </summary>
/// <remarks>
/// <b>This type is the single clock-domain declaration for the wake/restart path.</b> Every due instant that crosses
/// <c>IInternalJobManager.GetNextJobs</c>, <c>JobsExecutionContext.SetNextPlannedOccurrence</c>, and
/// <c>IJobsHostScheduler.RestartIfNeeded</c> is a STORE instant, because due-ness in this subsystem is decided by the
/// store: time-job claims filter on the database clock, and a cron projection is authorized by comparing
/// <c>NextDueUtc</c> against the store's instant inside the advance. The calling node's clock has exactly one
/// legitimate role — measuring how long to sleep — and it enters at exactly one place, the scheduler loop's
/// node/store offset.
/// <para>
/// Mixing the two domains is a live defect, not a style point: folding a store-derived duration into a node-domain
/// deadline makes a skewed node mis-arbitrate restarts. With store time 12:00 and node time 11:00, a 12:30 wake
/// recorded as 11:30 makes a newly enqueued 12:05 job look later than the planned wake, so the sleep is not
/// interrupted and the job runs late or falls into misfire recovery.
/// </para>
/// </remarks>
/// <param name="StoreUtcNow">
/// The store instant this decision was anchored on. <see langword="null"/> only when no store read was made at all, in
/// which case the scheduler keeps its previous offset rather than assuming the clocks agree.
/// </param>
/// <param name="WakeAtStoreUtc">The store instant to wake at, or <see langword="null"/> when nothing is scheduled.</param>
internal readonly record struct JobsWakeSchedule(DateTime? StoreUtcNow, DateTime? WakeAtStoreUtc)
{
    /// <summary>Nothing pending and no store instant observed.</summary>
    public static readonly JobsWakeSchedule Idle = new(StoreUtcNow: null, WakeAtStoreUtc: null);

    /// <summary>
    /// How long to sleep: the store-domain distance from <see cref="StoreUtcNow"/> to <see cref="WakeAtStoreUtc"/>,
    /// clamped at zero, or <see cref="Timeout.InfiniteTimeSpan"/> when nothing is scheduled. A duration is
    /// domain-free — it is the one value that may be handed to a node-clock timer.
    /// </summary>
    public TimeSpan Remaining
    {
        get
        {
            if (WakeAtStoreUtc is not { } wakeAt)
            {
                return Timeout.InfiniteTimeSpan;
            }

            // An anchorless wake instant cannot be measured against anything, so treat it as immediately due rather
            // than silently measuring it against this node's clock.
            var anchor = StoreUtcNow ?? wakeAt;

            return wakeAt > anchor ? wakeAt - anchor : TimeSpan.Zero;
        }
    }
}
