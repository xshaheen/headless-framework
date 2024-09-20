// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using StackExchange.Redis;

namespace Framework.Caching;

public sealed class RedisCacheOptions : CacheOptions
{
    public required IConnectionMultiplexer ConnectionMultiplexer { get; set; }

    /// <summary>The behaviour required when performing read operations from cache.</summary>
    public CommandFlags ReadMode { get; set; } = CommandFlags.None;
}
