// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Headless.DistributedLocks;

/// <summary>
/// One wait budget shared by every resource of a transaction-scoped lock set: each resource may wait only for what
/// the earlier ones left, so a set of N never waits N times the caller's timeout.
/// </summary>
/// <remarks>
/// The budget bounds a database wait, so it runs on the monotonic system clock rather than the registered
/// <see cref="TimeProvider" />, which tests may freeze.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct TransactionLockBudget
{
    private readonly TimeSpan _total;
    private readonly long _startedAt;

    private TransactionLockBudget(TimeSpan total, long startedAt)
    {
        _total = total;
        _startedAt = startedAt;
    }

    /// <summary>Starts a budget of <paramref name="total" />.</summary>
    /// <param name="total">
    /// The caller's wait. <see cref="TimeSpan.Zero" /> (one attempt per resource) and
    /// <see cref="Timeout.InfiniteTimeSpan" /> pass through to every resource unchanged.
    /// </param>
    public static TransactionLockBudget Start(TimeSpan total)
    {
        return new(total, Stopwatch.GetTimestamp());
    }

    /// <summary>Gets the wait the next resource may use.</summary>
    /// <param name="wait">The remaining wait, or the caller's sentinel for a one-attempt or unbounded budget.</param>
    /// <returns><see langword="false" /> when a bounded budget has run out before the next resource.</returns>
    public bool TryGetRemaining(out TimeSpan wait)
    {
        if (_total == TimeSpan.Zero || _total == Timeout.InfiniteTimeSpan)
        {
            wait = _total;

            return true;
        }

        wait = _total - Stopwatch.GetElapsedTime(_startedAt);

        return wait > TimeSpan.Zero;
    }
}
