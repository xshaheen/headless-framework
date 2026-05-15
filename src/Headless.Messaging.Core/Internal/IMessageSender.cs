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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal interface IMessageSender
{
    /// <summary>
    /// Publishes a single outbox message using the sender's root service provider for
    /// scoped service resolution. Prefer <see cref="SendAsync(MediumMessage, IServiceProvider)"/>
    /// to surface the live per-message dispatch scope to the exhausted callback.
    /// </summary>
    Task<OperateResult> SendAsync(MediumMessage message);

    /// <summary>
    /// Publishes a single outbox message and threads the caller's per-message DI scope through
    /// to the retry pipeline so that <c>OnExhausted</c>'s <c>FailedInfo.ServiceProvider</c>
    /// reflects the SAME scope used while sending. The caller (Dispatcher) owns this scope's
    /// lifetime.
    /// </summary>
    Task<OperateResult> SendAsync(MediumMessage message, IServiceProvider dispatchServices);
}

internal sealed class MessageSender : IMessageSender
{
    // ReSharper disable once InconsistentNaming
    private static readonly DiagnosticListener _DiagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    private readonly IServiceProvider _serviceProvider;
    private readonly IDataStorage _dataStorage;
    private readonly ILogger _logger;
    private readonly MessagingOptions _options;
    private readonly ISerializer _serializer;
    private readonly ITransport _transport;
    private readonly TimeProvider _timeProvider;
    private readonly RetryPolicyOptions _retryPolicy;
    private readonly CancellationToken _shutdownToken;

    public MessageSender(ILogger<MessageSender> logger, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _dataStorage = serviceProvider.GetRequiredService<IDataStorage>();
        _serializer = serviceProvider.GetRequiredService<ISerializer>();
        _transport = serviceProvider.GetRequiredService<ITransport>();
        _timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
        var opts = serviceProvider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        _options = opts;
        _retryPolicy = opts.RetryPolicy;

        // IHostApplicationLifetime is optional so MessageSender remains usable in test contexts
        // (and non-hosted scenarios). When absent, shutdown cancellation cannot be observed and
        // exceptions are treated as failures rather than cancellations — the pre-existing behavior.
        _shutdownToken =
            serviceProvider.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None;
    }

    public Task<OperateResult> SendAsync(MediumMessage message) => SendAsync(message, _serviceProvider);

    public Task<OperateResult> SendAsync(MediumMessage message, IServiceProvider dispatchServices)
    {
        Headless.Checks.Argument.IsNotNull(dispatchServices);

        // Outbox sender doesn't propagate user cancellation; messages should be delivered.
        // The inline-retry loop uses CancellationToken.None for Task.Delay as well so a
        // background shutdown does not partially-retry mid-delay.
        return InlineRetryLoop.ExecuteAsync(
            (inlineRetries, _) => _SendWithoutRetryAsync(message, dispatchServices, inlineRetries),
            _retryPolicy,
            CancellationToken.None
        );
    }

    private async Task<(RetryDecision Decision, OperateResult Result)> _SendWithoutRetryAsync(
        MediumMessage message,
        IServiceProvider dispatchServices,
        int inlineRetries
    )
    {
        var transportMsg = await _serializer.SerializeToTransportMessageAsync(message.Origin).ConfigureAwait(false);

        var tracingTimestamp = _TracingBefore(transportMsg, _transport.BrokerAddress);

        // Note: Outbox sender doesn't propagate user cancellation; messages should be delivered
        var result = await _transport.SendAsync(transportMsg, CancellationToken.None).ConfigureAwait(false);

        if (result.Succeeded)
        {
            await _SetSuccessfulState(message, CancellationToken.None).ConfigureAwait(false);

            _TracingAfter(tracingTimestamp, transportMsg, _transport.BrokerAddress);

            return (RetryDecision.Stop, OperateResult.Success);
        }

        _TracingError(tracingTimestamp, transportMsg, _transport.BrokerAddress, result);

        var decision = await _SetFailedState(message, result.Exception!, dispatchServices, inlineRetries)
            .ConfigureAwait(false);

        return (decision, OperateResult.Failed(result.Exception!));
    }

