// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal interface IRedisConnectionPool
{
    Task<IConnectionMultiplexer> ConnectAsync();
}
