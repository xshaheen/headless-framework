// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Headers = Headless.Messaging.Messages.Headers;

namespace Headless.Messaging.RabbitMq;

public sealed class RabbitMqBasicConsumer(
    IChannel channel,
    byte concurrent,
    string groupName,
    Func<TransportMessage, object?, Task> msgCallback,
    Action<LogMessageEventArgs> logCallback,
    Func<BasicDeliverEventArgs, IServiceProvider, List<KeyValuePair<string, string>>>? customHeadersBuilder,
    IServiceProvider serviceProvider
) : AsyncDefaultBasicConsumer(channel), IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(concurrent);
    private readonly bool _usingTaskRun = concurrent > 0;

    public override async Task HandleBasicDeliverAsync(
        string consumerTag,
        ulong deliveryTag,
        bool redelivered,
        string exchange,
        string routingKey,
        IReadOnlyBasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default
    )
    {
        if (_usingTaskRun)
        {
            await _semaphore.WaitAsync(cancellationToken);
            // Copy of the body safe to use outside the RabbitMQ thread context
            ReadOnlyMemory<byte> safeBody = body.ToArray();
            _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            await _Consume(
                                    consumerTag,
                                    deliveryTag,
                                    redelivered,
                                    exchange,
                                    routingKey,
                                    properties,
                                    safeBody
                                )
                                .AnyContext();
                        }
                        catch (Exception ex)
                        {
                            var args = new LogMessageEventArgs
                            {
                                LogType = MqLogType.ConsumeError,
                                Reason = $"Error consuming message: {ex}",
                            };

                            logCallback(args);

                            try
                            {
                                if (Channel.IsOpen)
                                {
                                    await Channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true);
                                }
                            }
#pragma warning disable ERP022
                            catch
                            {
                                // Nack failure already logged via callback
                            }
#pragma warning restore ERP022
                            finally
                            {
                                _semaphore.Release();
                            }
                        }
                    },
                    cancellationToken
                )
                .AnyContext();
        }
        else
        {
            await _Consume(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties, body).AnyContext();
        }
    }

    private Task _Consume(
        string consumerTag,
        ulong deliveryTag,
        bool redelivered,
        string exchange,
        string routingKey,
        IReadOnlyBasicProperties properties,
        ReadOnlyMemory<byte> body
    )
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (properties.Headers != null)
        {
            foreach (var header in properties.Headers)
            {
                if (header.Value is byte[] val)
                {
                    headers.Add(header.Key, Encoding.UTF8.GetString(val));
                }
                else
                {
                    headers.Add(header.Key, header.Value?.ToString());
                }
            }
        }

        headers[Headers.Group] = groupName;

        if (customHeadersBuilder != null)
        {
            var e = new BasicDeliverEventArgs(
                consumerTag,
                deliveryTag,
                redelivered,
                exchange,
                routingKey,
                properties,
                body
            );
            var customHeaders = customHeadersBuilder(e, serviceProvider);
            foreach (var customHeader in customHeaders)
            {
                headers[customHeader.Key] = customHeader.Value;
            }
        }

        var message = new TransportMessage(headers, body);

        return msgCallback(message, deliveryTag);
    }

    public async Task BasicAck(ulong deliveryTag)
    {
        if (Channel.IsOpen)
        {
            await Channel.BasicAckAsync(deliveryTag, false);
        }

        _semaphore.Release();
    }

    public async Task BasicReject(ulong deliveryTag)
    {
        if (Channel.IsOpen)
        {
            await Channel.BasicRejectAsync(deliveryTag, true);
        }

        _semaphore.Release();
    }

    protected override async Task OnCancelAsync(string[] consumerTags, CancellationToken cancellationToken = default)
    {
        await base.OnCancelAsync(consumerTags, cancellationToken);

        var args = new LogMessageEventArgs
        {
            LogType = MqLogType.ConsumerCancelled,
            Reason = string.Join(",", consumerTags),
        };

        logCallback(args);
    }

    public override async Task HandleBasicCancelOkAsync(
        string consumerTag,
        CancellationToken cancellationToken = default
    )
    {
        await base.HandleBasicCancelOkAsync(consumerTag, cancellationToken);

        var args = new LogMessageEventArgs { LogType = MqLogType.ConsumerUnregistered, Reason = consumerTag };

        logCallback(args);
    }

    public override async Task HandleBasicConsumeOkAsync(
        string consumerTag,
        CancellationToken cancellationToken = default
    )
    {
        await base.HandleBasicConsumeOkAsync(consumerTag, cancellationToken);

        var args = new LogMessageEventArgs { LogType = MqLogType.ConsumerRegistered, Reason = consumerTag };

        logCallback(args);
    }

    public override async Task HandleChannelShutdownAsync(object channel, ShutdownEventArgs reason)
    {
        await base.HandleChannelShutdownAsync(channel, reason);

        var args = new LogMessageEventArgs { LogType = MqLogType.ConsumerShutdown, Reason = reason.ReplyText };

        logCallback(args);
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
