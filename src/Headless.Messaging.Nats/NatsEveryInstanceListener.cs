// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Threading.Channels;
using Headless.Checks;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Transport;
using NATS.Client.Core;

namespace Headless.Messaging.Nats;

/// <summary>
/// Listens for an every-instance NATS consumer client. It reads plain core subscriptions on the Bus subjects, reports a
/// reconnect or a burst of dropped messages to the consumer as a gap, and hands each delivery back to its client.
/// </summary>
internal sealed class NatsEveryInstanceListener
{
    private readonly NatsConsumerClient _owner;
    private readonly string _subscriptionName;
    private readonly TimeProvider _timeProvider;
    private readonly NatsConnection _connection;

    /// <summary>Creates the listener and subscribes to the connection events that report gaps.</summary>
    /// <param name="owner">The client this listener receives for; it owns the pause gate, receive tokens, and dispatch.</param>
    /// <param name="subscriptionName">The consumer identity the subscription is named after.</param>
    /// <param name="timeProvider">The clock the drop-burst window runs on.</param>
    /// <param name="connection">The client's connection, whose events report reconnects and drops.</param>
    public NatsEveryInstanceListener(
        NatsConsumerClient owner,
        string subscriptionName,
        TimeProvider timeProvider,
        NatsConnection connection
    )
    {
        _owner = owner;
        _subscriptionName = subscriptionName;
        _timeProvider = timeProvider;
        _connection = connection;

        connection.ConnectionDisconnected += _OnConnectionDisconnectedAsync;
        connection.ConnectionOpened += _OnConnectionOpenedAsync;
        connection.MessageDropped += _OnMessageDroppedAsync;
    }

    // The core subscriptions an every-instance client is listening on, so a drop reported by the connection can be
    // attributed to them.
    private INatsSub<ReadOnlyMemory<byte>>[] _everyInstanceSubscriptions = [];
    private int _connectionLost;
    private int _droppedSinceSignal;
    private int _dropSignalScheduled;

    /// <summary>
    /// How long a burst of messages dropped on a full subscription channel is collected before the client reports one
    /// gap for all of them, so a slow consumer yields one signal per burst rather than one per dropped message.
    /// </summary>
    internal static readonly TimeSpan DropSignalCoalesceWindow = TimeSpan.FromSeconds(1);

    /// <summary>The <c>error.type</c> of an every-instance delivery the subscription channel dropped.</summary>
    internal const string DroppedOverflowErrorType = "overflow";

    // An every-instance client reads plain NATS subscriptions on the Bus subjects instead of a JetStream consumer:
    // the server keeps nothing for a core subscription once its connection closes, so a crashed process leaves no
    // durable consumer behind, and a publish reaches every subscriber of its subject. Publishing still goes through
    // the stream, which FetchMessageNamesAsync keeps provisioning.
    public async Task ListenAsync(IEnumerable<string> messageNames, CancellationToken cancellationToken)
    {
        var subjects = BuildEveryInstanceSubjects(messageNames, _owner.ResolveShardedMessageNames);
        var subscriptions = new List<INatsSub<ReadOnlyMemory<byte>>>(subjects.Count);

        using var listeningCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            try
            {
                foreach (var subject in subjects)
                {
                    subscriptions.Add(
                        await _connection
                            .SubscribeCoreAsync(
                                subject,
                                serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                                // The SDK ends the subscription when this token is cancelled. It is the token the
                                // subject loops read with, so a loop that sees the channel close can already tell a
                                // shutdown from a lost subscription.
                                cancellationToken: listeningCts.Token
                            )
                            .ConfigureAwait(false)
                    );
                }

                Volatile.Write(ref _everyInstanceSubscriptions, [.. subscriptions]);

                // The server handles a connection's protocol in order, so its PONG proves it registered every SUB
                // above: a message published after readiness cannot miss this process.
                await _connection.PingAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _owner.Ready.TrySetCanceled(cancellationToken);
                throw;
            }
            catch (Exception e)
            {
                var failure = e as BrokerConnectionException ?? new BrokerConnectionException(e);
                _owner.Ready.TrySetException(failure);
                throw failure;
            }

            _owner.Ready.TrySetResult();

            if (subscriptions.Count == 0)
            {
                return;
            }

            var loops = subscriptions.ConvertAll(subscription =>
                _ConsumeCoreSubscriptionAsync(subscription, listeningCts.Token)
            );

            // One subject loop ending means its subscription is gone; stop the siblings so a failure surfaces and
            // the core rebuilds the whole client.
            _ = await Task.WhenAny(loops).ConfigureAwait(false);
            await listeningCts.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(loops).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _everyInstanceSubscriptions, []);

