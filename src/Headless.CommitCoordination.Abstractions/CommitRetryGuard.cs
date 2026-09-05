// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Records when a transaction participant starts work that the unit-of-work owner cannot replay safely.
/// Retrieve the shared instance through <see cref="ICommitCoordinator.GetOrAdd{TBuffer}" />.
/// </summary>
/// <remarks>
/// Mark before attempting a write whose effects are not retained by the owner's change tracker.
/// Once marked, a failed transaction requires a fresh unit of work. The marker never resets, even when
/// the participant fails or rolls back to a savepoint, because the write outcome may be uncertain.
/// </remarks>
[PublicAPI]
public sealed class CommitRetryGuard : ICommitWorkBuffer
{
    private int _retryPrevented;

    /// <summary>Whether the enclosing unit of work must not be retried using retained state.</summary>
    public bool IsRetryPrevented => Volatile.Read(ref _retryPrevented) != 0;

    /// <summary>Prevents replay of this unit of work after a transaction failure.</summary>
    public void PreventRetry() => Interlocked.Exchange(ref _retryPrevented, 1);
}
