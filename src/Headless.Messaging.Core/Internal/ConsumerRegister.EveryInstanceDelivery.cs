// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Retry;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

/// <summary>
/// Delivery for every-instance subscriptions, which is at most once and keeps no state, plus the queue that runs their
/// <see cref="IOnSubscriptionEstablished"/> hooks in establishment order.
/// </summary>
internal sealed partial class ConsumerRegister
{
    // The establishment hooks of each every-instance subscription in this process, keyed by handle name. Kept across
    // rebuilds on purpose: the generation count is what tells a consumer that an earlier subscription existed, so
    // messages published between the two may never have arrived.
    private readonly ConcurrentDictionary<string, EstablishmentChain> _establishments = new(StringComparer.Ordinal);

    /// <summary>
    /// Raises one establishment of an every-instance subscription: at startup, after each rebuild, and when the client
    /// reports that it recovered on its own. The hooks run off the caller, after the previous establishment's hooks of
    /// the same subscription, so they keep establishment order without anyone holding the restart gate for them. The
    /// returned task completes when this establishment's hooks finish or time out, and never faults.
    /// </summary>
    private Task _RaiseSubscriptionEstablished(
        string handleName,
        IReadOnlyList<ConsumerExecutorDescriptor> descriptors,
        CancellationToken cancellationToken
    )
    {
        return _establishments
            .GetOrAdd(handleName, static _ => new EstablishmentChain())
            .Append((previous, generation) => _NotifyAfterAsync(previous, descriptors, generation, cancellationToken));
    }

