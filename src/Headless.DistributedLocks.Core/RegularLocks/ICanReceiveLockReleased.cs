// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Wake-up signal contract for distributed-lock waiters. Implemented by
/// <see cref="DistributedLockProvider"/> and consumed by <c>LockReleasedConsumer</c> so a
/// decorator on <see cref="IDistributedLockProvider"/> cannot accidentally break the
/// release-signal path.
/// </summary>
internal interface ICanReceiveLockReleased
{
    void OnLockReleased(DistributedLockReleased message);
}
