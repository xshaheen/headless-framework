// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages;

/// <summary>
/// Provides a fluent API for configuring individual consumer behavior.
/// </summary>
/// <typeparam name="TConsumer">
/// The consumer type being configured. Must implement at least one <see cref="IConsume{TMessage}"/> interface.
/// </typeparam>
/// <remarks>
/// <para>
/// The consumer builder allows fine-grained control over a specific consumer's runtime behavior,
/// including topic routing, retry policies, resource limits, and message filtering.
/// </para>
/// <para>
/// All configuration methods return <c>this</c> for method chaining, enabling fluent configuration:
/// <code>
/// options.Consumer&lt;OrderHandler&gt;()
///     .Topic("orders.v2")
///     .WithRetry(new ExponentialBackoffStrategy())
///     .WithConcurrency(maxInFlight: 10)
///     .WithThrottling(maxMessages: 100, perSecond: 1);
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
    /// <param name="isPartial">
    /// If true, the topic will be combined with a class-level topic to form the final topic name
    /// (e.g., class topic "orders" + method topic "created" = "orders.created").
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
    /// options.Consumer&lt;OrderPlacedHandler&gt;()
    ///     .Topic("orders.placed.v2");
    ///
    /// // Use partial topic (combines with class-level topic)
    /// options.Consumer&lt;OrderPlacedHandler&gt;()
    ///     .Topic("created", isPartial: true); // becomes "orders.created" if class topic is "orders"
    /// </code>
    /// </para>
    /// </remarks>
    IConsumerBuilder<TConsumer> Topic(string topic, bool isPartial = false);

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
    /// If not specified, defaults to the assembly name.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// options.Consumer&lt;OrderPlacedHandler&gt;()
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
    /// <strong>Note:</strong> If you set this value but don't specify a Group, a group will be
    /// automatically created using the topic name.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// // Process up to 10 messages concurrently
    /// options.Consumer&lt;OrderPlacedHandler&gt;()
    ///     .Topic("orders.placed")
    ///     .WithConcurrency(10);
    ///
    /// // Strictly sequential processing
    /// options.Consumer&lt;PaymentHandler&gt;()
    ///     .Topic("payments.process")
    ///     .WithConcurrency(1);
    /// </code>
    /// </para>
    /// </remarks>
    IConsumerBuilder<TConsumer> WithConcurrency(byte maxConcurrent);

    /// <summary>
    /// Completes the consumer configuration and returns to the messaging builder.
    /// </summary>
    /// <returns>
    /// The parent <see cref="IMessagingBuilder"/> instance for continued configuration.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Call this method after configuring all consumer-specific settings to finalize
    /// the configuration and return to the messaging builder for additional setup.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// services.AddMessages(options =>
    /// {
    ///     options.Consumer&lt;OrderHandler&gt;()
    ///         .Topic("orders.placed")
    ///         .WithConcurrency(maxInFlight: 10)
    ///         .Build(); // Finalizes consumer configuration
    ///
    ///     options.Consumer&lt;PaymentHandler&gt;()
    ///         .Topic("payments.processed")
    ///         .Build();
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    IMessagingBuilder Build();
}
