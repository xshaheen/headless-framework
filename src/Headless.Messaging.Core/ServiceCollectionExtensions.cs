// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering message consumers directly on <see cref="IServiceCollection"/>.
/// </summary>
[PublicAPI]
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers a broadcast (publish/subscribe) message consumer with the specified topic.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer type implementing <see cref="IConsume{TMessage}"/>.</typeparam>
    /// <typeparam name="TMessage">The message type to consume. Must be a reference type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="topic">The topic name to subscribe to.</param>
    /// <returns>A <see cref="IConsumerBuilder{TConsumer}"/> for fluent configuration.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="topic"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Use this for publish/subscribe (broadcast) delivery: every subscriber receives its own copy
    /// of the message. For point-to-point work-queue delivery use
    /// <see cref="AddQueueConsumer{TConsumer,TMessage}"/> instead.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// services.AddBusConsumer&lt;OrderPlacedHandler, OrderPlaced&gt;("orders.placed")
    ///     .Concurrency(10)
    ///     .WithTimeout(TimeSpan.FromSeconds(30));
    /// </code>
    /// </para>
    /// </remarks>
    [PublicAPI]
    public static IConsumerBuilder<TConsumer> AddBusConsumer<TConsumer, TMessage>(
        this IServiceCollection services,
        string topic
    )
        where TConsumer : class, IConsume<TMessage>
        where TMessage : class
    {
        return _AddConsumer<TConsumer, TMessage>(services, topic, IntentType.Bus);
    }

    /// <summary>
    /// Registers a point-to-point (work-queue) message consumer with the specified topic.
    /// </summary>
    /// <remarks>
    /// Use this for competing-consumer delivery: exactly one worker in the group receives each
    /// message. For publish/subscribe (broadcast) delivery use
    /// <see cref="AddBusConsumer{TConsumer,TMessage}"/> instead.
    /// </remarks>
    [PublicAPI]
    public static IConsumerBuilder<TConsumer> AddQueueConsumer<TConsumer, TMessage>(
        this IServiceCollection services,
        string topic
    )
        where TConsumer : class, IConsume<TMessage>
        where TMessage : class
    {
        return _AddConsumer<TConsumer, TMessage>(services, topic, IntentType.Queue);
    }

    private static IConsumerBuilder<TConsumer> _AddConsumer<TConsumer, TMessage>(
        IServiceCollection services,
        string topic,
        IntentType intentType
    )
        where TConsumer : class, IConsume<TMessage>
        where TMessage : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNullOrWhiteSpace(topic);

        // Register consumer in DI as scoped service
        services.TryAddScoped<TConsumer>();
        services.TryAddScoped<IConsume<TMessage>>(sp => sp.GetRequiredService<TConsumer>());

        // Create consumer metadata
        var metadata = new ConsumerMetadata(
            MessageType: typeof(TMessage),
            ConsumerType: typeof(TConsumer),
            Topic: topic,
            Group: null,
            Concurrency: 1,
            HandlerId: MessagingConventions.GetDefaultHandlerId(typeof(TConsumer), typeof(TMessage)),
            IntentType: intentType
        );

        // Register metadata as singleton for discovery
        services.AddSingleton(metadata);

        // Return fluent builder
        return new ServiceCollectionConsumerBuilder<TConsumer>(services, metadata);
    }
}
