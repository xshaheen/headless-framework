// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// Thrown when a reclaim sweep failed in part: one store, one owner, or the follow-up reconcile threw. The rows the
/// sweep moved before the failure stay moved, and <see cref="Reclaimed" /> counts them, so a caller can still report
/// them; a retry finds them already moved and will not count them again.
/// </summary>
/// <param name="reclaimed">The rows the sweep moved, by transition.</param>
/// <param name="failures">What failed; at least one.</param>
[PublicAPI]
public sealed class JobReclaimException(JobReclaimCounts reclaimed, IEnumerable<Exception> failures)
    : AggregateException("One or more reclaim steps failed; the rows already moved stay moved.", failures)
{
    /// <summary>Gets the rows the sweep moved before or despite the failure, by transition.</summary>
    public JobReclaimCounts Reclaimed { get; } = reclaimed;
}
