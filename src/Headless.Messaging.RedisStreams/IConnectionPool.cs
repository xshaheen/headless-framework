// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Headless.Messaging.RedisStreams;

internal interface IRedisConnectionPool
{
    Task<IConnectionMultiplexer> ConnectAsync();
}
