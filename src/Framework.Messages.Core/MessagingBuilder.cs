// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Messages;

/// <summary>
/// Provides a fluent API for configuring message consumers and messaging infrastructure.
/// </summary>
internal sealed class MessagingBuilder : IMessagingBuilder
{
    private readonly IServiceCollection _services;
    private readonly ConsumerRegistry _registry;
    private readonly Dictionary<Type, string> _topicMappings = new();

    internal MessagingBuilder(IServiceCollection services)
    {
        Argument.IsNotNull(services);

        _services = services;
        _registry = new ConsumerRegistry();

        // Register the registry as a singleton
        _services.TryAddSingleton(_registry);
    }

    /// <inheritdoc />
    public IMessagingBuilder ScanConsumers(Assembly assembly)
    {
        Argument.IsNotNull(assembly);

        // Find all types implementing IConsume<T>
        var consumerTypes = assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .Where(t =>
                t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>))
            )
            .ToList();

        foreach (var consumerType in consumerTypes)
        {
            // Get all IConsume<T> interfaces (supports multi-message handlers)
            var consumeInterfaces = consumerType
                .GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>))
                .ToList();

            foreach (var consumeInterface in consumeInterfaces)
            {
                var messageType = consumeInterface.GetGenericArguments()[0];

                // Register consumer with default configuration
                RegisterConsumer(consumerType, messageType, topic: null, group: null, concurrency: 1);
            }
        }

        return this;
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Consumer<TConsumer>()
        where TConsumer : class
    {
        // Find IConsume<T> interface
        var consumeInterface = typeof(TConsumer)
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>));

        if (consumeInterface == null)
        {
            throw new InvalidOperationException($"{typeof(TConsumer).Name} does not implement IConsume<T>");
        }

        var messageType = consumeInterface.GetGenericArguments()[0];

        return new ConsumerBuilder<TConsumer>(this, messageType);
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Consumer<TConsumer>(string topic)
        where TConsumer : class
    {
        Argument.IsNotNullOrWhiteSpace(topic);

        // Find IConsume<T> interface
        var consumeInterface = typeof(TConsumer)
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>));

        if (consumeInterface == null)
        {
            throw new InvalidOperationException($"{typeof(TConsumer).Name} does not implement IConsume<T>");
        }

        var messageType = consumeInterface.GetGenericArguments()[0];

        // Automatically create topic mapping
        WithTopicMapping(messageType, topic);

        return new ConsumerBuilder<TConsumer>(this, messageType, topic);
    }

    /// <inheritdoc />
    public IMessagingBuilder WithTopicMapping<TMessage>(string topic)
        where TMessage : class
    {
        Argument.IsNotNullOrWhiteSpace(topic);

        WithTopicMapping(typeof(TMessage), topic);
        return this;
    }

    /// <summary>
    /// Registers a topic mapping for a message type (non-generic version for internal use).
    /// </summary>
    internal void WithTopicMapping(Type messageType, string topic)
    {
        Argument.IsNotNullOrWhiteSpace(topic);

        if (_topicMappings.TryGetValue(messageType, out var existingTopic) && existingTopic != topic)
        {
            throw new InvalidOperationException(
                $"Message type {messageType.Name} is already mapped to topic '{existingTopic}'. Cannot map to '{topic}'."
            );
        }

        _topicMappings[messageType] = topic;
    }

    /// <summary>
    /// Registers a consumer with the specified metadata.
    /// </summary>
    internal void RegisterConsumer(Type consumerType, Type messageType, string? topic, string? group, byte concurrency)
    {
        // Determine the topic name
        var finalTopic =
            topic
            ?? (_topicMappings.TryGetValue(messageType, out var mappedTopic) ? mappedTopic : null)
            ?? messageType.Name; // Default to message type name

        // Create metadata
        var metadata = new ConsumerMetadata(messageType, consumerType, finalTopic, group, concurrency);

        // Register in registry
        _registry.Register(metadata);

        // Register consumer in DI as scoped service
        var serviceType = typeof(IConsume<>).MakeGenericType(messageType);
        _services.TryAddScoped(serviceType, consumerType);
    }
}
