// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using StackExchange.Redis;

namespace Framework.Redis;

public sealed class FrameworkRedisScriptsLoader(
    IConnectionMultiplexer multiplexer,
    TimeProvider? timeProvider = null,
    ILogger<FrameworkRedisScriptsLoader>? logger = null
)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private bool _scriptsLoaded;
    private readonly AsyncLock _loadScriptsLock = new();

    public LoadedLuaScript? IncrementWithExpireScript { get; private set; }

    public LoadedLuaScript? RemoveIfEqualScript { get; private set; }

    public LoadedLuaScript? ReplaceIfEqualScript { get; private set; }

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

                await Task.WhenAll(loadIncrementScriptTask, loadRemoveScriptTask, loadReplaceScriptTask)
                    .WithAggregatedExceptions()
                    .AnyContext();

                IncrementWithExpireScript = await incrementWithExpire.LoadAsync(server).AnyContext();
                RemoveIfEqualScript = await removeIfEqual.LoadAsync(server).AnyContext();
                ReplaceIfEqualScript = await replaceIfEqual.LoadAsync(server).AnyContext();
            }

            _scriptsLoaded = true;

            if (traceEnabled)
            {
                logger!.LogTrace("Scripts loaded successfully in {Elapsed:g}", _timeProvider.GetElapsedTime(timestamp));
            }
        }
    }

    public static object GetReplaceIfEqualParameters(RedisKey key, string value, string expected, TimeSpan? expires)
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
            expires = "",
        };
    }
}
