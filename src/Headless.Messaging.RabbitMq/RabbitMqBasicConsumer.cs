// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

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
            _ObserveBackgroundHandler(
                Task.Run(
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
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            _ReleaseSemaphore();
                        }
                    },
                    CancellationToken.None // Ensure semaphore release even if cancellation is requested during handler execution
                )
            );
        }
        else
        {
            await _Consume(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties, body)
                .ConfigureAwait(false);
        }
    }

    private async Task _Consume(
        string consumerTag,
        ulong deliveryTag,
        bool redelivered,
        string exchange,
        string routingKey,
        IReadOnlyBasicProperties properties,
        ReadOnlyMemory<byte> body
    )
    {
        TransportMessage message;
        try
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

            message = new TransportMessage(headers, body);
        }
        catch (Exception ex)
        {
            logCallback(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason = $"Failed to build transport message, nacking delivery {deliveryTag}: {ex}",
                }
            );

            try
            {
                if (Channel.IsOpen)
                {
                    await Channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true).ConfigureAwait(false);
                }
            }
            catch (Exception nackEx)
            {
                logCallback(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ConsumeError,
                        Reason = $"Failed to nack delivery {deliveryTag}: {nackEx}",
                    }
                );
            }

            return;
        }

        await msgCallback(message, deliveryTag).ConfigureAwait(false);
    }

    public async Task BasicAck(ulong deliveryTag)
    {
        if (Channel.IsOpen)
        {
            await Channel.BasicAckAsync(deliveryTag, false);
        }
    }

    public async Task BasicReject(ulong deliveryTag)
    {
        if (Channel.IsOpen)
        {
            await Channel.BasicRejectAsync(deliveryTag, true);
        }
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

    private void _ReleaseSemaphore()
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
            // Shutdown in progress
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
                    logCallback(
                        new LogMessageEventArgs
                        {
                            LogType = MqLogType.ConsumeError,
                            Reason = $"Error consuming message: {exception}",
                        }
                    );
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
