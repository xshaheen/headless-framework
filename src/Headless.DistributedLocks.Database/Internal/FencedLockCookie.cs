// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DistributedLocks;

/// <summary>
/// Lock cookie a synchronization strategy returns when the statement that granted the lock also drew its fencing token
/// on the same session. Drawing both in one statement means a session that died before the grant returned never holds a
/// token, so no stale holder can carry a token above the next real holder's.
/// </summary>
/// <param name="fencingToken">The token drawn with the grant.</param>
internal sealed class FencedLockCookie(LockFencingToken fencingToken)
{
    public LockFencingToken FencingToken { get; } = fencingToken;

    /// <summary>Returns the token a strategy cookie carries, or <see langword="null"/> when the grant drew none.</summary>
    public static LockFencingToken? FencingTokenOf(object? lockCookie)
    {
        return (lockCookie as FencedLockCookie)?.FencingToken;
    }
}
