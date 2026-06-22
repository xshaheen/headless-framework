// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Serialization;

namespace Headless.Messaging.Internal;

internal sealed class Bus(
    ISerializer serializer,
    IBusTransport transport,
    IMessagePublishRequestFactory publishRequestFactory,
    IPublishMiddlewarePipeline publishPipeline,
    TimeProvider timeProvider
) : IBus
{
    public Task PublishAsync<T>(
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return publishPipeline.ExecuteAsync(
            contentObj,
            IntentType.Bus,
            // Delay on PublishOptions is ignored by the direct publisher; the pipeline receives
            // delayTime=null, so no delay is scheduled regardless of what the caller set.
            options,
            delayTime: null,
            innerPublish: (middlewareOptions, _, ct) =>
            {
                var publishRequest = publishRequestFactory.Create(
                    contentObj,
                    middlewareOptions,
                    intentType: IntentType.Bus
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

    private long _NowUnixTimeMilliseconds() => timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
}
