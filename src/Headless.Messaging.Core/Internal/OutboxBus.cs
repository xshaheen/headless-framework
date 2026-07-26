// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

internal sealed class OutboxBus : IOutboxBus
{
    private static readonly IMessageCapabilityGate _DirectConstructionCapabilities = MessagingCapabilityModel.Compose([
        MessagingProviderCapabilities.Transport("Direct", [MessageLane.Bus], supportsIndependentLaneTopology: true),
        MessagingProviderCapabilities.Storage("Direct", [MessageLane.Bus], supportsDelayedScheduling: true),
    ]);

    private readonly Func<OutboxMessageWriter> _publisherResolver;
    private readonly IMessageCapabilityGate _capabilities;

    internal OutboxBus(IServiceProvider serviceProvider, IMessageCapabilityGate capabilities)
        : this(() => serviceProvider.GetRequiredService<OutboxMessageWriter>(), capabilities) { }

    internal OutboxBus(OutboxMessageWriter publisher)
        : this(() => publisher, _DirectConstructionCapabilities) { }

    private OutboxBus(Func<OutboxMessageWriter> publisherResolver, IMessageCapabilityGate capabilities)
    {
        _publisherResolver = publisherResolver;
        _capabilities = capabilities;
    }

    public Task PublishAsync<T>(
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        _capabilities.EnsureOutboxSupported(MessageLane.Bus, scheduled: options?.Delay is not null);
        var publisher = _publisherResolver();
        return options?.Delay is { } delay
            ? publisher.PublishDelayAsync(delay, contentObj, options, IntentType.Bus, cancellationToken)
            : publisher.PublishAsync(contentObj, options, IntentType.Bus, cancellationToken);
    }
}
