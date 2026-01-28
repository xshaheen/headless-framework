// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Numerics;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using StackExchange.Redis;

namespace Headless.Redis;

public sealed class HeadlessRedisScriptsLoader(
    IConnectionMultiplexer multiplexer,
    TimeProvider? timeProvider = null,
    ILogger<HeadlessRedisScriptsLoader>? logger = null
)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private bool _scriptsLoaded;
    private readonly AsyncLock _loadScriptsLock = new();

    public LoadedLuaScript? IncrementWithExpireScript { get; private set; }

    public LoadedLuaScript? RemoveIfEqualScript { get; private set; }

    public LoadedLuaScript? ReplaceIfEqualScript { get; private set; }

    public LoadedLuaScript? SetIfHigherScript { get; private set; }

    public LoadedLuaScript? SetIfLowerScript { get; private set; }

    public async Task LoadScriptsAsync()
    {
        if (_scriptsLoaded)
        {
            return;
        }

        var timestamp = _timeProvider.GetTimestamp();
        var traceEnabled = logger?.IsEnabled(LogLevel.Trace) ?? false;

        using (await _loadScriptsLock.LockAsync())
        {
            if (_scriptsLoaded)
            {
                return;
            }

            if (traceEnabled)
            {
                logger!.LogTrace("Preparing Lua script");
            }

            var incrementWithExpire = LuaScript.Prepare(RedisScripts.IncrementWithExpire);
            var removeIfEqual = LuaScript.Prepare(RedisScripts.RemoveIfEqual);
            var replaceIfEqual = LuaScript.Prepare(RedisScripts.ReplaceIfEqual);
            var setIfHigher = LuaScript.Prepare(RedisScripts.SetIfHigher);
            var setIfLower = LuaScript.Prepare(RedisScripts.SetIfLower);

            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                var server = multiplexer.GetServer(endpoint);

                if (server.IsReplica)
                {
                    continue;
                }

                if (traceEnabled)
                {
                    logger!.LogTrace("Loading Lua scripts to server: {@Endpoint}", endpoint);
                }

                var loadIncrementScriptTask = incrementWithExpire.LoadAsync(server);
                var loadRemoveScriptTask = removeIfEqual.LoadAsync(server);
                var loadReplaceScriptTask = replaceIfEqual.LoadAsync(server);
                var loadSetIfHigherScriptTask = setIfHigher.LoadAsync(server);
                var loadSetIfLowerScriptTask = setIfLower.LoadAsync(server);

                var results = await Task.WhenAll(
                        loadIncrementScriptTask,
                        loadRemoveScriptTask,
                        loadReplaceScriptTask,
                        loadSetIfHigherScriptTask,
                        loadSetIfLowerScriptTask
                    )
                    .WithAggregatedExceptions()
                    .AnyContext();

                IncrementWithExpireScript = results[0];
                RemoveIfEqualScript = results[1];
                ReplaceIfEqualScript = results[2];
                SetIfHigherScript = results[3];
                SetIfLowerScript = results[4];
            }

            _scriptsLoaded = true;

            if (traceEnabled)
            {
                logger!.LogTrace("Scripts loaded successfully in {Elapsed:g}", _timeProvider.GetElapsedTime(timestamp));
            }
        }
    }

    public async Task<bool> ReplaceIfEqualAsync(
        IDatabase db,
        RedisKey key,
        string? expectedValue,
        string? newValue,
        TimeSpan? newTtl = null
    )
    {
        await LoadScriptsAsync().AnyContext();

        var redisResult = await db.ScriptEvaluateAsync(
                ReplaceIfEqualScript!,
                _GetReplaceIfEqualParameters(key, newValue, expectedValue, newTtl)
            )
            .AnyContext();

        var result = (int)redisResult;

        return result > 0;
    }

    public async Task<bool> RemoveIfEqualAsync(IDatabase db, RedisKey key, string? expectedValue)
    {
        await LoadScriptsAsync().AnyContext();
        var parameters = _GetRemoveIfEqualParameters(key, expectedValue);
        var redisResult = await db.ScriptEvaluateAsync(RemoveIfEqualScript!, parameters).AnyContext();
        var result = (int)redisResult;

        return result > 0;
    }

    public async Task<long> IncrementAsync(IDatabase db, string resource, long value, TimeSpan ttl)
    {
        await LoadScriptsAsync().AnyContext();
        var parameters = _GetIncrementParameters(resource, value, ttl);
        var result = await db.ScriptEvaluateAsync(IncrementWithExpireScript!, parameters).AnyContext();

        return (long)result;
    }

    public async Task<double> IncrementAsync(IDatabase db, string resource, double value, TimeSpan ttl)
    {
        await LoadScriptsAsync().AnyContext();
        var parameters = _GetIncrementParameters(resource, value, ttl);
        var result = await db.ScriptEvaluateAsync(IncrementWithExpireScript!, parameters).AnyContext();

        return (double)result;
    }

    public async Task<long> SetIfLowerAsync(IDatabase db, string key, long value, TimeSpan? ttl = null)
    {
        await LoadScriptsAsync().AnyContext();
        var parameters = _GetIfParameters(key, value, ttl);
        var result = await db.ScriptEvaluateAsync(SetIfLowerScript!, parameters).AnyContext();

        return (long)result;
    }

    public async Task<double> SetIfLowerAsync(IDatabase db, string key, double value, TimeSpan? ttl = null)
    {
        await LoadScriptsAsync().AnyContext();
        var parameters = _GetIfParameters(key, value, ttl);
        var result = await db.ScriptEvaluateAsync(SetIfLowerScript!, parameters).AnyContext();

        return (double)result;
    }

    public async Task<long> SetIfHigherAsync(IDatabase db, string key, long value, TimeSpan? ttl = null)
    {
        await LoadScriptsAsync().AnyContext();
        var parameters = _GetIfParameters(key, value, ttl);
        var result = await db.ScriptEvaluateAsync(SetIfHigherScript!, parameters).AnyContext();

        return (long)result;
    }

    public async Task<double> SetIfHigherAsync(IDatabase db, string key, double value, TimeSpan? ttl = null)
    {
        await LoadScriptsAsync().AnyContext();
        var parameters = _GetIfParameters(key, value, ttl);
        var result = await db.ScriptEvaluateAsync(SetIfHigherScript!, parameters).AnyContext();

        return (double)result;
    }

    #region Helpers

    private static object _GetReplaceIfEqualParameters(RedisKey key, string? value, string? expected, TimeSpan? expires)
    {
        if (expires.HasValue)
        {
            return new
            {
                key,
                value,
                expected,
                expires = (int)expires.Value.TotalMilliseconds,
            };
        }

        return new
        {
            key,
            value,
            expected,
            expires = RedisValue.EmptyString,
        };
    }

    private static object _GetRemoveIfEqualParameters(RedisKey key, string? expected)
    {
        return new { key, expected };
    }

    private static object _GetIncrementParameters<T>(string resource, T value, TimeSpan ttl)
        where T : INumber<T>
    {
        return new
        {
            key = (RedisKey)resource,
            value,
            expires = (int)ttl.TotalMilliseconds,
        };
    }

    private static object _GetIfParameters<T>(string key, T value, TimeSpan? ttl)
        where T : INumber<T>
    {
        return ttl.HasValue
            ? new
            {
                key = (RedisKey)key,
                value,
                expires = (int)ttl.Value.TotalMilliseconds,
            }
            : new
            {
                key = (RedisKey)key,
                value,
                expires = RedisValue.EmptyString,
            };
    }

    #endregion
}
