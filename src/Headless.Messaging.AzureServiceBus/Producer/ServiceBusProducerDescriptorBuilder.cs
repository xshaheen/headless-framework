// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.AzureServiceBus.Producer;

/// <summary>
/// Fluent builder for a custom Azure Service Bus producer descriptor, used with
/// <see cref="AzureServiceBusMessagingOptions.ConfigureCustomProducer{T}"/>.
/// </summary>
/// <typeparam name="T">The message type this producer will publish.</typeparam>
public sealed class ServiceBusProducerDescriptorBuilder<T>
{
    private string TopicPath { get; set; } = null!;
    private bool CreateSubscription { get; set; }
    private bool EnableSessions { get; set; }

    /// <summary>
    /// Sets the Service Bus topic path that messages of type <typeparamref name="T"/> are published to.
    /// </summary>
    /// <param name="topicPath">The topic path relative to the Service Bus namespace.</param>
    /// <returns>The same builder for chaining.</returns>
    public ServiceBusProducerDescriptorBuilder<T> UseTopic(string topicPath)
    {
        TopicPath = topicPath;
        return this;
    }

    /// <summary>
    /// Instructs the framework to auto-create a subscription for this topic on startup
    /// (when <see cref="AzureServiceBusMessagingOptions.AutoProvision"/> is enabled).
    /// </summary>
    /// <returns>The same builder for chaining.</returns>
    public ServiceBusProducerDescriptorBuilder<T> WithSubscription()
    {
        CreateSubscription = true;
        return this;
    }

    /// <summary>
    /// Enables session-aware processing for this producer's topic. Every message published to
    /// this topic must include a <see cref="AzureServiceBusHeaders.SessionId"/> header.
    /// </summary>
    /// <returns>The same builder for chaining.</returns>
    public ServiceBusProducerDescriptorBuilder<T> WithSessions()
    {
        EnableSessions = true;
        return this;
    }

    /// <summary>Builds the configured <see cref="ServiceBusProducerDescriptor{T}"/>.</summary>
    public ServiceBusProducerDescriptor<T> Build()
    {
        return new ServiceBusProducerDescriptor<T>(TopicPath, CreateSubscription, EnableSessions);
    }
}
