// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;

namespace Headless.Messaging.Internal;

/// <summary>
/// Shared send-and-trace kernel for direct publishers (<see cref="Bus"/> and <see cref="Queue"/>).
/// Handles serialization, transport send, and the three diagnostic listener events
/// (BeforePublish, AfterPublish, ErrorPublish / ErrorPublishMessageStore) so neither publisher
/// duplicates the pattern.
/// </summary>
internal static class DirectPublisherCore
{
    private static readonly DiagnosticListener _DiagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    internal static async Task SendAsync(
        Message message,
        IntentType intentType,
        ISerializer serializer,
        BrokerAddress brokerAddress,
        Func<TransportMessage, CancellationToken, Task<OperateResult>> sendTransport,
        Func<long> nowMs,
        CancellationToken cancellationToken
    )
    {
        TransportMessage transportMsg;
        try
        {
            transportMsg = await serializer.SerializeToTransportMessageAsync(message).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _TracingErrorSerialization(message, intentType, e, nowMs, cancellationToken);
            throw;
        }

        long? tracingTimestamp = null;
        try
        {
            tracingTimestamp = _TracingBeforeSend(transportMsg, intentType, brokerAddress, nowMs, cancellationToken);

            var result = await sendTransport(transportMsg, cancellationToken).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                _TracingErrorSend(
                    tracingTimestamp,
                    transportMsg,
                    intentType,
                    brokerAddress,
                    result,
                    nowMs,
                    cancellationToken
                );
                throw new PublisherSentFailedException(result.ToString(), result.Exception);
            }

            _TracingAfterSend(tracingTimestamp, transportMsg, intentType, brokerAddress, nowMs, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (e is not PublisherSentFailedException)
        {
            try
            {
                _TracingErrorSend(
                    tracingTimestamp,
                    transportMsg,
                    intentType,
                    brokerAddress,
                    e,
                    nowMs,
                    cancellationToken
                );
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

    private static long? _TracingBeforeSend(
        TransportMessage message,
        IntentType intentType,
        BrokerAddress brokerAddress,
        Func<long> nowMs,
        CancellationToken cancellationToken
    )
    {
        MessageEventCounterSource.Log.WritePublishMetrics();

        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforePublish))
        {
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = nowMs(),
                Operation = message.Name,
                BrokerAddress = brokerAddress,
                TransportMessage = message,
                IntentType = intentType,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforePublish, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private static void _TracingAfterSend(
        long? tracingTimestamp,
        TransportMessage message,
        IntentType intentType,
        BrokerAddress brokerAddress,
        Func<long> nowMs,
        CancellationToken cancellationToken
    )
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterPublish))
        {
            var now = nowMs();
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.Name,
                BrokerAddress = brokerAddress,
                TransportMessage = message,
                IntentType = intentType,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterPublish, eventData);
        }
    }

    private static void _TracingErrorSend(
        long? tracingTimestamp,
        TransportMessage message,
        IntentType intentType,
        BrokerAddress brokerAddress,
        OperateResult result,
        Func<long> nowMs,
        CancellationToken cancellationToken
    )
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublish))
        {
            var ex = new PublisherSentFailedException(result.ToString(), result.Exception);
            var now = nowMs();

            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.Name,
                BrokerAddress = brokerAddress,
                TransportMessage = message,
                IntentType = intentType,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublish, eventData);
        }
    }

    private static void _TracingErrorSend(
        long? tracingTimestamp,
        TransportMessage message,
        IntentType intentType,
        BrokerAddress brokerAddress,
        Exception exception,
        Func<long> nowMs,
        CancellationToken cancellationToken
    )
    {
        // Guard on IsEnabled regardless of tracingTimestamp: if the listener is interested in
        // errors, we emit even when BeforePublish was not active (e.g., listener enabled mid-flight).
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublish))
        {
            var now = nowMs();

            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.Name,
                BrokerAddress = brokerAddress,
                TransportMessage = message,
                IntentType = intentType,
                ElapsedTimeMs = tracingTimestamp.HasValue ? now - tracingTimestamp.Value : null,
                Exception = exception,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublish, eventData);
        }
    }

    private static void _TracingErrorSerialization(
        Message message,
        IntentType intentType,
        Exception exception,
        Func<long> nowMs,
        CancellationToken cancellationToken
    )
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublishMessageStore))
        {
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = nowMs(),
                Operation = message.Name,
                Message = message,
                IntentType = intentType,
                Exception = exception,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublishMessageStore, eventData);
        }
    }
}