            foreach (var subscription in subscriptions)
            {
                await _UnsubscribeQuietlyAsync(subscription).ConfigureAwait(false);
            }
        }
    }

    private async Task _ConsumeCoreSubscriptionAsync(
        INatsSub<ReadOnlyMemory<byte>> subscription,
        CancellationToken cancellationToken
    )
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _owner.PauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

            NatsMsg<ReadOnlyMemory<byte>> msg;
            using (var receiveLease = _owner.AcquireReceiveLease(cancellationToken))
            {
                try
                {
                    msg = await subscription.Msgs.ReadAsync(receiveLease.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException) when (_owner.PauseGate.IsPaused)
                {
                    continue;
                }
                catch (ChannelClosedException ex)
                {
                    // Cancelling the listening token also completes the channel of every subscription made with it,
                    // and the read can see the closed channel before the cancellation: that is a shutdown, not a fault.
                    if (_owner.IsDisposed || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    // The SDK completes a subscription only when it or its connection ends; either way this client
                    // no longer receives, so fail the listener for the core to rebuild it.
                    throw new BrokerConnectionException(
                        new InvalidOperationException(
                            $"The NATS subscription to '{subscription.Subject}' ended while the consumer was listening.",
                            ex
                        )
                    );
                }
            }

            await _owner
                .DispatchEnvelopeAsync(msg.Headers, msg.Data, jsMsg: null, settlement: msg, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task _UnsubscribeQuietlyAsync(INatsSub<ReadOnlyMemory<byte>> subscription)
    {
        try
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Closing the connection drops the subscription on the server anyway, so a failed UNSUB leaks nothing.
            _owner.OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ExceptionReceived,
                    Reason = $"Failed to unsubscribe NATS subject '{subscription.Subject}': {ex}",
                }
            );
        }
    }

    /// <summary>
    /// The Bus subjects an every-instance client subscribes to: the same subjects a competing client's JetStream
    /// consumers filter on, so both kinds receive the same messages. A subject a '.&gt;' wildcard in the list already
    /// covers is dropped, because two core subscriptions matching one publish deliver it twice to this process.
    /// </summary>
    internal static IReadOnlyList<string> BuildEveryInstanceSubjects(
        IEnumerable<string> messageNames,
        Func<IEnumerable<string>, ISet<string>> resolveShardedMessageNames
    )
    {
        Argument.IsNotNull(messageNames);
        Argument.IsNotNull(resolveShardedMessageNames);

        var names = messageNames.AsIReadOnlyList();

        return
        [
            .. NatsConsumerClient
                .PruneOverlappingSubjects(
                    NatsConsumerClient.BuildConsumerSubjects(names, resolveShardedMessageNames(names))
                )
                .Select(subject => NatsPhysicalAddress.Subject(MessageLane.Bus, subject)),
        ];
    }

    private ValueTask _OnConnectionDisconnectedAsync(object? sender, NatsEventArgs args)
    {
        Volatile.Write(ref _connectionLost, 1);
        return ValueTask.CompletedTask;
    }

    // The SDK reconnects on its own and re-sends every SUB before it raises ConnectionOpened, so the subscriptions
    // are live again here. Messages published while the connection was down never reached them, and the consumer
    // must learn that.
    private async ValueTask _OnConnectionOpenedAsync(object? sender, NatsEventArgs args)
    {
        if (
            Interlocked.Exchange(ref _connectionLost, 0) == 0
            || !_owner.Ready.Task.IsCompletedSuccessfully
            || _owner.IsDisposed
            || _owner.OnReestablished is not { } onReestablished
        )
        {
            return;
        }

        try
        {
            await _connection.PingAsync().ConfigureAwait(false);

            await onReestablished(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A throwing event handler would reach the SDK's event loop; report it instead.
            _owner.OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ExceptionReceived,
                    Reason = $"Reporting a re-established NATS subscription failed: {ex}",
                }
            );
        }
    }

    // A core subscription buffers deliveries in a bounded channel that drops the newest message once it is full, so a
    // consumer slower than the publish rate silently misses invalidations. Each drop counts in the every-instance
    // metric, and a burst of them is reported to the consumer once, like a reconnect, so it can discard state the
    // missed messages would have changed.
    private ValueTask _OnMessageDroppedAsync(object? sender, NatsMessageDroppedEventArgs args)
    {
        if (
            _owner.IsDisposed
            || !Array.Exists(
                Volatile.Read(ref _everyInstanceSubscriptions),
                subscription => ReferenceEquals(subscription, args.Subscription)
            )
        )
        {
            return ValueTask.CompletedTask;
        }

        MessagingMetrics.RecordEveryInstanceDelivery(_subscriptionName, "dropped", DroppedOverflowErrorType);
        Interlocked.Increment(ref _droppedSinceSignal);

        if (Interlocked.CompareExchange(ref _dropSignalScheduled, 1, 0) == 0)
        {
            _SignalDroppedBurstAsync().Forget();
        }

        return ValueTask.CompletedTask;
    }

    private async Task _SignalDroppedBurstAsync()
    {
        try
        {
            await Task.Delay(DropSignalCoalesceWindow, _timeProvider, CancellationToken.None).ConfigureAwait(false);

            // Reopen the window before reading the count: a drop from here on schedules its own signal, which is at
            // worst redundant, while a drop that lands after the count was read is still covered by this signal.
            Volatile.Write(ref _dropSignalScheduled, 0);
            var dropped = Interlocked.Exchange(ref _droppedSinceSignal, 0);

            if (_owner.IsDisposed)
            {
                return;
            }

            _owner.OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.AsyncErrorEvent,
                    Reason = string.Create(
                        CultureInfo.InvariantCulture,
                        $"NATS every-instance consumer '{_subscriptionName}' dropped {dropped} message(s) because its subscription channel was full; reporting the gap to the consumer."
                    ),
                }
            );

            if (_owner.OnReestablished is { } onReestablished)
            {
                await onReestablished(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _owner.OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ExceptionReceived,
                    Reason = $"Reporting dropped NATS every-instance messages failed: {ex}",
                }
            );
        }
    }
}
