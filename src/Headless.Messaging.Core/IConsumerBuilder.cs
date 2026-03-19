// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging;

/// <summary>
/// Provides a fluent API for configuring individual consumer behavior.
/// </summary>
/// <typeparam name="TConsumer">
/// The consumer type being configured. Must implement at least one <see cref="IConsume{TMessage}"/> interface.
/// </typeparam>
/// <remarks>
/// <para>
/// The consumer builder allows fine-grained control over a specific consumer's runtime behavior,
/// including topic routing, handler identity, and concurrency limits.
/// </para>
/// <para>
/// All configuration methods return <c>this</c> for method chaining, enabling fluent configuration:
/// <code>
/// options.Subscribe&lt;OrderHandler&gt;()
///     .Topic("orders.v2")
///     .Group("order-service")
///     .Concurrency(maxInFlight: 10);
/// </code>
/// </para>
/// </remarks>
public interface IConsumerBuilder<TConsumer>
    where TConsumer : class
{
    /// <summary>
    /// Overrides the default topic name for this consumer.
    /// </summary>
    /// <param name="topic">
    /// The topic name to subscribe to. Must not be null or whitespace.
    /// </param>
    /// <returns>
    /// The current <see cref="IConsumerBuilder{TConsumer}"/> instance for method chaining.
    /// </returns>
    /// <remarks>
    /// <para>
    /// By default, consumers subscribe to a topic named after the message type (e.g., <c>OrderPlaced</c>).
    /// Use this method to override the convention and subscribe to a custom topic name.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// // Subscribe to versioned topic instead of default
    /// options.Subscribe&lt;OrderPlacedHandler&gt;()
    ///     .Topic("orders.placed.v2");
    /// </code>
    /// </para>
    /// </remarks>
    IConsumerBuilder<TConsumer> Topic(string topic);

    /// <summary>
    /// Sets the consumer group name for this consumer.
    /// </summary>
    /// <param name="group">
    /// The consumer group name. For Kafka, this is the group.id.
    /// For RabbitMQ, this is the queue name.
    /// </param>
    /// <returns>
    /// The current <see cref="IConsumerBuilder{TConsumer}"/> instance for method chaining.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The consumer group determines message distribution:
    /// <list type="bullet">
    /// <item><description><strong>Kafka:</strong> Messages are distributed across consumers in the same group (load balancing)</description></item>
    /// <item><description><strong>RabbitMQ:</strong> Each group gets its own queue; messages are duplicated across groups (pub/sub)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// If not specified, a deterministic default is derived from the configured application id,
    /// the handler identity, and the messaging version.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// options.Subscribe&lt;OrderPlacedHandler&gt;()
    ///     .Topic("orders.placed")
    ///     .Group("order-processing-service");
    /// </code>
    /// </para>
    /// </remarks>
    IConsumerBuilder<TConsumer> Group(string group);

    /// <summary>
    /// Limits the number of messages consumed concurrently by this consumer.
    /// </summary>
    /// <param name="maxConcurrent">
    /// The maximum number of messages to process concurrently. Must be greater than 0.
    /// </param>
    /// <returns>
    /// The current <see cref="IConsumerBuilder{TConsumer}"/> instance for method chaining.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This setting controls message processing parallelism within a single consumer instance.
    /// It does NOT control the number of consumer instances.
    /// </para>
    /// <para>
    /// Use this to:
    /// <list type="bullet">
    /// <item><description>Limit resource usage (database connections, API calls, etc.)</description></item>
    /// <item><description>Prevent overwhelming downstream services</description></item>
    /// <item><description>Maintain message ordering (set to 1 for strict ordering)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Note:</strong> If you do not specify a group, a deterministic group name is generated
    /// from the configured application id, handler identity, and version.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// // Process up to 10 messages concurrently
    /// options.Subscribe&lt;OrderPlacedHandler&gt;()
    ///     .Topic("orders.placed")
    ///     .Concurrency(10);
    ///
    /// // Strictly sequential processing
    /// options.Subscribe&lt;PaymentHandler&gt;()
    ///     .Topic("payments.process")
    ///     .Concurrency(1);
    /// </code>
    /// </para>
    /// </remarks>
    IConsumerBuilder<TConsumer> Concurrency(byte maxConcurrent);

    /// <summary>
    /// Overrides the deterministic default handler identity for this consumer registration.
    /// </summary>
    /// <param name="handlerId">The explicit handler identity to use for duplicate detection and diagnostics.</param>
    /// <returns>The current <see cref="IConsumerBuilder{TConsumer}"/> instance for method chaining.</returns>
    IConsumerBuilder<TConsumer> HandlerId(string handlerId);

    /// <summary>
    /// Configures per-consumer circuit breaker overrides for this consumer.
    /// </summary>
    /// <param name="configure">
    /// A delegate that receives a <see cref="ConsumerCircuitBreakerOptions"/> instance to configure.
    /// Any property left unset falls back to the global <c>MessagingOptions.CircuitBreaker</c> values.
    /// </param>
    /// <returns>The current <see cref="IConsumerBuilder{TConsumer}"/> instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Use this method when a specific consumer requires different circuit breaker sensitivity than
    /// the global default. For example, a latency-sensitive consumer might use a lower
    /// <see cref="ConsumerCircuitBreakerOptions.FailureThreshold"/>, while a best-effort consumer
    /// might disable the circuit breaker entirely.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// options.Subscribe&lt;PaymentHandler&gt;()
    ///     .Topic("payments.process")
    ///     .WithCircuitBreaker(cb =>
    ///     {
    ///         cb.FailureThreshold = 3;
    ///         cb.OpenDuration = TimeSpan.FromSeconds(60);
    ///     });
    /// </code>
    /// </para>
    /// </remarks>
    IConsumerBuilder<TConsumer> WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure);
}
