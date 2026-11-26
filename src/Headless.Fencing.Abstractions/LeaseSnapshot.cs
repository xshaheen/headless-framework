// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// The attempt a lease row holds, read by its key or listed by <see cref="IFencedLeaseInspector" />: its lease, its
/// state by the database clock, and what its grant and last renewal recorded.
/// </summary>
/// <remarks>
/// The snapshot never carries the progress bytes: only their size and contract tag, so an operator view can show that
/// an attempt recorded progress without the store shipping the payload. A takeover grant still returns the bytes
/// through <see cref="LeaseGrantResult.Progress" />.
/// </remarks>
/// <param name="Lease">The attempt on the row, with the generation its grant issued.</param>
/// <param name="Status">
/// The row's own generation judged by the database clock: <see cref="LeaseFenceStatus.Current" /> while active and
/// unexpired, <see cref="LeaseFenceStatus.Expired" /> once active past its expiry, or
/// <see cref="LeaseFenceStatus.Settled" />, <see cref="LeaseFenceStatus.Released" />,
/// <see cref="LeaseFenceStatus.Abandoned" />, or <see cref="LeaseFenceStatus.Revoked" /> once it ended. Never
/// <see cref="LeaseFenceStatus.Stale" />.
/// </param>
/// <param name="ExpiresAt">The attempt's expiry, as the database clock set it.</param>
/// <param name="Holder">The owner tag the grant stored, or <see langword="null" /> when it stored none.</param>
/// <param name="TakeoverCount">
/// How many times the lease's work was taken from an expired holder since it last settled, was released, or was
/// revoked.
/// </param>
/// <param name="GrantedAt">When the attempt's grant happened, by the database clock.</param>
/// <param name="EndedAt">
/// When the attempt settled, was released, was abandoned, or was revoked, by the database clock; <see langword="null" />
/// while it is active, live or expired.
/// </param>
/// <param name="ProgressSizeBytes">
/// The size in bytes of the last progress a renewal recorded, or <see langword="null" /> when none is stored.
/// </param>
/// <param name="ProgressContract">
/// The contract tag of the stored progress, or <see langword="null" /> when none is stored.
/// </param>
[PublicAPI]
public sealed record LeaseSnapshot(
    FencedLease Lease,
    LeaseFenceStatus Status,
    DateTimeOffset ExpiresAt,
    string? Holder,
    int TakeoverCount,
    DateTimeOffset GrantedAt,
    DateTimeOffset? EndedAt,
    int? ProgressSizeBytes,
    string? ProgressContract
)
{
    /// <summary>Gets whether the attempt is active and unexpired by the database clock.</summary>
    public bool IsLive => Status == LeaseFenceStatus.Current;

    /// <summary>
    /// Gets whether the attempt is still active but past its expiry by the database clock: a takeover grant or a sweep
    /// may end it.
    /// </summary>
    public bool IsExpired => Status == LeaseFenceStatus.Expired;

    /// <summary>Gets whether the attempt ended: settled, released, abandoned, or revoked.</summary>
    public bool IsEnded => Status is not (LeaseFenceStatus.Current or LeaseFenceStatus.Expired);
}
