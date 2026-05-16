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
        // We DO honor host shutdown (_shutdownToken from IHostApplicationLifetime.ApplicationStopping)
        // so a SIGTERM during a long inline delay can abort cleanly: the resulting OCE is detected
        // by RetryHelper.IsCancellation in _SetFailedState, which returns without writing terminal
        // state. The row keeps its prior NextRetryAt/Status and the persisted retry processor picks
        // it up on restart.
        return InlineRetryLoop.ExecuteAsync(
            (inlineRetries, ct) => _SendWithoutRetryAsync(message, dispatchServices, inlineRetries, ct),
            _retryPolicy,
            _shutdownToken
        );
    }

    private async Task<(RetryDecision Decision, OperateResult Result)> _SendWithoutRetryAsync(
        MediumMessage message,
        IServiceProvider dispatchServices,
        int inlineRetries,
        CancellationToken cancellationToken
    )
    {
        var leased = await _LeaseAsync(message, cancellationToken).ConfigureAwait(false);
        if (!leased)
        {
            return (RetryDecision.Stop, OperateResult.Success);
        }

        var transportMsg = await _serializer.SerializeToTransportMessageAsync(message.Origin).ConfigureAwait(false);

        var tracingTimestamp = _TracingBefore(transportMsg, _transport.BrokerAddress);

        using var publishCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken);
        publishCts.CancelAfter(_options.TransportPublishTimeout);
        var result = await _transport.SendAsync(transportMsg, publishCts.Token).ConfigureAwait(false);

        if (result.Succeeded)
        {
            await _SetSuccessfulState(message, CancellationToken.None).ConfigureAwait(false);

            _TracingAfter(tracingTimestamp, transportMsg, _transport.BrokerAddress);

            return (RetryDecision.Stop, OperateResult.Success);
        }

        _TracingError(tracingTimestamp, transportMsg, _transport.BrokerAddress, result);

        var decision = await _SetFailedState(
                message,
                result.Exception!,
                dispatchServices,
                inlineRetries,
                cancellationToken
            )
            .ConfigureAwait(false);

        return (decision, OperateResult.Failed(result.Exception!));
    }

    private async Task _SetSuccessfulState(MediumMessage message, CancellationToken cancellationToken)
    {
        message.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(_options.SucceedMessageExpiredAfter);
        var updated = await _dataStorage
            .ChangePublishStateAsync(message, StatusName.Succeeded, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!updated)
        {
            // Storage's terminal-row guard rejected the write — the row is already in a terminal
            // state (typically Failed/NULL after a prior exhausted attempt). The broker DID accept
            // the publish, so the consumer will observe the message; at-least-once delivery means
            // duplicates are the user's responsibility to dedupe on the receive side.
            _logger.PublishSucceededButStorageTerminal(message.StorageId);
        }
    }

    private async Task<RetryDecision> _SetFailedState(
        MediumMessage message,
        Exception ex,
        IServiceProvider dispatchServices,
        int inlineRetries,
        CancellationToken cancellationToken
    )
    {
        // Host shutdown: an OCE bound to the host's stopping token means the dispatch was aborted,
        // not that the message failed. Returning without writing state preserves the row's existing
        // NextRetryAt/Status, and the persisted retry processor will pick the row up on restart.
        // cancellationToken on this path IS _shutdownToken — see InlineRetryLoop wiring in
        // SendAsync above, which forwards _shutdownToken as the inline-loop CT.
        if (RetryHelper.IsCancellation(ex, cancellationToken))
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
        // Pass the message's current NextRetryAt so ResolveNextState can preserve InitialDispatchGrace
        // on inline-in-flight transitions and pad the schedule against polling races.
        var state = RetryHelper.ResolveNextState(
            decision,
            inlineRetries,
            _retryPolicy,
            _timeProvider,
            currentNextRetryAt: message.NextRetryAt
        );

        // Persist transition: inline budget consumed AND decision Continue means the call site
        // owns the Retries++ . The helper is pure with respect to MediumMessage; this is the only
        // place persisted-pickup count advances.
        if (decision.Outcome == RetryDecision.Kind.Continue && !state.IsInlineRetryInFlight)
        {
            var originalRetries = message.Retries;
            message.Retries++;
            return await _PersistFailedStateAsync(message, ex, dispatchServices, decision, state, originalRetries)
                .ConfigureAwait(false);
        }

        return await _PersistFailedStateAsync(message, ex, dispatchServices, decision, state, originalRetries: null)
            .ConfigureAwait(false);
    }

    private async Task<RetryDecision> _PersistFailedStateAsync(
        MediumMessage message,
        Exception ex,
        IServiceProvider dispatchServices,
        RetryDecision decision,
        RetryNextState state,
        int? originalRetries
    )
    {
        var affected = await _dataStorage
            .ChangePublishStateAsync(
                message,
                state.NextStatus,
                nextRetryAt: state.NextRetryAt,
                originalRetries: originalRetries,
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);

        if (affected && decision.Outcome == RetryDecision.Kind.Exhausted)
        {
            // Forward _shutdownToken so the callback's CT honors host shutdown — matching the
            // contract documented on RetryPolicyOptions.OnExhausted and the consume path's behavior.
            await _InvokeOnExhausted(message, ex, dispatchServices, _shutdownToken).ConfigureAwait(false);
        }
        else if (!affected)
        {
            // Storage proves the row is already terminal — only log when the decision would
            // otherwise have fired the callback (Stop never fires OnExhausted regardless).
            if (decision.Outcome == RetryDecision.Kind.Exhausted)
            {
                _logger.SkippingOnExhaustedAlreadyTerminal(message.StorageId);
            }

            return RetryDecision.Stop;
        }

        return decision;
    }

    private async Task<bool> _LeaseAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        var lockedUntil = _timeProvider.GetUtcNow().UtcDateTime.Add(_retryPolicy.DispatchTimeout);
        return await _dataStorage.LeasePublishAsync(message, lockedUntil, cancellationToken).ConfigureAwait(false);
    }

    private RetryDecision _UpdateMessageForRetry(MediumMessage message, Exception ex, int inlineRetries)
    {
        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            ex,
            _retryPolicy,
            inlineRetries,
            logger: _logger
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

        // Use the live dispatch scope so scoped services resolved here are the same
        // instances seen during SendAsync. The caller (Dispatcher) is responsible for
        // creating and disposing that scope. RetryHelper.InvokeOnExhaustedAsync applies
        // the configured OnExhaustedTimeout and swallows handler exceptions.
        using var tenantScope = TenantContextScope.ChangeFromEnvelope(dispatchServices, message.Origin, _logger);
        await RetryHelper
            .InvokeOnExhaustedAsync(
                callback,
                new FailedInfo
                {
                    ServiceProvider = dispatchServices,
                    MessageType = MessageType.Publish,
                    Message = message.Origin,
                    Exception = ex,
                },
                _retryPolicy.OnExhaustedTimeout,
                message.StorageId,
                _logger,
                cancellationToken
            )
            .ConfigureAwait(false);
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
