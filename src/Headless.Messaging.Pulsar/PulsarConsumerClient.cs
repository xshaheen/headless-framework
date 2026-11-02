// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Collections.Frozen;
using System.Reflection;
using DotPulsar;
using DotPulsar.Abstractions;
using DotPulsar.Extensions;
using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Pulsar;

internal sealed class PulsarConsumerClient : IConsumerClient
{
    private const string _TransportDisplayName = "Pulsar";

    // Bounded drain budget on shutdown, matching the other transports. A handler still running past it never
    // acknowledges, so the broker redelivers its message once this consumer disconnects (at-least-once).
    private static readonly TimeSpan _ShutdownDrainTimeout = TimeSpan.FromSeconds(30);

    // How long a subscription may take to start receiving before SubscribeAsync gives up on it.
    private static readonly TimeSpan _SubscribeTimeout = TimeSpan.FromSeconds(30);

    private readonly IConnectionFactory _connection;
    private readonly ConsumerClientRequest _request;
    private readonly PulsarConsumerConfig? _config;
    private readonly Func<IReadOnlyDictionary<string, string?>, byte[], TransportMessage>? _transportMessageFactory;
    private readonly IServiceProvider? _serviceProvider;
    private readonly Func<string, IPulsarReceiver> _receiverFactory;
    private readonly ILogger? _clientLogger;
    private readonly string _subscription;
    private readonly byte _groupConcurrent;
    private readonly bool _everyInstance;
    private readonly SemaphoreSlim _semaphore;
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly BrokerReachabilityReporter _reachability = new();
#pragma warning disable CA2213 // Disposing a transition gate can race queued pause/resume callers during shutdown.
    private readonly SemaphoreSlim _pauseResumeLock = new(1, 1);
#pragma warning restore CA2213
    private readonly Lock _receiveLock = new();
    private readonly Lock _connectionStateLock = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _timeProvider;

    // Tracks in-flight handler tasks so shutdown can let them settle before it closes the receivers.
    private readonly InFlightHandlerTracker _inFlightHandlers = new();
    private int _disposed;
    private CancellationTokenSource? _receiveCts = new();
    private readonly PulsarMessagingOptions _pulsarOptions;
    private readonly TransportEnvelopeGate _envelopeGate;

    // Keyed by the subscribed topic's complete, partition-free name, the form a delivery's topic normalizes to.
    private volatile FrozenDictionary<string, string> _arrivalNames = FrozenDictionary<string, string>.Empty;
    private List<IPulsarReceiver> _receivers = [];
    private bool[] _disconnected = [];
    private Func<CancellationToken, Task>? _onReestablished;

