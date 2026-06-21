// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.AzureServiceBus.Producer;

/// <summary>
/// Describes a custom Azure Service Bus producer that publishes messages to a dedicated topic
/// rather than the shared topic configured in <see cref="AzureServiceBusOptions.TopicPath"/>.
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
    /// topic on startup (subject to <see cref="AzureServiceBusOptions.AutoProvision"/>).
    /// </summary>
    bool CreateSubscription { get; }

    /// <summary>
    /// When <see langword="true"/>, session-aware processing is enabled for this producer's topic.
    /// Every message published to this topic must include a <see cref="AzureServiceBusHeaders.SessionId"/> header.
    /// </summary>
    bool EnableSessions { get; }
}

/// <summary>Default implementation of <see cref="IServiceBusProducerDescriptor"/>.</summary>
public class ServiceBusProducerDescriptor : IServiceBusProducerDescriptor
{
    /// <summary>
    /// Initialises a new descriptor, deriving <see cref="MessageTypeName"/> from <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The CLR message type whose <c>Name</c> becomes <see cref="MessageTypeName"/>.</param>
    /// <param name="topicPath">The Service Bus topic path.</param>
    /// <param name="createSubscription">Whether to auto-create a subscription on startup.</param>
    /// <param name="enableSessions">Whether session-aware processing is required.</param>
    public ServiceBusProducerDescriptor(
        Type type,
        string topicPath,
        bool createSubscription = true,
        bool enableSessions = false
    )
    {
        MessageTypeName = type.Name;
        TopicPath = topicPath;
        CreateSubscription = createSubscription;
        EnableSessions = enableSessions;
    }

    /// <summary>
    /// Initialises a new descriptor with an explicit message type name.
    /// </summary>
    /// <param name="typeName">The message type name used for routing and subscription creation.</param>
    /// <param name="topicPath">The Service Bus topic path.</param>
    /// <param name="createSubscription">Whether to auto-create a subscription on startup.</param>
    /// <param name="enableSessions">Whether session-aware processing is required.</param>
    public ServiceBusProducerDescriptor(
        string typeName,
        string topicPath,
        bool createSubscription = true,
        bool enableSessions = false
    )
    {
        MessageTypeName = typeName;
        TopicPath = topicPath;
        CreateSubscription = createSubscription;
        EnableSessions = enableSessions;
    }

    /// <inheritdoc/>
    public string TopicPath { get; set; }

    /// <inheritdoc/>
    public string MessageTypeName { get; }

    /// <inheritdoc/>
    public bool CreateSubscription { get; internal set; }

    /// <inheritdoc/>
    public bool EnableSessions { get; internal set; }
}

/// <summary>
/// Strongly-typed convenience descriptor that derives <see cref="ServiceBusProducerDescriptor.MessageTypeName"/>
/// from <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The message type published by this producer.</typeparam>
public class ServiceBusProducerDescriptor<T>(
    string topicPath,
    bool createSubscription = true,
    bool enableSessions = false
) : ServiceBusProducerDescriptor(typeof(T), topicPath, createSubscription, enableSessions);
