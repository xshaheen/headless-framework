// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
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
                    expires = newTtl.HasValue
                        ? (RedisValue)(long)newTtl.Value.TotalMilliseconds
                        : RedisValue.EmptyString,
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

    public static async Task<long> IncrementAsync(
        this HeadlessRedisScriptsLoader loader,
        IDatabase db,
        RedisKey key,
        long value,
        TimeSpan ttl
    )
    {
        var result = await loader
            .EvaluateAsync(
                db,
                IncrementWithExpireScriptDefinition.Instance,
                new
                {
                    key,
                    value = (RedisValue)value,
                    expires = (long)ttl.TotalMilliseconds,
                }
            )
            .ConfigureAwait(false);

        return long.Parse(result.ToString(), CultureInfo.InvariantCulture);
    }

    public static async Task<double> IncrementAsync(
        this HeadlessRedisScriptsLoader loader,
        IDatabase db,
        RedisKey key,
        double value,
        TimeSpan ttl
    )
    {
        var result = await loader
            .EvaluateAsync(
                db,
                IncrementWithExpireScriptDefinition.Instance,
                new
                {
                    key,
                    value = value.ToString(CultureInfo.InvariantCulture),
                    expires = (long)ttl.TotalMilliseconds,
                }
            )
            .ConfigureAwait(false);

        return double.Parse(result.ToString(), CultureInfo.InvariantCulture);
    }

    public static Task<long> SetIfHigherAsync(
        this HeadlessRedisScriptsLoader loader,
        IDatabase db,
        RedisKey key,
        long value,
        TimeSpan? ttl = null
    )
    {
        return loader._SetIfAsync<long>(db, SetIfHigherScriptDefinition.Instance, key, value, ttl);
    }

    public static Task<double> SetIfHigherAsync(
        this HeadlessRedisScriptsLoader loader,
        IDatabase db,
        RedisKey key,
        double value,
        TimeSpan? ttl = null
    )
    {
        return loader._SetIfAsync<double>(db, SetIfHigherScriptDefinition.Instance, key, value, ttl);
    }

    public static Task<long> SetIfLowerAsync(
        this HeadlessRedisScriptsLoader loader,
        IDatabase db,
        RedisKey key,
        long value,
        TimeSpan? ttl = null
    )
    {
        return loader._SetIfAsync<long>(db, SetIfLowerScriptDefinition.Instance, key, value, ttl);
    }

    public static Task<double> SetIfLowerAsync(
        this HeadlessRedisScriptsLoader loader,
        IDatabase db,
        RedisKey key,
        double value,
        TimeSpan? ttl = null
    )
    {
        return loader._SetIfAsync<double>(db, SetIfLowerScriptDefinition.Instance, key, value, ttl);
    }

    private static async Task<T> _SetIfAsync<T>(
        this HeadlessRedisScriptsLoader loader,
        IDatabase db,
        RedisScriptDefinition scriptDefinition,
        RedisKey key,
        T value,
        TimeSpan? ttl
    )
        where T : IParsable<T>, IFormattable
    {
        var result = await loader
            .EvaluateAsync(
                db,
                scriptDefinition,
                new
                {
                    key,
                    value = value.ToString(null, CultureInfo.InvariantCulture),
                    expires = ttl.HasValue ? (RedisValue)(long)ttl.Value.TotalMilliseconds : RedisValue.EmptyString,
                }
            )
            .ConfigureAwait(false);

        return T.Parse(result.ToString(), CultureInfo.InvariantCulture);
    }
}
