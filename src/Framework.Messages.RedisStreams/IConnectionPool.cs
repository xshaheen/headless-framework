// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Framework.Messages.RedisStreams;

internal interface IRedisConnectionPool
{
    Task<IConnectionMultiplexer> ConnectAsync();
}
