// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Transport;

// Transitional adapters that satisfy the new IBusTransport / IQueueTransport contracts by
// delegating to a single legacy ITransport implementation. Registered as a fallback in
// SetupMessaging so consumers can adopt the new abstractions before every provider migrates.
//
// REMOVE THESE TYPES — and their registrations in Setup.cs — once every transport provider
// (RabbitMQ, NATS, Azure Service Bus, AWS, Kafka, Redis*) ships native IBusTransport /
// IQueueTransport implementations per parent plan units U6-U9. Keeping the fallback after
// migration risks shadowing real provider-owned transports via DI ordering. The adapters do
// not pass DisposeAsync through to the wrapped ITransport: DI owns the ITransport singleton's
// lifetime (registered via Setup.cs), so adapter disposal must not double-dispose.
internal sealed class LegacyBusTransportAdapter(ITransport transport) : IBusTransport
{
    public BrokerAddress BrokerAddress => transport.BrokerAddress;

    public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default) =>
        transport.SendAsync(message, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class LegacyQueueTransportAdapter(ITransport transport) : IQueueTransport
{
    public BrokerAddress BrokerAddress => transport.BrokerAddress;

    public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default) =>
        transport.SendAsync(message, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
