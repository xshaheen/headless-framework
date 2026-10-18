// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
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

    Task<OperateResult> SendRetryAsync(
        MediumMessage message,
        IServiceProvider dispatchServices,
        RetryExecutionState executionState
    );
}

internal sealed class MessageSender : IMessageSender
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDataStorage _dataStorage;
    private readonly ILogger _logger;
    private readonly MessagingOptions _options;
    private readonly IMessageSerializer _serializer;
    private readonly Lazy<IBusTransport?> _busTransport;
    private readonly Lazy<IQueueTransport?> _queueTransport;
    private readonly TimeProvider _timeProvider;

    // The publish timeout and the lease-renewal timeout wait out a broker or store that stopped answering.
    private readonly TimeProvider _backoffClock;
    private readonly MessagingTelemetry _telemetry;
    private readonly RetryPolicyOptions _retryPolicy;
    private readonly MessagingPublishRetryPipeline _retryPipeline;
    private readonly CancellationToken _shutdownToken;
    private readonly TransportConnectivity _connectivity;

    public MessageSender(ILogger<MessageSender> logger, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _dataStorage = serviceProvider.GetRequiredService<IDataStorage>();
        _serializer = serviceProvider.GetRequiredService<IMessageSerializer>();
        _busTransport = new Lazy<IBusTransport?>(serviceProvider.GetService<IBusTransport>);
        _queueTransport = new Lazy<IQueueTransport?>(serviceProvider.GetService<IQueueTransport>);
        _timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
        _backoffClock = BackoffClock.Resolve(serviceProvider);
        _telemetry = serviceProvider.GetService<MessagingTelemetry>() ?? MessagingTelemetry.Default;
        var opts = serviceProvider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        _options = opts;
        _retryPolicy = opts.RetryPolicy;
        _retryPipeline = new MessagingPublishRetryPipeline(_retryPolicy, _timeProvider, logger);
        _connectivity = serviceProvider.GetRequiredService<TransportConnectivity>();

        // IHostApplicationLifetime is optional so MessageSender remains usable in test contexts
        // (and non-hosted scenarios). When absent, shutdown cancellation cannot be observed and
        // exceptions are treated as failures rather than cancellations — the pre-existing behavior.
        _shutdownToken =
            serviceProvider.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None;
    }

    public Task<OperateResult> SendAsync(MediumMessage message)
    {
        return SendAsync(message, _serviceProvider);
    }

    public Task<OperateResult> SendAsync(MediumMessage message, IServiceProvider dispatchServices)
    {
        return _SendAsync(message, dispatchServices, executionState: null);
    }

    public Task<OperateResult> SendRetryAsync(
        MediumMessage message,
        IServiceProvider dispatchServices,
        RetryExecutionState executionState
    )
    {
        return _SendAsync(message, dispatchServices, executionState);
    }

    private Task<OperateResult> _SendAsync(
        MediumMessage message,
        IServiceProvider dispatchServices,
        RetryExecutionState? executionState
    )
    {
        Argument.IsNotNull(dispatchServices);

        if (message.RoutingAffinityKey is { } key)
        {
            // Persisted envelopes may outlive their original topology configuration. Revalidate before reserving
            // an attempt or resolving a native client so a deployment cannot silently degrade keyed backlog.
            var capabilities =
                _serviceProvider.GetService<IMessageCapabilityGate>()
                ?? throw new MessagingConfigurationException(
                    "Stored routing affinity requires a frozen capability mapping."
                );
            capabilities.EnsureRoutingAffinitySupported(message.Origin.Name, message.Lane, key, message.Origin.Headers);
        }

        // Outbox sender doesn't propagate user cancellation; messages should be delivered.
        // We DO honor host shutdown (_shutdownToken from IHostApplicationLifetime.ApplicationStopping)
        // so a SIGTERM during a long inline delay can abort cleanly: the resulting OCE is detected
        // by RetryHelper.IsCancellation in _SetFailedState, which returns without writing terminal
        // state. The row keeps its prior NextRetryAt/Status and the persisted retry processor picks
        // it up on restart.
        return _retryPipeline.ExecuteAsync(
            (_, ct) => _SendWithoutRetryAsync(message, executionState, ct),
            (inlineRetries, exception, delay, strategyFailed, ct) =>
                _HandleRetryAsync(
                    message,
                    exception,
                    dispatchServices,
                    inlineRetries,
                    delay,
                    strategyFailed,
                    executionState,
                    ct
                ),
            (inlineRetries, exception, ct) =>
                _HandleNonRetryableAsync(message, exception, dispatchServices, inlineRetries, executionState, ct),
            message.StorageId,
            _shutdownToken
        );
    }

    private async Task<MessagingRetryAttempt> _SendWithoutRetryAsync(
        MediumMessage message,
        RetryExecutionState? executionState,
        CancellationToken cancellationToken
    )
    {
        // A storage-returned lease is already acquired under the provider's authoritative clock.
        // Core must not reinterpret its expiry through the application clock; reservation and
        // state-write predicates validate the stored lease identity and activity.
        var needsLease = message.LockedUntil is null;

        // A crashed dispatch that already reserved its final attempt must not run another; a row already past
        // MaxPersistedRetries (the cap was lowered since it was scheduled) must not be sent at all. Both end through
        // the retry decision without a transport send, so a lowered cap ends the row instead of stranding it.
        if (
            (
                RetryHelper.DetectCrashRecoveredReservation(message.InlineAttempts, _retryPolicy)
                ?? RetryHelper.DetectBudgetOverrun(message.Retries, _retryPolicy.MaxPersistedRetries)
            ) is
            { } recoveryAttempt
        )
        {
            // The recovery transition still writes CAS-guarded state, which requires an active
            // lease — take a plain lease (no fresh reservation; the crashed reservation is spent).
            if (needsLease && !await _LeaseAsync(message, cancellationToken).ConfigureAwait(false))
            {
                return MessagingRetryAttempt.Completed(OperateResult.Success);
            }

            return recoveryAttempt;
        }

        if (needsLease)
        {
            // Fresh-dispatch fast path: acquire the lease AND durably reserve the first attempt in
            // one statement instead of two sequential writes (lease + reserve) on the same row.
            if (!await _LeaseAndReserveAttemptAsync(message, cancellationToken).ConfigureAwait(false))
            {
                return MessagingRetryAttempt.Completed(OperateResult.Success);
            }
        }
        else if (!await _ReserveAttemptAsync(message, cancellationToken).ConfigureAwait(false))
        {
            return MessagingRetryAttempt.Completed(OperateResult.Success);
        }

        var transportMsg = await _serializer
            .SerializeToTransportMessageAsync(message.Origin, cancellationToken)
            .ConfigureAwait(false);
        var selected = await _ResolveTransportAsync(message, executionState).ConfigureAwait(false);
        if (selected.Result is { } failure)
        {
            return MessagingRetryAttempt.Completed(failure);
        }

        var transport = selected.Transport!;
        var brokerAddress = transport.BrokerAddress;
        var traceHandle = _TracingBefore(transportMsg, message.Lane, brokerAddress);

        // The timer is declared first so the linked source over it is disposed first, unregistering from a live token.
        var publishTimeout = _CreatePublishTimeoutSources();
        using var publishTimerCts = publishTimeout.Timer;
        using var publishCts = publishTimeout.Publish;
        OperateResult result;

        // Slides the lease forward while the send runs, so a send that outlasts DispatchTimeout keeps its row.
        var renewal = _StartLeaseRenewal(message, publishCts.Token);
        try
        {
            try
            {
                result = await transport.SendAsync(transportMsg, renewal.AttemptToken).ConfigureAwait(false);
            }
            finally
            {
                // The state write below fences on the lease identity, which a renewal in flight could still move.
                await renewal.StopAsync().ConfigureAwait(false);
            }

            renewal.ThrowIfLost();
        }
        catch (Exception ex) when (renewal.IsLost)
        {
            // Another node may own the row now, so the attempt writes nothing: any write would carry a lease identity
            // the row no longer has. The broker may still have taken the send, so the row's next dispatch can duplicate
            // it, as after a crash at this point.
            MessagingTelemetry.PublishAmbiguous(
                traceHandle.Activity,
                transportMsg,
                brokerAddress,
                ex,
                traceHandle.ElapsedMs(_NowMs()),
                message.Lane
            );

            return MessagingRetryAttempt.Completed(OperateResult.Failed(ex));
        }
        catch (OperationCanceledException ex)
        {
            // A send the publish timeout cut off is a broker that does not answer; one shutdown cut off says nothing.
            if (!_shutdownToken.IsCancellationRequested)
            {
                _connectivity.ReportBrokerFailure();
            }

            // Timeout or shutdown can race broker acceptance; record the at-least-once duplicate window either way.
            MessagingTelemetry.PublishAmbiguous(
                traceHandle.Activity,
                transportMsg,
                brokerAddress,
                ex,
                traceHandle.ElapsedMs(_NowMs()),
                message.Lane
            );

            // A publish timeout while the host keeps running is a slow broker, not a stop: rethrown, the retry engine
            // would not classify it, so the attempt would record no failure and the row would wait out its lease
            // (or be re-claimed every poll) with no back-off. As a failed attempt it takes the normal retry path.
            if (publishCts.IsCancellationRequested && !_shutdownToken.IsCancellationRequested)
            {
                return MessagingRetryAttempt.Retryable(
                    OperateResult.Failed(
                        new TimeoutException(
                            $"The transport did not confirm the publish within {_options.TransportPublishTimeout}.",
                            ex
                        )
                    )
                );
            }

            throw;
        }
        catch (Exception ex)
        {
            _ReportSendFailure(ex);

            // A throwing send must stop the span and record the failed send exactly like the failed-OperateResult
            // path below; the exception then propagates unchanged.
            _TracingError(traceHandle, transportMsg, message.Lane, brokerAddress, OperateResult.Failed(ex));
            throw;
        }
        finally
        {
            await renewal.DisposeAsync().ConfigureAwait(false);
        }

        if (result.Succeeded)
        {
            _connectivity.ReportBrokerSuccess();

            try
            {
                await _SetSuccessfulState(message, executionState, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Broker acceptance is known, but durable success-state persistence is not. A retry can
                // therefore duplicate delivery, so surface the ambiguity before preserving the storage failure.
                MessagingTelemetry.PublishAmbiguous(
                    traceHandle.Activity,
                    transportMsg,
                    brokerAddress,
                    ex,
                    traceHandle.ElapsedMs(_NowMs()),
                    message.Lane
                );
                throw;
            }

            _TracingAfter(traceHandle, transportMsg, message.Lane, brokerAddress);

            return MessagingRetryAttempt.Completed(OperateResult.Success);
        }

        // Only a failure that could not reach the broker counts: one message the broker refused says nothing about it.
        if (result.IsBrokerUnreachable)
        {
            _connectivity.ReportBrokerFailure();
        }

        _TracingError(traceHandle, transportMsg, message.Lane, brokerAddress, result);

        return MessagingRetryAttempt.Retryable(OperateResult.Failed(result.Exception!));
    }

    // Serialization and storage failures happen outside the transport call, so only what the transport itself failed
    // reaches here. A transport that throws instead of returning a result is judged by the failures all transports
    // share; a transport disposed under a stopping host refuses the send without asking the broker.
    private void _ReportSendFailure(Exception exception)
    {
        if (BrokerFailures.IsConnectivity(exception))
        {
            _connectivity.ReportBrokerFailure();
        }
    }

    // Returns the source that bounds the send, cancelled by the publish timeout or host shutdown, and the back-off
    // clock's timer source when it is a separate object the caller must also dispose.
    private (CancellationTokenSource Publish, CancellationTokenSource? Timer) _CreatePublishTimeoutSources()
    {
        // With no host lifetime to observe (test and non-hosted contexts) the shutdown token can never fire, so no
        // linked source wraps the timeout in extra registration state.
        if (ReferenceEquals(_backoffClock, TimeProvider.System))
        {
            // A source's own timer already runs on the system clock, so one source carries both the timeout and
            // shutdown: the hot path allocates a single source per send.
            var publish = _shutdownToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken)
                : new CancellationTokenSource();
            publish.CancelAfter(_options.TransportPublishTimeout);

            return (publish, null);
        }

        // Any other back-off clock needs a timer source of its own, so a test steps through a broker that never answers.
        var timer = new CancellationTokenSource(_options.TransportPublishTimeout, _backoffClock);

        return _shutdownToken.CanBeCanceled
            ? (CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken, timer.Token), timer)
            : (timer, null);
    }

    private MessageLeaseRenewal _StartLeaseRenewal(MediumMessage message, CancellationToken cancellationToken)
    {
        return MessageLeaseRenewal.Start(
            message,
            _StorageOf(message).RenewPublishLeaseAsync,
            _retryPolicy,
            clock: _timeProvider,
            backoffClock: _backoffClock,
            _logger,
            cancellationToken
        );
    }

    private async Task<(ITransport? Transport, OperateResult? Result)> _ResolveTransportAsync(
        MediumMessage message,
        RetryExecutionState? executionState
    )
    {
        if (!Enum.IsDefined(message.Lane))
        {
            var ex = new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Stored message {message.StorageId} has unsupported MessageLane value '{(short)message.Lane}'."
                )
            );
            await _MarkMissingLaneTransportFailedAsync(message, ex, executionState).ConfigureAwait(false);
            return (null, OperateResult.Failed(ex));
        }

        return message.Lane switch
        {
            MessageLane.Bus when _busTransport.Value is { } busTransport => (busTransport, null),
            MessageLane.Queue when _queueTransport.Value is { } queueTransport => (queueTransport, null),
            MessageLane.Bus => await _MissingTransportAsync(message, nameof(IBusTransport), executionState)
                .ConfigureAwait(false),
            MessageLane.Queue => await _MissingTransportAsync(message, nameof(IQueueTransport), executionState)
                .ConfigureAwait(false),
            _ => throw new UnreachableException(),
        };
    }

    private async Task<(ITransport? Transport, OperateResult? Result)> _MissingTransportAsync(
        MediumMessage message,
        string transportType,
        RetryExecutionState? executionState
    )
    {
        var ex = new InvalidOperationException(
            $"Stored message {message.StorageId} requires {transportType}, but no matching transport is registered."
        );
        await _MarkMissingLaneTransportFailedAsync(message, ex, executionState).ConfigureAwait(false);
        return (null, OperateResult.Failed(ex));
    }

    private async Task _MarkMissingLaneTransportFailedAsync(
        MediumMessage message,
        Exception ex,
        RetryExecutionState? executionState
    )
    {
        var originalInlineAttempts = message.InlineAttempts;
        message.ExpiresAt = _timeProvider.GetUtcNow().AddSeconds(_options.FailedMessageExpiredAfter);
        message.NextRetryAt = null;
        DateTimeOffset? lockedUntil = null;

        var affected = await _StorageOf(message)
            .ChangePublishRetryStateAsync(
                message,
                StatusName.Failed,
                // The envelope is untouched on this path: the failure is a configuration fault detected
                // before any transport attempt, so no exception is stamped onto Origin.
                MessageContentWrite.Preserve,
                retryDelay: null,
                lockedUntil,
                originalRetries: message.Retries,
                originalInlineAttempts,
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);
        executionState?.RecordLeaseTransition(affected, lockedUntil);

        _logger.StoredMessageMissingLaneTransport(ex, message.StorageId, message.Lane.ToString("D"));
    }

    private async Task _SetSuccessfulState(
        MediumMessage message,
        RetryExecutionState? executionState,
        CancellationToken cancellationToken
    )
    {
        message.ExpiresAt = _timeProvider.GetUtcNow().AddSeconds(_options.SucceedMessageExpiredAfter);
        DateTimeOffset? lockedUntil = null;
        var updated = await _StorageOf(message)
            .ChangePublishRetryStateAsync(
                message,
                StatusName.Succeeded,
                // A successful publish never mutates Origin, so the stored envelope is already
                // byte-identical — re-serializing it would be a wasted write on the hottest path.
                MessageContentWrite.Preserve,
                retryDelay: null,
                lockedUntil,
                originalRetries: message.Retries,
                originalInlineAttempts: message.InlineAttempts,
                cancellationToken
            )
            .ConfigureAwait(false);
        executionState?.RecordLeaseTransition(updated, lockedUntil);

        if (!updated)
        {
            // The broker DID accept the publish, so the consumer will observe the message; at-least-once delivery
            // means duplicates are the user's responsibility to dedupe on the receive side. The fenced write cannot
            // say why it missed, so read the row back on this rare path to tell the operator which cause it was.
            await _LogSuccessWriteMissAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task _LogSuccessWriteMissAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        MediumMessage? row;
        try
        {
            row = await _StorageOf(message)
                .GetMonitoringApi()
                .GetPublishedMessageAsync(message.StorageId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The diagnostic read must never fail a publish the broker already accepted.
            _logger.PublishSucceededButLeaseLost(ex, message.StorageId);
            return;
        }

        if (row is null)
        {
            _logger.PublishSucceededButStorageRowMissing(message.StorageId);
        }
        else if (MessageTerminalState.IsTerminal(row.StatusName, row.NextRetryAt))
        {
            // The terminal guard's own predicate: the row ended (typically Failed after a prior exhausted attempt).
            _logger.PublishSucceededButStorageTerminal(message.StorageId);
        }
        else
        {
            // A live row the fence rejected: another claimer took the lease or it lapsed. A claimed delayed publish
            // is live with no NextRetryAt, so the status, not NextRetryAt alone, tells it apart from a terminal row.
            _logger.PublishSucceededButLeaseLost(exception: null, message.StorageId);
        }
    }

    private async Task<bool> _HandleRetryAsync(
        MediumMessage message,
        Exception exception,
        IServiceProvider dispatchServices,
        int _,
        TimeSpan delay,
        bool strategyFailed,
        RetryExecutionState? executionState,
        CancellationToken cancellationToken
    )
    {
        var inlineRetries = Math.Max(0, message.InlineAttempts - 1);
        // A row past the cap is exhausted whatever inline budget remains: its attempt reserved no inline slot, so
        // continuing would re-enter the burst and persist another retry instead of ending the row.
        var decision =
            strategyFailed || RetryHelper.IsOverPersistedBudget(message.Retries, _retryPolicy.MaxPersistedRetries)
                ? MessagingRetryDecision.Exhausted
            : !_retryPolicy.HasMoreInlineAttempts(inlineRetries) && message.Retries >= _retryPolicy.MaxPersistedRetries
                ? MessagingRetryDecision.Exhausted
            : MessagingRetryDecision.Continue(delay);
        var persisted = await _SetFailedState(
                message,
                exception,
                dispatchServices,
                inlineRetries,
                decision,
                executionState,
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
        RetryExecutionState? executionState,
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
                executionState,
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
        RetryExecutionState? executionState,
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
        var state = RetryHelper.ResolveNextState(decision, inlineRetries, _retryPolicy);

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
                    originalInlineAttempts,
                    executionState
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
                originalInlineAttempts,
                executionState
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
        int originalInlineAttempts,
        RetryExecutionState? executionState
    )
    {
        // #14 — Preserve the active pickup lease on inline-in-flight transitions; clear it on
        // persisted-retry and terminal transitions so the retry processor can re-pick the row.
        // Mirrors the consume path's _PersistFailedStateAsync.
        var lockedUntil = state.IsInlineRetryInFlight ? message.LockedUntil : null;
        var affected = await _StorageOf(message)
            .ChangePublishRetryStateAsync(
                message,
                state.NextStatus,
                // _SetFailedState stamped the exception onto Origin, so the persisted envelope is stale
                // until this write refreshes it.
                MessageContentWrite.Refresh,
                state.NextRetry,
                lockedUntil,
                originalRetries,
                originalInlineAttempts,
                CancellationToken.None
            )
            .ConfigureAwait(false);
        executionState?.RecordLeaseTransition(affected, lockedUntil);

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

    // A row from an additional outbox lives in that outbox's database; every other published row is the primary's.
    private IDataStorage _StorageOf(MediumMessage message) => message.OutboxStorage ?? _dataStorage;

    private async Task<bool> _LeaseAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        return await _StorageOf(message)
            .LeasePublishAsync(message, _retryPolicy.DispatchTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> _LeaseAndReserveAttemptAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        var originalInlineAttempts = message.InlineAttempts;
        message.InlineAttempts++;
        var reserved = await _StorageOf(message)
            .LeasePublishAndReserveAttemptAsync(
                message,
                _retryPolicy.DispatchTimeout,
                originalInlineAttempts,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (!reserved)
        {
            message.InlineAttempts = originalInlineAttempts;
        }

        return reserved;
    }

    private async Task<bool> _ReserveAttemptAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        var originalInlineAttempts = message.InlineAttempts;
        message.InlineAttempts++;
        var reserved = await _StorageOf(message)
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

    private MessagingTraceHandle _TracingBefore(TransportMessage message, MessageLane lane, BrokerAddress broker)
    {
        MessageEventCounterSource.Log.WritePublishMetrics();

        if (!MessagingDiagnostics.IsEnabled)
        {
            return default;
        }

        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var activity = _telemetry.PublishStart(message, lane, broker, now);

        return new MessagingTraceHandle(activity, now);
    }

    private void _TracingAfter(
        MessagingTraceHandle traceHandle,
        TransportMessage message,
        MessageLane lane,
        BrokerAddress broker
    )
    {
        if (!traceHandle.IsRecording)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        MessagingTelemetry.PublishStop(
            traceHandle.Activity,
            message,
            broker,
            traceHandle.StartTimestampMs!.Value,
            now,
            lane
        );
    }

    private void _TracingError(
        MessagingTraceHandle traceHandle,
        TransportMessage message,
        MessageLane lane,
        BrokerAddress broker,
        OperateResult result
    )
    {
        if (!traceHandle.IsRecording)
        {
            return;
        }

        var ex = new PublisherSentFailedException(result.ToString(), result.Exception);
        MessagingTelemetry.PublishError(
            traceHandle.Activity,
            message,
            broker,
            ex,
            traceHandle.ElapsedMs(_NowMs()),
            lane
        );
    }

    private long _NowMs() => _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    #endregion
}
