// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Best-effort message sent directly when a distributed lock or semaphore slot is released.
/// Consumed by <see cref="DistributedLock.LockReleasedConsumer"/> to wake blocked acquirers
/// immediately instead of relying on the exponential-backoff polling loop.
/// </summary>
/// <param name="Resource">The resource name for which the lock/slot was released.</param>
/// <param name="LeaseId">The lease identifier of the just-released holder.</param>
[PublicAPI]
public sealed record DistributedLockReleased(string Resource, string LeaseId)
{
    /// <summary>
    /// The wire name this message is published and consumed under, at contract version <c>1</c>. The lock providers
    /// declare it, so the topic stays the same whatever naming conventions the host configures for its own messages.
    /// </summary>
    public const string MessageName = "headless.locks.released";
}
