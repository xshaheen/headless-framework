// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// Thrown when a reclaim sweep failed in part: one store, one owner, or the follow-up reconcile threw. The rows the
/// sweep moved before the failure stay moved, and <see cref="Reclaimed" /> counts them, so a caller can still report
/// them; a retry finds them already moved and will not count them again.
/// </summary>
[PublicAPI]
public sealed class JobReclaimException : AggregateException
{
    /// <summary>Creates the exception for a sweep that moved <paramref name="reclaimed" /> before it failed.</summary>
    /// <param name="reclaimed">The rows the sweep moved, by transition.</param>
    /// <param name="failures">What failed; at least one.</param>
    public JobReclaimException(JobReclaimCounts reclaimed, IEnumerable<Exception> failures)
        : base("One or more reclaim steps failed; the rows already moved stay moved.", failures)
    {
        Reclaimed = reclaimed;
    }

    /// <summary>Gets the rows the sweep moved before or despite the failure, by transition.</summary>
    public JobReclaimCounts Reclaimed { get; }
}
