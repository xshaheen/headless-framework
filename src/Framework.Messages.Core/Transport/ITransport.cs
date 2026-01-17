// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Internal;
using Framework.Messages.Messages;

namespace Framework.Messages.Transport;

public interface ITransport
{
    BrokerAddress BrokerAddress { get; }

    Task<OperateResult> SendAsync(TransportMessage message);
}
