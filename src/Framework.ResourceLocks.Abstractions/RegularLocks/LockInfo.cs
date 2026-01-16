// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

/// <summary>Information about an active resource lock.</summary>
[PublicAPI]
public sealed record LockInfo
{
    /// <summary>The resource name that is locked.</summary>
    public required string Resource { get; init; }

    /// <summary>The unique identifier of the lock holder.</summary>
    public required string LockId { get; init; }

    /// <summary>Remaining time until the lock expires, or null if no expiration.</summary>
    public TimeSpan? TimeToLive { get; init; }
}
