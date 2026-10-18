// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Reflection;
using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pulsar.Client.Api;
using Pulsar.Client.Common;

namespace Headless.Messaging.Pulsar;

internal sealed class PulsarConsumerClient(
    IOptions<PulsarMessagingOptions> options,
    PulsarClient client,
    ConsumerClientRequest request,
    PulsarConsumerConfig? config = null,
    TimeProvider? timeProvider = null,
    Func<IReadOnlyDictionary<string, string?>, byte[], TransportMessage>? transportMessageFactory = null,
    IServiceProvider? serviceProvider = null
) : IConsumerClient
{
    private const string _TransportDisplayName = "Pulsar";
    private const string _PartitionSuffix = "-partition-";

    // Bounded drain budget on shutdown, matching the other transports. A handler still running past it never
    // acknowledges, so the broker redelivers its message once this consumer disconnects (at-least-once).
    private static readonly TimeSpan _ShutdownDrainTimeout = TimeSpan.FromSeconds(30);

    private readonly ConsumerClientRequest _request = Argument.IsNotNull(request);
    private readonly byte _groupConcurrent = request.Concurrency;
    private readonly bool _everyInstance = request.Kind is ConsumerSubscriptionKind.EveryInstance;
    private readonly SemaphoreSlim _semaphore = new(request.Concurrency);
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly BrokerReachabilityReporter _reachability = new();
#pragma warning disable CA2213 // Disposing a transition gate can race queued pause/resume callers during shutdown.
    private readonly SemaphoreSlim _pauseResumeLock = new(1, 1);
#pragma warning restore CA2213
    private readonly Lock _receiveLock = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    // Tracks in-flight handler tasks so shutdown can let them settle before it closes the consumer.
    private readonly InFlightHandlerTracker _inFlightHandlers = new();
    private int _disposed;
    private CancellationTokenSource? _receiveCts = new();
    private readonly PulsarMessagingOptions _pulsarOptions = _RequireServiceProviderForHeadersBuilder(
        options.Value,
        serviceProvider
    );

    private readonly TransportEnvelopeGate _envelopeGate =
        serviceProvider?.GetService<TransportEnvelopeGate>() ?? TransportEnvelopeGate.Strict;

    // Keyed by the subscribed topic's complete, partition-free name, the form a delivery's topic normalizes to.
    private volatile FrozenDictionary<string, string> _arrivalNames = FrozenDictionary<string, string>.Empty;
    private IConsumer<byte[]>? _consumerClient;
    private Func<CancellationToken, Task>? _onReestablished;

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

    public async ValueTask SubscribeAsync(IEnumerable<string> topics, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(topics);

        var serviceName = Assembly.GetEntryAssembly()?.GetName().Name!.ToLowerInvariant();

        var names = topics.ToList();
        IndexArrivalNames(names);

        // Pulsar.Client's SubscribeAsync lacks CancellationToken — use WaitAsync as a
        // timeout guard. Plan to migrate to DotPulsar (apache/pulsar-dotnet) when producer
        // batching ships: https://github.com/apache/pulsar-dotpulsar/issues/7
        var cts = TimeSpan.FromSeconds(30).ToCancellationTokenSource(cancellationToken);
        var builder = ConfigureSubscription(
            client
                .NewConsumer()
                .Topics(names.Select(topic => PulsarPhysicalAddress.Topic(_request.Lane, topic)))
                .SubscriptionName(PulsarPhysicalAddress.Subscription(_request))
                .ConsumerName(serviceName)
                .NegativeAckRedeliveryDelay(_pulsarOptions.NegativeAckRedeliveryDelay),
            _everyInstance,
            config
        );

        var subscribeTask = builder.SubscribeAsync();

        try
        {
            _consumerClient = await subscribeTask.WaitAsync(cts.Token).ConfigureAwait(false);
            cts.Dispose();
        }
        catch
        {
#pragma warning disable CA2025 // The cleanup task owns both the SDK task and linked CTS until completion.
            _DisposeWhenCompletedAsync(subscribeTask, cts).Forget();
#pragma warning restore CA2025
            throw;
        }

        _ready.TrySetResult();
    }

    /// <summary>Applies the subscription type and the tuned consumer options to <paramref name="builder"/>.</summary>
    internal static ConsumerBuilder<byte[]> ConfigureSubscription(
        ConsumerBuilder<byte[]> builder,
        bool everyInstance,
        PulsarConsumerConfig? config
    )
    {
        // An every-instance subscription belongs to this process alone: exclusive, so no other consumer can attach to
        // it, and non-durable, so the broker keeps no cursor for it once the consumer disconnects or the process dies.
        // With no backlog to replay, it starts at the latest message.
        builder = everyInstance
            ? builder
                .SubscriptionType(SubscriptionType.Exclusive)
                .SubscriptionMode(SubscriptionMode.NonDurable)
                .SubscriptionInitialPosition(SubscriptionInitialPosition.Latest)
            : builder.SubscriptionType(SubscriptionType.Shared);

        return config?.ApplyTo(builder) ?? builder;
    }

    private static async Task _DisposeWhenCompletedAsync(
        Task<IConsumer<byte[]>> consumerTask,
        CancellationTokenSource cancellationTokenSource
    )
    {
        try
        {
            var consumer = await consumerTask.ConfigureAwait(false);
            await consumer.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable ERP022 // Best-effort cleanup for an SDK operation abandoned by caller cancellation.
        catch
        {
            // ignored
        }
#pragma warning restore ERP022
        finally
        {
            cancellationTokenSource.Dispose();
        }
    }

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
        // The receive loop is drained with the handlers, so shutdown closes the consumer only after the loop stopped
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
        var retryDelay = TimeSpan.FromMilliseconds(200);
        CancellationTokenSource? linkedReceiveCts = null;
        var receiveToken = CancellationToken.None;
        using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reconnectMonitor = _StartReconnectMonitor(monitorCts.Token);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Message<byte[]> consumerResult;
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

                    consumerResult = await _consumerClient!.ReceiveAsync(linkedReceiveCts!.Token).ConfigureAwait(false);
                    retryDelay = TimeSpan.FromMilliseconds(200);
                    _reachability.ReportSuccess(OnLogCallback);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException) when (receiveToken.IsCancellationRequested)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        break;
                    }

                    continue;
                }
                catch (Exception e)
                {
                    _reachability.ReportFailure(OnLogCallback, e);
                    retryDelay = _NextBackoff(retryDelay);
                    await _timeProvider.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (_groupConcurrent > 0)
                {
                    try
                    {
                        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
                    {
                        // Shutdown raced the concurrency-gate wait: settle the in-flight message
                        // best-effort so it is redelivered promptly, then stop. Mirrors the
                        // must-complete nack for malformed messages below and the ObjectDisposedException
                        // guard already on _semaphore.Release() in _ReleaseSemaphore.
                        await _SafeShutdownRejectAsync(consumerResult.MessageId).ConfigureAwait(false);
                        break;
                    }

                    var handlerTask = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await consumeAsync(consumerResult).ConfigureAwait(false);
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
                    var inlineTask = consumeAsync(consumerResult);
                    _inFlightHandlers.Track(inlineTask);
                    await inlineTask.ConfigureAwait(false);
                }

                async Task consumeAsync(Message<byte[]> currentMessage)
                {
                    TransportMessage message;
                    try
                    {
                        var headers = new Dictionary<string, string?>(
                            currentMessage.Properties.Count,
                            StringComparer.Ordinal
                        );
                        foreach (var header in currentMessage.Properties)
                        {
                            headers.Add(header.Key, header.Value);
                        }

                        if (_pulsarOptions.CustomHeadersBuilder is { } customHeadersBuilder)
                        {
                            foreach (var customHeader in customHeadersBuilder(currentMessage, serviceProvider!))
                            {
                                headers[customHeader.Key] = customHeader.Value;
                            }
                        }

                        // Stamped after the custom headers builder so neither the wire nor the builder can choose the
                        // address.
                        headers[Headers.TransportAddress] = currentMessage.MessageId.TopicName;

                        message = transportMessageFactory is null
                            ? new TransportMessage(headers, currentMessage.Data)
                            : transportMessageFactory(headers, currentMessage.Data);

                        _envelopeGate.Admit(
                            message.Headers,
                            new EnvelopeArrival(
                                _TransportDisplayName,
                                _request.Lane,
                                ArrivalName(currentMessage.MessageId.TopicName)
                            ),
                            currentMessage,
                            static delivery => NativeFacts(delivery)
                        );
                    }
                    catch (Exception ex)
                    {
                        OnLogCallback!(
                            new LogMessageEventArgs
                            {
                                LogType = MqLogType.ConsumeError,
                                Reason =
                                    $"Malformed Pulsar transport envelope terminally acknowledged: {ex.GetType().Name}",
                            }
                        );

                        // Settlement is must-complete: acknowledge malformed broker data so it cannot redeliver forever.
                        await _consumerClient!.AcknowledgeAsync(currentMessage.MessageId).ConfigureAwait(false);
                        return;
                    }

                    await OnMessageCallback!(message, currentMessage.MessageId).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            linkedReceiveCts?.Dispose();
            await monitorCts.CancelAsync().ConfigureAwait(false);
            await reconnectMonitor.ConfigureAwait(false);
        }
    }

    // Pulsar.Client recovers a lost connection on its own and resubscribes, and a receive waits quietly meanwhile, so
    // the monitor's connection state is how the host learns of an outage. A non-durable subscription also keeps nothing
    // for the time it was away, so the core must learn about each recovery to tell the every-instance consumer.
    private Task _StartReconnectMonitor(CancellationToken cancellationToken)
    {
        if (_consumerClient is null)
        {
            return Task.CompletedTask;
        }

        return PulsarReconnectMonitor.RunAsync(
            _consumerClient,
            _everyInstance ? token => Volatile.Read(ref _onReestablished)?.Invoke(token) ?? Task.CompletedTask : null,
            _ReportConnectionState,
            e =>
                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ConsumeError,
                        Reason = $"Pulsar reconnect monitor failed: {e.Message}",
                    }
                ),
            _timeProvider,
            cancellationToken
        );
    }

    private void _ReportConnectionState(bool connected)
    {
        if (connected)
        {
            _reachability.ReportSuccess(OnLogCallback);
        }
        else
        {
            _reachability.ReportFailure(
                OnLogCallback,
                "The Pulsar consumer lost its broker connection and is reconnecting."
            );
        }
    }

    public async ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
    {
        await _consumerClient!.AcknowledgeAsync((MessageId)sender!).ConfigureAwait(false);
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
            .GroupBy(name => NormalizeTopic(PulsarPhysicalAddress.Topic(_request.Lane, name)), StringComparer.Ordinal)
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

        return arrivalNames.Count > 0 && arrivalNames.TryGetValue(NormalizeTopic(topic), out var name) ? name : null;
    }

    /// <summary>
    /// Returns the complete name of <paramref name="topic"/> without a partition suffix. Pulsar reports a delivery's
    /// topic fully qualified (<c>persistent://tenant/namespace/topic</c>) and, on a partitioned topic, with a
    /// <c>-partition-N</c> suffix, while the subscription names the short form.
    /// </summary>
    internal static string NormalizeTopic(string topic)
    {
        var topicName = new TopicName(topic);
        var complete = topicName.CompleteTopicName;

        if (topicName.PartitionIndex < 0)
        {
            return complete;
        }

        var suffix = _PartitionSuffix + topicName.PartitionIndex.ToString(CultureInfo.InvariantCulture);

        return complete.EndsWith(suffix, StringComparison.Ordinal) ? complete[..^suffix.Length] : complete;
    }

    /// <summary>
    /// Reads the broker metadata that identifies a delivery which carries no message id. Pulsar has no producer
    /// idempotency key a consumer can read, so the identity is the stored message's position: a ledger id is unique
    /// across the cluster and never reused, so the position stays the same on every redelivery and differs after the
    /// topic is recreated.
    /// </summary>
    internal static NativeEnvelopeFacts NativeFacts(Message<byte[]> message)
    {
        var id = message.MessageId;
        var batchIndex = id.Type.IsBatch ? id.Type.Item.Item1 : -1;

        var position = NativeEnvelopeFacts.Position(
            "pulsar",
            NormalizeTopic(id.TopicName),
            id.LedgerId.ToString(CultureInfo.InvariantCulture),
            id.EntryId.ToString(CultureInfo.InvariantCulture),
            id.Partition.ToString(CultureInfo.InvariantCulture),
            batchIndex.ToString(CultureInfo.InvariantCulture)
        );

        DateTimeOffset? storedAt =
            message.PublishTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(message.PublishTime) : null;

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

    public async ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
    {
        if (sender is MessageId id)
        {
            await _consumerClient!.NegativeAcknowledge(id).ConfigureAwait(false);
        }
    }

    // Pulsar holds a negatively acknowledged message back for NegativeAckRedeliveryDelay itself, so the delivery is
    // released at once rather than held for the core's delay as well.
    public ValueTask RejectAsync(object? sender, TimeSpan redeliveryDelay, CancellationToken cancellationToken)
    {
        return RejectAsync(sender, CancellationToken.None);
    }

    private async Task _SafeShutdownRejectAsync(MessageId messageId)
    {
        try
        {
            await RejectAsync(messageId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
        {
            // Consumer already disposed during shutdown; Pulsar redelivers the unacknowledged message.
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason = $"Best-effort shutdown nack skipped (consumer disposed): {e.Message}",
                }
            );
        }
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

        // Drain in-flight handlers while the consumer is still open, so each one can acknowledge or negatively
        // acknowledge its message. Bounded so a stuck handler cannot block shutdown; a message whose handler is still
        // running past the budget stays unacknowledged, and the broker redelivers it (at-least-once).
        try
        {
            await _inFlightHandlers.DrainAsync(timeout, _timeProvider).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Handler faults are already surfaced by _ObserveBackgroundHandler; on a drain timeout or fault,
            // log and proceed, because disposal must never block or throw.
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason = $"Timed out or faulted draining in-flight Pulsar handlers during shutdown: {ex}",
                }
            );
        }

        _semaphore.Dispose();
        if (_consumerClient is not null)
        {
            await _consumerClient.DisposeAsync().ConfigureAwait(false);
        }
    }
}
