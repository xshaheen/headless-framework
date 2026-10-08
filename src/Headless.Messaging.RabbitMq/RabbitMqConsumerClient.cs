// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Headless.Messaging.RabbitMq;

internal sealed class RabbitMqConsumerClient : IConsumerClient
{
    // The fallback drain budget when the client is disposed without a shutdown budget from the messaging core.
    private static readonly TimeSpan _ShutdownDrainTimeout = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly string _subscriptionName;
    private readonly byte _groupConcurrent;
    private readonly IConnectionChannelPool _connectionChannelPool;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly string _exchangeName;
    private readonly RabbitMqMessagingOptions _rabbitMqOptions;
    private readonly RabbitMqConsumerConfig? _consumerConfig;
    private readonly MessageLane _lane;
    private readonly List<string> _queueNames = [];
    private readonly Dictionary<string, string> _consumerTags = new(StringComparer.Ordinal);
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly Func<RabbitMqConsumerLifecycleCheckpoint, ValueTask>? _lifecycleCheckpointAsync;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConsumerSubscriptionKind _kind;

    // Completes with the broker's reason when an every-instance client's channel shuts down while it is in use.
    private readonly TaskCompletionSource<string> _channelLost = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private RabbitMqBasicConsumer? _consumer;
    private IChannel? _channel;

    // An every-instance client owns its connection; a competing client borrows the pool's shared one.
    private IConnection? _ownedConnection;
    private int _disposed;

