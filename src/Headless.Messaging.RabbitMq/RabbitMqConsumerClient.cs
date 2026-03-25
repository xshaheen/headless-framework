// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
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
    private readonly string _exchangeName;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly ConsumerPauseGate _pauseGate = new();
    private RabbitMqBasicConsumer? _consumer;
    private IChannel? _channel;
    private string? _consumerTag;
    private int _disposed;

    public RabbitMqConsumerClient(
        string groupName,
        byte groupConcurrent,
        IConnectionChannelPool connectionChannelPool,
        IOptions<RabbitMqOptions> options,
        IServiceProvider serviceProvider
    )
    {
        RabbitMqValidation.ValidateQueueName(groupName);

        _groupName = groupName;
        _groupConcurrent = groupConcurrent;
        _connectionChannelPool = connectionChannelPool;
        _serviceProvider = serviceProvider;
        _exchangeName = connectionChannelPool.Exchange;
        _rabbitMqOptions = options.Value;
    }

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("rabbitmq", $"{_rabbitMqOptions.HostName}:{_rabbitMqOptions.Port}");

    public async ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);

        await ConnectAsync();

        foreach (var topic in topics)
        {
            RabbitMqValidation.ValidateTopicName(topic);
            await _channel!.QueueBindAsync(_groupName, _exchangeName, topic);
        }
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await ConnectAsync();

        if (_rabbitMqOptions.BasicQosOptions != null)
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
            ushort prefetch = _groupConcurrent > 0 ? _groupConcurrent : (ushort)1;
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
            _consumerTag = await _channel!.BasicConsumeAsync(_groupName, false, _consumer, cancellationToken);
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
        }

        // RabbitMQ is push-based — after BasicConsumeAsync the broker delivers messages
        // via the consumer callback. We just need to keep this task alive until shutdown.
        // Using Timeout.Infinite avoids repeated timer+task allocations from a polling loop.
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
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
            return;
        if (!await _pauseGate.PauseAsync())
            return;

        if (_consumerTag is not null)
        {
            await _channel!.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken);
        }
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        if (!await _pauseGate.ResumeAsync())
            return;

        // Re-register consumer after transitioning so the broker is
        // delivering messages when the gate unblocks waiters.
        if (_consumerTag is not null)
        {
            _consumerTag = await _channel!.BasicConsumeAsync(_groupName, false, _consumer!, cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        _pauseGate.Release();

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

                await channel
                    .QueueDeclareAsync(
                        _groupName,
                        _rabbitMqOptions.QueueOptions.Durable,
                        _rabbitMqOptions.QueueOptions.Exclusive,
                        _rabbitMqOptions.QueueOptions.AutoDelete,
                        arguments
                    )
                    .ConfigureAwait(false);

                _channel = channel;
            }
            catch (TimeoutException ex)
            {
                _channel = channel;
                var args = new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumerShutdown,
                    Reason = ex.Message + "-->" + nameof(channel.QueueDeclareAsync),
                };

                OnLogCallback!(args);
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
}
