// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Transport;

public interface ITransport : IAsyncDisposable
{
    BrokerAddress BrokerAddress { get; }

    Task<OperateResult> SendAsync(TransportMessage message);
}
