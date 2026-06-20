// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.AzureServiceBus.Producer;

public interface IServiceBusProducerDescriptorFactory
{
    IServiceBusProducerDescriptor CreateProducerForMessage(TransportMessage transportMessage);
}
