// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Fencing;

/// <summary>
/// The capped counts of the leases a query matches, by state, read in one statement on one database clock instant.
/// </summary>
/// <param name="Live">Active and unexpired leases.</param>
/// <param name="Expired">Active leases past their expiry.</param>
/// <param name="Ended">Settled, released, abandoned, and revoked leases.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct FencedLeaseCounts(LeaseCount Live, LeaseCount Expired, LeaseCount Ended)
{
    /// <summary>
    /// Gets the sum of the three counts; <see cref="LeaseCount.Capped" /> when any of them is, so the sum is then a
    /// lower bound.
    /// </summary>
    public LeaseCount Total =>
        new(Live.Count + Expired.Count + Ended.Count, Live.Capped || Expired.Capped || Ended.Capped);
}
