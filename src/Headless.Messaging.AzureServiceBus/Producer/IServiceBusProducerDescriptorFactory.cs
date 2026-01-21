// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.AzureServiceBus.Producer;

public interface IServiceBusProducerDescriptorFactory
{
    IServiceBusProducerDescriptor CreateProducerForMessage(TransportMessage transportMessage);
}
