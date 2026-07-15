// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Headless.Caching.Benchmarks.Infrastructure;

internal sealed class NoOpBus : IBus
{
    public Task PublishAsync<T>(
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.CompletedTask;
    }
}
