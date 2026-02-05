// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

public interface IMessageSender
{
    Task<OperateResult> SendAsync(MediumMessage message);
}

internal sealed class MessageSender(ILogger<MessageSender> logger, IServiceProvider serviceProvider) : IMessageSender
{
    // ReSharper disable once InconsistentNaming
    private static readonly DiagnosticListener _DiagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    private readonly IDataStorage _dataStorage = serviceProvider.GetRequiredService<IDataStorage>();
    private readonly ILogger _logger = logger;
    private readonly IOptions<MessagingOptions> _options = serviceProvider.GetRequiredService<
        IOptions<MessagingOptions>
    >();
    private readonly ISerializer _serializer = serviceProvider.GetRequiredService<ISerializer>();
    private readonly ITransport _transport = serviceProvider.GetRequiredService<ITransport>();
    private readonly TimeProvider _timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
    private readonly IRetryBackoffStrategy _backoffStrategy = serviceProvider
        .GetRequiredService<IOptions<MessagingOptions>>()
        .Value.RetryBackoffStrategy;

    public async Task<OperateResult> SendAsync(MediumMessage message)
    {
        bool retry;
        OperateResult result;
        do
        {
            (retry, result) = await _SendWithoutRetryAsync(message).ConfigureAwait(false);
            if (result.Equals(OperateResult.Success))
            {
                return result;
            }
        } while (retry);

        return result;
    }

    private async Task<(bool, OperateResult)> _SendWithoutRetryAsync(MediumMessage message)
    {
        var transportMsg = await _serializer.SerializeToTransportMessageAsync(message.Origin).ConfigureAwait(false);

        var tracingTimestamp = _TracingBefore(transportMsg, _transport.BrokerAddress);

        // Note: Outbox sender doesn't propagate user cancellation; messages should be delivered
        var result = await _transport.SendAsync(transportMsg, CancellationToken.None).ConfigureAwait(false);

        if (result.Succeeded)
        {
            await _SetSuccessfulState(message).ConfigureAwait(false);

            _TracingAfter(tracingTimestamp, transportMsg, _transport.BrokerAddress);

            return (false, OperateResult.Success);
        }

        _TracingError(tracingTimestamp, transportMsg, _transport.BrokerAddress, result);

        var needRetry = await _SetFailedState(message, result.Exception!).ConfigureAwait(false);

        return (needRetry, OperateResult.Failed(result.Exception!));
    }

    private async Task _SetSuccessfulState(MediumMessage message)
    {
        message.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(_options.Value.SucceedMessageExpiredAfter);
        await _dataStorage.ChangePublishStateAsync(message, StatusName.Succeeded).ConfigureAwait(false);
    }

    private async Task<bool> _SetFailedState(MediumMessage message, Exception ex)
    {
        var needRetry = _UpdateMessageForRetry(message, ex);

        message.Origin.AddOrUpdateException(ex);
        message.ExpiresAt = message.Added.AddSeconds(_options.Value.FailedMessageExpiredAfter);

        await _dataStorage.ChangePublishStateAsync(message, StatusName.Failed).ConfigureAwait(false);

        return needRetry;
    }

    private bool _UpdateMessageForRetry(MediumMessage message, Exception ex)
    {
        // Check if exception is retryable
        if (!_backoffStrategy.ShouldRetry(ex))
        {
            message.Retries = _options.Value.FailedRetryCount; // Mark as exhausted
            _logger.LogWarning(
                "Message {MessageId} failed with non-retryable exception: {ExceptionType}. Skipping retries.",
                message.DbId,
                ex.GetType().Name
            );
            return false;
        }

        var retries = ++message.Retries;
        var retryCount = Math.Min(_options.Value.FailedRetryCount, 3);
        if (retries >= retryCount)
        {
            if (retries == _options.Value.FailedRetryCount)
            {
                try
                {
                    _options.Value.FailedThresholdCallback?.Invoke(
                        new FailedInfo
                        {
                            ServiceProvider = serviceProvider,
                            MessageType = MessageType.Publish,
                            Message = message.Origin,
                        }
                    );

                    _logger.SenderAfterThreshold(message.DbId, _options.Value.FailedRetryCount);
                }
                catch (Exception callbackEx)
                {
                    _logger.ExecutedThresholdCallbackFailed(callbackEx);
                }
            }

            return false;
        }

        _logger.SenderRetrying(message.DbId, retries);

        return true;
    }

    #region tracing

    private static long? _TracingBefore(TransportMessage message, BrokerAddress broker)
    {
        MessageEventCounterSource.Log.WritePublishMetrics();

        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforePublish))
        {
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforePublish, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private static void _TracingAfter(long? tracingTimestamp, TransportMessage message, BrokerAddress broker)
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterPublish))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterPublish, eventData);
        }
    }

    private static void _TracingError(
        long? tracingTimestamp,
        TransportMessage message,
        BrokerAddress broker,
        OperateResult result
    )
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublish))
        {
            var ex = new Headless.Messaging.PublisherSentFailedException(result.ToString(), result.Exception);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublish, eventData);
        }
    }

    #endregion
}
