// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Messaging.Serialization;
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
    private readonly IBusTransport? _busTransport;
    private readonly IQueueTransport? _queueTransport;
    private readonly TimeProvider _timeProvider;
    private readonly RetryPolicyOptions _retryPolicy;
    private readonly MessagingRetryPipeline _retryPipeline;
    private readonly CancellationToken _shutdownToken;

    public MessageSender(ILogger<MessageSender> logger, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _dataStorage = serviceProvider.GetRequiredService<IDataStorage>();
        _serializer = serviceProvider.GetRequiredService<ISerializer>();
        _busTransport = serviceProvider.GetService<IBusTransport>();
        _queueTransport = serviceProvider.GetService<IQueueTransport>();
        _timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
        var opts = serviceProvider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        _options = opts;
        _retryPolicy = opts.RetryPolicy;
        _retryPipeline = new MessagingRetryPipeline(_retryPolicy, _timeProvider, logger);

        // IHostApplicationLifetime is optional so MessageSender remains usable in test contexts
        // (and non-hosted scenarios). When absent, shutdown cancellation cannot be observed and
        // exceptions are treated as failures rather than cancellations — the pre-existing behavior.
        _shutdownToken =
            serviceProvider.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None;
    }

    public Task<OperateResult> SendAsync(MediumMessage message) => SendAsync(message, _serviceProvider);

    public Task<OperateResult> SendAsync(MediumMessage message, IServiceProvider dispatchServices)
    {
        Argument.IsNotNull(dispatchServices);

        // Outbox sender doesn't propagate user cancellation; messages should be delivered.
        // We DO honor host shutdown (_shutdownToken from IHostApplicationLifetime.ApplicationStopping)
        // so a SIGTERM during a long inline delay can abort cleanly: the resulting OCE is detected
        // by RetryHelper.IsCancellation in _SetFailedState, which returns without writing terminal
        // state. The row keeps its prior NextRetryAt/Status and the persisted retry processor picks
        // it up on restart.
        return _retryPipeline.ExecuteAsync(
            (inlineRetries, ct) => _SendWithoutRetryAsync(message, inlineRetries, ct),
            (inlineRetries, exception, delay, strategyFailed, ct) =>
                _HandleRetryAsync(message, exception, dispatchServices, inlineRetries, delay, strategyFailed, ct),
            (inlineRetries, exception, ct) =>
                _HandleNonRetryableAsync(message, exception, dispatchServices, inlineRetries, ct),
            message.StorageId,
            _shutdownToken
        );
    }

    private async Task<MessagingRetryAttempt> _SendWithoutRetryAsync(
        MediumMessage message,
        int inlineRetries,
        CancellationToken cancellationToken
    )
    {
        // Atomic pickup already wrote a live lease. Re-leasing would immediately fail against the
        // storage lease predicate and strand rows returned by GetPublishedMessagesOfNeedRetryAsync.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (message.LockedUntil is not { } lockedUntil || lockedUntil <= now)
        {
            var leased = await _LeaseAsync(message, cancellationToken).ConfigureAwait(false);
            if (!leased)
            {
                return MessagingRetryAttempt.Completed(OperateResult.Success);
            }
        }

        inlineRetries = message.InlineAttempts;
        if (RetryHelper.DetectCrashRecoveredReservation(inlineRetries, _retryPolicy) is { } recoveryAttempt)
        {
            return recoveryAttempt;
        }

        if (!await _ReserveAttemptAsync(message, cancellationToken).ConfigureAwait(false))
        {
            return MessagingRetryAttempt.Completed(OperateResult.Success);
        }

        var transportMsg = await _serializer.SerializeToTransportMessageAsync(message.Origin).ConfigureAwait(false);
        var selected = await _ResolveTransportAsync(message).ConfigureAwait(false);
        if (selected.Result is { } failure)
        {
            return MessagingRetryAttempt.Completed(failure);
        }

        var transport = selected.Transport!;
        var brokerAddress = transport.BrokerAddress;
        var tracingTimestamp = _TracingBefore(transportMsg, message.IntentType, brokerAddress, cancellationToken);

        using var publishCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken);
        publishCts.CancelAfter(_options.TransportPublishTimeout);
        var result = await transport.SendAsync(transportMsg, publishCts.Token).ConfigureAwait(false);

        if (result.Succeeded)
        {
            await _SetSuccessfulState(message, CancellationToken.None).ConfigureAwait(false);

            _TracingAfter(tracingTimestamp, transportMsg, message.IntentType, brokerAddress, cancellationToken);

            return MessagingRetryAttempt.Completed(OperateResult.Success);
        }

        _TracingError(tracingTimestamp, transportMsg, message.IntentType, brokerAddress, result, cancellationToken);

        return MessagingRetryAttempt.Retryable(OperateResult.Failed(result.Exception!));
    }

    private async Task<(ITransport? Transport, OperateResult? Result)> _ResolveTransportAsync(MediumMessage message)
    {
        if (!Enum.IsDefined(message.IntentType))
        {
            var ex = new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Stored message {message.StorageId} has unsupported IntentType value '{(short)message.IntentType}'."
                )
            );
            await _MarkUnsupportedIntentFailedAsync(message, ex).ConfigureAwait(false);
            return (null, OperateResult.Failed(ex));
        }

        return message.IntentType switch
        {
            IntentType.Bus when _busTransport is not null => (_busTransport, null),
            IntentType.Queue when _queueTransport is not null => (_queueTransport, null),
            IntentType.Bus => await _MissingTransportAsync(message, nameof(IBusTransport)).ConfigureAwait(false),
            IntentType.Queue => await _MissingTransportAsync(message, nameof(IQueueTransport)).ConfigureAwait(false),
            _ => throw new UnreachableException(),
        };
    }

    private async Task<(ITransport? Transport, OperateResult? Result)> _MissingTransportAsync(
        MediumMessage message,
        string transportType
    )
    {
        var ex = new InvalidOperationException(
            $"Stored message {message.StorageId} requires {transportType}, but no matching transport is registered."
        );
        await _MarkUnsupportedIntentFailedAsync(message, ex).ConfigureAwait(false);
        return (null, OperateResult.Failed(ex));
    }

    private async Task _MarkUnsupportedIntentFailedAsync(MediumMessage message, Exception ex)
    {
        message.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(_options.FailedMessageExpiredAfter);
        message.NextRetryAt = null;
        message.LockedUntil = null;

        await _dataStorage
            .ChangePublishStateAsync(
                message,
                StatusName.Failed,
                nextRetryAt: null,
                lockedUntil: null,
                originalRetries: message.Retries,
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);

        _logger.StoredMessageUnsupportedIntent(ex, message.StorageId, message.IntentType.ToString("D"));
    }

    private async Task _SetSuccessfulState(MediumMessage message, CancellationToken cancellationToken)
    {
        message.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(_options.SucceedMessageExpiredAfter);
        var updated = await _dataStorage
            .ChangePublishRetryStateAsync(
                message,
                StatusName.Succeeded,
                nextRetryAt: null,
                lockedUntil: null,
                originalRetries: message.Retries,
                originalInlineAttempts: message.InlineAttempts,
                cancellationToken
            )
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

    private async Task<bool> _HandleRetryAsync(
        MediumMessage message,
        Exception exception,
        IServiceProvider dispatchServices,
        int _,
        TimeSpan delay,
        bool strategyFailed,
        CancellationToken cancellationToken
    )
    {
        var inlineRetries = Math.Max(0, message.InlineAttempts - 1);
        var decision =
            strategyFailed ? MessagingRetryDecision.Exhausted
            : !_retryPolicy.HasMoreInlineAttempts(inlineRetries) && message.Retries >= _retryPolicy.MaxPersistedRetries
                ? MessagingRetryDecision.Exhausted
            : MessagingRetryDecision.Continue(delay);
        var persisted = await _SetFailedState(
                message,
                exception,
                dispatchServices,
                inlineRetries,
                decision,
                cancellationToken
            )
            .ConfigureAwait(false);
        return persisted.Outcome == MessagingRetryDecision.Kind.Continue;
    }

    private async Task _HandleNonRetryableAsync(
        MediumMessage message,
        Exception exception,
        IServiceProvider dispatchServices,
        int _,
        CancellationToken cancellationToken
    )
    {
        var inlineRetries = Math.Max(0, message.InlineAttempts - 1);
        await _SetFailedState(
                message,
                exception,
                dispatchServices,
                inlineRetries,
                MessagingRetryDecision.Stop,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<MessagingRetryDecision> _SetFailedState(
        MediumMessage message,
        Exception ex,
        IServiceProvider dispatchServices,
        int inlineRetries,
        MessagingRetryDecision decision,
        CancellationToken cancellationToken
    )
    {
        // Host shutdown: an OCE bound to the host's stopping token means the dispatch was aborted,
        // not that the message failed. Returning without writing state preserves the row's existing
        // NextRetryAt/Status, and the persisted retry processor will pick the row up on restart.
        // cancellationToken on this path IS _shutdownToken — see the Polly pipeline wiring in
        // SendAsync above, which forwards _shutdownToken as the Polly pipeline CT.
        if (RetryHelper.IsCancellation(ex, cancellationToken))
        {
            _logger.StoredMessageNonRetryableFailure(message.StorageId, "HostShutdown");
            return MessagingRetryDecision.Stop;
        }

        _LogRetryDecision(message, ex, decision);
        var originalInlineAttempts = message.InlineAttempts;

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
        if (decision.Outcome == MessagingRetryDecision.Kind.Continue && !state.IsInlineRetryInFlight)
        {
            var originalRetries = message.Retries;
            message.Retries++;
            message.InlineAttempts = 0;
            await _PersistFailedStateAsync(
                    message,
                    ex,
                    dispatchServices,
                    decision,
                    state,
                    originalRetries,
                    originalInlineAttempts
                )
                .ConfigureAwait(false);
            return MessagingRetryDecision.Stop;
        }

        // #7 — enforce the storage CAS predicate on every state-write site (including inline-in-flight
        // and terminal transitions). The Retries counter doesn't advance on those paths, so the
        // witness is just the current value; storage will accept the write only if another writer
        // hasn't bumped Retries (or won the terminal-race) since this dispatch read the row.
        return await _PersistFailedStateAsync(
                message,
                ex,
                dispatchServices,
                decision,
                state,
                originalRetries: message.Retries,
                originalInlineAttempts
            )
            .ConfigureAwait(false);
    }

    private async Task<MessagingRetryDecision> _PersistFailedStateAsync(
        MediumMessage message,
        Exception ex,
        IServiceProvider dispatchServices,
        MessagingRetryDecision decision,
        RetryNextState state,
        int originalRetries,
        int originalInlineAttempts
    )
    {
        // #14 — Preserve the active pickup lease on inline-in-flight transitions; clear it on
        // persisted-retry and terminal transitions so the retry processor can re-pick the row.
        // Mirrors the consume path's _PersistFailedStateAsync.
        var lockedUntil = state.IsInlineRetryInFlight ? message.LockedUntil : null;
        var affected = await _dataStorage
            .ChangePublishRetryStateAsync(
                message,
                state.NextStatus,
                state.NextRetryAt,
                lockedUntil,
                originalRetries,
                originalInlineAttempts,
                CancellationToken.None
            )
            .ConfigureAwait(false);

        if (affected && decision.Outcome == MessagingRetryDecision.Kind.Exhausted)
        {
            // Forward _shutdownToken so the callback's CT honors host shutdown — matching the
            // contract documented on RetryPolicyOptions.OnExhausted and the consume path's behavior.
            // #6 — the tenant-scope + FailedInfo + InvokeOnExhaustedAsync trio is identical between
            // publish and consume paths except for MessageType; the shared body lives in RetryHelper.
            await RetryHelper
                .RunOnExhaustedAsync(
                    _retryPolicy,
                    message,
                    ex,
                    dispatchServices,
                    MessageType.Publish,
                    _logger,
                    _timeProvider,
                    _shutdownToken
                )
                .ConfigureAwait(false);
        }
        else if (!affected)
        {
            // Storage proves the row is already terminal — only log when the decision would
            // otherwise have fired the callback (Stop never fires OnExhausted regardless).
            if (decision.Outcome == MessagingRetryDecision.Kind.Exhausted)
            {
                _logger.SkippingOnExhaustedAlreadyTerminal(message.StorageId);
            }

            return MessagingRetryDecision.Stop;
        }

        return decision;
    }

    private async Task<bool> _LeaseAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        var lockedUntil = _timeProvider.GetUtcNow().UtcDateTime.Add(_retryPolicy.DispatchTimeout);
        return await _dataStorage.LeasePublishAsync(message, lockedUntil, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> _ReserveAttemptAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        var originalInlineAttempts = message.InlineAttempts;
        message.InlineAttempts++;
        var reserved = await _dataStorage
            .ReservePublishAttemptAsync(message, originalInlineAttempts, cancellationToken)
            .ConfigureAwait(false);
        if (!reserved)
        {
            message.InlineAttempts = originalInlineAttempts;
        }

        return reserved;
    }

    private void _LogRetryDecision(MediumMessage message, Exception ex, MessagingRetryDecision decision)
    {
        switch (decision.Outcome)
        {
            case MessagingRetryDecision.Kind.Stop:
                _logger.StoredMessageNonRetryableFailure(message.StorageId, ex.GetType().Name);
                break;
            case MessagingRetryDecision.Kind.Exhausted:
                _logger.SenderStoredMessageAfterThreshold(message.StorageId, _retryPolicy.MaxPersistedRetries);
                break;
            case MessagingRetryDecision.Kind.Continue:
                _logger.SenderRetrying(message.StorageId, message.Retries);
                break;
        }
    }

    #region tracing

    private long? _TracingBefore(
        TransportMessage message,
        IntentType intentType,
        BrokerAddress broker,
        CancellationToken cancellationToken
    )
    {
        MessageEventCounterSource.Log.WritePublishMetrics();

        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforePublish))
        {
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                Operation = message.Name,
                BrokerAddress = broker,
                TransportMessage = message,
                IntentType = intentType,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforePublish, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private void _TracingAfter(
        long? tracingTimestamp,
        TransportMessage message,
        IntentType intentType,
        BrokerAddress broker,
        CancellationToken cancellationToken
    )
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterPublish))
        {
            var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.Name,
                BrokerAddress = broker,
                TransportMessage = message,
                IntentType = intentType,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterPublish, eventData);
        }
    }

    private void _TracingError(
        long? tracingTimestamp,
        TransportMessage message,
        IntentType intentType,
        BrokerAddress broker,
        OperateResult result,
        CancellationToken cancellationToken
    )
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublish))
        {
            var ex = new PublisherSentFailedException(result.ToString(), result.Exception);
            var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.Name,
                BrokerAddress = broker,
                TransportMessage = message,
                IntentType = intentType,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublish, eventData);
        }
    }

    #endregion
}
