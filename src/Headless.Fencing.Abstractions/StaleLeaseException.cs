// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Fencing;

/// <summary>
/// Thrown by a fence when the caller's attempt no longer owns the lease, so its writes must not commit. Let it roll
/// the unit of work back.
/// </summary>
/// <remarks>Creates the exception for a lease the fence refused.</remarks>
/// <param name="lease">The refused lease.</param>
/// <param name="reason">What the fence found; never <see cref="LeaseFenceStatus.Current" />.</param>
/// <exception cref="ArgumentNullException"><paramref name="lease" /> is <see langword="null" />.</exception>
[PublicAPI]
public sealed class StaleLeaseException(FencedLease lease, LeaseFenceStatus reason)
    : InvalidOperationException(_Message(Argument.IsNotNull(lease), reason))
{
    /// <summary>Gets the refused lease.</summary>
    public FencedLease Lease { get; } = lease;

    /// <summary>Gets what the fence found.</summary>
    public LeaseFenceStatus Reason { get; } = reason;

    private static string _Message(FencedLease lease, LeaseFenceStatus reason)
    {
        return $"Lease '{lease.Kind}/{lease.Resource}' generation {lease.Generation.ToString(CultureInfo.InvariantCulture)} "
            + $"no longer owns its writes ({reason}); roll the unit of work back.";
    }
}
