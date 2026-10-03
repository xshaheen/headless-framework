// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Messaging.Runtime;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

/// <summary>
/// Delivery for competing subscriptions: circuit admission, the receive stage, poison rows, and inbox admission and
/// dispatch.
/// </summary>
internal sealed partial class ConsumerRegister
{
    /// <summary>
    /// Delivers one message of a competing subscription: resolves its consumer and circuit, runs the receive stage,
    /// then stores and dispatches it through the inbox, or stores a poison row and commits.
    /// </summary>
    private async Task _OnCompetingMessageAsync(
        IConsumerClient client,
        ConsumerSubscriptionKey subscriptionKey,
        SubscriptionHandle clientHandle,
        TransportMessage transportMessage,
        object? sender,
        CancellationToken hostShutdownToken
    )
    {
        var subscription = subscriptionKey.SubscriptionName;
        var lane = subscriptionKey.Lane;

        long? probeEpoch = null;
        var admissionEpoch = 0L;
        var probeOutcomeTransferred = false;
        var transportSettled = false;
        MessagingTraceHandle traceHandle = default;

        // Exactly one consume outcome (success or error) may be recorded per message: the trace handle is an
        // immutable struct, so this flag is what keeps the subscriber-not-found path (error emitted inline,
        // then routed to the poison store) from also emitting the success outcome on the same handle.
        var consumeOutcomeRecorded = false;

        // Receive-stage state: the context exists only when receive middleware ran for this
        // delivery; the two flags below let the poison and cancellation handlers distinguish an
        // explicit policy decision (Reject/Skip) from a middleware fault, and a cancellation
        // bound to the receive token from a foreign one.
        ReceiveContext? receiveContext = null;
        var receiveRejectIsPolicy = false;
        var receiveOutcomeCancelled = false;

        // Resolved once up front: the consumer identity keys the circuit, the stored row, the metrics, and the
        // header every later stage reads. A delivery no consumer claims has no identity and no circuit, so its
        // poison row is labelled with the subscription it arrived on.
        string? circuitKey = null;

        try
        {
            var name = transportMessage.Name;
            var canFindSubscriber = _selector.TryGetMessageNameExecutor(name, subscriptionKey, out var executor);
            var consumerIdentity = executor?.ResolvedConsumerIdentity ?? subscription;

            // Replaces whatever the publisher sent, so the header always names the consumer that received it.
            transportMessage.Headers[Headers.ConsumerIdentity] = consumerIdentity;

            if (executor is not null)
            {
                circuitKey = CircuitBreakerKeys.For(executor);
            }

            if (_circuitBreakerStateManager is not null && circuitKey is not null)
            {
                probeEpoch = _circuitBreakerStateManager.TryAcquireHalfOpenProbe(circuitKey);

                if (probeEpoch is null)
                {
                    // Settlement is must-complete: never abandon a reject on host shutdown.
                    await client.RejectAsync(sender, CancellationToken.None).ConfigureAwait(false);

                    return;
                }

                admissionEpoch = probeEpoch.Value;
                if (_circuitBreakerStateManager.TryGetOpenEpoch(circuitKey, out var openEpoch))
                {
                    var safeCircuitKey = LogSanitizer.Sanitize(circuitKey);
                    if (clientHandle.IsPauseAppliedForEpoch(openEpoch))
                    {
                        if (_logger.IsEnabled(LogLevel.Warning))
                        {
                            _logger.DeliveryAdmittedWhileOpenAfterPause(safeCircuitKey);
                        }
                    }
                    else
                    {
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.DeliveryAdmittedDuringPauseLatency(safeCircuitKey);
                        }
                    }
                }
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var safeMessageId = LogSanitizer.Sanitize(transportMessage.Id);
                var safeMessageName = LogSanitizer.Sanitize(transportMessage.Name);
                _logger.MessageReceived(safeMessageId, safeMessageName);
            }

            traceHandle = _TracingBefore(transportMessage, lane, _serverAddress);

            Message message;
            Exception? dispatchBypassException = null;
            string? exceptionInfo = null;

            try
            {
                if (!canFindSubscriber)
                {
                    var safeName = LogSanitizer.Sanitize(name);
                    var safeSubscription = LogSanitizer.Sanitize(subscription);
                    var error =
                        $"Message can not be found subscriber. Name:{safeName}, Subscription:{safeSubscription}. {Environment.NewLine} Ensure a consumer is registered for the message on this subscription.";
                    var ex = new SubscriberNotFoundException(error);

                    _TracingError(traceHandle, transportMessage, client.BrokerAddress, ex);
                    consumeOutcomeRecorded = true;

                    throw ex;
                }

                // The receive ring: receive middleware wraps the inner steps (contract-version
                // validation, deserialization, null-payload check) when any descriptor matches;
                // otherwise the inner steps run directly and the delivery path is byte-for-byte
                // today's zero-middleware behavior.
                var receiveOutcome = await _RunReceiveRingAsync(
                        transportMessage,
                        executor!,
                        lane,
                        _RunInnerReceiveAsync,
                        hostShutdownToken
                    )
                    .ConfigureAwait(false);

                if (receiveOutcome.Context is { } ringContext)
                {
                    receiveContext = ringContext;
                }

                if (receiveOutcome.Result == ReceiveRingResult.Skipped)
                {
                    // Skip commits and drops: no storage row, no exhausted callback, and the
                    // half-open probe is released by the callback's finally (neither success nor
                    // failure — a header-based filter decision must not advance the breaker).
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.ReceiveMessageSkipped(
                            receiveOutcome.MiddlewareType!,
                            LogSanitizer.Sanitize(transportMessage.Id),
                            LogSanitizer.Sanitize(name),
                            LogSanitizer.Sanitize(consumerIdentity),
                            lane.ToString(),
                            LogSanitizer.Sanitize(receiveOutcome.OutcomeReason)
                        );
                    }

                    MessagingMetrics.RecordReceiveOutcome("skipped");
                    traceHandle.Activity?.SetTag(MessagingMetrics.TagReceiveOutcome, "skipped");

                    _TracingAfter(traceHandle, transportMessage, _serverAddress);
                    consumeOutcomeRecorded = true;

                    // Settlement is must-complete: never abandon a commit on host shutdown.
                    await client.CommitAsync(sender, CancellationToken.None).ConfigureAwait(false);
                    transportSettled = true;
                    return;
                }

                if (receiveOutcome.Result == ReceiveRingResult.Cancelled)
                {
                    receiveOutcomeCancelled = true;
                    MessagingMetrics.RecordReceiveOutcome("cancelled");
                    traceHandle.Activity?.SetTag(MessagingMetrics.TagReceiveOutcome, "cancelled");
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.ReceiveOutcomeCancelled(
                            LogSanitizer.Sanitize(transportMessage.Id),
                            LogSanitizer.Sanitize(name),
                            LogSanitizer.Sanitize(consumerIdentity)
                        );
                    }

                    throw receiveOutcome.Exception!;
                }

