// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.RateLimiting;

public sealed class DistributedRateLimiterLease(
    string resource,
    TimeSpan timeWaitedForLease,
    DateTimeOffset dateAcquired
) : IDistributedRateLimiterLease
{
    public string Resource { get; } = resource;

    public DateTimeOffset DateAcquired { get; } = dateAcquired;

    public TimeSpan TimeWaitedForLease { get; } = timeWaitedForLease;
}
