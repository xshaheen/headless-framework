// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Enqueues point-to-point (work-queue) messages through the outbox pattern for reliable,
/// persisted delivery — with optional delivery delay.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IOutboxQueue"/> contract is point-to-point intent with durability: each enqueued
/// message is first written to the outbox storage, then dispatched to the broker by the
/// background drainer once the host's transaction (if any) commits.
/// </para>
/// <para>
/// Set <see cref="EnqueueOptions.Delay"/> to defer dispatch until a future point in time; the
/// outbox row's <c>DelayTime</c> / <c>ExpiresAt</c> fields are stamped accordingly and the drainer respects them.
/// </para>
/// <para>
/// At least one <see cref="IQueueTransport"/> must be registered in DI before the drainer dispatches
/// queue-intent rows. Consumer-side intent mismatches are caught at host startup; publisher-only
/// mismatches surface when the publisher is resolved.
/// </para>
/// </remarks>
[PublicAPI]
public interface IOutboxQueue
{
    /// <summary>
    /// Persists a message through the outbox so a background drainer dispatches it via a
    /// <see cref="IQueueTransport"/> implementation.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="contentObj">The message payload. Can be <see langword="null"/>.</param>
    /// <param name="options">Optional enqueue overrides for destination, correlation, custom headers, and dispatch delay.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the persistence operation.</returns>
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