    /// <summary>Creates a consumer client for one subscription.</summary>
    /// <param name="options">The transport options.</param>
    /// <param name="connection">The shared client and producer cache; a dead-letter policy publishes through it.</param>
    /// <param name="request">The subscription this client consumes.</param>
    /// <param name="config">The consumer's tuned Pulsar options, if any.</param>
    /// <param name="timeProvider">
    /// The clock of the receive back-off and redelivery delays. Both wait real time, so production passes none and gets
    /// the system clock; tests pass a fake one.
    /// </param>
    /// <param name="transportMessageFactory">Builds the transport message; tests use it to fail the envelope.</param>
    /// <param name="serviceProvider">The provider a custom headers builder receives.</param>
    /// <param name="receiverFactory">Opens the receiver of one topic; tests replace the broker with it.</param>
    /// <param name="clientLogger">Logs each receiver state change when client logging is on.</param>
    public PulsarConsumerClient(
        IOptions<PulsarMessagingOptions> options,
        IConnectionFactory connection,
        ConsumerClientRequest request,
        PulsarConsumerConfig? config = null,
        TimeProvider? timeProvider = null,
        Func<IReadOnlyDictionary<string, string?>, byte[], TransportMessage>? transportMessageFactory = null,
        IServiceProvider? serviceProvider = null,
        Func<string, IPulsarReceiver>? receiverFactory = null,
        ILogger? clientLogger = null
    )
    {
        _request = Argument.IsNotNull(request);
        _connection = connection;
        _config = config;
        _transportMessageFactory = transportMessageFactory;
        _serviceProvider = serviceProvider;
        _clientLogger = clientLogger;
        _pulsarOptions = _RequireServiceProviderForHeadersBuilder(options.Value, serviceProvider);
        _envelopeGate = serviceProvider?.GetService<TransportEnvelopeGate>() ?? TransportEnvelopeGate.Strict;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _subscription = PulsarPhysicalAddress.Subscription(request);
        _groupConcurrent = request.Concurrency;
        _everyInstance = request.Kind is ConsumerSubscriptionKind.EveryInstance;
        _semaphore = new SemaphoreSlim(request.Concurrency);
        _receiverFactory = receiverFactory ?? _CreateReceiver;
        Redelivery = new PulsarRedeliveryTracker(
            _pulsarOptions.NegativeAckRedeliveryDelay,
            config?.EffectiveAckTimeout,
            config?.MaxRedeliveryCount,
            _DeadLetterAsync,
            e => _LogConsumeError($"Pulsar redelivery request failed; the broker redelivers on close: {e.Message}"),
            _timeProvider
        );
    }

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog)
    {
        OnMessageCallback = onMessage;
        OnLogCallback = onLog;
    }

    public void AttachReestablishedCallback(Func<CancellationToken, Task>? onReestablished)
    {
        Volatile.Write(ref _onReestablished, onReestablished);
    }

    public BrokerAddress BrokerAddress => new("pulsar", BrokerAddressDisplay.Format(_pulsarOptions.ServiceUrl));

    /// <summary>Gets the tracker of rejected and unacknowledged messages.</summary>
    internal PulsarRedeliveryTracker Redelivery { get; }

    public async ValueTask SubscribeAsync(IEnumerable<string> topics, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(topics);

        var names = topics.ToList();
        IndexArrivalNames(names);

        var physicalTopics = names
            .Select(name => PulsarPhysicalAddress.Topic(_request.Lane, name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        using var timeout = _SubscribeTimeout.ToCancellationTokenSource(cancellationToken);
        var receivers = new List<IPulsarReceiver>(physicalTopics.Count);

        try
        {
            foreach (var topic in physicalTopics)
            {
                receivers.Add(_receiverFactory(topic));
            }

            // Creating a consumer or reader only starts it: DotPulsar subscribes in the background and retries a broker
            // rejection such as a subscription-type mismatch, so readiness waits until every receiver receives.
            foreach (var receiver in receivers)
            {
                await _WaitUntilConnectedAsync(receiver, timeout.Token, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            foreach (var receiver in receivers)
            {
                await _DisposeQuietlyAsync(receiver).ConfigureAwait(false);
            }

            throw;
        }

        _receivers = receivers;
        _disconnected = new bool[receivers.Count];
        _ready.TrySetResult();
    }

    private static async Task _WaitUntilConnectedAsync(
        IPulsarReceiver receiver,
        CancellationToken timeoutToken,
        CancellationToken callerToken
    )
    {
        var state = PulsarLinkState.Disconnected;

        try
        {
            while (state is not PulsarLinkState.Connected)
            {
                state = await receiver.WaitForLinkChangeAsync(state, timeoutToken).ConfigureAwait(false);

                if (state is PulsarLinkState.Ended)
                {
                    throw new BrokerConnectionException(
                        new InvalidOperationException(
                            $"The Pulsar subscription on '{receiver.Topic}' faulted before it started receiving."
                        )
                    );
                }
            }
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new BrokerConnectionException(
                new TimeoutException(
                    $"The Pulsar subscription on '{receiver.Topic}' did not start receiving within {_SubscribeTimeout}."
                )
            );
        }
    }

    private IPulsarReceiver _CreateReceiver(string topic)
    {
        var client = _connection.RentClient();
        var consumerName = Assembly.GetEntryAssembly()?.GetName().Name?.ToLowerInvariant();

        if (_everyInstance)
        {
            // An every-instance subscription belongs to this process alone: exclusive, so no other consumer can attach
            // to it, and non-durable, so the broker keeps no cursor for it once the reader disconnects or the process
            // dies. With no backlog to replay, it starts at the latest message.
            var readerBuilder = client
                .NewReader()
                .Topic(topic)
                .SubscriptionName(_subscription)
                .StartMessageId(MessageId.Latest);

            if (consumerName is not null)
            {
                readerBuilder.ReaderName(consumerName);
            }

            if (_clientLogger is { } readerLogger)
            {
                readerBuilder.StateChangedHandler(changed =>
                    readerLogger.LogPulsarReaderState(topic, changed.ReaderState)
                );
            }

            return new PulsarReaderReceiver(readerBuilder.Create(), topic);
        }

        var builder = client
            .NewConsumer()
            .Topic(topic)
            .SubscriptionName(_subscription)
            .SubscriptionType(SubscriptionTypeFor(_config))
            .InitialPosition(SubscriptionInitialPosition.Latest);

        if (consumerName is not null)
        {
            builder.ConsumerName(consumerName);
        }

        if (_clientLogger is { } logger)
        {
            builder.StateChangedHandler(changed => logger.LogPulsarConsumerState(topic, changed.ConsumerState));
        }

        return new PulsarConsumerReceiver(builder.Create(), topic);
    }

    /// <summary>Returns the subscription type a competing consumer with <paramref name="config"/> opens.</summary>
    /// <remarks>
    /// A Key_Shared subscription without key-shared metadata gets the broker's auto-split policy: each attached consumer
    /// owns a share of the key hash range, rebalanced as consumers come and go, so replicas need no sticky ranges.
    /// </remarks>
    internal static SubscriptionType SubscriptionTypeFor(PulsarConsumerConfig? config) =>
        config is { KeyShared: true } ? SubscriptionType.KeyShared : SubscriptionType.Shared;

    internal static string GetSubscriptionName(string subscriptionName, MessageLane lane)
    {
        return PulsarPhysicalAddress.Subscription(lane, subscriptionName);
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        // The receive loop is drained with the handlers, so shutdown closes the receivers only after the loop stopped
        // dispatching: a message the loop already received past its shutdown check still gets its handler tracked and
        // waited for.
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inFlightHandlers.TryTrack(listening.Task))
        {
            return;
        }

        try
        {
            await _ReceiveLoopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            listening.TrySetResult();
        }
    }

    private async Task _ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var receivers = _receivers;
        if (receivers.Count == 0)
        {
            return;
        }

        var retryDelay = TimeSpan.FromMilliseconds(200);
        CancellationTokenSource? linkedReceiveCts = null;
        var receiveToken = CancellationToken.None;

        // One receive is outstanding per receiver; a message that arrives while a pause cancels the others waits here
        // and is dispatched first once the consumer resumes.
        var pending = new Task<IMessage<ReadOnlySequence<byte>>>?[receivers.Count];
        var ready = new Queue<PulsarDelivery>();

        // The listener's cancellation ends the loop and the background work without waiting on the SDK, so a stop that
        // waits for the SDK to honor it cannot outlive the shutdown budget.
        using var backgroundCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var background = _StartBackgroundWork(receivers, backgroundCts.Token);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PulsarDelivery delivery;
                try
                {
                    await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                    CancellationToken currentReceiveToken;
                    lock (_receiveLock)
                    {
                        if (_receiveCts is null)
                        {
                            break;
                        }

                        currentReceiveToken = _receiveCts.Token;
                    }

                    if (currentReceiveToken != receiveToken)
                    {
                        linkedReceiveCts?.Dispose();
                        linkedReceiveCts = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken,
                            currentReceiveToken
                        );
                        receiveToken = currentReceiveToken;
                    }

                    if (ready.TryDequeue(out var queued))
                    {
                        delivery = queued;
                    }
                    else
                    {
                        delivery = await _ReceiveAnyAsync(
                                receivers,
                                pending,
                                linkedReceiveCts!.Token,
                                cancellationToken
                            )
                            .ConfigureAwait(false);

                        // A receive can complete after a pause began; its message waits for the resume like the rest.
                        // Shutdown instead dispatches it, and waits for its handler, as the broker already sent it.
                        if (receiveToken.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
                        {
                            ready.Enqueue(delivery);
                            throw new OperationCanceledException(receiveToken);
                        }
                    }

                    retryDelay = TimeSpan.FromMilliseconds(200);
                    _reachability.ReportSuccess(OnLogCallback);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // A message an abandoned receive still hands over stays unacknowledged, and the broker redelivers
                    // it once this consumer closes.
                    _ObserveAbandoned(pending);
                    break;
                }
                catch (OperationCanceledException) when (receiveToken.IsCancellationRequested)
                {
                    await _CollectCanceledAsync(receivers, pending, ready, cancellationToken).ConfigureAwait(false);

                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        break;
                    }

                    continue;
                }
                catch (Exception e)
                {
                    if (receivers.Exists(static receiver => receiver.HasEnded))
                    {
                        // DotPulsar recovers a lost connection on its own; an ended receiver never delivers again, so
                        // the listener fails and the core rebuilds the client.
                        _ObserveAbandoned(pending);
                        throw new BrokerConnectionException(e);
                    }

                    _reachability.ReportFailure(OnLogCallback, e);
                    retryDelay = _NextBackoff(retryDelay);
                    await _timeProvider.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                Redelivery.OnReceived(delivery);

                if (_groupConcurrent > 0)
                {
                    try
                    {
                        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
                    {
                        // Shutdown raced the concurrency-gate wait: the message stays unacknowledged, and the broker
                        // redelivers it once this consumer closes.
                        break;
                    }

                    var handlerTask = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await _ConsumeAsync(delivery).ConfigureAwait(false);
                            }
                            finally
                            {
                                _ReleaseSemaphore();
                            }
                        },
                        CancellationToken.None // Ensure semaphore release even if cancellation is requested during handler execution
                    );

                    _inFlightHandlers.Track(handlerTask);
                    _ObserveBackgroundHandler(handlerTask);
                }
                else
                {
                    var inlineTask = _ConsumeAsync(delivery);
                    _inFlightHandlers.Track(inlineTask);
                    await inlineTask.ConfigureAwait(false);
                }
            }
        }
        finally
        {
            linkedReceiveCts?.Dispose();
            await backgroundCts.CancelAsync().ConfigureAwait(false);
            await background.ConfigureAwait(false);
        }
    }

    private static async Task<PulsarDelivery> _ReceiveAnyAsync(
        List<IPulsarReceiver> receivers,
        Task<IMessage<ReadOnlySequence<byte>>>?[] pending,
        CancellationToken receiveToken,
        CancellationToken cancellationToken
    )
    {
        for (var i = 0; i < receivers.Count; i++)
        {
            pending[i] ??= receivers[i].ReceiveAsync(receiveToken).AsTask();
        }

        var completed =
            pending.Length == 1
                ? pending[0]!
                : await Task.WhenAny(pending!).WaitAsync(cancellationToken).ConfigureAwait(false);
        await ((Task)completed).WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (!completed.IsCompleted)
        {
            // Only the listener's cancellation leaves the receive running, and the caller abandons it.
            throw new OperationCanceledException(cancellationToken);
        }

        var index = Array.IndexOf(pending, completed);
        pending[index] = null;

        return new PulsarDelivery(receivers[index], await completed.ConfigureAwait(false));
    }

    // A pause cancels every outstanding receive; one that completed with a message before it saw the cancellation keeps
    // its message for the resumed loop rather than losing it until the consumer closes.
    private static async Task _CollectCanceledAsync(
        List<IPulsarReceiver> receivers,
        Task<IMessage<ReadOnlySequence<byte>>>?[] pending,
        Queue<PulsarDelivery> ready,
        CancellationToken cancellationToken
    )
    {
        var outstanding = pending.OfType<Task>().ToArray();
        if (outstanding.Length > 0)
        {
            await Task.WhenAll(outstanding)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        for (var i = 0; i < pending.Length; i++)
        {
            var task = pending[i];
            if (task?.IsCompleted != true)
            {
                continue;
            }

            pending[i] = null;
            if (task.IsCompletedSuccessfully)
            {
                ready.Enqueue(new PulsarDelivery(receivers[i], await task.ConfigureAwait(false)));
            }
            else
            {
                _ = task.Exception;
            }
        }
    }

    private async Task _ConsumeAsync(PulsarDelivery delivery)
    {
        var currentMessage = delivery.Message;
        var headers = new Dictionary<string, string?>(currentMessage.Properties.Count, StringComparer.Ordinal);
        foreach (var header in currentMessage.Properties)
        {
            headers.Add(header.Key, header.Value);
        }

        if (_pulsarOptions.CustomHeadersBuilder is { } customHeadersBuilder)
        {
            try
            {
                foreach (var customHeader in customHeadersBuilder(currentMessage, _serviceProvider!))
                {
                    headers[customHeader.Key] = customHeader.Value;
                }
            }
            catch (Exception ex)
            {
                // A builder is user code that may depend on a service that is briefly down, so its failure says nothing
                // about the message: the broker redelivers it, and a dead-letter policy bounds the retries.
                OnLogCallback!(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ConsumeError,
                        Reason =
                            $"Pulsar custom headers builder failed; message negatively acknowledged for redelivery: {ex.GetType().Name}",
                    }
                );

                Redelivery.OnRejected(delivery);
                return;
            }
        }

        TransportMessage message;
        try
        {
            // Stamped after the custom headers builder so neither the wire nor the builder can choose the address.
            headers[Headers.TransportAddress] = PulsarTopicName.Complete(delivery.DeliveryTopic);

            var body = currentMessage.Data.ToArray();
            message = _transportMessageFactory is null
                ? new TransportMessage(headers, body)
                : _transportMessageFactory(headers, body);

            _envelopeGate.Admit(
                message.Headers,
                new EnvelopeArrival(_TransportDisplayName, _request.Lane, ArrivalName(delivery.DeliveryTopic)),
                delivery,
                static current => NativeFacts(current)
            );
        }
        catch (Exception ex)
        {
            OnLogCallback!(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason = $"Malformed Pulsar transport envelope terminally acknowledged: {ex.GetType().Name}",
                }
            );

            // Settlement is must-complete: acknowledge malformed broker data so it cannot redeliver forever.
            await _AcknowledgeAsync(delivery).ConfigureAwait(false);
            return;
        }

        try
        {
            await OnMessageCallback!(message, delivery).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The core settles every handler outcome itself, so its callback throws only when that settlement failed or
            // the core broke. Nothing else settles the message then, and with no ack timeout the live subscription never
            // redelivers an unsettled one, so it is released for redelivery here.
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason = $"Error consuming message; negatively acknowledged for redelivery: {ex}",
                }
            );

            Redelivery.OnRejected(delivery);
        }
    }

    private static void _ObserveAbandoned(Task?[] tasks)
    {
        foreach (var task in tasks)
        {
            _ObserveAbandoned(task);
        }
    }

    private static void _ObserveAbandoned(Task? task)
    {
        _ = task?.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    // DotPulsar recovers a lost connection on its own and resubscribes while a receive waits quietly, so each
    // receiver's state is how the host learns of an outage. A non-durable subscription also keeps nothing for the time
    // it was away, so the core must learn about each recovery to tell the every-instance consumer.
    private Task _StartBackgroundWork(List<IPulsarReceiver> receivers, CancellationToken cancellationToken)
    {
        var work = new List<Task>(receivers.Count + 1) { Redelivery.RunAsync(cancellationToken) };

        for (var i = 0; i < receivers.Count; i++)
        {
            var index = i;
            work.Add(
                PulsarReconnectMonitor.RunAsync(
                    receivers[i],
                    _everyInstance
                        ? token => Volatile.Read(ref _onReestablished)?.Invoke(token) ?? Task.CompletedTask
                        : null,
                    connected => _ReportConnectionState(index, connected),
                    e => _LogConsumeError($"Pulsar reconnect monitor failed: {e.Message}"),
                    cancellationToken
                )
            );
        }

        return Task.WhenAll(work);
    }

    private void _ReportConnectionState(int index, bool connected)
    {
        bool allConnected;
        lock (_connectionStateLock)
        {
            _disconnected[index] = !connected;
            allConnected = !_disconnected.Contains(true);
        }

        if (!connected)
        {
            _reachability.ReportFailure(
                OnLogCallback,
                "The Pulsar consumer lost its broker connection and is reconnecting."
            );
        }
        else if (allConnected)
        {
            _reachability.ReportSuccess(OnLogCallback);
        }
    }

    public async ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
    {
        await _AcknowledgeAsync((PulsarDelivery)sender!).ConfigureAwait(false);
    }

    private async Task _AcknowledgeAsync(PulsarDelivery delivery)
    {
        Redelivery.OnAcknowledged(delivery);
        await delivery.Receiver.AcknowledgeAsync(delivery.MessageId, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Records the topic of each subscribed message name, so <see cref="ArrivalName"/> can map a delivery's topic back
    /// to its message. It uses the same address function the subscription uses, so it never parses a name out of a
    /// topic. A lane with no raw route keeps the lookup empty, because header routing alone serves it, so its deliveries
    /// skip the topic parse. A topic two names resolve to, such as <c>orders</c> and <c>public/default/orders</c>, names no one
    /// message, so it is left out.
    /// </summary>
    internal void IndexArrivalNames(IEnumerable<string> names)
    {
        if (!_envelopeGate.HasRawRoutes(_request.Lane))
        {
            _arrivalNames = FrozenDictionary<string, string>.Empty;
            return;
        }

        _arrivalNames = names
            .Distinct(StringComparer.Ordinal)
            .GroupBy(
                name => PulsarTopicName.Normalize(PulsarPhysicalAddress.Topic(_request.Lane, name)),
                StringComparer.Ordinal
            )
            .Where(static group => group.Take(2).Count() == 1)
            .ToFrozenDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns the message a delivery on <paramref name="topic"/> belongs to: the subscribed message whose topic it is,
    /// or <see langword="null"/> when this client did not subscribe it.
    /// </summary>
    internal string? ArrivalName(string topic)
    {
        var arrivalNames = _arrivalNames;

        return arrivalNames.Count > 0 && arrivalNames.TryGetValue(PulsarTopicName.Normalize(topic), out var name)
            ? name
            : null;
    }

    /// <summary>
    /// Reads the broker metadata that identifies a delivery which carries no message id. Pulsar has no producer
    /// idempotency key a consumer can read, so the identity is the stored message's position: a ledger id is unique
    /// across the cluster and never reused, so the position stays the same on every redelivery and differs after the
    /// topic is recreated.
    /// </summary>
    internal static NativeEnvelopeFacts NativeFacts(PulsarDelivery delivery)
    {
        var id = delivery.MessageId;

        var position = NativeEnvelopeFacts.Position(
            "pulsar",
            PulsarTopicName.Normalize(delivery.DeliveryTopic),
            id.LedgerId.ToString(CultureInfo.InvariantCulture),
            id.EntryId.ToString(CultureInfo.InvariantCulture),
            id.Partition.ToString(CultureInfo.InvariantCulture),
            id.BatchIndex.ToString(CultureInfo.InvariantCulture)
        );

        var publishTime = delivery.Message.PublishTime;
        DateTimeOffset? storedAt = publishTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)publishTime) : null;

        return new NativeEnvelopeFacts(ProducerMessageId: null, position, storedAt);
    }

    private static PulsarMessagingOptions _RequireServiceProviderForHeadersBuilder(
        PulsarMessagingOptions options,
        IServiceProvider? serviceProvider
    )
    {
        if (options.CustomHeadersBuilder is not null && serviceProvider is null)
        {
            throw new InvalidOperationException(
                "A Pulsar consumer client with a CustomHeadersBuilder needs the service provider the builder receives."
            );
        }

        return options;
    }

    // The consumer holds a rejected message back for NegativeAckRedeliveryDelay itself, so the delivery is released at
    // once rather than held for the core's delay as well.
    public ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
    {
        if (sender is PulsarDelivery delivery)
        {
            Redelivery.OnRejected(delivery);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RejectAsync(object? sender, TimeSpan redeliveryDelay, CancellationToken cancellationToken)
    {
        return RejectAsync(sender, CancellationToken.None);
    }

    // Publishes the message to its dead-letter topic, then acknowledges it. A failure leaves it for redelivery, so the
    // message is never lost between the two topics; a publish whose acknowledgement then fails is dead-lettered again.
    private async Task<bool> _DeadLetterAsync(PulsarDelivery delivery, CancellationToken cancellationToken)
    {
        var topic =
            _config?.DeadLetterTopic ?? $"{PulsarTopicName.Normalize(delivery.Receiver.Topic)}-{_subscription}-DLQ";

        try
        {
            var original = delivery.Message;
            var metadata = new MessageMetadata();
            if (original.HasKey)
            {
                metadata.Key = original.Key;
            }

            foreach (var property in original.Properties)
            {
                metadata[property.Key] = property.Value;
            }

            // The property names the Apache Pulsar clients write on a dead-lettered message.
            metadata["REAL_TOPIC"] = PulsarTopicName.Complete(delivery.DeliveryTopic);
            metadata["ORIGIN_MESSAGE_ID"] = string.Create(
                CultureInfo.InvariantCulture,
                $"{delivery.MessageId.LedgerId}:{delivery.MessageId.EntryId}:{delivery.MessageId.Partition}:{delivery.MessageId.BatchIndex}"
            );

            await PulsarTransport
                .SendAsync(
                    _connection.GetProducer(topic),
                    _connection.SizeRefusals,
                    metadata,
                    original.Data,
                    _connection.SendTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await _AcknowledgeAsync(delivery).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception e)
        {
            _LogConsumeError($"Pulsar dead-letter publish to '{topic}' failed; message redelivered instead: {e}");
            return false;
        }
    }

    private void _LogConsumeError(string reason)
    {
        OnLogCallback?.Invoke(new LogMessageEventArgs { LogType = MqLogType.ConsumeError, Reason = reason });
    }

    private static TimeSpan _NextBackoff(TimeSpan current)
    {
        var floor = TimeSpan.FromMilliseconds(200);
        var ceiling = TimeSpan.FromSeconds(30);
        var doubled = TimeSpan.FromTicks(Math.Max(current.Ticks * 2, floor.Ticks));
        var capped = doubled > ceiling ? ceiling : doubled;
#pragma warning disable CA5394 // Non-security jitter for retry backoff; cryptographic RNG is unnecessary here.
        var jitterMs = Random.Shared.Next(0, (int)Math.Max(1, capped.TotalMilliseconds / 4));
#pragma warning restore CA5394
        return capped + TimeSpan.FromMilliseconds(jitterMs);
    }

    private void _ReleaseSemaphore()
    {
        if (_groupConcurrent > 0)
        {
            try
            {
                _semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // Defensive: ignore over-release
            }
            catch (ObjectDisposedException)
            {
                // Shutdown in progress — semaphore already disposed.
            }
        }
    }

    private void _ObserveBackgroundHandler(Task task)
    {
        _ = task.ContinueWith(
            completedTask =>
            {
                var exception = completedTask.Exception?.GetBaseException();
                if (exception is not null)
                {
                    OnLogCallback?.Invoke(
                        new LogMessageEventArgs { LogType = MqLogType.ConsumeError, Reason = exception.Message }
                    );
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        await _pauseResumeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || !await _pauseGate.PauseAsync().ConfigureAwait(false))
            {
                return;
            }

            lock (_receiveLock)
            {
                if (Volatile.Read(ref _disposed) != 0 || _receiveCts is null)
                {
                    return;
                }

#pragma warning disable CA1849 // Cancellation must stay under the lock so disposal cannot win the race.
                _receiveCts.Cancel();
#pragma warning restore CA1849
            }
        }
        finally
        {
            _pauseResumeLock.Release();
        }
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _pauseResumeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || !_pauseGate.IsPaused)
            {
                return;
            }

            CancellationTokenSource previousReceiveCts;
            lock (_receiveLock)
            {
                if (Volatile.Read(ref _disposed) != 0 || _receiveCts is null)
                {
                    return;
                }

                previousReceiveCts = _receiveCts;
                _receiveCts = new CancellationTokenSource();
            }

            previousReceiveCts.Dispose();
            await _pauseGate.ResumeAsync().ConfigureAwait(false);
        }
        finally
        {
            _pauseResumeLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        return ShutdownAsync(_ShutdownDrainTimeout);
    }

    public async ValueTask ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _pauseGate.Release();
        _ready.TrySetCanceled(CancellationToken.None);
        var startedAt = _timeProvider.GetTimestamp();
        CancellationTokenSource? receiveCts;
        lock (_receiveLock)
        {
            receiveCts = _receiveCts;
            _receiveCts = null;
#pragma warning disable CA1849 // Cancellation must stay under the lock so no pause operation observes a disposed source.
            receiveCts?.Cancel();
#pragma warning restore CA1849
        }

        receiveCts?.Dispose();

        // Drain in-flight handlers while the receivers are still open, so each one can acknowledge or reject its
        // message. Bounded so a stuck handler cannot block shutdown; a message whose handler is still running past the
        // budget stays unacknowledged, and the broker redelivers it (at-least-once).
        try
        {
            await _inFlightHandlers.DrainAsync(timeout, _timeProvider).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Handler faults are already surfaced by _ObserveBackgroundHandler; on a drain timeout or fault,
            // log and proceed, because disposal must never block or throw.
            _LogConsumeError($"Timed out or faulted draining in-flight Pulsar handlers during shutdown: {ex}");
        }

        _semaphore.Dispose();
        if (_receivers.Count > 0)
        {
            await _CloseWithinAsync(_receivers, timeout - _timeProvider.GetElapsedTime(startedAt))
                .ConfigureAwait(false);
        }
    }

    // Closing sends a close command and waits for the broker's answer, which a receiver that just reconnected after a
    // broker restart can wait on past the caller's budget. The close is bounded by what the drain left of the budget;
    // an abandoned close finishes in the background, and the broker reclaims the subscription when the connection drops.
    // Every message still unacknowledged, rejected ones waiting for their delay included, is redelivered by the broker.
    private async Task _CloseWithinAsync(List<IPulsarReceiver> receivers, TimeSpan remaining)
    {
        var close = Task.WhenAll(receivers.Select(static receiver => receiver.DisposeAsync().AsTask()));
        await close
            .WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, _timeProvider)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // The close is read once, after the wait: a close that finishes between the budget's timeout and this read
        // counts as finished, so the timeout never escapes shutdown while the close itself succeeded.
        if (close.IsCompleted)
        {
            await close.ConfigureAwait(false);
            return;
        }

        _LogConsumeError(
            "Pulsar consumer close did not finish within the shutdown budget; abandoned to finish in the background."
        );

        _ObserveAbandoned(close);
    }

    private static async Task _DisposeQuietlyAsync(IPulsarReceiver receiver)
    {
        try
        {
            await receiver.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable ERP022 // Best-effort cleanup of a subscription that failed to start; the failure is rethrown.
        catch
        {
            // ignored
        }
#pragma warning restore ERP022
    }
}

internal static partial class PulsarConsumerClientLog
{
    [LoggerMessage(
        EventId = 3101,
        EventName = "PulsarConsumerState",
        Level = LogLevel.Information,
        Message = "Pulsar consumer on topic {Topic} is now {State}"
    )]
    internal static partial void LogPulsarConsumerState(this ILogger logger, string topic, ConsumerState state);

    [LoggerMessage(
        EventId = 3102,
        EventName = "PulsarReaderState",
        Level = LogLevel.Information,
        Message = "Pulsar every-instance reader on topic {Topic} is now {State}"
    )]
    internal static partial void LogPulsarReaderState(this ILogger logger, string topic, ReaderState state);
}
