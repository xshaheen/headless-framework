// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;

namespace Framework.Messages.Transport;

public interface IDispatcher : IProcessingServer
{
    ValueTask EnqueueToPublish(MediumMessage message);

    ValueTask EnqueueToExecute(MediumMessage message, ConsumerExecutorDescriptor? descriptor = null);

    Task EnqueueToScheduler(MediumMessage message, DateTime publishTime, object? transaction = null);
}
