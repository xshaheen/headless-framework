// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Publishes messages through the outbox pattern, providing at-least-once delivery guarantees.
/// </summary>
/// <remarks>
/// <para>
/// Messages are persisted to a durable outbox store (typically a database table) before being
/// dispatched to the transport. This ensures that messages are not lost even if the application
/// crashes or the transport is temporarily unavailable. A background process picks up pending
/// messages and forwards them to the broker.
/// </para>
/// <para>
/// <b>When to use <see cref="IOutboxPublisher"/> vs <see cref="IDirectPublisher"/>:</b>
/// </para>
/// <list type="table">
/// <listheader>
/// <term>Concern</term>
/// <description>Outbox (<see cref="IOutboxPublisher"/>) / Direct (<see cref="IDirectPublisher"/>)</description>
/// </listheader>
/// <item>
/// <term>Delivery guarantee</term>
/// <description>At-least-once / Best-effort (fire-and-forget)</description>
/// </item>
/// <item>
/// <term>Transaction support</term>
/// <description>Yes — coordinates with <see cref="IOutboxTransaction"/> / No</description>
/// </item>
/// <item>
/// <term>Delayed delivery</term>
/// <description>Yes (via <c>PublishDelayAsync</c>) / No</description>
/// </item>
/// <item>
/// <term>Persistence</term>
/// <description>Messages stored until confirmed delivered / Messages sent immediately, not stored</description>
/// </item>
/// <item>
/// <term>Typical use cases</term>
/// <description>Order processing, payments, domain events, saga orchestration / Metrics, telemetry, cache invalidation, real-time notifications</description>
/// </item>
/// </list>
/// <para>
/// <b>Transaction usage:</b> Assign an <see cref="IOutboxTransaction"/> to <see cref="Transaction"/>
/// to atomically publish messages with database changes. When the transaction commits, buffered
/// messages are dispatched; on rollback, they are discarded.
/// </para>
/// <para>
/// <b>Topic resolution:</b> Overloads accepting a <c>name</c> parameter use an explicit topic.
/// Overloads constrained to <c>where T : class</c> resolve the topic from mappings configured via
/// <see cref="IMessagingBuilder.WithTopicMapping{TMessage}"/> or naming conventions, and throw
/// <see cref="InvalidOperationException"/> if no mapping exists.
/// </para>
/// <para>
/// <b>Example (explicit topic):</b>
/// <code>
/// await publisher.PublishAsync("orders.placed", new OrderPlaced { OrderId = 42 });
/// </code>
/// </para>
/// <para>
/// <b>Example (type-safe topic mapping):</b>
/// <code>
/// // Registration: options.WithTopicMapping&lt;OrderPlaced&gt;("orders.placed");
/// await publisher.PublishAsync(new OrderPlaced { OrderId = 42 });
/// </code>
/// </para>
/// <para>
/// <b>Example (transactional):</b>
/// <code>
/// using var tx = outboxTransaction; // from DI
/// tx.DbTransaction = dbTransaction;
/// publisher.Transaction = tx;
///
/// await publisher.PublishAsync("orders.placed", new OrderPlaced { OrderId = 42 });
/// await tx.CommitAsync();
/// </code>
/// </para>
/// </remarks>
/// <seealso cref="IDirectPublisher"/>
/// <seealso cref="IOutboxTransaction"/>
/// <seealso cref="IMessagingBuilder.WithTopicMapping{TMessage}"/>
public interface IOutboxPublisher
{
    /// <summary>
    /// Gets the service provider associated with this publisher instance.
    /// </summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Gets or sets the outbox transaction that coordinates message publishing with a database transaction.
    /// When set, published messages are buffered until the transaction is committed.
    /// Set to <see langword="null"/> to publish without transactional coordination.
    /// </summary>
    /// <seealso cref="IOutboxTransaction"/>
    IOutboxTransaction? Transaction { get; set; }