                if (receiveOutcome.Result == ReceiveRingResult.Rejected)
                {
                    dispatchBypassException = receiveOutcome.Exception!;
                    receiveRejectIsPolicy = receiveOutcome.IsPolicyReject;
                    exceptionInfo = dispatchBypassException.ExpandMessage();
                    message = _BuildPoisonMessage(transportMessage, receiveContext, dispatchBypassException);

                    MessagingMetrics.RecordReceiveOutcome("rejected");
                    traceHandle.Activity?.SetTag(MessagingMetrics.TagReceiveOutcome, "rejected");
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.ReceiveMessageRejected(
                            dispatchBypassException is ReceiveMessageRejectedException ? null : dispatchBypassException,
                            receiveOutcome.MiddlewareType!,
                            LogSanitizer.Sanitize(transportMessage.Id),
                            LogSanitizer.Sanitize(name),
                            LogSanitizer.Sanitize(consumerIdentity),
                            lane.ToString(),
                            LogSanitizer.Sanitize(
                                receiveOutcome.OutcomeReason ?? dispatchBypassException.ExpandMessage()
                            )
                        );
                    }

                    // A middleware reject is a policy decision on this delivery, not a subscriber
                    // fault: finalize the span as an error here (mirroring subscriber-not-found)
                    // so the shared poison block below does not emit a success stop for it.
                    if (!consumeOutcomeRecorded)
                    {
                        _TracingError(traceHandle, transportMessage, client.BrokerAddress, dispatchBypassException);
                        consumeOutcomeRecorded = true;
                    }
                }
                else
                {
                    message = receiveOutcome.Message!;

                    // The delivery continued to admission; the ring's copy-on-write headers/body
                    // (already reflected in the deserialized message) are what storage persists.
                    MessagingMetrics.RecordReceiveOutcome("accepted");
                    traceHandle.Activity?.SetTag(MessagingMetrics.TagReceiveOutcome, "accepted");
                }
            }
            catch (Exception e) when (!receiveOutcomeCancelled)
            {
                dispatchBypassException = e;
                receiveRejectIsPolicy = false;
                exceptionInfo = e.ExpandMessage();
                message = _BuildPoisonMessage(transportMessage, receiveContext, e);

                // Every row built here is a poison-on-arrival outcome: subscriber-not-found,
                // contract-version mismatch, a Stage A deserialization failure, or a receive
                // middleware fault the ring converted into a reject.
                MessagingMetrics.RecordReceiveOutcome("rejected");
                traceHandle.Activity?.SetTag(MessagingMetrics.TagReceiveOutcome, "rejected");
            }

            if (message.HasException())
            {
                if (
                    dispatchBypassException is not null
                    && _circuitBreakerStateManager is not null
                    && circuitKey is not null
                )
                {
                    // An explicit Reject() (or its Stage A equivalents routed as policy) is a
                    // policy decision on attacker-controllable input, not a subscriber failure:
                    // neither report nor transfer the probe — the callback's finally releases it,
                    // exactly like Skip. Middleware faults, undeclared outcomes, and version or
                    // deserialization failures keep reporting, and the failure report owns the probe.
                    if (!receiveRejectIsPolicy)
                    {
                        await _circuitBreakerStateManager
                            .ReportFailureAsync(circuitKey, dispatchBypassException, CancellationToken.None)
                            .ConfigureAwait(false);

                        probeOutcomeTransferred = true;
                    }
                }

                var content = _serializer.Serialize(message);

                var stored = await _storage
                    .StoreReceivedExceptionMessageAsync(
                        name,
                        consumerIdentity,
                        new MediumMessage
                        {
                            StorageId = Guid.Empty,
                            Origin = message,
                            Content = content,
                            Lane = lane,
                        },
                        exceptionInfo,
                        hostShutdownToken
                    )
                    .ConfigureAwait(false);

                // Settlement is must-complete: never abandon a commit on host shutdown.
                await client.CommitAsync(sender, CancellationToken.None).ConfigureAwait(false);
                transportSettled = true;

                var bypassCallback = _options.RetryPolicy.OnExhausted;

                if (stored && bypassCallback is not null)
                {
                    // Poisoned-on-arrival messages bypass the normal Dispatcher scope,
                    // so we create a fresh async scope here instead of using the root provider.
                    // RetryHelper.InvokeOnExhaustedAsync applies the configured OnExhaustedTimeout
                    // and swallows handler exceptions; pass the subscription/host shutdown token so a
                    // cooperative callback can short-circuit when the consumer is stopping.
                    await using var exhaustedScope = serviceScopeFactory.CreateAsyncScope();

                    using var tenantScope = TenantContextScope.ChangeFromEnvelope(
                        exhaustedScope.ServiceProvider,
                        message,
                        _logger
                    );

                    await RetryHelper
                        .InvokeOnExhaustedAsync(
                            bypassCallback,
                            new FailedInfo
                            {
                                ServiceProvider = exhaustedScope.ServiceProvider,
                                MessageType = MessageType.Subscribe,
                                Message = message,
                                Lane = lane,
                                Exception =
                                    dispatchBypassException
                                    ?? new InvalidOperationException(
                                        exceptionInfo ?? "Received message contains exception information."
                                    ),
                                // Poisoned-on-arrival messages bypass the dispatch scope and have
                                // no associated MediumMessage; storageId is the storage's
                                // sentinel here too (Guid.Empty == "no row identifier"), and the
                                // retry count is zero because no consume attempt ever ran.
                                StorageId = Guid.Empty,
                                RetryCount = 0,
                            },
                            _options.RetryPolicy.OnExhaustedTimeout,
                            storageId: Guid.Empty,
                            _logger,
                            _timeProvider,
                            hostShutdownToken
                        )
                        .ConfigureAwait(false);
                }
                else if (!stored)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.SkippingPoisonedOnExhaustedAlreadyTerminal(message.Id);
                    }
                }

                // A poisoned message never reached its consumer, so no attempt ran; the publish-side retry budget says
                // nothing about it.
                _logger.ConsumerReceivedMessageAfterThreshold(message.Id, retries: 0);

                if (consumeOutcomeRecorded)
                {
                    // The span + consume outcome were already finalized as an error (subscriber-not-found);
                    // only the legacy EventCounter fires here so its counts match the pre-native bridge.
                    MessageEventCounterSource.Log.WriteConsumeMetrics();
                }
                else
                {
                    _TracingAfter(traceHandle, transportMessage, _serverAddress);
                    consumeOutcomeRecorded = true;
                }
            }
            else
            {
                var messageContractVersion = executor!.MessageContractVersion;
                if (
                    string.IsNullOrWhiteSpace(executor.ConsumerIdentity)
                    || string.IsNullOrWhiteSpace(messageContractVersion)
                )
                {
                    // Runtime subscriptions are intentionally process-local and have no durable identity.
                    // Preserve their legacy delivery path; bootstrap validation prevents configured durable
                    // consumers from reaching this branch without a stable identity and contract version.
                    var runtimeMessage = await _storage
                        .StoreReceivedMessageAsync(
                            name,
                            consumerIdentity,
                            new MediumMessage
                            {
                                StorageId = Guid.Empty,
                                Origin = message,
                                Content = string.Empty,
                                Lane = lane,
                            },
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);

                    runtimeMessage.Origin = message;
                    _TracingAfter(traceHandle, transportMessage, _serverAddress);
                    consumeOutcomeRecorded = true;

                    // The executor releases the HalfOpen probe with this epoch; without it the
                    // release is a no-op and the probe slot stays held on this path.
                    runtimeMessage.ProbeEpoch = admissionEpoch;
                    await _dispatcher
                        .EnqueueToExecute(runtimeMessage, executor, CancellationToken.None)
                        .ConfigureAwait(false);
                    probeOutcomeTransferred = true;

                    await client.CommitAsync(sender, CancellationToken.None).ConfigureAwait(false);
                    transportSettled = true;
                    return;
                }

                var admission = await _storage
                    .AdmitReceivedMessageAsync(
                        name,
                        consumerIdentity,
                        message.Headers[Headers.ContractVersion]!,
                        new MediumMessage
                        {
                            StorageId = Guid.Empty,
                            Origin = message,
                            Content = string.Empty,
                            Lane = lane,
                        },
                        inboxRetention: executor.InboxRetention,
                        cancellationToken: CancellationToken.None
                    )
                    .ConfigureAwait(false);

                admission.Message.Origin = message;

                var storageCapability = _capabilityModel.Providers.FirstOrDefault(capability =>
                    capability.Role is MessagingProviderRole.Storage
                );
                if (storageCapability?.InboxCapability is { } inboxTier)
                {
                    MessagingMetrics.RecordInbox(
                        admission.Disposition is InboxAdmissionDisposition.Winner
                            ? InboxMetricKind.Capability
                            : InboxMetricKind.Duplicate,
                        consumerIdentity,
                        lane,
                        admission.Disposition switch
                        {
                            InboxAdmissionDisposition.Winner => InboxMetricOutcome.Winner,
                            InboxAdmissionDisposition.InFlightDuplicate => InboxMetricOutcome.InFlightDuplicate,
                            InboxAdmissionDisposition.SucceededDuplicate => InboxMetricOutcome.SucceededDuplicate,
                            InboxAdmissionDisposition.TerminalFailedDuplicate =>
                                InboxMetricOutcome.TerminalFailedDuplicate,
                            _ => throw new InvalidOperationException(
                                $"Unsupported inbox admission disposition '{admission.Disposition}'."
                            ),
                        },
                        inboxTier,
                        storageCapability.Provider,
                        message.Headers.TryGetValue(Headers.TenantId, out var tenantId) ? tenantId : null,
                        _inboxMetricPolicy.TenantTagName
                    );
                }

                _TracingAfter(traceHandle, transportMessage, _serverAddress);
                consumeOutcomeRecorded = true;

                // Settlement is must-complete: never abandon a commit on host shutdown.
                await client.CommitAsync(sender, CancellationToken.None).ConfigureAwait(false);
                transportSettled = true;

                if (admission.ShouldDispatch)
                {
                    admission.Message.ProbeEpoch = admissionEpoch;
                    await _dispatcher
                        .EnqueueToExecute(admission.Message, executor, CancellationToken.None)
                        .ConfigureAwait(false);

                    probeOutcomeTransferred = true;
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogProcessReceivedMessageFailed(e, transportMessage);

            if (!transportSettled)
            {
                // Settlement is must-complete: never abandon a reject on host shutdown.
                await client.RejectAsync(sender, CancellationToken.None).ConfigureAwait(false);
            }

            if (e is OperationCanceledException)
            {
                // Benign cancellation (host shutdown) is not a consume failure: stop (export) the span
                // without an error status, matching the publish/subscriber-invoke emission sites.
                traceHandle.Activity?.Dispose();
            }
            else if (!consumeOutcomeRecorded)
            {
                _TracingError(traceHandle, transportMessage, client.BrokerAddress, e);
            }
        }
        finally
        {
            if (probeEpoch is not null && !probeOutcomeTransferred && circuitKey is not null)
            {
                _circuitBreakerStateManager?.ReleaseHalfOpenProbe(circuitKey, admissionEpoch);
            }
        }
    }
}
