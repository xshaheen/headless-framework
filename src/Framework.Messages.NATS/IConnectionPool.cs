// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NATS.Client;

namespace Framework.Messages;

public interface IConnectionPool
{
    string ServersAddress { get; }

    IConnection RentConnection();

    bool Return(IConnection connection);
}
