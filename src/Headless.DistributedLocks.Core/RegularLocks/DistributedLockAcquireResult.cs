// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// The result returned by a single storage acquire attempt, carrying both the outcome flag and
/// the optional fencing token issued by the backend.
/// </summary>
/// <param name="Acquired">
/// <see langword="true"/> when the caller was granted ownership of the lock/slot; <see langword="false"/>
/// when the resource was already held, the caller's capacity cap was reached, or a transient storage
/// error prevented the acquire.
/// </param>
/// <param name="FencingToken">
/// A monotonically-increasing integer token assigned by the backend when <paramref name="Acquired"/> is
/// <see langword="true"/>, used to detect stale writes from prior holders. <see langword="null"/> when
/// the backend does not support fencing tokens or acquisition failed.
/// </param>
[PublicAPI]
public readonly record struct DistributedLockAcquireResult(bool Acquired, long? FencingToken)
{
    /// <summary>Canonical not-acquired sentinel. Safe to compare by value; both fields are value types.</summary>
    public static DistributedLockAcquireResult Failed => new(Acquired: false, FencingToken: null);
}
