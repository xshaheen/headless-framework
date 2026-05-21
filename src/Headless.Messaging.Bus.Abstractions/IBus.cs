// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Publishes broadcast (publish/subscribe) messages directly to the configured bus transport.
/// Fire-and-forget — no persistence, no scheduling.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IBus"/> contract is broadcast intent: every subscriber receives its own copy of
/// each published message. <see cref="IBus"/> writes straight to the broker; if your application
/// needs at-least-once delivery semantics, use <see cref="IOutboxBus"/> instead.
/// </para>
/// <para>
/// <see cref="PublishOptions.Delay"/> is ignored on this interface (direct publishers are
/// fire-and-forget). Use <see cref="IOutboxBus"/> with <see cref="PublishOptions.Delay"/> set for
/// scheduled delivery.
/// </para>
/// <para>
/// At least one <see cref="IBusTransport"/> must be registered in DI for an application that
/// resolves <see cref="IBus"/>. Misconfiguration is caught at host startup, not at first call.
/// </para>
/// </remarks>
[PublicAPI]
public interface IBus
{
    /// <summary>
    /// Publishes a message to the configured bus transport using the resolved topic.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="contentObj">The message payload. Can be <see langword="null"/>.</param>
    /// <param name="options">Optional publish overrides for topic, correlation, and custom headers.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the publish operation.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="MessagePublishOptionsBase.TenantId"/> is set to an empty or whitespace value.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="MessagePublishOptionsBase.MessageId"/> exceeds <see cref="MessagePublishOptionsBase.MessageIdMaxLength"/>
    /// or <see cref="MessagePublishOptionsBase.TenantId"/> exceeds <see cref="MessagePublishOptionsBase.TenantIdMaxLength"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="MessagePublishOptionsBase.Headers"/> contains a reserved messaging header
    /// (use <see cref="MessagePublishOptionsBase"/> overrides instead), when a raw <see cref="Headers.TenantId"/>
    /// header is supplied without setting <see cref="MessagePublishOptionsBase.TenantId"/>, or when both are
    /// supplied with disagreeing values.
    /// </exception>
    Task PublishAsync<T>(T? contentObj, PublishOptions? options = null, CancellationToken cancellationToken = default);
}
