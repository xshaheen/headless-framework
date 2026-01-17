// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Pulsar.Client.Api;

namespace Framework.Messages;

public interface IConnectionFactory
{
    string ServersAddress { get; }

    Task<IProducer<byte[]>> CreateProducerAsync(string topic);

    PulsarClient RentClient();
}