    private async Task _NotifyAfterAsync(
        Task previous,
        IReadOnlyList<ConsumerExecutorDescriptor> descriptors,
        long generation,
        CancellationToken cancellationToken
    )
    {
        await previous.ConfigureAwait(false);

        try
        {
            await _NotifySubscriptionEstablishedAsync(descriptors, generation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client generation stopped; a later establishment raises its own hooks.
        }
    }

    /// <summary>
    /// Calls <see cref="IOnSubscriptionEstablished"/> on every consumer class of one every-instance subscription for
    /// one establishment, each bounded by <see cref="MessagingOptions.SubscriptionEstablishedTimeout"/>.
    /// </summary>
    private async Task _NotifySubscriptionEstablishedAsync(
        IReadOnlyList<ConsumerExecutorDescriptor> descriptors,
        long generation,
        CancellationToken cancellationToken
    )
    {
        // Only a generated consumer class that implements the hook carries one; a runtime subscription never does.
        foreach (
            var consumer in descriptors
                .Where(static x => x.OnSubscriptionEstablished is not null)
                .GroupBy(static x => x.ConsumerType)
        )
        {
            var onSubscriptionEstablished = consumer.First().OnSubscriptionEstablished!;
            var identity = consumer.First().ConsumerIdentity!;
            var context = new SubscriptionEstablishedContext(
                identity,
                [.. consumer.Select(static x => x.MessageName).Distinct(StringComparer.Ordinal)],
                IsReconnect: generation > 1,
                generation
            );

            cancellationToken.ThrowIfCancellationRequested();
            var timeout = _options.SubscriptionEstablishedTimeout;
            using var timeoutCts = new CancellationTokenSource(timeout, _timeProvider);
            using var hookCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var hook = _InvokeSubscriptionEstablishedAsync(onSubscriptionEstablished, context, hookCts.Token);

            try
            {
                // Waits on the token too, so a hook that ignores it still releases the establishments queued behind it.
                await hook.WaitAsync(hookCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                hook.Forget();
                throw;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                hook.Forget();
                _logger.SubscriptionEstablishedHookTimedOut(LogSanitizer.Sanitize(identity), timeout, generation);
            }
            catch (Exception ex)
            {
                _logger.SubscriptionEstablishedHookFailed(ex, LogSanitizer.Sanitize(identity), generation);
            }
        }
    }

    private async Task _InvokeSubscriptionEstablishedAsync(
        SubscriptionEstablishedDispatch onSubscriptionEstablished,
        SubscriptionEstablishedContext context,
        CancellationToken cancellationToken
    )
    {
        // The generated call builds the consumer the way its delivery dispatch does, preferring the scope's own
        // registration, and releases only an instance it created.
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        await onSubscriptionEstablished(scope.ServiceProvider, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Delivers one message of an every-instance subscription: receive middleware, the contract-version check, and
    /// deserialization run as on the durable path, then the consumer runs in a fresh scope and the message is committed.
    /// </summary>
    /// <remarks>
    /// Delivery is at most once by design, so nothing durable is involved: no inbox row or admission, no reservation or
    /// lease, no retry pipeline, no circuit breaker, and no dashboard row. A consumer failure, a receive-stage reject,
    /// and a message no consumer on the subscription handles are logged, counted, and committed; none is requeued,
    /// because a per-process subscription has no one else to redeliver to and a poison message would loop forever.
    /// </remarks>
    private async Task _OnEveryInstanceMessageAsync(
        IConsumerClient client,
        ConsumerSubscriptionKey subscriptionKey,
        TransportMessage transportMessage,
        object? sender,
        CancellationToken hostShutdownToken
    )
    {
        var commitAttempted = false;
        var consumeOutcomeRecorded = false;
        MessagingTraceHandle traceHandle = default;
        var lane = subscriptionKey.Lane;
        var consumerIdentity = subscriptionKey.SubscriptionName;

        try
        {
            var name = transportMessage.Name;
            _selector.TryGetMessageNameExecutor(name, subscriptionKey, out var executor);
            consumerIdentity = executor?.ResolvedConsumerIdentity ?? consumerIdentity;

            // Replaces whatever the publisher sent, so the header always names the consumer that received it.
            transportMessage.Headers[Headers.ConsumerIdentity] = consumerIdentity;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.MessageReceived(
                    LogSanitizer.Sanitize(transportMessage.Id),
                    LogSanitizer.Sanitize(transportMessage.Name)
                );
            }

            traceHandle = _TracingBefore(transportMessage, lane, _serverAddress);

            if (executor is null)
            {
                var notFound = new SubscriberNotFoundException(
                    $"Message can not be found subscriber. Name:{LogSanitizer.Sanitize(name)}, "
                        + $"Subscription:{LogSanitizer.Sanitize(subscriptionKey.SubscriptionName)}."
                );
                _DropEveryInstanceMessage(transportMessage, consumerIdentity, notFound, notFound.Message);
                _TracingError(traceHandle, transportMessage, client.BrokerAddress, notFound);
                consumeOutcomeRecorded = true;
            }
            else
            {
                consumeOutcomeRecorded = await _ReceiveAndConsumeEveryInstanceAsync(
                        client,
                        subscriptionKey,
                        executor,
                        transportMessage,
                        traceHandle,
                        hostShutdownToken
                    )
                    .ConfigureAwait(false);
            }

            // Settlement is must-complete: never abandon a commit on host shutdown.
            commitAttempted = true;
            await client.CommitAsync(sender, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (hostShutdownToken.IsCancellationRequested && !commitAttempted)
        {
            // The only reject: the consumer was stopped mid-delivery, not failed by the message, so this process may
            // still take it if its subscription survives the stop.
            _logger.LogProcessReceivedMessageFailed(e, transportMessage);
            await client.RejectAsync(sender, CancellationToken.None).ConfigureAwait(false);
            traceHandle.Activity?.Dispose();
        }
        catch (Exception e)
        {
            // A fault outside the consumer is the core's or the transport's, so a redelivery would fault the same way:
            // a requeue on a per-process subscription only loops. Commit instead, like a consumer failure.
            _logger.EveryInstanceDeliveryFaulted(
                e,
                LogSanitizer.Sanitize(consumerIdentity),
                LogSanitizer.Sanitize(transportMessage.Headers.TryGetValue(Headers.MessageId, out var id) ? id : null),
                LogSanitizer.Sanitize(
                    transportMessage.Headers.TryGetValue(Headers.MessageName, out var messageName) ? messageName : null
                )
            );

            if (!consumeOutcomeRecorded)
            {
                MessagingMetrics.RecordEveryInstanceDelivery(consumerIdentity, "dropped", e.GetType().FullName);
                _TracingError(traceHandle, transportMessage, client.BrokerAddress, e);
            }

            if (!commitAttempted)
            {
                await _CommitFaultedEveryInstanceMessageAsync(client, transportMessage, sender).ConfigureAwait(false);
            }
        }
    }

    private async Task _CommitFaultedEveryInstanceMessageAsync(
        IConsumerClient client,
        TransportMessage transportMessage,
        object? sender
    )
    {
        try
        {
            await client.CommitAsync(sender, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogProcessReceivedMessageFailed(ex, transportMessage);
        }
    }

    /// <summary>
    /// Runs the receive stage and the consumer of one every-instance delivery. Returns whether the consume outcome was
    /// recorded on the trace; an exception it lets escape is a cancellation or a fault outside the consumer.
    /// </summary>
    private async Task<bool> _ReceiveAndConsumeEveryInstanceAsync(
        IConsumerClient client,
        ConsumerSubscriptionKey subscriptionKey,
        ConsumerExecutorDescriptor executor,
        TransportMessage transportMessage,
        MessagingTraceHandle traceHandle,
        CancellationToken hostShutdownToken
    )
    {
        var consumerIdentity = executor.ResolvedConsumerIdentity;
        ReceiveRingOutcome receiveOutcome;

        try
        {
            receiveOutcome = await _RunReceiveRingAsync(
                    transportMessage,
                    executor,
                    subscriptionKey.Lane,
                    _RunInnerReceiveAsync,
                    hostShutdownToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A contract-version mismatch or a deserialization failure with no receive middleware to convert it.
            receiveOutcome = new ReceiveRingOutcome(ReceiveRingResult.Rejected, Exception: ex);
        }

        switch (receiveOutcome.Result)
        {
            case ReceiveRingResult.Skipped:
                MessagingMetrics.RecordReceiveOutcome(MessagingMetrics.ReceiveOutcomeSkipped);
                MessagingMetrics.RecordEveryInstanceDelivery(consumerIdentity, "skipped");
                traceHandle.Activity?.SetTag(
                    MessagingMetrics.TagReceiveOutcome,
                    MessagingMetrics.ReceiveOutcomeSkipped
                );
                _TracingAfter(traceHandle, transportMessage, _serverAddress);
                return true;
            case ReceiveRingResult.Cancelled:
                MessagingMetrics.RecordReceiveOutcome(MessagingMetrics.ReceiveOutcomeCancelled);
                traceHandle.Activity?.SetTag(
                    MessagingMetrics.TagReceiveOutcome,
                    MessagingMetrics.ReceiveOutcomeCancelled
                );
                throw receiveOutcome.Exception!;
            case ReceiveRingResult.Rejected:
            {
                var reason = receiveOutcome.OutcomeReason ?? receiveOutcome.Exception!.ExpandMessage();
                MessagingMetrics.RecordReceiveOutcome(MessagingMetrics.ReceiveOutcomeRejected);
                traceHandle.Activity?.SetTag(
                    MessagingMetrics.TagReceiveOutcome,
                    MessagingMetrics.ReceiveOutcomeRejected
                );
                _DropEveryInstanceMessage(transportMessage, consumerIdentity, receiveOutcome.Exception, reason);
                _TracingError(traceHandle, transportMessage, client.BrokerAddress, receiveOutcome.Exception!);
                return true;
            }
        }

        MessagingMetrics.RecordReceiveOutcome(MessagingMetrics.ReceiveOutcomeAccepted);
        traceHandle.Activity?.SetTag(MessagingMetrics.TagReceiveOutcome, MessagingMetrics.ReceiveOutcomeAccepted);

        var delivery = new MediumMessage
        {
            StorageId = Guid.Empty,
            Origin = receiveOutcome.Message!,
            Content = string.Empty,
            Lane = subscriptionKey.Lane,
            Added = _timeProvider.GetUtcNow(),
        };

        try
        {
            // The invoker opens a fresh scope and runs the consume middleware, then the generated dispatch or the
            // runtime handler, exactly as a durable delivery's final attempt would.
            await _subscribeInvoker
                .InvokeAsync(new ConsumerContext(executor, delivery), hostShutdownToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (hostShutdownToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable ERP022 // False positive: the failure is logged and counted; every-instance delivery commits a failed message by design.
        catch (Exception ex)
        {
            var failure = RetryExceptionClassifier.Unwrap(ex);
            _logger.EveryInstanceConsumerFailed(
                failure,
                LogSanitizer.Sanitize(consumerIdentity),
                LogSanitizer.Sanitize(transportMessage.Id),
                LogSanitizer.Sanitize(transportMessage.Name)
            );
            MessagingMetrics.RecordEveryInstanceDelivery(consumerIdentity, "failed", failure.GetType().FullName);
            _TracingError(traceHandle, transportMessage, client.BrokerAddress, failure);
            return true;
        }
#pragma warning restore ERP022

        MessagingMetrics.RecordEveryInstanceDelivery(consumerIdentity, "succeeded");
        _TracingAfter(traceHandle, transportMessage, _serverAddress);
        return true;
    }

    private void _DropEveryInstanceMessage(
        TransportMessage transportMessage,
        string consumerIdentity,
        Exception? exception,
        string reason
    )
    {
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.EveryInstanceMessageDropped(
                exception is ReceiveMessageRejectedException ? null : exception,
                LogSanitizer.Sanitize(consumerIdentity),
                LogSanitizer.Sanitize(transportMessage.Id),
                LogSanitizer.Sanitize(transportMessage.Name),
                LogSanitizer.Sanitize(reason)
            );
        }

        MessagingMetrics.RecordEveryInstanceDelivery(consumerIdentity, "dropped", exception?.GetType().FullName);
    }

    /// <summary>The establishments of one every-instance subscription: their count and the tail of their hooks.</summary>
    private sealed class EstablishmentChain
    {
        private readonly Lock _sync = new();
        private long _generation;
        private Task _tail = Task.CompletedTask;

        /// <summary>
        /// Appends one establishment and returns its link; <paramref name="notify"/> receives the previous link and the
        /// new generation, and runs on the thread pool so no hook runs on the raising thread under the lock.
        /// </summary>
        public Task Append(Func<Task, long, Task> notify)
        {
            lock (_sync)
            {
                var previous = _tail;
                var generation = ++_generation;
                var link = Task.Run(() => notify(previous, generation), CancellationToken.None);
                _tail = link;
                return link;
            }
        }
    }
}
