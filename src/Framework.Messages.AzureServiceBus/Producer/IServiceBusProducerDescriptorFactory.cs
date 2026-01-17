// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Internal;
using Framework.Messages.Messages;

namespace Framework.Messages.Producer;

public interface IServiceBusProducerDescriptorFactory
{
    IServiceBusProducerDescriptor CreateProducerForMessage(TransportMessage transportMessage);
}
