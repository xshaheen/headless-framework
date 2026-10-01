// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Provides a fluent API for configuring messaging infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// The messaging builder configures infrastructure and global behavior. Consumers declare themselves with
/// <see cref="BusConsumerAttribute"/> or <see cref="QueueConsumerAttribute"/> and register through their assembly's
/// generated <see cref="IMessagingModule"/>.
/// </para>
/// </remarks>
[PublicAPI]
public interface IMessagingBuilder
{
    /// <summary>
    /// Registers a message-name mapping for a message type to enable type-safe publishing.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="messageName">The message name to publish to.</param>
    /// <returns>The current <see cref="IMessagingBuilder"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="messageName"/> is not a valid message name.</exception>
    /// <remarks>
    /// <para>
    /// Message-name mappings enable type-safe publishing by associating message types with their destination message names.
    /// This eliminates magic strings and enables compile-time verification of message routing.
    /// </para>
    /// <para>
    /// The mapping is folded with the host's other message declarations when messaging builds its consumer registry, at
    /// startup or at an earlier publish; a second mapping of <typeparamref name="TMessage"/> to a different name fails
    /// there.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// options.WithMessageNameMapping&lt;OrderPlaced&gt;("orders.placed");
    /// options.WithMessageNameMapping&lt;OrderCancelled&gt;("orders.cancelled");
    ///
    /// // Later in code:
    /// await publisher.PublishAsync(new OrderPlaced { OrderId = 123 });
    /// // ^ automatically publishes to "orders.placed"
    /// </code>
    /// </para>
    /// </remarks>
    IMessagingBuilder WithMessageNameMapping<TMessage>(string messageName)
        where TMessage : class;

    /// <summary>
    /// Configures convention-based message naming.
    /// </summary>
    /// <param name="configure">A delegate to configure the messaging conventions.</param>
    /// <returns>The current <see cref="IMessagingBuilder"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configure"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Conventions allow automatic message-name generation from message types, reducing boilerplate
    /// and ensuring consistent naming across your messaging infrastructure.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// options.UseConventions(c =>
    /// {
    ///     c.UseKebabCaseMessageNames(); // OrderCreated → order-created
    ///     c.WithMessageNamePrefix("prod."); // → prod.order-created
    ///     c.WithMessageNameSuffix(".v1"); // → prod.order-created.v1
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    IMessagingBuilder UseConventions(Action<MessagingConventions> configure);
}
