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
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly string _groupName;
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
    private RabbitMqBasicConsumer? _consumer;
    private IChannel? _channel;
    private int _disposed;

    public RabbitMqConsumerClient(
        string groupName,
        byte groupConcurrent,
        IConnectionChannelPool connectionChannelPool,
        IOptions<RabbitMqMessagingOptions> options,
        IServiceProvider serviceProvider,
        RabbitMqConsumerConfig? consumerConfig = null,
        MessageLane lane = MessageLane.Bus,
        Func<RabbitMqConsumerLifecycleCheckpoint, ValueTask>? lifecycleCheckpointAsync = null
    )
    {
        RabbitMqValidation.ValidateQueueName(groupName);

        _groupName = groupName;
        _groupConcurrent = groupConcurrent;
        _connectionChannelPool = connectionChannelPool;
        _serviceProvider = serviceProvider;
        _timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        _exchangeName = connectionChannelPool.Exchange;
        _rabbitMqOptions = options.Value;
        _consumerConfig = consumerConfig;
        _lane = lane;
        _lifecycleCheckpointAsync = lifecycleCheckpointAsync;
    }

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog)
    {
        OnMessageCallback = onMessage;
        OnLogCallback = onLog;
    }

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

        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        foreach (var messageName in messageNames)
        {
            RabbitMqValidation.ValidateMessageName(messageName);
            var queueName = _GetQueueName(messageName);
            if (!_queueNames.Contains(queueName, StringComparer.Ordinal))
            {
                await _DeclareQueueAsync(queueName, cancellationToken).ConfigureAwait(false);
                _queueNames.Add(queueName);
            }

            await _channel!
                .QueueBindAsync(queueName, _exchangeName, messageName, cancellationToken: cancellationToken)
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
            _groupName,
            OnMessageCallback!,
            OnLogCallback!,
            _rabbitMqOptions.CustomHeadersBuilder,
            _serviceProvider
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _pauseGate.Release();
        _ready.TrySetCanceled();

        _consumer?.Dispose();
        _channel?.Dispose();
        _semaphore.Dispose();
        return ValueTask.CompletedTask;
        //The connection should not be closed here, because the connection is still in use elsewhere.
        //_connection?.Dispose();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _connectionChannelPool.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel?.IsClosed == false)
            {
                return;
            }

            var channel = await connection
                .CreateChannelAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await channel
                    .ExchangeDeclareAsync(
                        _exchangeName,
                        RabbitMqMessagingOptions.ExchangeType,
                        durable: true,
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);

                _channel = channel;

                if (_lane == MessageLane.Bus && !_queueNames.Contains(_groupName, StringComparer.Ordinal))
                {
                    await _DeclareQueueAsync(_groupName, cancellationToken).ConfigureAwait(false);
                    _queueNames.Add(_groupName);
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
            var waitForResume = false;
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

    private string _GetQueueName(string messageName)
    {
        return GetQueueName(_groupName, messageName, _lane);
    }

    internal static string GetQueueName(string groupName, string messageName, MessageLane lane)
    {
        return lane == MessageLane.Queue ? messageName : groupName;
    }

    private async Task _DeclareQueueAsync(string queueName, CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            { "x-message-ttl", _rabbitMqOptions.QueueArguments.MessageTTL },
        };

        if (!string.IsNullOrEmpty(_rabbitMqOptions.QueueArguments.QueueMode))
        {
            arguments.Add("x-queue-mode", _rabbitMqOptions.QueueArguments.QueueMode);
        }

        if (!string.IsNullOrEmpty(_rabbitMqOptions.QueueArguments.QueueType))
        {
            arguments.Add("x-queue-type", _rabbitMqOptions.QueueArguments.QueueType);
        }

        await _channel!
            .QueueDeclareAsync(
                queueName,
                _rabbitMqOptions.QueueOptions.Durable,
                _rabbitMqOptions.QueueOptions.Exclusive,
                _rabbitMqOptions.QueueOptions.AutoDelete,
                arguments,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }
}

internal enum RabbitMqConsumerLifecycleCheckpoint
{
    BeforeStartLock = 0,
    StartDeferredByPause = 1,
}
