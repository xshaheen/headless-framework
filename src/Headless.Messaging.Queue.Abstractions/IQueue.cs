// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Enqueues point-to-point (work-queue) messages directly to the configured queue transport.
/// Fire-and-forget — no persistence, no scheduling.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IQueue"/> contract is point-to-point intent: exactly one competing worker
/// receives each enqueued message. <see cref="IQueue"/> writes straight to the broker; if your
/// application needs at-least-once delivery semantics, use <see cref="IOutboxQueue"/> instead.
/// </para>
/// <para>
/// <see cref="EnqueueOptions.Delay"/> is ignored on this interface (direct enqueuers are
/// fire-and-forget). Use <see cref="IOutboxQueue"/> with <see cref="EnqueueOptions.Delay"/> set for
/// scheduled delivery.
/// </para>
/// <para>
/// At least one <see cref="IQueueTransport"/> must be registered in DI for direct queue publishing.
/// Consumer-side intent mismatches are caught at host startup; publisher-only mismatches surface
/// when the publisher is resolved.
/// </para>
/// </remarks>
[PublicAPI]
public interface IQueue
{
    /// <summary>
    /// Enqueues a message to the configured queue transport using the resolved destination.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="contentObj">The message payload. Can be <see langword="null"/>.</param>
    /// <param name="options">Optional enqueue overrides for destination, correlation, and custom headers.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the enqueue operation.</returns>
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
    Task EnqueueAsync<T>(T? contentObj, EnqueueOptions? options = null, CancellationToken cancellationToken = default);
}
