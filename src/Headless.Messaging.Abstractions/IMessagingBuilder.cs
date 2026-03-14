// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;

namespace Headless.Messaging;

/// <summary>
/// Provides a fluent API for configuring message consumers and messaging infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// The messaging builder is the primary configuration interface for the type-safe messaging system.
/// Use it to:
/// <list type="bullet">
/// <item><description>Register consumers automatically via assembly scanning</description></item>
/// <item><description>Register consumers manually with explicit configuration</description></item>
/// <item><description>Configure global messaging behavior and middleware</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Example:</strong>
/// <code>
/// services.AddMessaging(options =>
/// {
///     // Auto-discover all IConsume&lt;T&gt; implementations in assembly
///     options.SubscribeFromAssembly(typeof(Program).Assembly);
///
///     // Or register specific consumers with custom configuration
///     options.Subscribe&lt;OrderPlacedHandler&gt;()
///         .Topic("orders.placed")
///         .Concurrency(10);
/// });
/// </code>
/// </para>
/// </remarks>
public interface IMessagingBuilder
{
    /// <summary>
    /// Scans the specified assembly for types implementing <see cref="IConsume{TMessage}"/> and registers them.
    /// </summary>
    /// <param name="assembly">
    /// The assembly to scan for consumer types.
    /// </param>
    /// <returns>
    /// The current <see cref="IMessagingBuilder"/> instance for method chaining.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Discovery Rules:</strong>
    /// <list type="bullet">
    /// <item><description>Finds all non-abstract, non-generic types</description></item>
    /// <item><description>Discovers ALL <see cref="IConsume{TMessage}"/> interfaces per type (supports multi-type consumers)</description></item>
    /// <item><description>Automatically registers types with dependency injection as scoped services</description></item>
    /// <item><description>Uses convention-based topic naming (message type name) unless overridden</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Multi-Type Consumers:</strong>
    /// If a consumer implements multiple <see cref="IConsume{TMessage}"/> interfaces, it is registered
    /// for each message type. All handlers share the same DI scope and consumer instance.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// // Discovers and registers:
    /// // - OrderPlacedHandler : IConsume&lt;OrderPlaced&gt;
    /// // - OrderCancelledHandler : IConsume&lt;OrderCancelled&gt;
    /// // - OrderConsumer : IConsume&lt;OrderPlaced&gt;, IConsume&lt;OrderCancelled&gt; (multi-type)
    /// options.SubscribeFromAssembly(typeof(Program).Assembly);
/// </code>
/// </para>
/// </remarks>
    IMessagingBuilder SubscribeFromAssembly(Assembly assembly);

    /// <summary>
    /// Scans the assembly containing the specified marker type for closed <see cref="IConsume{TMessage}"/> handlers.
    /// </summary>
    /// <typeparam name="TMarker">A type from the target assembly.</typeparam>
    /// <returns>The current <see cref="IMessagingBuilder"/> instance for method chaining.</returns>
    IMessagingBuilder SubscribeFromAssemblyContaining<TMarker>();

    /// <summary>
    /// Registers a specific consumer type and returns a builder for additional configuration.
    /// </summary>
    /// <typeparam name="TConsumer">
    /// The consumer type to register. Must implement at least one <see cref="IConsume{TMessage}"/> interface.
    /// </typeparam>
    /// <returns>
    /// An <see cref="IConsumerBuilder{TConsumer}"/> instance for configuring the consumer's behavior.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <typeparamref name="TConsumer"/> does not implement any <see cref="IConsume{TMessage}"/> interfaces.
    /// Explicit registration also rejects consumers implementing multiple <see cref="IConsume{TMessage}"/> interfaces.
    /// Use <see cref="SubscribeFromAssembly(Assembly)"/> for multi-message consumers.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Use this method when you need fine-grained control over a specific consumer's configuration,
    /// such as custom topic names, concurrency limits, or filtering.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// options.Subscribe&lt;OrderPlacedHandler&gt;()
///     .Topic("orders.placed.v2") // Override convention-based topic
///     .Concurrency(5); // Limit concurrent processing
/// </code>
/// </para>
/// </remarks>
    IConsumerBuilder<TConsumer> Subscribe<TConsumer>()
        where TConsumer : class;

