// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using StackExchange.Redis;

namespace Headless.Caching;

/// <summary>Configuration options for <see cref="RedisCache"/>.</summary>
[PublicAPI]
public sealed class RedisCacheOptions : CacheOptions
{
    /// <summary>
    /// Gets or sets the StackExchange.Redis connection multiplexer used for all cache operations. Required.
    /// </summary>
    public required IConnectionMultiplexer ConnectionMultiplexer { get; set; }

    /// <summary>
    /// Gets or sets the StackExchange.Redis command flags applied to all read operations (for example
    /// <c>CommandFlags.PreferReplica</c> to route reads to replicas). Defaults to <c>CommandFlags.None</c>
    /// (primary node reads).
    /// </summary>
    public CommandFlags ReadMode { get; set; } = CommandFlags.None;

    /// <summary>
    /// How long a Family-2 tag/clear invalidation marker fetched from Redis is reused from the process-local
    /// marker cache before it is re-fetched on the next read that needs it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>RemoveByTagAsync</c>/<c>ClearAsync</c> write O(1) timestamp markers in Redis; reads compare a tagged
    /// entry's birth time against the newest applicable marker. To avoid a Redis round-trip on every read, each
    /// instance caches resolved markers for this window. A larger window cuts marker round-trips at the cost of a
    /// longer cross-instance visibility lag for a marker bumped by another instance (the physical TTL still
    /// backstops staleness). The instance that issues the bump invalidates immediately on its own next read.
    /// </para>
    /// <para>The default of 2 seconds balances read overhead against cross-instance propagation latency.</para>
    /// </remarks>
    public TimeSpan TagMarkerRefreshWindow { get; set; } = TimeSpan.FromSeconds(2);
}

internal sealed class RedisCacheOptionsValidator : AbstractValidator<RedisCacheOptions>
{
    public RedisCacheOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotNull();
        RuleFor(x => x.ConnectionMultiplexer).NotNull();
        RuleFor(x => x.ReadMode).IsInEnum();
        RuleFor(x => x.TagMarkerRefreshWindow).GreaterThan(TimeSpan.Zero);
    }
}
