// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;

namespace Headless.Messaging.InMemory;

/// <summary>
/// Factory for creating in-memory consumer clients.
/// </summary>
internal sealed class InMemoryConsumerClientFactory(MemoryQueue queue) : IConsumerClientFactory
{
    /// <summary>Creates a new consumer client for the requested subscription.</summary>
    /// <remarks>
    /// An every-instance client subscribes under its own group, named after the subscription and the requesting
    /// host's instance id, so several hosts that share one <see cref="MemoryQueue"/> each receive every Bus message. The
    /// group disappears with its last client, as a broker removes a per-process subscription.
    /// </remarks>
    public Task<IConsumerClient> CreateAsync(
        ConsumerClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var client = new InMemoryConsumerClient(queue, GroupId(request), request.Concurrency, request.Lane);
        return Task.FromResult<IConsumerClient>(client);
    }

    /// <summary>The in-memory group a client of <paramref name="request"/> joins.</summary>
    internal static string GroupId(ConsumerClientRequest request) =>
        request.Kind is ConsumerSubscriptionKind.EveryInstance
            ? $"{request.SubscriptionName}.{request.InstanceId:N}"
            : request.SubscriptionName;
}
