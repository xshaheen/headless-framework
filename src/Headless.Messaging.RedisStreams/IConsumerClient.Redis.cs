// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.RedisStreams;

internal class RedisConsumerClient(
    string groupId,
    byte groupConcurrent,
    IRedisStreamManager redis,
    IOptions<MessagingRedisOptions> options,
    ILogger<RedisConsumerClient> logger
) : IConsumerClient
{
    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);
    private string[] _topics = default!;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("redis", options.Value.Endpoint);

    public async ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);

        foreach (var topic in topics)
        {
            await redis.CreateStreamWithConsumerGroupAsync(topic, groupId);
        }

        _topics = topics.ToArray();
    }

    public ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ = _ListeningForMessagesAsync(timeout, cancellationToken);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cancellationToken.WaitHandle.WaitOne(timeout);
        }
        // ReSharper disable once FunctionNeverReturns
    }

    public async ValueTask CommitAsync(object? sender)
    {
        var (stream, group, id) = ((string stream, string group, string id))sender!;

        await redis.Ack(stream, group, id);

        _semaphore.Release();
    }

    public ValueTask RejectAsync(object? sender)
    {
        _semaphore.Release();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task _ListeningForMessagesAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        //first time, we want to read our pending messages, in case we crashed and are recovering.
        var pendingMsgs = redis.PollStreamsPendingMessagesAsync(_topics, groupId, timeout, cancellationToken);

        await _ConsumeMessages(pendingMsgs, StreamPosition.Beginning, cancellationToken).ConfigureAwait(false);

        //Once we consumed our history, we can start getting new messages.
        var newMsgs = redis.PollStreamsLatestMessagesAsync(_topics, groupId, timeout, cancellationToken);

        _ = _ConsumeMessages(newMsgs, StreamPosition.NewMessages, cancellationToken);
    }

    private async Task _ConsumeMessages(
        IAsyncEnumerable<IEnumerable<RedisStream>> streamsSet,
        RedisValue position,
        CancellationToken cancellationToken
    )
    {
        await foreach (var set in streamsSet.WithCancellation(cancellationToken))
        {
            foreach (var stream in set)
            {
                foreach (var entry in stream.Entries)
                {
                    if (entry.IsNull)
                    {
                        return;
                    }

                    if (groupConcurrent > 0)
                    {
                        await _semaphore.WaitAsync(cancellationToken);
                        _ = Task.Run(() => consumeAsync(position, stream, entry), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await consumeAsync(position, stream, entry);
                    }
                }
            }
        }

        async Task consumeAsync(RedisValue position, RedisStream stream, StreamEntry entry)
        {
            try
            {
                var message = RedisMessage.Create(entry, groupId);
                await OnMessageCallback!(message, (stream.Key.ToString(), groupId, entry.Id.ToString()));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    message: "Redis entry {EntryId} on stream {StreamKey} at position {Position} of group {GroupId} is not valid for Messaging, see inner exception for more details.",
                    entry.Id,
                    stream.Key,
                    position,
                    groupId
                );

                var logArgs = new LogMessageEventArgs { LogType = MqLogType.RedisConsumeError, Reason = ex.ToString() };

                try
                {
                    var onError = options.Value.OnConsumeError?.Invoke(
                        new MessagingRedisOptions.ConsumeErrorContext(ex, entry)
                    );

                    await (onError ?? Task.CompletedTask).ConfigureAwait(false);
                }
                catch (Exception onError)
                {
                    logger.LogError(
                        onError,
                        "Unhandled exception occurred in {Action} action, Exception has been caught",
                        nameof(MessagingRedisOptions.OnConsumeError)
                    );
                }
                finally
                {
                    OnLogCallback!(logArgs);
                }
            }
            finally
            {
                var positionName =
                    position == StreamPosition.Beginning
                        ? nameof(StreamPosition.Beginning)
                        : nameof(StreamPosition.NewMessages);
                logger.LogDebug(
                    "Redis stream entry [{EntryId}] [position : {PositionName}] was delivered",
                    entry.Id,
                    positionName
                );
            }
        }
    }
}
