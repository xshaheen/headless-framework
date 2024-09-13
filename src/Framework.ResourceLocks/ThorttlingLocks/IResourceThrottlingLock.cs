// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

[PublicAPI]
public interface IResourceThrottlingLock
{
    /// <summary>A name that uniquely identifies the lock.</summary>
    string Resource { get; }

    /// <summary>The time the lock was acquired.</summary>
    DateTimeOffset DateAcquired { get; }

    /// <summary>The amount of time waited to acquire the lock.</summary>
    TimeSpan TimeWaitedForLock { get; }
}

public sealed class ResourceThrottlingLock(string resource, TimeSpan timeWaitedForLock, DateTimeOffset dateAcquired)
    : IResourceThrottlingLock
{
    public string Resource { get; } = resource;

    public DateTimeOffset DateAcquired { get; } = dateAcquired;

    public TimeSpan TimeWaitedForLock { get; } = timeWaitedForLock;
}
