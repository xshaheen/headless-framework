// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Publishes messages directly to the transport without persistence.
/// <para>
/// <b>Fire-and-forget semantics:</b> Messages are sent immediately to the transport
/// without storing in the outbox. No retries, no transaction support, no delayed delivery.
/// </para>
/// <para>
/// Topics are resolved from message type mappings configured via
/// <see cref="IMessagingBuilder.WithTopicMapping{TMessage}"/>.
/// </para>
/// <para>
/// <b>Use cases:</b> Metrics, telemetry, real-time notifications, cache invalidation -
/// scenarios where occasional message loss is acceptable.
/// </para>
/// <para>
/// For reliable delivery with at-least-once guarantees, use <see cref="IOutboxPublisher"/>.
/// </para>
/// </summary>
public interface IDirectPublisher
{
    /// <summary>
    /// Publishes a message directly to the transport without persistence.
    /// The topic is resolved from the message type's configured topic mapping.
    /// </summary>
    /// <typeparam name="T">The message type. Must have a registered topic mapping.</typeparam>
    /// <param name="contentObj">The message content to serialize and publish.</param>
    /// <param name="cancellationToken">
    /// Cancellation token. Note: Cannot cancel in-flight transport sends.
    /// </param>
    /// <returns>A task that completes when the message is sent to the transport.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contentObj"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no topic mapping exists for type <typeparamref name="T"/>.</exception>
    /// <exception cref="Exception">PublisherSentFailedException thrown when the transport fails to send.</exception>
    /// <remarks>
    /// <b>WARNING:</b> Messages may be lost if:
    /// <list type="bullet">
    /// <item>The application crashes before the broker acknowledges receipt</item>
    /// <item>The transport is temporarily unavailable</item>
    /// <item>Network issues occur during transmission</item>
    /// </list>
    /// </remarks>
    Task PublishAsync<T>(T contentObj, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Publishes a message with custom headers directly to the transport without persistence.
    /// The topic is resolved from the message type's configured topic mapping.
    /// </summary>
    /// <typeparam name="T">The message type. Must have a registered topic mapping.</typeparam>
    /// <param name="contentObj">The message content to serialize and publish.</param>
    /// <param name="headers">Custom headers to include with the message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the message is sent to the transport.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contentObj"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no topic mapping exists for type <typeparamref name="T"/>.</exception>
    /// <exception cref="Exception">PublisherSentFailedException thrown when the transport fails to send.</exception>
    Task PublishAsync<T>(
        T contentObj,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    )
        where T : class;
}