    /// <summary>
    /// Registers a specific consumer type with a topic, automatically creating a topic mapping for type-safe publishing.
    /// </summary>
    /// <typeparam name="TConsumer">
    /// The consumer type to register. Must implement at least one <see cref="IConsume{TMessage}"/> interface.
    /// </typeparam>
    /// <param name="topic">
    /// The topic name to subscribe to. This automatically creates a topic mapping for the message type.
    /// </param>
    /// <returns>
    /// An <see cref="IConsumerBuilder{TConsumer}"/> instance for configuring the consumer's behavior.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="topic"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <typeparamref name="TConsumer"/> does not implement any <see cref="IConsume{TMessage}"/> interfaces.
    /// Explicit registration also rejects consumers implementing multiple <see cref="IConsume{TMessage}"/> interfaces.
    /// Use <see cref="SubscribeFromAssembly(Assembly)"/> for multi-message consumers.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This is the preferred method for registering consumers as it eliminates topic name duplication
    /// by automatically creating a topic mapping for the message type. This enables type-safe publishing
    /// without requiring a separate <see cref="WithTopicMapping{TMessage}"/> call.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// // Register consumer and implicitly map OrderPlaced → "orders.placed"
    /// options.Subscribe&lt;OrderPlacedHandler&gt;("orders.placed")
///     .Concurrency(5);
///
/// // Later in code - type-safe publishing works automatically:
/// await publisher.PublishAsync(new OrderPlaced { OrderId = 123 });
/// // ^ automatically publishes to "orders.placed"
/// </code>
/// </para>
/// </remarks>
    IConsumerBuilder<TConsumer> Subscribe<TConsumer>(string topic)
        where TConsumer : class;

    /// <summary>
    /// Registers a specific consumer type and applies explicit subscription overrides in one call.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer type to register.</typeparam>
    /// <param name="configure">The override configuration to apply to the registration.</param>
    /// <returns>The current <see cref="IMessagingBuilder"/> instance for method chaining.</returns>
    IMessagingBuilder Subscribe<TConsumer>(Action<IConsumerBuilder<TConsumer>> configure)
        where TConsumer : class;

    /// <summary>
    /// Registers a topic mapping for a message type to enable type-safe publishing.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="topic">The topic to publish to.</param>
    /// <returns>The current <see cref="IMessagingBuilder"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="topic"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a different topic is already registered for <typeparamref name="TMessage"/>.</exception>
    /// <remarks>
    /// <para>
    /// Topic mappings enable type-safe publishing by associating message types with their destination topics.
    /// This eliminates magic strings and enables compile-time verification of message routing.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// options.WithTopicMapping&lt;OrderPlaced&gt;("orders.placed");
    /// options.WithTopicMapping&lt;OrderCancelled&gt;("orders.cancelled");
    ///
    /// // Later in code:
    /// await publisher.PublishAsync(new OrderPlaced { OrderId = 123 });
    /// // ^ automatically publishes to "orders.placed"
    /// </code>
    /// </para>
    /// </remarks>
    IMessagingBuilder WithTopicMapping<TMessage>(string topic)
        where TMessage : class;

    /// <summary>
    /// Configures convention-based topic naming and default consumer settings.
    /// </summary>
    /// <param name="configure">A delegate to configure the messaging conventions.</param>
    /// <returns>The current <see cref="IMessagingBuilder"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configure"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Conventions allow automatic topic name generation from message types, reducing boilerplate
    /// and ensuring consistent naming across your messaging infrastructure.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// options.UseConventions(c =>
/// {
///     c.UseKebabCaseTopics(); // OrderCreated → order-created
///     c.WithTopicPrefix("prod."); // → prod.order-created
///     c.WithTopicSuffix(".v1"); // → prod.order-created.v1
///     c.UseApplicationId("my-service");
///     c.UseVersion("v1");
/// });
/// </code>
/// </para>
/// </remarks>
    IMessagingBuilder UseConventions(Action<MessagingConventions> configure);
}
