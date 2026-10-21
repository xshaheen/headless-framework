// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Caching.Scripts;

/// <summary>
/// Loads the Redis cache's Lua scripts after the host starts, onto every writable node of each registered cache
/// (the default one and every named one), so the first cache call runs the script by SHA instead of paying the load.
/// </summary>
/// <remarks>
/// It does not gate readiness: a script not loaded yet still loads on its first use, and the <c>cache-redis</c> check
/// already reports a Redis that is down. A failure is retried with the warm-up back-off.
/// </remarks>
internal sealed class RedisCacheScriptsWarmup(IServiceProvider services, IEnumerable<RedisCacheScriptsLoaderKey> keys)
    : IWarmup
{
    private static readonly IReadOnlyList<RedisScriptDefinition> _Scripts =
    [
        IncrementWithExpireScriptDefinition.Instance,
        CacheRemoveIfEqualScriptDefinition.Instance,
        CacheReplaceIfEqualScriptDefinition.Instance,
        CacheTaggedSetScriptDefinition.Instance,
        SetIfHigherScriptDefinition.Instance,
        SetIfLowerScriptDefinition.Instance,
        SlidingRearmScriptDefinition.Instance,
    ];

    public string Name => "RedisCacheScripts";

    public bool GatesReadiness => false;

    public Task WarmUpAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(
            keys.Select(k => k.Key)
                .Distinct(StringComparer.Ordinal)
                .Select(key =>
                    services
                        .GetRequiredKeyedService<HeadlessRedisScriptsLoader>(key)
                        .LoadAsync(_Scripts, cancellationToken)
                        .AsTask()
                )
        );
}

/// <summary>Names one registered cache's scripts loader, so the warm-up loads the scripts onto every cache's Redis.</summary>
internal sealed record RedisCacheScriptsLoaderKey(string Key);
