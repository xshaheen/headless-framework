// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

internal sealed class Bus : IBus
{
    private static readonly IMessageCapabilityGate _DirectConstructionCapabilities = MessagingCapabilityModel.Compose([
        MessagingProviderCapabilities.Transport("Direct", [MessageLane.Bus], supportsIndependentLaneTopology: true),
    ]);

    private readonly Func<IBusTransport> _transportResolver;
    private readonly ISerializer _serializer;
    private readonly IMessagePublishRequestFactory _publishRequestFactory;
    private readonly IPublishMiddlewarePipeline _publishPipeline;
    private readonly TimeProvider _timeProvider;
    private readonly IMessageCapabilityGate _capabilities;
    private readonly MessagingTelemetry _telemetry;

    internal Bus(
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
            () => serviceProvider.GetRequiredService<IBusTransport>(),
            publishRequestFactory,
            publishPipeline,
            timeProvider,
            capabilities,
            telemetry
        ) { }

    internal Bus(
        ISerializer serializer,
        IBusTransport transport,
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

    private Bus(
        ISerializer serializer,
        Func<IBusTransport> transportResolver,
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

    public Task PublishAsync<T>(
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        _capabilities.EnsureDirectSupported(MessageLane.Bus);
        var transport = _transportResolver();
        var declaredMessageType = options?.MessageType ?? typeof(T);

        return _publishPipeline.ExecuteAsync(
            contentObj,
            IntentType.Bus,
            // Delay on PublishOptions is ignored by the direct publisher; the pipeline receives
            // delayTime=null, so no delay is scheduled regardless of what the caller set.
            options,
            delayTime: null,
            innerPublish: (middlewareOptions, _, ct) =>
            {
                var publishRequest = _publishRequestFactory.Create(
                    contentObj,
                    declaredMessageType,
                    middlewareOptions,
                    intentType: IntentType.Bus
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
