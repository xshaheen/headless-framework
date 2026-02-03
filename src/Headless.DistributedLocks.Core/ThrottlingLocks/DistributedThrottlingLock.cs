// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public sealed class DistributedThrottlingLock(string resource, TimeSpan timeWaitedForLock, DateTimeOffset dateAcquired)
    : IDistributedThrottlingLock
{
    public string Resource { get; } = resource;

    public DateTimeOffset DateAcquired { get; } = dateAcquired;

    public TimeSpan TimeWaitedForLock { get; } = timeWaitedForLock;
}