    private async Task _SetSuccessfulState(MediumMessage message, CancellationToken cancellationToken)
    {
        message.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(_options.SucceedMessageExpiredAfter);
        await _dataStorage
            .ChangePublishStateAsync(message, StatusName.Succeeded, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<RetryDecision> _SetFailedState(
        MediumMessage message,
        Exception ex,
        IServiceProvider dispatchServices,
        int inlineRetries
    )
    {
        // Host shutdown: an OCE bound to the host's stopping token means the dispatch was aborted,
        // not that the message failed. Returning without writing state preserves the row's existing
        // NextRetryAt/Status, and the persisted retry processor will pick the row up on restart.
        if (RetryHelper.IsCancellation(ex, _shutdownToken))
        {
            _logger.StoredMessageNonRetryableFailure(message.StorageId, "HostShutdown");
            return RetryDecision.Stop;
        }

        var decision = _UpdateMessageForRetry(message, ex, inlineRetries);

        message.Origin.AddOrUpdateException(ex);
        message.ExpiresAt = message.Added.AddSeconds(_options.FailedMessageExpiredAfter);

        // Inline-retry budget still available: persist as Scheduled/NULL so a crash mid-delay
        // leaves the row picked up by the polling query on restart (Failed/NULL is filtered out).
        // Only transition to Failed on terminal decisions (Stop, Exhausted) or when persisting
        // for the persisted-retry processor (Continue with inline budget exhausted, NextRetryAt set).
        var state = RetryHelper.ResolveNextState(decision, inlineRetries, _retryPolicy, _timeProvider);

        // Persist transition: inline budget consumed AND decision Continue means the call site
        // owns the Retries++ . The helper is pure with respect to MediumMessage; this is the only
        // place persisted-pickup count advances.
        if (decision.Outcome == RetryDecision.Kind.Continue && !state.IsInlineRetryInFlight)
        {
            message.Retries++;
        }

        var affected = await _dataStorage
            .ChangePublishStateAsync(
                message,
                state.NextStatus,
                nextRetryAt: state.NextRetryAt,
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);

        if (affected && decision.Outcome == RetryDecision.Kind.Exhausted)
        {
            await _InvokeOnExhausted(message, ex, dispatchServices, CancellationToken.None).ConfigureAwait(false);
        }
        else if (!affected)
        {
            _logger.SkippingOnExhaustedAlreadyTerminal(message.StorageId);
        }

        return decision;
    }

    private RetryDecision _UpdateMessageForRetry(MediumMessage message, Exception ex, int inlineRetries)
    {
        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            ex,
            _retryPolicy,
            inlineRetries,
            isCancellation: false
        );
        switch (decision.Outcome)
        {
            case RetryDecision.Kind.Stop:
                _logger.StoredMessageNonRetryableFailure(message.StorageId, ex.GetType().Name);
                break;
            case RetryDecision.Kind.Exhausted:
                _logger.SenderStoredMessageAfterThreshold(message.StorageId, _retryPolicy.MaxPersistedRetries);
                break;
            case RetryDecision.Kind.Continue:
                _logger.SenderRetrying(message.StorageId, message.Retries);
                break;
        }

        return decision;
    }

    private async Task _InvokeOnExhausted(
        MediumMessage message,
        Exception ex,
        IServiceProvider dispatchServices,
        CancellationToken cancellationToken
    )
    {
        var callback = _retryPolicy.OnExhausted;
        if (callback is null)
        {
            return;
        }

        try
        {
            // Use the live dispatch scope so scoped services resolved here are the same
            // instances seen during SendAsync. The caller (Dispatcher) is responsible for
            // creating and disposing that scope.
            await callback(
                    new FailedInfo
                    {
                        ServiceProvider = dispatchServices,
                        MessageType = MessageType.Publish,
                        Message = message.Origin,
                        Exception = ex,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
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
