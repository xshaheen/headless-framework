// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

internal sealed class Queue : IQueue
{
    private static readonly IMessageCapabilityGate _DirectConstructionCapabilities = MessagingCapabilityModel.Compose([
        MessagingProviderCapabilities.Transport("Direct", [MessageLane.Queue], supportsIndependentLaneTopology: true),
    ]);

    private readonly Func<IQueueTransport> _transportResolver;
    private readonly ISerializer _serializer;
    private readonly IMessagePublishRequestFactory _publishRequestFactory;
    private readonly IPublishMiddlewarePipeline _publishPipeline;
    private readonly TimeProvider _timeProvider;
    private readonly IMessageCapabilityGate _capabilities;
    private readonly MessagingTelemetry _telemetry;

    internal Queue(
        ISerializer serializer,
        IServiceProvider serviceProvider,
        IMessagePublishRequestFactory publishRequestFactory,
        IPublishMiddlewarePipeline publishPipeline,
        TimeProvider timeProvider,
        IMessageCapabilityGate capabilities,
        MessagingTelemetry? telemetry = null
    )
        : this(
            serializer,
            () => serviceProvider.GetRequiredService<IQueueTransport>(),
            publishRequestFactory,
            publishPipeline,
            timeProvider,
            capabilities,
            telemetry
        ) { }

    internal Queue(
        ISerializer serializer,
        IQueueTransport transport,
        IMessagePublishRequestFactory publishRequestFactory,
        IPublishMiddlewarePipeline publishPipeline,
        TimeProvider timeProvider,
        MessagingTelemetry? telemetry = null
    )
        : this(
            serializer,
            () => transport,
            publishRequestFactory,
            publishPipeline,
            timeProvider,
            _DirectConstructionCapabilities,
            telemetry
        ) { }

    private Queue(
        ISerializer serializer,
        Func<IQueueTransport> transportResolver,
        IMessagePublishRequestFactory publishRequestFactory,
        IPublishMiddlewarePipeline publishPipeline,
        TimeProvider timeProvider,
        IMessageCapabilityGate capabilities,
        MessagingTelemetry? telemetry
    )
    {
        _serializer = serializer;
        _transportResolver = transportResolver;
        _publishRequestFactory = publishRequestFactory;
        _publishPipeline = publishPipeline;
        _timeProvider = timeProvider;
        _capabilities = capabilities;
        _telemetry = telemetry ?? MessagingTelemetry.Default;
    }

    public Task EnqueueAsync<T>(
        T? contentObj,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        _capabilities.EnsureDirectSupported(MessageLane.Queue);
        var transport = _transportResolver();
        var declaredMessageType = options?.MessageType ?? typeof(T);

        return _publishPipeline.ExecuteAsync(
            contentObj,
            IntentType.Queue,
            // Delay on EnqueueOptions is ignored by the direct publisher; delayTime=null ensures
            // no scheduling side-effect regardless of what the caller set.
            options,
            delayTime: null,
            innerPublish: (middlewareOptions, _, ct) =>
            {
                var publishRequest = _publishRequestFactory.Create(
                    contentObj,
                    declaredMessageType,
                    middlewareOptions,
                    intentType: IntentType.Queue
                );
                return DirectPublisherCore.SendAsync(
                    publishRequest.Message,
                    publishRequest.IntentType,
                    _serializer,
                    transport.BrokerAddress,
                    transport.SendAsync,
                    _NowUnixTimeMilliseconds,
                    _telemetry,
                    ct
                );
            },
            isTransactional: false,
            cancellationToken
        );
    }

    private long _NowUnixTimeMilliseconds()
    {
        return _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
    }
}
