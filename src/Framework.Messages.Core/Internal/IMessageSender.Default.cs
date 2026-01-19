// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Framework.Messages.Configuration;
using Framework.Messages.Diagnostics;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;
using Framework.Messages.Persistence;
using Framework.Messages.Serialization;
using Framework.Messages.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages.Internal;

internal class MessageSender(ILogger<MessageSender> logger, IServiceProvider serviceProvider) : IMessageSender
{
    // ReSharper disable once InconsistentNaming
    protected static readonly DiagnosticListener s_diagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    private readonly IDataStorage _dataStorage = serviceProvider.GetRequiredService<IDataStorage>();
    private readonly ILogger _logger = logger;
    private readonly IOptions<MessagingOptions> _options = serviceProvider.GetRequiredService<IOptions<MessagingOptions>>();
    private readonly ISerializer _serializer = serviceProvider.GetRequiredService<ISerializer>();
    private readonly ITransport _transport = serviceProvider.GetRequiredService<ITransport>();
    private readonly TimeProvider _timeProvider = serviceProvider.GetRequiredService<TimeProvider>();

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

        var result = await _transport.SendAsync(transportMsg).ConfigureAwait(false);

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
        var needRetry = _UpdateMessageForRetry(message);

        message.Origin.AddOrUpdateException(ex);
        message.ExpiresAt = message.Added.AddSeconds(_options.Value.FailedMessageExpiredAfter);

        await _dataStorage.ChangePublishStateAsync(message, StatusName.Failed).ConfigureAwait(false);

        return needRetry;
    }

    private bool _UpdateMessageForRetry(MediumMessage message)
    {
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
                catch (Exception ex)
                {
                    _logger.ExecutedThresholdCallbackFailed(ex);
                }
            }

            return false;
        }

        _logger.SenderRetrying(message.DbId, retries);

        return true;
    }

    #region tracing

    private long? _TracingBefore(TransportMessage message, BrokerAddress broker)
    {
        MessageEventCounterSource.Log.WritePublishMetrics();

        if (s_diagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforePublish))
        {
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
            };

            s_diagnosticListener.Write(MessageDiagnosticListenerNames.BeforePublish, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private void _TracingAfter(long? tracingTimestamp, TransportMessage message, BrokerAddress broker)
    {
        if (tracingTimestamp != null && s_diagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterPublish))
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

            s_diagnosticListener.Write(MessageDiagnosticListenerNames.AfterPublish, eventData);
        }
    }

    private void _TracingError(
        long? tracingTimestamp,
        TransportMessage message,
        BrokerAddress broker,
        OperateResult result
    )
    {
        if (tracingTimestamp != null && s_diagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublish))
        {
            var ex = new PublisherSentFailedException(result.ToString(), result.Exception);
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

            s_diagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublish, eventData);
        }
    }

    #endregion
}
