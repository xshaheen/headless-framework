// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
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
    private static readonly DiagnosticListener _DiagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    public Task PublishAsync<T>(
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return publishPipeline.ExecuteAsync(
            contentObj,
            options,
            delayTime: null,
            innerPublish: (middlewareOptions, _, ct) =>
            {
                var publishRequest = publishRequestFactory.Create(contentObj, middlewareOptions);
                return _SendAsync(publishRequest.Message, ct);
            },
            isTransactional: false,
            cancellationToken
        );
    }

    private async Task _SendAsync(Message message, CancellationToken cancellationToken)
    {
        TransportMessage transportMsg;
        try
        {
            transportMsg = await serializer.SerializeToTransportMessageAsync(message).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _TracingErrorSerialization(message, e, cancellationToken);
            throw;
        }

        long? tracingTimestamp = null;
        try
        {
            tracingTimestamp = _TracingBeforeSend(transportMsg, cancellationToken);

            var result = await transport.SendAsync(transportMsg, cancellationToken).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                _TracingErrorSend(tracingTimestamp, transportMsg, result, cancellationToken);
                throw new PublisherSentFailedException(result.ToString(), result.Exception);
            }

            _TracingAfterSend(tracingTimestamp, transportMsg, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (e is not PublisherSentFailedException)
        {
            try
            {
                _TracingErrorSend(tracingTimestamp, transportMsg, e, cancellationToken);
            }
#pragma warning disable ERP022 // Intentional: tracing failure should not mask the original exception
            catch
            {
                // Tracing failure should not mask the original exception.
            }
#pragma warning restore ERP022

            throw;
        }
    }

    private long? _TracingBeforeSend(TransportMessage message, CancellationToken cancellationToken)
    {
        MessageEventCounterSource.Log.WritePublishMetrics();

        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforePublish))
        {
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = _NowUnixTimeMilliseconds(),
                Operation = message.GetName(),
                BrokerAddress = transport.BrokerAddress,
                TransportMessage = message,
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
                BrokerAddress = transport.BrokerAddress,
                TransportMessage = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterPublish, eventData);
        }
    }

    private void _TracingErrorSend(
        long? tracingTimestamp,
        TransportMessage message,
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
                BrokerAddress = transport.BrokerAddress,
                TransportMessage = message,
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
                BrokerAddress = transport.BrokerAddress,
                TransportMessage = message,
                ElapsedTimeMs = tracingTimestamp.HasValue ? now - tracingTimestamp.Value : null,
                Exception = exception,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublish, eventData);
        }
    }

    private void _TracingErrorSerialization(Message message, Exception exception, CancellationToken cancellationToken)
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublishMessageStore))
        {
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = _NowUnixTimeMilliseconds(),
                Operation = message.GetName(),
                Message = message,
                Exception = exception,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublishMessageStore, eventData);
        }
    }

    private long _NowUnixTimeMilliseconds() => timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
}
