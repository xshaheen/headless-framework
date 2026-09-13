// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Headless.Caching.Benchmarks.Infrastructure;

internal sealed class NoOpBus : IBus
{
    public Task<PublishReceipt> PublishAsync<T>(T? contentObj, CancellationToken cancellationToken = default) =>
        PublishAsync(contentObj, options: null, cancellationToken);

    public Task<PublishReceipt> PublishAsync<T>(
        T? contentObj,
        PublishOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(default(PublishReceipt));
    }
}
