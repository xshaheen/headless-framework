// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Internal;

internal sealed class DirectPublisher(
    ISerializer serializer,
    ITransport transport,
    IMessagePublishRequestFactory publishRequestFactory,
    IPublishMiddlewarePipeline publishPipeline,
    TimeProvider timeProvider
) : IDirectPublisher
{
    private static readonly DiagnosticListener _DiagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    private readonly ISerializer _serializer = serializer;
    private readonly ITransport _transport = transport;
    private readonly IMessagePublishRequestFactory _publishRequestFactory = publishRequestFactory;
    private readonly IPublishMiddlewarePipeline _publishPipeline = publishPipeline;

    public Task PublishAsync<T>(
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _publishPipeline.ExecuteAsync(
            contentObj,
            options,
            // DelayTime is undefined for the immediate publish path; ignored.
            delayTime: null,
            innerPublish: (middlewareOptions, _, ct) =>
            {
                var publishRequest = _publishRequestFactory.Create(contentObj, middlewareOptions);
                return _SendAsync(publishRequest.Message, publishRequest.IntentType, ct);
            },
            // DirectPublisher always commits to the wire inside the pipeline; PublishingContext.IsTransactional
            // remains false because rollback semantics do not apply.
            isTransactional: false,
            cancellationToken
        );
    }

    private async Task _SendAsync(Message message, IntentType intentType, CancellationToken cancellationToken)
    {
        TransportMessage transportMsg;
        try
        {
            transportMsg = await _serializer.SerializeToTransportMessageAsync(message).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _TracingErrorSerialization(message, intentType, e, cancellationToken);
            throw;
        }

        long? tracingTimestamp = null;
        try
        {
            tracingTimestamp = _TracingBeforeSend(transportMsg, intentType, cancellationToken);

            var result = await _transport.SendAsync(transportMsg, cancellationToken).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                _TracingErrorSend(tracingTimestamp, transportMsg, intentType, result, cancellationToken);
                throw new PublisherSentFailedException(result.ToString(), result.Exception);
            }

            _TracingAfterSend(tracingTimestamp, transportMsg, intentType, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected behavior, not an error - let it propagate without tracing
            throw;
        }
        catch (Exception e) when (e is not PublisherSentFailedException)
        {
            try
            {
                _TracingErrorSend(tracingTimestamp, transportMsg, intentType, e, cancellationToken);
            }
#pragma warning disable ERP022 // Intentional: tracing failure should not mask the original exception
            catch
            {
                // Tracing failure should not mask the original exception
            }
#pragma warning restore ERP022

            throw;
        }
    }

    #region Tracing

    private long? _TracingBeforeSend(TransportMessage message, IntentType intentType, CancellationToken cancellationToken)
    {
        MessageEventCounterSource.Log.WritePublishMetrics();

        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforePublish))
        {
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = _NowUnixTimeMilliseconds(),
                Operation = message.GetName(),
                BrokerAddress = _transport.BrokerAddress,
                TransportMessage = message,
                IntentType = intentType,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforePublish, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private void _TracingAfterSend(
        long? tracingTimestamp,
        TransportMessage message,
        IntentType intentType,
        CancellationToken cancellationToken
    )
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterPublish))
        {
            var now = _NowUnixTimeMilliseconds();
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = _transport.BrokerAddress,
                TransportMessage = message,
                IntentType = intentType,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterPublish, eventData);
        }
    }

    private void _TracingErrorSend(
        long? tracingTimestamp,
        TransportMessage message,
        IntentType intentType,
        OperateResult result,
        CancellationToken cancellationToken
    )
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublish))
        {
            var ex = new PublisherSentFailedException(result.ToString(), result.Exception);
            var now = _NowUnixTimeMilliseconds();

            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = _transport.BrokerAddress,
                TransportMessage = message,
                IntentType = intentType,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublish, eventData);
        }
    }

    private void _TracingErrorSend(
        long? tracingTimestamp,
        TransportMessage message,
        IntentType intentType,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublish))
        {
            var now = _NowUnixTimeMilliseconds();

            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = _transport.BrokerAddress,
                TransportMessage = message,
                IntentType = intentType,
                ElapsedTimeMs = tracingTimestamp.HasValue ? now - tracingTimestamp.Value : null,
                Exception = exception,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublish, eventData);
        }
    }

    private void _TracingErrorSerialization(
        Message message,
        IntentType intentType,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublishMessageStore))
        {
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = _NowUnixTimeMilliseconds(),
                Operation = message.GetName(),
                Message = message,
                IntentType = intentType,
                Exception = exception,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublishMessageStore, eventData);
        }
    }

    private long _NowUnixTimeMilliseconds() => timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    #endregion
}
