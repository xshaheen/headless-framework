// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal sealed class RedisPubSubConsumerClient(
    string groupName,
    byte groupConcurrent,
    IRedisPubSubConnectionProvider connectionProvider,
    IOptions<RedisPubSubOptions> options,
    ILogger<RedisPubSubConsumerClient> logger,
    TimeProvider timeProvider
) : IConsumerClient
{
    private readonly SemaphoreSlim? _semaphore = groupConcurrent > 0 ? new SemaphoreSlim(groupConcurrent) : null;
    private readonly Dictionary<string, ChannelMessageQueue> _subscriptions = new(StringComparer.Ordinal);
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("redis_pubsub", options.Value.DisplayEndpoint);

    public ValueTask<ICollection<string>> FetchMessageNamesAsync(IEnumerable<string> messageNames)
    {
        return ValueTask.FromResult<ICollection<string>>([.. Argument.IsNotNull(messageNames)]);
    }

    public async ValueTask SubscribeAsync(IEnumerable<string> messageNames)
    {
        Argument.IsNotNull(messageNames);

        if (Volatile.Read(ref _disposed) != 0)
        {
            ObjectDisposedException.ThrowIf(condition: true, instance: this);
        }

        var connection = await connectionProvider.ConnectAsync().ConfigureAwait(false);
        var subscriber = connection.GetSubscriber();

        foreach (var messageName in Argument.IsNotNull(messageNames).Distinct(StringComparer.Ordinal))
        {
            if (_subscriptions.ContainsKey(messageName))
            {
                continue;
            }

            var queue = await subscriber.SubscribeAsync(RedisChannel.Literal(messageName)).ConfigureAwait(false);
            queue.OnMessage(_DispatchWithConcurrencyAsync);
            _subscriptions.Add(messageName, queue);
        }

        _ready.TrySetResult();
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ = timeout;
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await timeProvider.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
    }

    public ValueTask CommitAsync(object? sender)
    {
        _ = sender;
        return ValueTask.CompletedTask;
    }

    public ValueTask RejectAsync(object? sender)
    {
        _ = sender;
        return ValueTask.CompletedTask;
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default) =>
        await _pauseGate.PauseAsync().ConfigureAwait(false);

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default) =>
        await _pauseGate.ResumeAsync().ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _pauseGate.Release();

        foreach (var subscription in _subscriptions.Values)
        {
            await subscription.UnsubscribeAsync().ConfigureAwait(false);
        }

        _subscriptions.Clear();
        _semaphore?.Dispose();
        _ready.TrySetCanceled();
    }

    private async Task _DispatchWithConcurrencyAsync(ChannelMessage channelMessage)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (_semaphore is null)
        {
            await _DispatchAsync(channelMessage).ConfigureAwait(false);
            return;
        }

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await _DispatchAsync(channelMessage).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task _DispatchAsync(ChannelMessage channelMessage)
    {
        TransportMessage message = default;
        var messageDeserialized = false;
        try
        {
            await _pauseGate.WaitIfPausedAsync(CancellationToken.None).ConfigureAwait(false);

            message = RedisPubSubEnvelope.Deserialize(channelMessage.Message!);
            messageDeserialized = true;
            message.Headers[Headers.Group] = groupName;

            if (OnMessageCallback is not null)
            {
                await OnMessageCallback(message, null).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.RedisPubSubMessageDispatchFailed(ex, channelMessage.Channel.ToString());
            MessageEventCounterSource.Log.WritePubSubDispatchFailureMetric();
            OnLogCallback?.Invoke(
                new LogMessageEventArgs { LogType = MqLogType.ExceptionReceived, Reason = ex.Message }
            );

            var onDispatchFailed = options.Value.OnDispatchFailed;
            if (onDispatchFailed is not null)
            {
                try
                {
                    await onDispatchFailed(ex, messageDeserialized ? message : null).ConfigureAwait(false);
                }
                catch (Exception callbackEx)
                {
                    logger.RedisPubSubDispatchFailedCallbackFailed(callbackEx, channelMessage.Channel.ToString());
                }
            }
        }
    }
}

internal static partial class RedisPubSubConsumerClientLog
{
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Redis Pub/Sub message dispatch failed for channel {Channel}."
    )]
    public static partial void RedisPubSubMessageDispatchFailed(
        this ILogger logger,
        Exception exception,
        string channel
    );

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Redis Pub/Sub OnDispatchFailed callback threw an exception for channel {Channel}."
    )]
    public static partial void RedisPubSubDispatchFailedCallbackFailed(
        this ILogger logger,
        Exception exception,
        string channel
    );
}
