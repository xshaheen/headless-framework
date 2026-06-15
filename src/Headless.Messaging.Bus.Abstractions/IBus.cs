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
/// At least one <see cref="IBusTransport"/> must be registered in DI for direct bus publishing.
/// Consumer-side intent mismatches are caught at host startup; publisher-only mismatches surface
/// when the publisher is resolved.
/// </para>
/// </remarks>
[PublicAPI]
public interface IBus
{
    /// <summary>
    /// Publishes a message to the configured bus transport using the resolved message name.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="contentObj">The message payload. Can be <see langword="null"/>.</param>
    /// <param name="options">Optional publish overrides for message name, correlation, and custom headers.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the publish operation.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="MessageOptions.TenantId"/> is set to an empty or whitespace value.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="MessageOptions.MessageId"/> exceeds <see cref="MessageOptions.MessageIdMaxLength"/>
    /// or <see cref="MessageOptions.TenantId"/> exceeds <see cref="MessageOptions.TenantIdMaxLength"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="MessageOptions.Headers"/> contains a reserved messaging header
    /// (use <see cref="MessageOptions"/> overrides instead), when a raw <see cref="Headers.TenantId"/>
    /// header is supplied without setting <see cref="MessageOptions.TenantId"/>, or when both are
    /// supplied with disagreeing values, or when any outbound header name/value contains control
    /// characters.
    /// </exception>
    Task PublishAsync<T>(T? contentObj, PublishOptions? options = null, CancellationToken cancellationToken = default);
}
