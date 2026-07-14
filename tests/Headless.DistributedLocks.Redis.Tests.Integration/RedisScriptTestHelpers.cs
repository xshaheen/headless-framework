// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.Redis.Scripts;
using Headless.Redis;
using StackExchange.Redis;

namespace Tests;

internal static class RedisScriptTestHelpers
{
    public static async Task<bool> ReplaceIfEqualAsync(
        this HeadlessRedisScriptsLoader loader,
        IDatabase db,
        RedisKey key,
        string? expectedValue,
        string? newValue,
        TimeSpan? newTtl = null
    )
    {
        var result = await loader
            .EvaluateAsync(
                db,
                ReplaceIfEqualScriptDefinition.Instance,
                new
                {
                    key,
                    value = (RedisValue?)newValue ?? RedisValue.Null,
                    expected = (RedisValue)(expectedValue ?? string.Empty),
                    expires = newTtl.HasValue ? (long)newTtl.Value.TotalMilliseconds : RedisValue.EmptyString,
                }
            )
            .ConfigureAwait(false);

        return (int)result > 0;
    }

    public static async Task<bool> RemoveIfEqualAsync(
        this HeadlessRedisScriptsLoader loader,
        IDatabase db,
        RedisKey key,
        string? expectedValue
    )
    {
        var result = await loader
            .EvaluateAsync(
                db,
                RemoveIfEqualScriptDefinition.Instance,
                new { key, expected = (RedisValue?)expectedValue ?? RedisValue.Null }
            )
            .ConfigureAwait(false);

        return (int)result > 0;
    }
}
