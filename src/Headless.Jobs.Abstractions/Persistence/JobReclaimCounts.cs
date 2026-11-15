// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Jobs;

/// <summary>
/// The rows one reclaim sweep moved, by the transition the row's <see cref="NodeDeathPolicy" /> chose for it.
/// </summary>
/// <remarks>
/// A reclaim ends a lease its owner no longer holds: the lease lapsed, or its node died or left the membership. The
/// counts are what the sweep wrote, so a row another node had already moved is not counted. The scheduler meters them
/// as <c>headless.jobs.reclaims</c>.
/// </remarks>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct JobReclaimCounts
{
    /// <summary>
    /// Rows handed back to <c>Idle</c> to be claimed again: rows that never started, and running rows under
    /// <see cref="NodeDeathPolicy.Retry" />.
    /// </summary>
    public int Released { get; init; }

    /// <summary>Running rows ended as <c>Failed</c> under <see cref="NodeDeathPolicy.MarkFailed" />.</summary>
    public int MarkedFailed { get; init; }

    /// <summary>Running rows ended as <c>Skipped</c> under <see cref="NodeDeathPolicy.Skip" />.</summary>
    public int Skipped { get; init; }

    /// <summary>Gets every row the sweep moved.</summary>
    public int Total => Released + MarkedFailed + Skipped;

    /// <summary>Adds two sweeps' counts, action by action.</summary>
    /// <param name="left">The first sweep's counts.</param>
    /// <param name="right">The second sweep's counts.</param>
    /// <returns>The counts of both sweeps.</returns>
    public static JobReclaimCounts operator +(JobReclaimCounts left, JobReclaimCounts right)
    {
        return new()
        {
            Released = left.Released + right.Released,
            MarkedFailed = left.MarkedFailed + right.MarkedFailed,
            Skipped = left.Skipped + right.Skipped,
        };
    }

    /// <summary>Adds two sweeps' counts, action by action.</summary>
    /// <param name="left">The first sweep's counts.</param>
    /// <param name="right">The second sweep's counts.</param>
    /// <returns>The counts of both sweeps.</returns>
    public static JobReclaimCounts Add(JobReclaimCounts left, JobReclaimCounts right) => left + right;
}
