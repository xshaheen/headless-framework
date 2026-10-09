// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal sealed class RedisStreamManager(
    IRedisConnectionPool connectionsPool,
    IOptions<RedisMessagingOptions> options,
    ILogger<RedisStreamManager> logger,
    TimeProvider? timeProvider = null
) : IRedisStreamManager
{
    // Checks and deletes in one atomic step: XGROUP DELCONSUMER discards the consumer's pending entries, so a
    // consumer that read between a separate check and the delete would lose what it read. RESP2 returns each
    // XINFO CONSUMERS row as a flat name/value list. pcall turns a missing stream or group into "nothing deleted".
    private const string _DeleteIdleConsumersScript = """
        local consumers = redis.pcall('XINFO', 'CONSUMERS', KEYS[1], ARGV[1])
        if type(consumers) ~= 'table' or consumers.err then
            return 0
        end
        local minIdle = tonumber(ARGV[2])
        local deleted = 0
        for _, consumer in ipairs(consumers) do
            local name, pending, idle
            for i = 1, #consumer, 2 do
                local field = consumer[i]
                if field == 'name' then
                    name = consumer[i + 1]
                elseif field == 'pending' then
                    pending = consumer[i + 1]
                elseif field == 'idle' then
                    idle = consumer[i + 1]
                end
            end
            if name and pending == 0 and idle and idle >= minIdle then
                redis.call('XGROUP', 'DELCONSUMER', KEYS[1], ARGV[1], name)
                deleted = deleted + 1
            end
        end
        return deleted
        """;

    private readonly RedisMessagingOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private IConnectionMultiplexer? _redis;

    public async Task CreateStreamWithConsumerGroupAsync(
        string stream,
        string consumerGroup,
        CancellationToken cancellationToken = default
    )
    {
        await _ConnectAsync(cancellationToken).ConfigureAwait(false);

        //The object returned from GetDatabase is a cheap pass - thru object, and does not need to be stored
        var database = _redis!.GetDatabase();

        await database
            .TryGetOrCreateStreamConsumerGroupAsync(stream, consumerGroup)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task PublishAsync(
        string stream,
        NameValueEntry[] message,
        CancellationToken cancellationToken = default
    )
    {
        await _ConnectAsync(cancellationToken).ConfigureAwait(false);
        var redis = _redis!;

        //The object returned from GetDatabase is a cheap pass - thru object, and does not need to be stored
        await redis.GetDatabase().StreamAddAsync(stream, message, _CreateAddOptions()).ConfigureAwait(false);

        if (_options.WakeConsumersOnPublish)
        {
            _AnnounceEntry(redis, stream);
        }
    }

    // Sent only after the XADD has completed, so a consumer it wakes always finds the entry. Fire-and-forget: a lost
    // wake-up only delays the entry to the consumers' next poll, so it must neither fail nor slow the publish.
    private void _AnnounceEntry(IConnectionMultiplexer redis, string stream)
    {
        try
        {
            _ = redis
                .GetSubscriber()
                .PublishAsync(
                    RedisChannel.Literal(RedisPhysicalAddress.WakeChannel(stream)),
                    RedisValue.EmptyString,
                    CommandFlags.FireAndForget
                );
        }
        catch (Exception ex)
        {
            logger.LogWakePublishFailed(ex, stream);
        }
    }

    public async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> PollStreamsLatestMessagesAsync(
        string[] streams,
        string consumerGroup,
        string consumerName,
        TimeSpan pollDelay,
        [EnumeratorCancellation] CancellationToken token
    )
    {
        // Subscription set is fixed for the consumer's lifetime, so materialize positions once
        // instead of rebuilding the StreamPosition[] on every poll iteration.
        var positions = streams.Select(stream => new StreamPosition(stream, StreamPosition.NewMessages)).ToArray();

        var errorDelay = pollDelay;
        await using var wake = _CreateWake(streams);

        while (true)
        {
            await wake.EnsureSubscribedAsync(token).ConfigureAwait(false);
            var readStartedAt = _timeProvider.GetTimestamp();

            var (succeeded, streamsRead) = await _TryReadConsumerGroupAsync(
                    consumerGroup,
                    consumerName,
                    positions,
                    token
                )
                .ConfigureAwait(false);

            // Materialized once: it is yielded and then inspected for a full batch.
            var result = streamsRead.ToArray();

            yield return result;

            if (succeeded)
            {
                errorDelay = pollDelay;

                // A full batch means a backlog: read on at once instead of waiting a poll interval per batch.
                if (!_HasFullBatch(result))
                {
                    await wake.WaitAsync(pollDelay, readStartedAt, token).ConfigureAwait(false);
                }
            }
            else
            {
                // During a Redis outage back off instead of re-hammering at the fixed poll cadence.
                errorDelay = _NextBackoff(errorDelay);
                await _timeProvider.Delay(errorDelay, token).ConfigureAwait(false);
            }
        }

        // ReSharper disable once IteratorNeverReturns
    }

    public async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> PollStreamsPendingMessagesAsync(
        string[] streams,
        string consumerGroup,
        string consumerName,
        TimeSpan pollDelay,
        [EnumeratorCancellation] CancellationToken token
    )
    {
        // Subscription set is fixed for the consumer's lifetime, so materialize positions once
        // instead of rebuilding the StreamPosition[] on every poll iteration.
        var positions = streams.Select(stream => new StreamPosition(stream, StreamPosition.Beginning)).ToArray();

        while (true)
        {
            token.ThrowIfCancellationRequested();

            // Materialize the lazy SelectMany result once: it is both yielded to the consumer and
            // re-inspected by the All() check below, so leaving it deferred would flatten it twice.
            var result = (
                await _TryReadConsumerGroupAsync(consumerGroup, consumerName, positions, token).ConfigureAwait(false)
            ).Streams.ToArray();

            yield return result;

            //Once we consumed our history of pending messages, we can break the loop.
            if (result.All(s => s.Entries.Length < _options.StreamEntriesCount))
            {
                break;
            }

            await _timeProvider.Delay(pollDelay, token).ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> PollStreamsStalePendingMessagesAsync(
        string[] streams,
        string consumerGroup,
        string consumerName,
        TimeSpan claimMinIdleTime,
        TimeSpan pollDelay,
        [EnumeratorCancellation] CancellationToken token
    )
    {
        var positions = streams.Select(stream => new StreamPosition(stream, StreamPosition.Beginning)).ToArray();
        var nextStartIds = streams.ToDictionary(stream => (RedisKey)stream, _ => StreamPosition.Beginning);
        var errorDelay = pollDelay;

        // The sweep runs on the first pass and then once per claim interval: dead consumers only need to go
        // eventually, and the script reads every consumer of the group.
        long? lastSweep = null;

        while (true)
        {
            if (
                _options.IdleConsumerDeleteAfter > TimeSpan.Zero
                && (lastSweep is null || _timeProvider.GetElapsedTime(lastSweep.Value) >= claimMinIdleTime)
            )
            {
                lastSweep = _timeProvider.GetTimestamp();
                await _TryDeleteIdleConsumersAsync(streams, consumerGroup, token).ConfigureAwait(false);
            }

            var (succeeded, result) = await _TryAutoClaimStalePendingAsync(
                    consumerGroup,
                    consumerName,
                    positions,
                    nextStartIds,
                    claimMinIdleTime,
                    _options.StreamEntriesCount,
                    token
                )
                .ConfigureAwait(false);

            yield return result;

            if (succeeded)
            {
                errorDelay = pollDelay;

                // A full batch of stale entries, such as a crashed consumer's backlog, is claimed on at once.
                if (!_HasFullBatch(result))
                {
                    await _timeProvider.Delay(pollDelay, token).ConfigureAwait(false);
                }
            }
            else
            {
                errorDelay = _NextBackoff(errorDelay);
                await _timeProvider.Delay(errorDelay, token).ConfigureAwait(false);
            }
        }

        // ReSharper disable once IteratorNeverReturns
    }

    public async Task<StreamPosition[]> GetStreamTailPositionsAsync(
        string[] streams,
        CancellationToken cancellationToken = default
    )
    {
        await _ConnectAsync(cancellationToken).ConfigureAwait(false);

        var database = _redis!.GetDatabase();
        var positions = new StreamPosition[streams.Length];

        for (var i = 0; i < streams.Length; i++)
        {
            // XREVRANGE COUNT 1 answers an absent stream with no entries instead of an error, unlike XINFO STREAM.
            var newest = await database
                .StreamRangeAsync(streams[i], count: 1, messageOrder: Order.Descending)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            // "0-0" reads an absent stream from its first entry, and every entry it gets is newer than this call.
            positions[i] = new StreamPosition(streams[i], newest.Length > 0 ? newest[0].Id : StreamPosition.Beginning);
        }

        return positions;
    }

    public async IAsyncEnumerable<IEnumerable<RedisStreamMessages>> PollStreamsFromAsync(
        StreamPosition[] startPositions,
        TimeSpan pollDelay,
        [EnumeratorCancellation] CancellationToken token
    )
    {
        // StackExchange.Redis never sends blocking commands, which would stall its shared multiplexer, so XREAD
        // polls at the same cadence as the consumer-group reads, woken early by publishes. Resuming from the last id
        // read keeps a reconnect gap-free for as long as the stream retains the entries.
        var positions = startPositions.ToArray();
        var errorDelay = pollDelay;
        await using var wake = _CreateWake(positions.Select(position => position.Key.ToString()));

        while (true)
        {
            await wake.EnsureSubscribedAsync(token).ConfigureAwait(false);
            var readStartedAt = _timeProvider.GetTimestamp();

            var (succeeded, result) = await _TryReadAsync(positions, token).ConfigureAwait(false);

            yield return result;

            if (!succeeded)
            {
                errorDelay = _NextBackoff(errorDelay);
                await _timeProvider.Delay(errorDelay, token).ConfigureAwait(false);
                continue;
            }

            errorDelay = pollDelay;

            // A full batch means a backlog: read on at once instead of waiting a poll interval per batch.
            if (!_HasFullBatch(result))
            {
                await wake.WaitAsync(pollDelay, readStartedAt, token).ConfigureAwait(false);
            }
        }

        // ReSharper disable once IteratorNeverReturns
    }

    public async Task Ack(
        string stream,
        string consumerGroup,
        string messageId,
        CancellationToken cancellationToken = default
    )
    {
        await _ConnectAsync(cancellationToken).ConfigureAwait(false);

        await _redis!.GetDatabase().StreamAcknowledgeAsync(stream, consumerGroup, messageId).ConfigureAwait(false);
    }

    private async Task<(bool Succeeded, IEnumerable<RedisStreamMessages> Streams)> _TryReadConsumerGroupAsync(
        string consumerGroup,
        string consumerName,
        StreamPosition[] positions,
        CancellationToken token
    )
    {
        try
        {
            token.ThrowIfCancellationRequested();

            List<StreamPosition> createdPositions = [];

            await _ConnectAsync(token).ConfigureAwait(false);

            var database = _redis!.GetDatabase();

            await foreach (
                var position in database
                    .TryGetOrCreateConsumerGroupPositionsAsync(positions, consumerGroup, logger)
                    .ConfigureAwait(false)
                    .WithCancellation(token)
            )
            {
                createdPositions.Add(position);
            }

            if (createdPositions.Count == 0)
            {
                return (Succeeded: true, Streams: []);
            }

            //calculate keys HashSlots to start reading per HashSlot
            var groupedPositions = createdPositions
                .GroupBy(s => _redis.GetHashSlot(s.Key))
                .Select(group =>
                    database.StreamReadGroupAsync(
                        [.. group],
                        consumerGroup,
                        consumerName,
                        _options.StreamEntriesCount,
                        noAck: false
                    )
                );

            var readSet = await Task.WhenAll(groupedPositions).ConfigureAwait(false);

            return (
                Succeeded: true,
                Streams: readSet.SelectMany(set =>
                    set.Select(stream => new RedisStreamMessages(stream.Key, stream.Entries))
                )
            );
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a read failure; the caller's next Delay(token) ends the poll loop.
            return (Succeeded: true, Streams: []);
        }
        catch (Exception ex)
        {
            logger.LogReadConsumerGroupFailed(ex, consumerGroup);
        }

        return (Succeeded: false, Streams: []);
    }

    private async Task<(bool Succeeded, RedisStreamMessages[] Streams)> _TryReadAsync(
        StreamPosition[] positions,
        CancellationToken token
    )
    {
        try
        {
            token.ThrowIfCancellationRequested();

            await _ConnectAsync(token).ConfigureAwait(false);

            var database = _redis!.GetDatabase();

            // A multi-key XREAD must stay within one hash slot on a cluster.
            var reads = positions
                .Select((position, index) => (position, index))
                .GroupBy(x => _redis.GetHashSlot(x.position.Key))
                .Select(async group =>
                {
                    var members = group.ToArray();
                    var read = await database
                        .StreamReadAsync([.. members.Select(x => x.position)], _options.StreamEntriesCount)
                        .ConfigureAwait(false);

                    return (members, read);
                });

            var results = await Task.WhenAll(reads).ConfigureAwait(false);
            List<RedisStreamMessages> streams = [];

            foreach (var (members, read) in results)
            {
                foreach (var stream in read)
                {
                    if (stream.Entries.Length == 0)
                    {
                        continue;
                    }

                    var index = Array.Find(members, x => x.position.Key == stream.Key).index;
                    positions[index] = new StreamPosition(stream.Key, stream.Entries[^1].Id);
                    streams.Add(new RedisStreamMessages(stream.Key, stream.Entries));
                }
            }

            return (Succeeded: true, Streams: [.. streams]);
        }
        catch (OperationCanceledException)
        {
            return (Succeeded: true, Streams: []);
        }
        catch (Exception ex)
        {
            logger.LogReadStreamsFailed(ex);
        }

        return (Succeeded: false, Streams: []);
    }

    private async Task<(bool Succeeded, RedisStreamMessages[] Streams)> _TryAutoClaimStalePendingAsync(
        string consumerGroup,
        string consumerName,
        StreamPosition[] positions,
        Dictionary<RedisKey, RedisValue> nextStartIds,
        TimeSpan claimMinIdleTime,
        int count,
        CancellationToken token
    )
    {
        try
        {
            token.ThrowIfCancellationRequested();

            List<RedisStreamMessages> streams = [];

            await _ConnectAsync(token).ConfigureAwait(false);

            var database = _redis!.GetDatabase();
            var minIdleTimeMilliseconds = _ToRedisMilliseconds(claimMinIdleTime);

            await foreach (
                var position in database
                    .TryGetOrCreateConsumerGroupPositionsAsync(positions, consumerGroup, logger)
                    .ConfigureAwait(false)
                    .WithCancellation(token)
            )
            {
                if (!nextStartIds.TryGetValue(position.Key, out var startId))
                {
                    startId = StreamPosition.Beginning;
                }

                var result = await database
                    .StreamAutoClaimAsync(
                        position.Key,
                        consumerGroup,
                        consumerName,
                        minIdleTimeMilliseconds,
                        startId,
                        count
                    )
                    .ConfigureAwait(false);

                if (result.IsNull)
                {
                    continue;
                }

                nextStartIds[position.Key] = result.NextStartId;

                if (result.ClaimedEntries.Length > 0)
                {
                    streams.Add(new RedisStreamMessages(position.Key, result.ClaimedEntries));
                }
            }

            return (Succeeded: true, Streams: [.. streams]);
        }
        catch (OperationCanceledException)
        {
            return (Succeeded: true, Streams: []);
        }
        catch (Exception ex)
        {
            logger.LogAutoClaimConsumerGroupFailed(ex, consumerGroup);
        }

        return (Succeeded: false, Streams: []);
    }

    private async Task _TryDeleteIdleConsumersAsync(string[] streams, string consumerGroup, CancellationToken token)
    {
        try
        {
            await _ConnectAsync(token).ConfigureAwait(false);

            var database = _redis!.GetDatabase();
            RedisValue[] args = [consumerGroup, _ToRedisMilliseconds(_options.IdleConsumerDeleteAfter)];

            foreach (var stream in streams)
            {
                var deleted = (long)
                    await database
                        .ScriptEvaluateAsync(_DeleteIdleConsumersScript, [stream], args)
                        .WaitAsync(token)
                        .ConfigureAwait(false);

                if (deleted > 0)
                {
                    logger.LogIdleConsumersDeleted(deleted, consumerGroup, stream);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown: the claim loop's next Delay(token) ends it.
        }
        catch (Exception ex)
        {
            // A failed sweep only leaves dead consumer names listed until the next one.
            logger.LogDeleteIdleConsumersFailed(ex, consumerGroup);
        }
    }

    private RedisStreamWake _CreateWake(IEnumerable<string> streams)
    {
        return new RedisStreamWake(
            _options.WakeConsumersOnPublish ? streams : [],
            connectionsPool,
            _timeProvider,
            logger
        );
    }

    private StreamAddOptions _CreateAddOptions()
    {
        if (_options.StreamMaxAge <= TimeSpan.Zero)
        {
            return default;
        }

        // Entry ids lead with the server's millisecond clock, so the smallest id to keep encodes the age limit.
        // "~" lets Redis trim whole internal nodes only, which keeps the trim cheap and only ever keeps entries longer.
        var minIdMilliseconds = Math.Max(
            0,
            _timeProvider.GetUtcNow().ToUnixTimeMilliseconds() - _ToRedisMilliseconds(_options.StreamMaxAge)
        );

        return new StreamAddOptions
        {
            MinId = string.Create(CultureInfo.InvariantCulture, $"{minIdMilliseconds}-0"),
            Approximate = true,
        };
    }

    private bool _HasFullBatch(RedisStreamMessages[] streams)
    {
        return Array.Exists(streams, stream => stream.Entries.Length >= _options.StreamEntriesCount);
    }

    private static long _ToRedisMilliseconds(TimeSpan value)
    {
        return Math.Max(1, (long)Math.Ceiling(value.TotalMilliseconds));
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

    private async Task _ConnectAsync(CancellationToken cancellationToken = default)
    {
        _redis = await connectionsPool.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal static partial class RedisStreamManagerLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ReadConsumerGroupFailed",
        Level = LogLevel.Error,
        Message = "Redis error when trying read consumer group {ConsumerGroup}"
    )]
    public static partial void LogReadConsumerGroupFailed(
        this ILogger logger,
        Exception exception,
        string consumerGroup
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "ReadStreamsFailed",
        Level = LogLevel.Error,
        Message = "Redis error when trying to read streams without a consumer group"
    )]
    public static partial void LogReadStreamsFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        EventName = "AutoClaimConsumerGroupFailed",
        Level = LogLevel.Error,
        Message = "Redis error when trying to auto-claim pending messages for consumer group {ConsumerGroup}"
    )]
    public static partial void LogAutoClaimConsumerGroupFailed(
        this ILogger logger,
        Exception exception,
        string consumerGroup
    );

    [LoggerMessage(
        EventId = 4,
        EventName = "IdleConsumersDeleted",
        Level = LogLevel.Information,
        Message = "Deleted {Count} idle Redis consumers with no pending entries from group {ConsumerGroup} of stream {Stream}"
    )]
    public static partial void LogIdleConsumersDeleted(
        this ILogger logger,
        long count,
        string consumerGroup,
        string stream
    );

    [LoggerMessage(
        EventId = 7,
        EventName = "WakePublishFailed",
        Level = LogLevel.Warning,
        Message = "Redis error when announcing a new entry on stream {Stream}; its consumers read it at their next poll"
    )]
    public static partial void LogWakePublishFailed(this ILogger logger, Exception exception, string stream);

    [LoggerMessage(
        EventId = 5,
        EventName = "DeleteIdleConsumersFailed",
        Level = LogLevel.Warning,
        Message = "Redis error when trying to delete idle consumers of consumer group {ConsumerGroup}"
    )]
    public static partial void LogDeleteIdleConsumersFailed(
        this ILogger logger,
        Exception exception,
        string consumerGroup
    );
}