    public RabbitMqConsumerClient(
        string subscriptionName,
        byte groupConcurrent,
        IConnectionChannelPool connectionChannelPool,
        IOptions<RabbitMqMessagingOptions> options,
        IServiceProvider serviceProvider,
        RabbitMqConsumerConfig? consumerConfig = null,
        MessageLane lane = MessageLane.Bus,
        Func<RabbitMqConsumerLifecycleCheckpoint, ValueTask>? lifecycleCheckpointAsync = null,
        ConsumerSubscriptionKind kind = ConsumerSubscriptionKind.Competing
    )
    {
        // The subscription name is a consumer identity or a message name, not a queue name; the queue name derived from
        // it is validated when it is built.
        Argument.IsNotNullOrWhiteSpace(subscriptionName);

        _subscriptionName = subscriptionName;
        _groupConcurrent = groupConcurrent;
        _connectionChannelPool = connectionChannelPool;
        _serviceProvider = serviceProvider;
        _timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        _exchangeName = RabbitMqPhysicalAddress.Exchange(connectionChannelPool.Exchange, lane);
        _rabbitMqOptions = options.Value;
        _consumerConfig = consumerConfig;
        _lane = lane;
        _lifecycleCheckpointAsync = lifecycleCheckpointAsync;
        _kind = Argument.IsInEnum(kind);

        if (kind is ConsumerSubscriptionKind.EveryInstance && lane is not MessageLane.Bus)
        {
            throw new ArgumentException("Only the Bus lane has every-instance subscriptions.", nameof(kind));
        }

        if (lane == MessageLane.Bus)
        {
            _ = RabbitMqPhysicalAddress.Queue(lane, subscriptionName, subscriptionName);
        }
    }

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog)
    {
        OnMessageCallback = onMessage;
        OnLogCallback = onLog;
    }

    /// <summary>The queues this client consumes; an every-instance client's one queue has a broker-generated name.</summary>
    internal IReadOnlyList<string> QueueNames => _queueNames;

    public BrokerAddress BrokerAddress =>
        new(
            "rabbitmq",
            string.Create(CultureInfo.InvariantCulture, $"{_rabbitMqOptions.HostName}:{_rabbitMqOptions.Port}")
        );

    public async ValueTask SubscribeAsync(
        IEnumerable<string> messageNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(messageNames);

        if (_kind is ConsumerSubscriptionKind.EveryInstance)
        {
            await _SubscribeEveryInstanceAsync(messageNames, cancellationToken).ConfigureAwait(false);
            return;
        }

        var subscriptions = messageNames
            .Select(messageName =>
            {
                RabbitMqValidation.ValidateMessageName(messageName);
                return (
                    QueueName: _GetQueueName(messageName),
                    RoutingKey: RabbitMqPhysicalAddress.RoutingKey(_lane, messageName)
                );
            })
            .ToArray();

        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        foreach (var (queueName, routingKey) in subscriptions)
        {
            if (!_queueNames.Contains(queueName, StringComparer.Ordinal))
            {
                await _DeclareQueueAsync(queueName, cancellationToken).ConfigureAwait(false);
                _queueNames.Add(queueName);
            }

            await RabbitMqQueueTopology
                .BindQueueAsync(_channel!, _rabbitMqOptions, _exchangeName, queueName, routingKey, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        if (_consumerConfig?.PrefetchCount is { } configuredPrefetch)
        {
            await _channel!
                .BasicQosAsync(0, configuredPrefetch, global: false, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (_rabbitMqOptions.BasicQosOptions != null)
        {
            await _channel!
                .BasicQosAsync(
                    0,
                    _rabbitMqOptions.BasicQosOptions.PrefetchCount,
                    _rabbitMqOptions.BasicQosOptions.Global,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        else
        {
            var prefetch = _groupConcurrent > 0 ? _groupConcurrent : (ushort)1;
            await _channel!
                .BasicQosAsync(prefetchSize: 0, prefetchCount: prefetch, global: false, cancellationToken)
                .ConfigureAwait(false);
        }

        await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

        if (_lifecycleCheckpointAsync is not null)
        {
            await _lifecycleCheckpointAsync(RabbitMqConsumerLifecycleCheckpoint.BeforeStartLock).ConfigureAwait(false);
        }

        var consumer = new RabbitMqBasicConsumer(
            _channel!,
            _groupConcurrent,
            OnMessageCallback!,
            OnLogCallback!,
            _rabbitMqOptions.CustomHeadersBuilder,
            _serviceProvider,
            _kind is ConsumerSubscriptionKind.EveryInstance ? _OnEveryInstanceConsumerCancelledByBroker : null
        );

        try
        {
            await _StartConsumingAsync(consumer, cancellationToken).ConfigureAwait(false);

            _ready.TrySetResult();
        }
        catch (TimeoutException ex)
        {
            await consumer
                .HandleChannelShutdownAsync(
                    null!,
                    new ShutdownEventArgs(
                        ShutdownInitiator.Application,
                        0,
                        ex.Message + "-->" + nameof(_channel.BasicConsumeAsync)
                    )
                )
                .ConfigureAwait(false);
            _ready.TrySetException(ex);
            throw;
        }

        if (_kind is ConsumerSubscriptionKind.EveryInstance)
        {
            await _WaitUntilChannelLostAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // RabbitMQ is push-based — after BasicConsumeAsync the broker delivers messages
        // via the consumer callback. We just need to keep this task alive until shutdown.
        // Using Timeout.Infinite avoids repeated timer+task allocations from a polling loop.
        try
        {
            await _timeProvider.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
    }

    public async ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
    {
        await _consumer!.BasicAck((ulong)sender!, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
    {
        await _consumer!.BasicReject((ulong)sender!, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!await _pauseGate.PauseAsync().ConfigureAwait(false))
            {
                return;
            }

            try
            {
                foreach (var (queueName, consumerTag) in _consumerTags.ToArray())
                {
                    await _channel!
                        .BasicCancelAsync(consumerTag, cancellationToken: CancellationToken.None)
                        .ConfigureAwait(false);
                    _consumerTags.Remove(queueName);
                }
            }
            catch (Exception cancellationException)
            {
                try
                {
                    await _ConsumeQueuesAsync(CancellationToken.None).ConfigureAwait(false);
                    await _pauseGate.ResumeAsync().ConfigureAwait(false);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "RabbitMQ consumer pause failed and the active registrations could not be restored.",
                        cancellationException,
                        rollbackException
                    );
                }

                throw;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_pauseGate.IsPaused)
            {
                return;
            }

            // Register while the gate is still paused. If broker registration or caller cancellation
            // fails, _ConsumeQueuesAsync rolls back the partial registration and the gate stays paused.
            if (_consumer is not null)
            {
                await _ConsumeQueuesAsync(cancellationToken).ConfigureAwait(false);
            }

            await _pauseGate.ResumeAsync().ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
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

        // Drain in-flight handlers before closing the channel, so a running handler's ack reaches the broker instead of
        // being skipped on a closed channel and redelivered. Bounded so a stuck handler cannot block shutdown; a handler
        // still running past the budget has its ack skipped and the message is redelivered (at-least-once). Deliveries
        // that arrive during the drain are not dispatched and return to the queue when the channel closes.
        if (_consumer is { } consumer)
        {
            try
            {
                if (timeout <= TimeSpan.Zero)
                {
                    consumer.StopDispatching();
                    throw new TimeoutException("The shared messaging shutdown deadline has expired.");
                }

                await consumer.DrainAsync(timeout, _timeProvider).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Handler faults are already surfaced by the consumer; on a drain timeout, log and proceed — shutdown
                // must never block or throw.
                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ExceptionReceived,
                        Reason = $"Timed out draining in-flight RabbitMQ handlers during shutdown: {ex}",
                    }
                );
            }
        }

        _consumer?.Dispose();
        _channel?.Dispose();
        _semaphore.Dispose();

        // A competing client leaves the pool's shared connection open for its other users. An every-instance client
        // closes its own, and the broker deletes the exclusive queue with it.
        if (_ownedConnection is { } ownedConnection)
        {
            await _CloseOwnedConnectionAsync(ownedConnection).ConfigureAwait(false);
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var connection =
            _kind is ConsumerSubscriptionKind.EveryInstance
                ? null
                : await _connectionChannelPool.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel?.IsClosed == false)
            {
                return;
            }

            if (_kind is ConsumerSubscriptionKind.EveryInstance)
            {
                // The exclusive queue died with the channel's connection, so a fresh channel would consume nothing;
                // failing lets the core rebuild the client with a new queue and tell the consumer about the gap.
                if (_channel is not null)
                {
                    throw new BrokerConnectionException(
                        new InvalidOperationException(
                            "The every-instance RabbitMQ consumer lost its connection and must be rebuilt."
                        )
                    );
                }

                // A retry after a failed declare reuses the connection; it has no queue on it yet.
                _ownedConnection ??= await _connectionChannelPool
                    .CreateNonRecoveringConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);
                connection = _ownedConnection;
            }

            var channel = await connection!
                .CreateChannelAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (_kind is ConsumerSubscriptionKind.EveryInstance)
            {
                channel.ChannelShutdownAsync += _OnEveryInstanceChannelShutdownAsync;
            }

            try
            {
                await RabbitMqQueueTopology
                    .DeclareExchangeAsync(
                        channel,
                        _rabbitMqOptions,
                        _exchangeName,
                        RabbitMqPhysicalAddress.ExchangeType(_lane),
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                _channel = channel;

                // An every-instance client declares its queue only when it subscribes, so the topology-only client
                // the core opens to provision message names leaves no queue behind.
                var busQueue = RabbitMqPhysicalAddress.Queue(MessageLane.Bus, _subscriptionName, _subscriptionName);
                if (
                    _lane == MessageLane.Bus
                    && _kind is ConsumerSubscriptionKind.Competing
                    && !_queueNames.Contains(busQueue, StringComparer.Ordinal)
                )
                {
                    await _DeclareQueueAsync(busQueue, cancellationToken).ConfigureAwait(false);
                    _queueNames.Add(busQueue);
                }
            }
            catch (TimeoutException ex)
            {
                // RabbitMQ channel timed out during queue/exchange declare; surface to caller so the
                // outer reconnect loop can recover instead of leaving a half-initialized channel.
                await channel.DisposeAsync().ConfigureAwait(false);
                _channel = null;
                var args = new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumerShutdown,
                    Reason = ex.Message + "-->" + nameof(channel.QueueDeclareAsync),
                };

                OnLogCallback!(args);
                throw;
            }
            catch
            {
                await channel.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task _StartConsumingAsync(RabbitMqBasicConsumer consumer, CancellationToken cancellationToken)
    {
        while (true)
        {
            bool waitForResume;
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Publish the consumer under the same lock as registration so ResumeAsync can safely
                // take ownership when PauseAsync wins the lifecycle race.
                _consumer = consumer;

                if (_pauseGate.IsPaused)
                {
                    waitForResume = true;
                }
                else
                {
                    await _ConsumeQueuesAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            finally
            {
                _semaphore.Release();
            }

            if (waitForResume)
            {
                if (_lifecycleCheckpointAsync is not null)
                {
                    await _lifecycleCheckpointAsync(RabbitMqConsumerLifecycleCheckpoint.StartDeferredByPause)
                        .ConfigureAwait(false);
                }

                await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task _ConsumeQueuesAsync(CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, string>> registrations = [];

        try
        {
            foreach (var queueName in _queueNames)
            {
                if (_consumerTags.ContainsKey(queueName))
                {
                    continue;
                }

                var consumerTag = await _channel!
                    .BasicConsumeAsync(queueName, autoAck: false, _consumer!, cancellationToken)
                    .ConfigureAwait(false);

                _consumerTags.Add(queueName, consumerTag);
                registrations.Add(KeyValuePair.Create(queueName, consumerTag));
            }
        }
        catch (Exception registrationException)
        {
            List<Exception> rollbackExceptions = [];

            foreach (var (queueName, consumerTag) in registrations)
            {
                try
                {
                    await _channel!
                        .BasicCancelAsync(consumerTag, cancellationToken: CancellationToken.None)
                        .ConfigureAwait(false);
                    _consumerTags.Remove(queueName);
                }
                catch (Exception rollbackException)
                {
                    rollbackExceptions.Add(rollbackException);
                }
            }

            if (rollbackExceptions.Count > 0)
            {
                throw new AggregateException(
                    "RabbitMQ consumer registration failed and its partial registrations could not be cancelled.",
                    [registrationException, .. rollbackExceptions]
                );
            }

            throw;
        }
    }

    private async Task _SubscribeEveryInstanceAsync(
        IEnumerable<string> messageNames,
        CancellationToken cancellationToken
    )
    {
        var routingKeys = messageNames
            .Select(messageName =>
            {
                RabbitMqValidation.ValidateMessageName(messageName);
                return RabbitMqPhysicalAddress.RoutingKey(MessageLane.Bus, messageName);
            })
            .ToArray();

        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        if (_queueNames.Count == 0)
        {
            var declared = await _channel!
                .QueueDeclareAsync(
                    queue: string.Empty,
                    durable: false,
                    exclusive: true,
                    autoDelete: false,
                    arguments: BuildEveryInstanceQueueArguments(),
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            _queueNames.Add(declared.QueueName);
        }

        foreach (var routingKey in routingKeys)
        {
            await _channel!
                .QueueBindAsync(_queueNames[0], _exchangeName, routingKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// How long an every-instance queue keeps a message nobody has taken. A fixed minute, not the competing queues'
    /// <see cref="RabbitMqMessagingOptions.QueueArgumentsOptions.MessageTTL"/>: a queue that stalls or stays paused
    /// would otherwise hand the process days-old per-process state when it resumes.
    /// </summary>
    internal static readonly TimeSpan EveryInstanceMessageTtl = TimeSpan.FromSeconds(60);

    /// <summary>The arguments of an every-instance client's server-named queue.</summary>
    /// <remarks>
    /// The queue is exclusive to the client's own connection, so the broker deletes it when the connection closes or
    /// the process dies. It is deliberately not auto-delete: pausing cancels the queue's only consumer, and an
    /// auto-delete queue would disappear then and fail the resume. Nothing carries over from
    /// <see cref="RabbitMqMessagingOptions.QueueArguments"/>: quorum and stream queue types cannot be exclusive, and the
    /// message TTL is <see cref="EveryInstanceMessageTtl"/>.
    /// </remarks>
    internal static Dictionary<string, object?> BuildEveryInstanceQueueArguments()
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            { "x-message-ttl", (int)EveryInstanceMessageTtl.TotalMilliseconds },
        };
    }

    private async Task _WaitUntilChannelLostAsync(CancellationToken cancellationToken)
    {
        string reason;
        try
        {
            reason = await _channelLost.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // The connection does not recover on its own, so the listener fails and the core rebuilds the client.
        throw new BrokerConnectionException(
            new InvalidOperationException($"The every-instance RabbitMQ consumer stopped receiving: {reason}")
        );
    }

    // The channel stays open when the broker cancels the consumer, so without this the listener would wait forever on a
    // queue that no longer exists instead of failing for the core to rebuild it with a new one.
    private void _OnEveryInstanceConsumerCancelledByBroker(string consumerTag)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            _channelLost.TrySetResult(
                $"the broker cancelled consumer '{consumerTag}', as it does when its queue is deleted"
            );
        }
    }

    private Task _OnEveryInstanceChannelShutdownAsync(object sender, ShutdownEventArgs reason)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            _channelLost.TrySetResult($"the channel shut down: {reason.ReplyText}");
        }

        return Task.CompletedTask;
    }

    private async Task _CloseOwnedConnectionAsync(IConnection connection)
    {
        try
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The connection is already gone, and the broker dropped the exclusive queue with it.
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ExceptionReceived,
                    Reason = $"Closing the every-instance RabbitMQ connection failed: {ex}",
                }
            );
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private string _GetQueueName(string messageName)
    {
        return GetQueueName(_subscriptionName, messageName, _lane);
    }

    internal static string GetQueueName(string subscriptionName, string messageName, MessageLane lane)
    {
        return RabbitMqPhysicalAddress.Queue(lane, subscriptionName, messageName);
    }

    // Bindings go with SubscribeAsync, which knows the routing keys; the queue declare here only creates the queue.
    private Task _DeclareQueueAsync(string queueName, CancellationToken cancellationToken)
    {
        return RabbitMqQueueTopology.DeclareQueueAsync(
            _channel!,
            _rabbitMqOptions,
            _exchangeName,
            queueName,
            routingKey: null,
            cancellationToken
        );
    }
}

internal enum RabbitMqConsumerLifecycleCheckpoint
{
    BeforeStartLock = 0,
    StartDeferredByPause = 1,
}
