// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal static class RedisStreamManagerExtensions
{
    /// <summary>
    /// Yields the positions whose consumer group exists, creating a missing group on first sight. A pair confirmed
    /// once is remembered in <paramref name="ensuredGroups"/> and not checked again until the caller forgets it, which
    /// it does on a <c>NOGROUP</c> reply; checking on every poll costs two round trips per stream.
    /// </summary>
    /// <remarks>
    /// Only an error the server answered with skips a stream. A connection or timeout failure propagates so the read
    /// fails as a whole and counts toward the broker's reachability; swallowing it would report a successful empty
    /// read and reset the failure count after every real failure.
    /// </remarks>
    public static async IAsyncEnumerable<StreamPosition> TryGetOrCreateConsumerGroupPositionsAsync(
        this IDatabase database,
        StreamPosition[] positions,
        string consumerGroup,
        ConcurrentDictionary<(string Stream, string Group), byte> ensuredGroups,
        ILogger logger
    )
    {
        foreach (var position in positions)
        {
            var key = (position.Key.ToString(), consumerGroup);

            if (!ensuredGroups.ContainsKey(key))
            {
                var created = false;
                try
                {
                    await database
                        .TryGetOrCreateStreamConsumerGroupAsync(position.Key, consumerGroup)
                        .ConfigureAwait(false);

                    ensuredGroups[key] = 0;
                    created = true;
                }
                catch (RedisServerException ex)
                {
                    if (ex.GetRedisErrorType() == RedisErrorTypes.Unknown)
                    {
                        logger?.LogCreateConsumerGroupFailed(ex, consumerGroup, position.Key);
                    }
                }

                if (!created)
                {
                    continue;
                }
            }

            yield return position;
        }
    }

    public static async Task TryGetOrCreateStreamConsumerGroupAsync(
        this IDatabase database,
        RedisKey stream,
        RedisValue consumerGroup
    )
    {
        var streamExist = await database.KeyExistsAsync(stream).ConfigureAwait(false);
        if (streamExist)
        {
            await database._TryGetOrCreateStreamGroupAsync(stream, consumerGroup).ConfigureAwait(false);
            return;
        }

        try
        {
            await database
                .StreamCreateConsumerGroupAsync(stream, consumerGroup, StreamPosition.NewMessages)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex.GetRedisErrorType() == RedisErrorTypes.GroupAlreadyExists)
        {
            // ignore, group already exists
        }
    }

    private static async Task _TryGetOrCreateStreamGroupAsync(
        this IDatabase database,
        RedisKey stream,
        RedisValue consumerGroup
    )
    {
        try
        {
            var groupInfo = await database.StreamGroupInfoAsync(stream).ConfigureAwait(false);

            if (groupInfo.Any(g => g.Name == consumerGroup))
            {
                return;
            }

            await database
                .StreamCreateConsumerGroupAsync(stream, consumerGroup, StreamPosition.NewMessages)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (ex.GetRedisErrorType() is RedisErrorTypes.NoGroupInfoExists)
            {
                await database.TryGetOrCreateStreamConsumerGroupAsync(stream, consumerGroup).ConfigureAwait(false);
                return;
            }

            throw;
        }
    }
}

internal static partial class RedisStreamManagerExtensionsLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "CreateConsumerGroupFailed",
        Level = LogLevel.Error,
        Message = "Redis error while creating consumer group [{ConsumerGroup}] of stream [{Position}]"
    )]
    public static partial void LogCreateConsumerGroupFailed(
        this ILogger logger,
        Exception exception,
        string consumerGroup,
        RedisKey position
    );
}
