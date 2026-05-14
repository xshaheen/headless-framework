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
    private readonly RetryPolicyOptions _retryPolicy = serviceProvider
        .GetRequiredService<IOptions<MessagingOptions>>()
        .Value.RetryPolicy;

    public async Task<OperateResult> SendAsync(MediumMessage message)
    {
        OperateResult result;
        var inlineRetries = 0;
        do
        {
            var (retryDecision, operateResult) = await _SendWithoutRetryAsync(message, inlineRetries)
                .ConfigureAwait(false);
            result = operateResult;
            if (result.Equals(OperateResult.Success))
            {
                return result;
            }

            if (retryDecision.Outcome != RetryDecision.Kind.Continue)
            {
                return result;
            }

            inlineRetries++;
            if (inlineRetries <= _retryPolicy.MaxInlineRetries)
            {
                await Task.Delay(retryDecision.Delay).ConfigureAwait(false);
                continue;
            }

            return result;
        } while (true);
    }

    private async Task<(RetryDecision, OperateResult)> _SendWithoutRetryAsync(MediumMessage message, int inlineRetries)
    {
        var transportMsg = await _serializer.SerializeToTransportMessageAsync(message.Origin).ConfigureAwait(false);

        var tracingTimestamp = _TracingBefore(transportMsg, _transport.BrokerAddress);

        // Note: Outbox sender doesn't propagate user cancellation; messages should be delivered
        var result = await _transport.SendAsync(transportMsg, CancellationToken.None).ConfigureAwait(false);

        if (result.Succeeded)
        {
            await _SetSuccessfulState(message).ConfigureAwait(false);

            _TracingAfter(tracingTimestamp, transportMsg, _transport.BrokerAddress);

            return (RetryDecision.Stop, OperateResult.Success);
        }

        _TracingError(tracingTimestamp, transportMsg, _transport.BrokerAddress, result);

        var needRetry = await _SetFailedState(message, result.Exception!, inlineRetries).ConfigureAwait(false);

        return (needRetry, OperateResult.Failed(result.Exception!));
    }

    private async Task _SetSuccessfulState(MediumMessage message)
    {
        message.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(_options.Value.SucceedMessageExpiredAfter);
        await _dataStorage.ChangePublishStateAsync(message, StatusName.Succeeded).ConfigureAwait(false);
    }

    private async Task<RetryDecision> _SetFailedState(MediumMessage message, Exception ex, int inlineRetries)
    {
        var needRetry = _UpdateMessageForRetry(message, ex);

        message.Origin.AddOrUpdateException(ex);
        message.ExpiresAt = message.Added.AddSeconds(_options.Value.FailedMessageExpiredAfter);

        var nextRetryAt =
            needRetry.Outcome == RetryDecision.Kind.Continue && inlineRetries + 1 > _retryPolicy.MaxInlineRetries
                ? _timeProvider.GetUtcNow().UtcDateTime.Add(needRetry.Delay)
                : (DateTime?)null;

        await _dataStorage
            .ChangePublishStateAsync(message, StatusName.Failed, nextRetryAt: nextRetryAt)
            .ConfigureAwait(false);

        if (needRetry.Outcome == RetryDecision.Kind.Exhausted)
        {
            _InvokeOnExhausted(message, ex);
        }

        return needRetry;
    }

    private RetryDecision _UpdateMessageForRetry(MediumMessage message, Exception ex)
    {
        var decision = RetryHelper.ComputeRetryDecision(message, ex, _retryPolicy, isCancellation: false);
        switch (decision.Outcome)
        {
            case RetryDecision.Kind.Stop:
                _logger.LogWarning(
                    "Stored message {StorageId} failed with non-retryable exception: {ExceptionType}. Skipping retries.",
                    message.StorageId,
                    ex.GetType().Name
                );
                break;
            case RetryDecision.Kind.Exhausted:
                _logger.SenderStoredMessageAfterThreshold(message.StorageId, _retryPolicy.MaxAttempts);
                break;
            case RetryDecision.Kind.Continue:
                _logger.SenderRetrying(message.StorageId, message.Retries);
                break;
        }

        return decision;
    }

    private void _InvokeOnExhausted(MediumMessage message, Exception ex)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            _retryPolicy.OnExhausted?.Invoke(
                new FailedInfo
                {
                    ServiceProvider = scope.ServiceProvider,
                    MessageType = MessageType.Publish,
                    Message = message.Origin,
                    Exception = ex,
                }
            );
        }
        catch (Exception callbackEx)
        {
            _logger.ExecutedThresholdCallbackFailed(callbackEx, callbackEx.Message);
        }
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

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublish, eventData);
        }
    }

    #endregion
}
