// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Caching.EasyCache;

public sealed class RedisCacheOptions : CacheOptions
{
    /// <summary>Gets or sets the connection string for the Redis cache.</summary>
    public string ConnectionString { get; set; } = default!;

    /// <summary>Gets or sets the Redis database index the cache will use.</summary>
    /// <value>The database.</value>
    public int Database { get; set; }

    /// <summary>Gets or sets the timeout for any connect operations.</summary>
    /// <value>The connection timeout.</value>
    public int ConnectionTimeout { get; set; } = 5000;
}
