// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Publishes messages directly to the transport without persistence (fire-and-forget).
/// </summary>
/// <remarks>
/// <para>
/// Messages are serialized and sent immediately to the broker. There is no outbox, no
/// persistence, no automatic retries, no transaction coordination, and no delayed delivery.
/// If the send succeeds, the broker has acknowledged receipt; if it fails, a
/// <see cref="PublisherSentFailedException"/> is thrown and the message is lost.
/// </para>
/// <para>
/// <b>When to use <see cref="IDirectPublisher"/> vs <see cref="IOutboxPublisher"/>:</b>
/// </para>
/// <list type="table">
/// <listheader>
/// <term>Concern</term>
/// <description>Direct (<see cref="IDirectPublisher"/>) / Outbox (<see cref="IOutboxPublisher"/>)</description>
/// </listheader>
/// <item>
/// <term>Delivery guarantee</term>
/// <description>Best-effort (fire-and-forget) / At-least-once</description>
/// </item>
/// <item>
/// <term>Transaction support</term>
/// <description>No / Yes — coordinates with <see cref="IOutboxTransaction"/></description>
/// </item>
/// <item>
/// <term>Delayed delivery</term>
/// <description>No / Yes</description>
/// </item>
/// <item>
/// <term>Persistence</term>
/// <description>Messages sent immediately, not stored / Messages stored until confirmed delivered</description>
/// </item>
/// <item>
/// <term>Latency</term>
/// <description>Lower — single network hop to broker / Higher — write to outbox store, then background dispatch</description>
/// </item>
/// <item>
/// <term>Typical use cases</term>
/// <description>Metrics, telemetry, cache invalidation, real-time notifications / Order processing, payments, domain events, saga orchestration</description>
/// </item>
/// </list>
/// <para>
/// <b>Topic resolution:</b> The topic is resolved from explicit type mappings configured via
/// <see cref="IMessagingBuilder.WithTopicMapping{TMessage}"/>, or from naming conventions
/// when no explicit mapping is registered. An <see cref="InvalidOperationException"/> is
/// thrown if neither exists for the message type.
/// </para>
/// <para>
/// <b>Message loss scenarios:</b> Messages may be lost if:
/// </para>
/// <list type="bullet">
/// <item><description>The application crashes before the broker acknowledges receipt.</description></item>
/// <item><description>The transport is temporarily unavailable.</description></item>
/// <item><description>Network issues occur during transmission.</description></item>
/// </list>
/// <para>
/// For reliable delivery with at-least-once guarantees and transactional support,
/// use <see cref="IOutboxPublisher"/>.
/// </para>
/// <para>
/// <b>Example:</b>
/// <code>
/// // Registration: options.WithTopicMapping&lt;MetricEvent&gt;("metrics.collected");
/// await directPublisher.PublishAsync(new MetricEvent { Name = "cpu", Value = 42.5 });
/// </code>
/// </para>
/// </remarks>
/// <seealso cref="IOutboxPublisher"/>
/// <seealso cref="IMessagingBuilder.WithTopicMapping{TMessage}"/>
public interface IDirectPublisher
{
    /// <summary>
    /// Publishes a message directly to the transport.
    /// The topic is resolved from the message type's registered topic mapping or naming conventions.
    /// </summary>
    /// <typeparam name="T">
    /// The message type. Must have a topic mapping registered via
    /// <see cref="IMessagingBuilder.WithTopicMapping{TMessage}"/> or follow naming conventions.
    /// </typeparam>
    /// <param name="contentObj">The message content to serialize and publish. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation. Note: cannot cancel an in-flight transport send once the
    /// message has been handed to the broker client.
    /// </param>
    /// <returns>A task that completes when the broker acknowledges receipt of the message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="contentObj"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No topic mapping or naming convention exists for <typeparamref name="T"/>.
    /// </exception>
    /// <exception cref="PublisherSentFailedException">The transport failed to send the message.</exception>
    Task PublishAsync<T>(T contentObj, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Publishes a message with custom headers directly to the transport.
    /// The topic is resolved from the message type's registered topic mapping or naming conventions.
    /// </summary>
    /// <typeparam name="T">
    /// The message type. Must have a topic mapping registered via
    /// <see cref="IMessagingBuilder.WithTopicMapping{TMessage}"/> or follow naming conventions.
    /// </typeparam>
    /// <param name="contentObj">The message content to serialize and publish. Must not be <see langword="null"/>.</param>
    /// <param name="headers">Additional headers attached to the message envelope.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the broker acknowledges receipt of the message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="contentObj"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No topic mapping or naming convention exists for <typeparamref name="T"/>.
    /// </exception>
    /// <exception cref="PublisherSentFailedException">The transport failed to send the message.</exception>
    Task PublishAsync<T>(
        T contentObj,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    )
        where T : class;
}
