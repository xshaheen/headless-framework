// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Information about an active resource lock.</summary>
[PublicAPI]
public sealed record DistributedLockInfo
{
    /// <summary>The resource name that is locked.</summary>
    public required string Resource { get; init; }

    /// <summary>The unique identifier of the lock holder.</summary>
    public required string LeaseId { get; init; }

    /// <summary>
    /// A per-resource monotonic grant counter used by protected resources to reject stale writes,
    /// or null when the backend or inspection path cannot report a fencing token.
    /// </summary>
    /// <remarks>
    /// The inspection path (<c>GetLockInfoAsync</c> / <c>ListActiveLocksAsync</c>) reports null for
    /// backends whose fence is stored separately (for example Redis); the live token is delivered on
    /// the acquire handle's <see cref="IDistributedLease.FencingToken"/>.
    /// </remarks>
    public long? FencingToken { get; init; }

    /// <summary>Remaining time until the lock expires, or null if no expiration.</summary>
    public TimeSpan? TimeToLive { get; init; }
}
