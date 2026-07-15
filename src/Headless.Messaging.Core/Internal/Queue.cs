// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Serialization;

namespace Headless.Messaging.Internal;

internal sealed class Queue(
    ISerializer serializer,
    IQueueTransport transport,
    IMessagePublishRequestFactory publishRequestFactory,
    IPublishMiddlewarePipeline publishPipeline,
    TimeProvider timeProvider
) : IQueue
{
    public Task EnqueueAsync<T>(
        T? contentObj,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return publishPipeline.ExecuteAsync(
            contentObj,
            IntentType.Queue,
            // Delay on EnqueueOptions is ignored by the direct publisher; delayTime=null ensures
            // no scheduling side-effect regardless of what the caller set.
            options,
            delayTime: null,
            innerPublish: (middlewareOptions, _, ct) =>
            {
                var publishRequest = publishRequestFactory.Create(
                    contentObj,
                    middlewareOptions,
                    intentType: IntentType.Queue
                );
                return DirectPublisherCore.SendAsync(
                    publishRequest.Message,
                    publishRequest.IntentType,
                    serializer,
                    transport.BrokerAddress,
                    transport.SendAsync,
                    _NowUnixTimeMilliseconds,
                    ct
                );
            },
            isTransactional: false,
            cancellationToken
        );
    }

    private long _NowUnixTimeMilliseconds()
    {
        return timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
    }
}
