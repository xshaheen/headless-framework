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
    IPublishExecutionPipeline publishPipeline
) : IDirectPublisher
{
    private static readonly DiagnosticListener _DiagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    private readonly ISerializer _serializer = serializer;
    private readonly ITransport _transport = transport;
    private readonly IMessagePublishRequestFactory _publishRequestFactory = publishRequestFactory;
    private readonly IPublishExecutionPipeline _publishPipeline = publishPipeline;

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
            innerPublish: (filteredOptions, _, ct) =>
            {
                var publishRequest = _publishRequestFactory.Create(contentObj, filteredOptions);
                return _SendAsync(publishRequest.Message, ct);
            },
            cancellationToken
        );
    }

    private async Task _SendAsync(Message message, CancellationToken cancellationToken)
    {
        TransportMessage transportMsg;
        try
        {
            transportMsg = await _serializer.SerializeToTransportMessageAsync(message).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _TracingErrorSerialization(message, e);
            throw;
        }

        long? tracingTimestamp = null;
        try
        {
            tracingTimestamp = _TracingBeforeSend(transportMsg);

            var result = await _transport.SendAsync(transportMsg, cancellationToken).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                _TracingErrorSend(tracingTimestamp, transportMsg, result);
                throw new PublisherSentFailedException(result.ToString(), result.Exception);
            }

            _TracingAfterSend(tracingTimestamp, transportMsg);
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
                _TracingErrorSend(tracingTimestamp, transportMsg, e);
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

    private long? _TracingBeforeSend(TransportMessage message)
    {
        MessageEventCounterSource.Log.WritePublishMetrics();

        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforePublish))
        {
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                BrokerAddress = _transport.BrokerAddress,
                TransportMessage = message,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforePublish, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private void _TracingAfterSend(long? tracingTimestamp, TransportMessage message)
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterPublish))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = _transport.BrokerAddress,
                TransportMessage = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterPublish, eventData);
        }
    }

    private void _TracingErrorSend(long? tracingTimestamp, TransportMessage message, OperateResult result)
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublish))
        {
            var ex = new PublisherSentFailedException(result.ToString(), result.Exception);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = _transport.BrokerAddress,
                TransportMessage = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublish, eventData);
        }
    }

    private void _TracingErrorSend(long? tracingTimestamp, TransportMessage message, Exception exception)
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublish))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = _transport.BrokerAddress,
                TransportMessage = message,
                ElapsedTimeMs = tracingTimestamp.HasValue ? now - tracingTimestamp.Value : null,
                Exception = exception,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublish, eventData);
        }
    }

    private static void _TracingErrorSerialization(Message message, Exception exception)
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublishMessageStore))
        {
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                Message = message,
                Exception = exception,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublishMessageStore, eventData);
        }
    }

    #endregion
}
