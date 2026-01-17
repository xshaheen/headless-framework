// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;

namespace Framework.Messages;

public interface IConnectionPool
{
    string ServersAddress { get; }

    IProducer<string, byte[]> RentProducer();

    bool Return(IProducer<string, byte[]> producer);
}
