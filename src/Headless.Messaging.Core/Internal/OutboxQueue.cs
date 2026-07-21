// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

internal sealed class OutboxQueue : IOutboxQueue
{
    private static readonly IMessageCapabilityGate _DirectConstructionCapabilities = MessagingCapabilityModel.Compose([
        MessagingProviderCapabilities.Transport("Direct", [MessageLane.Queue], supportsIndependentLaneTopology: true),
        MessagingProviderCapabilities.Storage("Direct", [MessageLane.Queue], supportsDelayedScheduling: true),
    ]);

    private readonly Func<OutboxMessageWriter> _publisherResolver;
    private readonly IMessageCapabilityGate _capabilities;

    internal OutboxQueue(IServiceProvider serviceProvider, IMessageCapabilityGate capabilities)
        : this(() => serviceProvider.GetRequiredService<OutboxMessageWriter>(), capabilities) { }

    internal OutboxQueue(OutboxMessageWriter publisher)
        : this(() => publisher, _DirectConstructionCapabilities) { }

    private OutboxQueue(Func<OutboxMessageWriter> publisherResolver, IMessageCapabilityGate capabilities)
    {
        _publisherResolver = publisherResolver;
        _capabilities = capabilities;
    }

    public Task EnqueueAsync<T>(
        T? contentObj,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        _capabilities.EnsureOutboxSupported(MessageLane.Queue, scheduled: options?.Delay is not null);
        var publisher = _publisherResolver();
        return options?.Delay is { } delay
            ? publisher.PublishDelayAsync(delay, contentObj, options, IntentType.Queue, cancellationToken)
            : publisher.PublishAsync(contentObj, options, IntentType.Queue, cancellationToken);
    }
}
