// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>Which leases an <see cref="IFencedLeaseInspector" /> list or count reads, by their state.</summary>
/// <remarks>
/// <see cref="Live" />, <see cref="Expired" />, and <see cref="Ended" /> partition the leases: every lease is in
/// exactly one of them, judged by the database clock when the statement runs.
/// </remarks>
[PublicAPI]
public enum FencedLeaseStateFilter
{
    /// <summary>Every lease, whatever its state.</summary>
    All = 0,

    /// <summary>Active leases the database clock still sees as unexpired: their holder may write.</summary>
    Live = 1,

    /// <summary>
    /// Active leases past their expiry by the database clock: no holder may write, and a takeover grant or a sweep
    /// ends them.
    /// </summary>
    Expired = 2,

    /// <summary>Leases whose last attempt settled, was released, was abandoned, or was revoked.</summary>
    Ended = 3,
}
