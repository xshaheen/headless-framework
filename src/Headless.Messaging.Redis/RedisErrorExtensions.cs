// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Redis;

internal static class RedisErrorExtensions
{
    public static RedisErrorTypes GetRedisErrorType(this string redisError)
    {
        if (string.Equals("BUSYGROUP Consumer Group name already exists", redisError, StringComparison.Ordinal))
        {
            return RedisErrorTypes.GroupAlreadyExists;
        }

        if (string.Equals("ERR no such key", redisError, StringComparison.OrdinalIgnoreCase))
        {
            return RedisErrorTypes.NoGroupInfoExists;
        }

        return RedisErrorTypes.Unknown;
    }

    public static RedisErrorTypes GetRedisErrorType(this Exception exception)
    {
        return exception.Message.GetRedisErrorType();
    }
}
