// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Enqueues point-to-point (work-queue) messages through the configured delivery mode.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IQueue"/> contract is point-to-point intent: exactly one competing worker
/// receives each enqueued message. The <c>DeliveryMode</c> on <see cref="EnqueueOptions"/> selects automatic,
/// durable, or transport-direct delivery without changing the Queue lane.
/// </para>
/// <para>
/// Delayed delivery is durable and cannot be combined with <c>TransportDirect</c> delivery.
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
    /// <param name="options">Optional enqueue overrides for delivery, destination, correlation, delay, and custom headers.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the enqueue operation.</returns>
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
    Task EnqueueAsync<T>(T? contentObj, EnqueueOptions? options = null, CancellationToken cancellationToken = default);
}
