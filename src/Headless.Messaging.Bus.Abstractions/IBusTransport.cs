// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Transport;

namespace Headless.Messaging;

/// <summary>
/// Broker-side transport that dispatches messages with broadcast (publish/subscribe) semantics.
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="IBusTransport"/> implementation maps the framework-side broadcast intent to a
/// broker-native broadcast primitive — RabbitMQ fanout exchange, NATS Core subject pub/sub,
/// Azure Service Bus messageName, AWS SNS, Pulsar messageName with independent subscriptions, etc. Providers
/// that cannot natively broadcast (for example, Kafka, Redis Streams) do not implement this
/// interface, and their NuGet packages do not reference <c>Headless.Messaging.Bus.Abstractions</c>.
/// </para>
/// <para>
/// Capability is therefore declared at the package boundary: if your application registers
/// <see cref="IBus"/> or <see cref="IOutboxBus"/>, the host must also register at least one provider
/// that ships an <see cref="IBusTransport"/>. Misconfigurations are caught at host startup.
/// </para>
/// </remarks>
[PublicAPI]
public interface IBusTransport : IAsyncDisposable
{
    /// <summary>
    /// Gets the broker address information.
    /// </summary>
    BrokerAddress BrokerAddress { get; }

    /// <summary>
    /// Sends a transport message asynchronously with broadcast (publish/subscribe) semantics.
    /// </summary>
    /// <param name="message">The transport message to send.</param>
    /// <param name="cancellationToken">Token to cancel the send operation.</param>
    /// <returns>A task that returns the operation result.</returns>
    Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default);
}
