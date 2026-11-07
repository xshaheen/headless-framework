// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// The attempt a lease row holds, read by its key: its lease, its state by the database clock, and what its grant
/// recorded.
/// </summary>
/// <param name="Lease">The attempt on the row, with the generation its grant issued.</param>
/// <param name="Status">
/// The row's own generation judged by the database clock: <see cref="LeaseFenceStatus.Current" /> while active and
/// unexpired, <see cref="LeaseFenceStatus.Expired" /> once active past its expiry, or
/// <see cref="LeaseFenceStatus.Settled" />, <see cref="LeaseFenceStatus.Released" />, or
/// <see cref="LeaseFenceStatus.Abandoned" /> once it ended. Never <see cref="LeaseFenceStatus.Stale" />.
/// </param>
/// <param name="ExpiresAt">The attempt's expiry, as the database clock set it.</param>
/// <param name="Holder">The owner tag the grant stored, or <see langword="null" /> when it stored none.</param>
/// <param name="Attributes">The attributes the grant stored, or <see langword="null" /> when it stored none.</param>
/// <param name="TakeoverCount">
/// How many times the lease's work was taken from an expired holder since it last settled or was released.
/// </param>
[PublicAPI]
public sealed record LeaseSnapshot(
    FencedLease Lease,
    LeaseFenceStatus Status,
    DateTimeOffset ExpiresAt,
    string? Holder,
    LeaseAttributes? Attributes,
    int TakeoverCount
)
{
    /// <summary>Gets whether the attempt is active and unexpired by the database clock.</summary>
    public bool IsLive => Status == LeaseFenceStatus.Current;
}
