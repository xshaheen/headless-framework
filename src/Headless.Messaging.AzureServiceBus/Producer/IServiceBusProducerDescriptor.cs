// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.AzureServiceBus.Producer;

/// <summary>
/// Describes a custom Azure Service Bus producer that publishes messages to a dedicated topic
/// rather than the shared topic configured in <see cref="AzureServiceBusMessagingOptions.TopicPath"/>.
/// </summary>
public interface IServiceBusProducerDescriptor
{
    /// <summary>The Service Bus topic path targeted by this producer.</summary>
    string TopicPath { get; }

    /// <summary>
    /// The message type name used to identify this producer's message type when routing or
    /// creating subscriptions.
    /// </summary>
    string MessageTypeName { get; }

    /// <summary>
    /// When <see langword="true"/>, the framework auto-creates a subscription for this producer's
    /// topic on startup (subject to <see cref="AzureServiceBusMessagingOptions.AutoProvision"/>).
    /// </summary>
    bool CreateSubscription { get; }

    /// <summary>
    /// When <see langword="true"/>, session-aware processing is enabled for this producer's topic.
    /// Every message published to this topic must include a <see cref="AzureServiceBusMessagingHeaders.SessionId"/> header.
    /// </summary>
    bool EnableSessions { get; }
}
