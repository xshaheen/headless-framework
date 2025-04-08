// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

public sealed class ResourceThrottlingLock(string resource, TimeSpan timeWaitedForLock, DateTimeOffset dateAcquired)
    : IResourceThrottlingLock
{
    public string Resource { get; } = resource;

    public DateTimeOffset DateAcquired { get; } = dateAcquired;

    public TimeSpan TimeWaitedForLock { get; } = timeWaitedForLock;
}
