// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// The attempt a lease row holds: its lease, its state by the database clock, and what its grant and last renewal
/// recorded.
/// </summary>
/// <remarks>
/// The snapshot never carries the progress bytes: only their size and contract tag, so an operator view can show that
/// an attempt recorded progress without the store shipping the payload. A takeover grant still returns the bytes
/// through <see cref="LeaseGrantResult.Progress" />. This record grows by <see langword="init" /> members.
/// </remarks>
[PublicAPI]
public sealed record LeaseSnapshot
{
    /// <summary>Gets the attempt on the row, with the generation its grant issued.</summary>
    public required FencedLease Lease { get; init; }

    /// <summary>
    /// Gets the row's own generation judged by the database clock: <see cref="LeaseFenceStatus.Current" /> while
    /// active and unexpired, <see cref="LeaseFenceStatus.Expired" /> once active past its expiry, or
    /// <see cref="LeaseFenceStatus.Settled" />, <see cref="LeaseFenceStatus.Released" />,
    /// <see cref="LeaseFenceStatus.Abandoned" />, or <see cref="LeaseFenceStatus.Revoked" /> once it ended. Never
    /// <see cref="LeaseFenceStatus.Stale" />.
    /// </summary>
    public required LeaseFenceStatus Status { get; init; }

    /// <summary>Gets the attempt's expiry, as the database clock set it.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Gets the owner tag the grant stored, or <see langword="null" /> when it stored none.</summary>
    public required string? Holder { get; init; }

    /// <summary>
    /// Gets how many times the lease's work was taken from an expired holder since it last settled, was released, or
    /// was revoked.
    /// </summary>
    public required int TakeoverCount { get; init; }

    /// <summary>Gets when the attempt's grant happened, by the database clock.</summary>
    public required DateTimeOffset GrantedAt { get; init; }

    /// <summary>
    /// Gets when the attempt settled, was released, was abandoned, or was revoked, by the database clock;
    /// <see langword="null" /> while it is active, live or expired.
    /// </summary>
    public required DateTimeOffset? EndedAt { get; init; }

    /// <summary>
    /// Gets the size in bytes of the last progress a renewal recorded, or <see langword="null" /> when none is stored.
    /// </summary>
    public required int? ProgressSizeBytes { get; init; }

    /// <summary>Gets the contract tag of the stored progress, or <see langword="null" /> when none is stored.</summary>
    public required string? ProgressContract { get; init; }

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
