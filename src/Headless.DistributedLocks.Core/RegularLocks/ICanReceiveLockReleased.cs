// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Wake-up signal contract for distributed-lock waiters. Implemented by
/// <see cref="DistributedLockProvider"/> and consumed by <c>LockReleasedConsumer</c> so a
/// decorator on <see cref="IDistributedLockProvider"/> cannot accidentally break the
/// release-signal path.
/// </summary>
internal interface ICanReceiveLockReleased
{
    /// <summary>
    /// Signals waiters that the lock for <see cref="DistributedLockReleased.Resource"/> has
    /// been released. Intentionally synchronous: the implementation pulses an in-process
    /// <see cref="AutoResetEvent"/>, which is non-blocking and must not perform I/O.
    /// Implementors that need async work should queue it to a background pump rather than
    /// changing this signature — the consumer pipeline depends on the wake-up returning
    /// promptly without awaiting downstream operations.
    /// </summary>
    void OnLockReleased(DistributedLockReleased message);
}