    /// <summary>
    /// Publishes a message to the specified topic.
    /// </summary>
    /// <typeparam name="T">The type of the message content, serialized as the message body.</typeparam>
    /// <param name="name">The topic name or exchange routing key.</param>
    /// <param name="contentObj">The message body content to serialize. Can be <see langword="null"/>.</param>
    /// <param name="callbackName">
    /// An optional callback subscriber name. When specified, the subscriber can respond back on this channel.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    Task PublishAsync<T>(
        string name,
        T? contentObj,
        string? callbackName = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Publishes a message to the specified topic with custom headers.
    /// </summary>
    /// <typeparam name="T">The type of the message content, serialized as the message body.</typeparam>
    /// <param name="name">The topic name or exchange routing key.</param>
    /// <param name="contentObj">The message body content to serialize. Can be <see langword="null"/>.</param>
    /// <param name="headers">Additional headers attached to the message envelope.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    Task PublishAsync<T>(
        string name,
        T? contentObj,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Schedules a message for delayed delivery to the specified topic with custom headers.
    /// The message is persisted immediately and dispatched after <paramref name="delayTime"/> elapses.
    /// </summary>
    /// <typeparam name="T">The type of the message content, serialized as the message body.</typeparam>
    /// <param name="delayTime">How long to wait before the message is dispatched to the transport.</param>
    /// <param name="name">The topic name or exchange routing key.</param>
    /// <param name="contentObj">The message body content to serialize. Can be <see langword="null"/>.</param>
    /// <param name="headers">Additional headers attached to the message envelope.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    Task PublishDelayAsync<T>(
        TimeSpan delayTime,
        string name,
        T? contentObj,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Schedules a message for delayed delivery to the specified topic.
    /// The message is persisted immediately and dispatched after <paramref name="delayTime"/> elapses.
    /// </summary>
    /// <typeparam name="T">The type of the message content, serialized as the message body.</typeparam>
    /// <param name="delayTime">How long to wait before the message is dispatched to the transport.</param>
    /// <param name="name">The topic name or exchange routing key.</param>
    /// <param name="contentObj">The message body content to serialize. Can be <see langword="null"/>.</param>
    /// <param name="callbackName">
    /// An optional callback subscriber name. When specified, the subscriber can respond back on this channel.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    Task PublishDelayAsync<T>(
        TimeSpan delayTime,
        string name,
        T? contentObj,
        string? callbackName = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Publishes a message using the topic mapping registered for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The message type. Must have a topic mapping registered via
    /// <see cref="IMessagingBuilder.WithTopicMapping{TMessage}"/> or naming conventions.
    /// </typeparam>
    /// <param name="contentObj">The message body content to serialize. Can be <see langword="null"/>.</param>
    /// <param name="callbackName">
    /// An optional callback subscriber name. When specified, the subscriber can respond back on this channel.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// No topic mapping or naming convention exists for <typeparamref name="T"/>.
    /// </exception>
    Task PublishAsync<T>(T? contentObj, string? callbackName = null, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Publishes a message with custom headers using the topic mapping registered for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The message type. Must have a topic mapping registered via
    /// <see cref="IMessagingBuilder.WithTopicMapping{TMessage}"/> or naming conventions.
    /// </typeparam>
    /// <param name="contentObj">The message body content to serialize. Can be <see langword="null"/>.</param>
    /// <param name="headers">Additional headers attached to the message envelope.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// No topic mapping or naming convention exists for <typeparamref name="T"/>.
    /// </exception>
    Task PublishAsync<T>(
        T? contentObj,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    )
        where T : class;

    /// <summary>
    /// Schedules a message for delayed delivery with custom headers, using the topic mapping
    /// registered for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The message type. Must have a topic mapping registered via
    /// <see cref="IMessagingBuilder.WithTopicMapping{TMessage}"/> or naming conventions.
    /// </typeparam>
    /// <param name="delayTime">How long to wait before the message is dispatched to the transport.</param>
    /// <param name="contentObj">The message body content to serialize. Can be <see langword="null"/>.</param>
    /// <param name="headers">Additional headers attached to the message envelope.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// No topic mapping or naming convention exists for <typeparamref name="T"/>.
    /// </exception>
    Task PublishDelayAsync<T>(
        TimeSpan delayTime,
        T? contentObj,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    )
        where T : class;

    /// <summary>
    /// Schedules a message for delayed delivery using the topic mapping registered for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The message type. Must have a topic mapping registered via
    /// <see cref="IMessagingBuilder.WithTopicMapping{TMessage}"/> or naming conventions.
    /// </typeparam>
    /// <param name="delayTime">How long to wait before the message is dispatched to the transport.</param>
    /// <param name="contentObj">The message body content to serialize. Can be <see langword="null"/>.</param>
    /// <param name="callbackName">
    /// An optional callback subscriber name. When specified, the subscriber can respond back on this channel.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// No topic mapping or naming convention exists for <typeparamref name="T"/>.
    /// </exception>
    Task PublishDelayAsync<T>(
        TimeSpan delayTime,
        T? contentObj,
        string? callbackName = null,
        CancellationToken cancellationToken = default
    )
        where T : class;
}
