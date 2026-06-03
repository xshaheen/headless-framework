// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public interface IReleaseSignal
{
    ValueTask WaitAsync(string resource, TimeSpan pollingFallback, CancellationToken cancellationToken = default);

    ValueTask PublishAsync(string resource, CancellationToken cancellationToken = default);
}
