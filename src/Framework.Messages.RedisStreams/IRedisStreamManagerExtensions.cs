// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Framework.Messages;

internal static class RedisStreamManagerExtensions
{
    public static async IAsyncEnumerable<StreamPosition> TryGetOrCreateConsumerGroupPositionsAsync(
        this IDatabase database,
        StreamPosition[] positions,
        string consumerGroup,
        ILogger logger
    )
    {
        foreach (var position in positions)
        {
            var created = false;
            try
            {
                await database.TryGetOrCreateStreamConsumerGroupAsync(position.Key, consumerGroup).AnyContext();

                created = true;
            }
            catch (Exception ex)
            {
                if (ex.GetRedisErrorType() == RedisErrorTypes.Unknown)
                {
                    logger?.LogError(
                        ex,
                        "Redis error while creating consumer group [{consumerGroup}] of stream [{position}]",
                        consumerGroup,
                        position.Key
                    );
                }
            }

            if (created)
            {
                yield return position;
            }
        }
    }

    public static async Task TryGetOrCreateStreamConsumerGroupAsync(
        this IDatabase database,
        RedisKey stream,
        RedisValue consumerGroup
    )
    {
        var streamExist = await database.KeyExistsAsync(stream).AnyContext();
        if (streamExist)
        {
            await database._TryGetOrCreateStreamGroupAsync(stream, consumerGroup).AnyContext();
            return;
        }

        try
        {
            await database
                .StreamCreateConsumerGroupAsync(stream, consumerGroup, StreamPosition.NewMessages)
                .AnyContext();
        }
        catch (Exception ex)
        {
            if (ex.GetRedisErrorType() != RedisErrorTypes.GroupAlreadyExists)
            {
                throw;
            }
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
            var groupInfo = await database.StreamGroupInfoAsync(stream);

            if (groupInfo.Any(g => g.Name == consumerGroup))
            {
                return;
            }

            await database
                .StreamCreateConsumerGroupAsync(stream, consumerGroup, StreamPosition.NewMessages)
                .AnyContext();
        }
        catch (Exception ex)
        {
            if (ex.GetRedisErrorType() is var type && type == RedisErrorTypes.NoGroupInfoExists)
            {
                await database.TryGetOrCreateStreamConsumerGroupAsync(stream, consumerGroup).AnyContext();
                return;
            }

            throw;
        }
    }
}
