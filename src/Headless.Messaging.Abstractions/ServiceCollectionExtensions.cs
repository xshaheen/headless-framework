// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering message consumers directly on <see cref="IServiceCollection"/>.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers a message consumer with the specified topic.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer type implementing <see cref="IConsume{TMessage}"/>.</typeparam>
    /// <typeparam name="TMessage">The message type to consume. Must be a reference type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="topic">The topic name to subscribe to.</param>
    /// <returns>A <see cref="IConsumerBuilder{TConsumer}"/> for fluent configuration.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="topic"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This method provides a unified registration pattern for both library authors and application developers.
    /// It registers the consumer with DI and creates a <see cref="ConsumerMetadata"/> instance
    /// for discovery during messaging startup.
    /// </para>
    /// <para>
    /// <strong>Example (Library Author):</strong>
    /// <code>
    /// public static IServiceCollection AddMyLibrary(this IServiceCollection services)
    /// {
    ///     services.AddConsumer&lt;MyEventHandler, MyEvent&gt;("my-library.events")
    ///         .WithConcurrency(5);
    ///     return services;
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <strong>Example (Application Developer):</strong>
    /// <code>
    /// services.AddConsumer&lt;OrderPlacedHandler, OrderPlaced&gt;("orders.placed")
    ///     .WithConcurrency(10)
    ///     .WithTimeout(TimeSpan.FromSeconds(30));
    /// </code>
    /// </para>
    /// </remarks>
    public static IConsumerBuilder<TConsumer> AddConsumer<TConsumer, TMessage>(
        this IServiceCollection services,
        string topic
    )
        where TConsumer : class, IConsume<TMessage>
        where TMessage : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNullOrWhiteSpace(topic);

        // Register consumer in DI as scoped service
        services.TryAddScoped<IConsume<TMessage>, TConsumer>();

        // Create consumer metadata
        var metadata = new ConsumerMetadata(
            MessageType: typeof(TMessage),
            ConsumerType: typeof(TConsumer),
            Topic: topic,
            Group: null,
            Concurrency: 1
        );

        // Register metadata as singleton for discovery
        services.AddSingleton(metadata);

        // Return fluent builder
        return new ServiceCollectionConsumerBuilder<TConsumer>(services, metadata);
    }
}
