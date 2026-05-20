// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.RateLimiting;

[PublicAPI]
public interface IDistributedRateLimiterLease
{
    /// <summary>A name that uniquely identifies the rate-limited resource.</summary>
    string Resource { get; }

    /// <summary>The time the lease was acquired.</summary>
    DateTimeOffset DateAcquired { get; }

    /// <summary>The amount of time waited to acquire the lease.</summary>
    TimeSpan TimeWaitedForLease { get; }
}
