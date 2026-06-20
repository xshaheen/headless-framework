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
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly RabbitMqConsumerConfig? _consumerConfig;
    private readonly IntentType _intentType;
    private readonly List<string> _queueNames = [];
    private readonly List<string> _consumerTags = [];
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RabbitMqBasicConsumer? _consumer;
    private IChannel? _channel;
    private int _disposed;

    public RabbitMqConsumerClient(
        string groupName,
        byte groupConcurrent,
        IConnectionChannelPool connectionChannelPool,
        IOptions<RabbitMqOptions> options,
        IServiceProvider serviceProvider,
        RabbitMqConsumerConfig? consumerConfig = null,
        IntentType intentType = IntentType.Bus
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
        _intentType = intentType;
    }

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("rabbitmq", $"{_rabbitMqOptions.HostName}:{_rabbitMqOptions.Port}");

    public async ValueTask SubscribeAsync(IEnumerable<string> messageNames)
    {
        Argument.IsNotNull(messageNames);

        await ConnectAsync();

        foreach (var messageName in messageNames)
        {
            RabbitMqValidation.ValidateMessageName(messageName);
            var queueName = _GetQueueName(messageName);
            if (!_queueNames.Contains(queueName, StringComparer.Ordinal))
            {
                await _DeclareQueueAsync(queueName).ConfigureAwait(false);
                _queueNames.Add(queueName);
            }

            await _channel!.QueueBindAsync(queueName, _exchangeName, messageName).ConfigureAwait(false);
        }
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await ConnectAsync();

        if (_consumerConfig?.PrefetchCount is { } configuredPrefetch)
        {
            await _channel!.BasicQosAsync(0, configuredPrefetch, global: false, cancellationToken);
        }
        else if (_rabbitMqOptions.BasicQosOptions != null)
        {
            await _channel!.BasicQosAsync(
                0,
                _rabbitMqOptions.BasicQosOptions.PrefetchCount,
                _rabbitMqOptions.BasicQosOptions.Global,
                cancellationToken
            );
        }
        else
        {
            var prefetch = _groupConcurrent > 0 ? _groupConcurrent : (ushort)1;
            await _channel!.BasicQosAsync(prefetchSize: 0, prefetchCount: prefetch, global: false, cancellationToken);
        }

        await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

        _consumer = new RabbitMqBasicConsumer(
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
            foreach (var queueName in _queueNames)
            {
                var consumerTag = await _channel!
                    .BasicConsumeAsync(queueName, false, _consumer, cancellationToken)
                    .ConfigureAwait(false);
                _consumerTags.Add(consumerTag);
            }

            _ready.TrySetResult();
        }
        catch (TimeoutException ex)
        {
            await _consumer.HandleChannelShutdownAsync(
                null!,
                new ShutdownEventArgs(
                    ShutdownInitiator.Application,
                    0,
                    ex.Message + "-->" + nameof(_channel.BasicConsumeAsync)
                )
            );
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

    public async ValueTask CommitAsync(object? sender)
    {
        await _consumer!.BasicAck((ulong)sender!);
    }

    public async ValueTask RejectAsync(object? sender)
    {
        await _consumer!.BasicReject((ulong)sender!);
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!await _pauseGate.PauseAsync())
        {
            return;
        }

        foreach (var consumerTag in _consumerTags)
        {
            await _channel!.BasicCancelAsync(consumerTag, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        _consumerTags.Clear();
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!await _pauseGate.ResumeAsync())
        {
            return;
        }

        // Re-register consumer after transitioning so the broker is
        // delivering messages when the gate unblocks waiters.
        foreach (var queueName in _queueNames)
        {
            var consumerTag = await _channel!
                .BasicConsumeAsync(queueName, false, _consumer!, cancellationToken)
                .ConfigureAwait(false);
            _consumerTags.Add(consumerTag);
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

    public async Task ConnectAsync()
    {
        var connection = await _connectionChannelPool.GetConnectionAsync().ConfigureAwait(false);

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_channel is not null && !_channel.IsClosed)
            {
                return;
            }

            var channel = await connection.CreateChannelAsync().ConfigureAwait(false);

            try
            {
                await channel
                    .ExchangeDeclareAsync(_exchangeName, RabbitMqOptions.ExchangeType, true)
                    .ConfigureAwait(false);

                _channel = channel;

                if (_intentType == IntentType.Bus && !_queueNames.Contains(_groupName, StringComparer.Ordinal))
                {
                    await _DeclareQueueAsync(_groupName).ConfigureAwait(false);
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

    private string _GetQueueName(string messageName)
    {
        return GetQueueName(_groupName, messageName, _intentType);
    }

    internal static string GetQueueName(string groupName, string messageName, IntentType intentType)
    {
        return intentType == IntentType.Queue ? messageName : groupName;
    }

    private async Task _DeclareQueueAsync(string queueName)
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
                arguments
            )
            .ConfigureAwait(false);
    }
}
