// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Headless.Messaging.RabbitMq;

internal sealed class RabbitMqConsumerClient(
    string groupName,
    byte groupConcurrent,
    IConnectionChannelPool connectionChannelPool,
    IOptions<RabbitMqOptions> options,
    IServiceProvider serviceProvider
) : IConsumerClient
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly string _exchangeName = connectionChannelPool.Exchange;
    private readonly RabbitMqOptions _rabbitMqOptions = options.Value;
    private RabbitMqBasicConsumer? _consumer;
    private IChannel? _channel;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("rabbitmq", $"{_rabbitMqOptions.HostName}:{_rabbitMqOptions.Port}");

    public async ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);

        await ConnectAsync();

        foreach (var topic in topics)
        {
            await _channel!.QueueBindAsync(groupName, _exchangeName, topic);
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
            ushort prefetch = groupConcurrent > 0 ? groupConcurrent : (ushort)1;
            await _channel!.BasicQosAsync(prefetchSize: 0, prefetchCount: prefetch, global: false, cancellationToken);
        }

        _consumer = new RabbitMqBasicConsumer(
            _channel!,
            groupConcurrent,
            groupName,
            OnMessageCallback!,
            OnLogCallback!,
            _rabbitMqOptions.CustomHeadersBuilder,
            serviceProvider
        );

        try
        {
            await _channel!.BasicConsumeAsync(groupName, false, _consumer, cancellationToken);
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

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(timeout, cancellationToken).AnyContext();
        }
        // ReSharper disable once FunctionNeverReturns
    }

    public async ValueTask CommitAsync(object? sender)
    {
        await _consumer!.BasicAck((ulong)sender!);
    }

    public async ValueTask RejectAsync(object? sender)
    {
        await _consumer!.BasicReject((ulong)sender!);
    }

    public ValueTask DisposeAsync()
    {
        _consumer?.Dispose();
        _channel?.Dispose();
        _semaphore.Dispose();
        return ValueTask.CompletedTask;
        //The connection should not be closed here, because the connection is still in use elsewhere.
        //_connection?.Dispose();
    }

    public async Task ConnectAsync()
    {
        var connection = await connectionChannelPool.GetConnectionAsync();

        await _semaphore.WaitAsync();

        if (_channel == null || _channel.IsClosed)
        {
            _channel = await connection.CreateChannelAsync();

            await _channel.ExchangeDeclareAsync(_exchangeName, RabbitMqOptions.ExchangeType, true);

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

            try
            {
                await _channel.QueueDeclareAsync(
                    groupName,
                    _rabbitMqOptions.QueueOptions.Durable,
                    _rabbitMqOptions.QueueOptions.Exclusive,
                    _rabbitMqOptions.QueueOptions.AutoDelete,
                    arguments
                );
            }
            catch (TimeoutException ex)
            {
                var args = new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumerShutdown,
                    Reason = ex.Message + "-->" + nameof(_channel.QueueDeclareAsync),
                };

                OnLogCallback!(args);
            }
        }

        _semaphore.Release();
    }
}
