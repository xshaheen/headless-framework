// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using StackExchange.Redis;

namespace Headless.Redis;

// ReSharper disable InconsistentNaming
#pragma warning disable IDE1006
public sealed class HeadlessRedisScriptsLoader(
    IConnectionMultiplexer multiplexer,
    TimeProvider? timeProvider = null,
    ILogger<HeadlessRedisScriptsLoader>? logger = null
) : IDisposable
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private volatile bool _scriptsLoaded;
    private bool _eventsSubscribed;
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

            // Subscribe to connection events for automatic script reload on reconnection
            if (!_eventsSubscribed)
            {
                multiplexer.ConnectionRestored += _OnConnectionRestored;
                _eventsSubscribed = true;
            }

            if (traceEnabled)
            {
                logger!.LogTrace("Scripts loaded successfully in {Elapsed:g}", _timeProvider.GetElapsedTime(timestamp));
            }
        }
    }

    /// <summary>Resets the scripts loaded state, forcing a reload on next operation.</summary>
    public void ResetScripts()
    {
        _scriptsLoaded = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_eventsSubscribed)
        {
            multiplexer.ConnectionRestored -= _OnConnectionRestored;
            _eventsSubscribed = false;
        }
    }

    private void _OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        logger?.LogInformation("Redis connection restored, resetting script state");
        _scriptsLoaded = false;
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

    private static ReplaceIfEqualParams _GetReplaceIfEqualParameters(
        RedisKey key,
        string? value,
        string? expected,
        TimeSpan? expires
    )
    {
        // Use empty string as sentinel for null expected (key should not exist)
        var expectedValue = expected ?? string.Empty;
        var expiresValue = expires.HasValue ? (int)expires.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new ReplaceIfEqualParams(key, value, expectedValue, expiresValue);
    }

    private static RemoveIfEqualParams _GetRemoveIfEqualParameters(RedisKey key, string? expected)
    {
        return new RemoveIfEqualParams(key, expected);
    }

    private static IncrementParams _GetIncrementParameters<T>(string resource, T value, TimeSpan ttl)
        where T : INumber<T>
    {
        // Convert to string to preserve precision and let Redis handle the type
        var valueStr = value.ToString(null, CultureInfo.InvariantCulture);
        return new IncrementParams((RedisKey)resource, valueStr, (int)ttl.TotalMilliseconds);
    }

    private static SetIfParams _GetIfParameters<T>(string key, T value, TimeSpan? ttl)
        where T : INumber<T>
    {
        // Convert value to string to preserve precision for floating-point numbers
        var valueStr = value.ToString(null, CultureInfo.InvariantCulture);
        var expiresValue = ttl.HasValue ? (int)ttl.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new SetIfParams((RedisKey)key, valueStr, expiresValue);
    }

    #endregion

    #region Parameter Types

    /// <summary>Parameters for the ReplaceIfEqual Lua script.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ReplaceIfEqualParams(
        RedisKey key,
        string? value,
        string expected,
        RedisValue expires
    );

    /// <summary>Parameters for the RemoveIfEqual Lua script.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct RemoveIfEqualParams(RedisKey key, string? expected);

    /// <summary>Parameters for the IncrementWithExpire Lua script.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct IncrementParams(RedisKey key, RedisValue value, int expires);

    /// <summary>Parameters for the SetIfHigher/SetIfLower Lua scripts.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct SetIfParams(RedisKey key, string value, RedisValue expires);

    #endregion
}
