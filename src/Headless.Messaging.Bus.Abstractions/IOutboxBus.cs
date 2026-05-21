// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Publishes broadcast (publish/subscribe) messages through the outbox pattern for reliable,
/// persisted delivery — with optional delivery delay.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IOutboxBus"/> contract is broadcast intent with durability: each published
/// message is first written to the outbox storage, then dispatched to the broker by the
/// background drainer once the host's transaction (if any) commits.
/// </para>
/// <para>
/// Set <see cref="PublishOptions.Delay"/> to defer dispatch until a future point in time; the
/// outbox row's <c>NotBefore</c> column is stamped accordingly and the drainer respects it.
/// </para>
/// <para>
/// At least one <see cref="IBusTransport"/> must be registered in DI for an application that
/// resolves <see cref="IOutboxBus"/>. Misconfiguration is caught at host startup, not at first call.
/// </para>
/// </remarks>
[PublicAPI]
public interface IOutboxBus
{
    /// <summary>
    /// Persists a message through the outbox so a background drainer dispatches it via a
    /// <see cref="IBusTransport"/> implementation.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="contentObj">The message payload. Can be <see langword="null"/>.</param>
    /// <param name="options">Optional publish overrides for topic, correlation, custom headers, and dispatch delay.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the persistence operation.</returns>
    Task PublishAsync<T>(T? contentObj, PublishOptions? options = null, CancellationToken cancellationToken = default);
}
