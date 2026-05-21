// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Transport;

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
