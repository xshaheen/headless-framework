// Copyright (c) Mahmoud Shaheen. All rights reserved.

using RabbitMQ.Client;

namespace Framework.Messages.RabbitMQ;

public interface IConnectionChannelPool
{
    string HostAddress { get; }

    string Exchange { get; }

    IConnection GetConnection();

    Task<IChannel> Rent();

    bool Return(IChannel context);
}
