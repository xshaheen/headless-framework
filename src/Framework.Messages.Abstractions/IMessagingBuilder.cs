// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;

namespace Framework.Messages;

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
/// services.AddMessages(options =>
/// {
///     // Auto-discover all IConsume&lt;T&gt; implementations in assembly
///     options.ScanConsumers(typeof(Program).Assembly);
///
///     // Or register specific consumers with custom configuration
///     options.Consumer&lt;OrderPlacedHandler&gt;()
///         .Topic("orders.placed")
///         .WithRetry(new ExponentialBackoffStrategy())
///         .WithConcurrency(maxInFlight: 10)
///         .WithThrottling(maxMessages: 100, perSecond: 1);
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
    /// options.ScanConsumers(typeof(Program).Assembly);
    /// </code>
    /// </para>
    /// </remarks>
    IMessagingBuilder ScanConsumers(Assembly assembly);

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
    /// </exception>
    /// <remarks>
    /// <para>
    /// Use this method when you need fine-grained control over a specific consumer's configuration,
    /// such as custom topic names, retry strategies, concurrency limits, or filtering.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// options.Consumer&lt;OrderPlacedHandler&gt;()
    ///     .Topic("orders.placed.v2") // Override convention-based topic
    ///     .WithConcurrency(maxInFlight: 5) // Limit concurrent processing
    ///     .WithRetry(new ExponentialBackoffStrategy())
    ///     .WithOrdering(ctx => ctx.Message.OrderId.ToString()); // Sequential per OrderId
    /// </code>
    /// </para>
    /// </remarks>
    IConsumerBuilder<TConsumer> Consumer<TConsumer>()
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
}
