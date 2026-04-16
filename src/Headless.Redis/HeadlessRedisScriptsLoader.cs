// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Numerics;
using System.Runtime.InteropServices;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using StackExchange.Redis;

namespace Headless.Redis;

using ScriptSelector = Func<HeadlessRedisScriptsLoader, LoadedLuaScript?>;

// ReSharper disable InconsistentNaming
#pragma warning disable IDE1006
public sealed class HeadlessRedisScriptsLoader(
    IConnectionMultiplexer multiplexer,
    TimeProvider? timeProvider = null,
    ILogger<HeadlessRedisScriptsLoader>? logger = null
) : IDisposable
{
    // Cached selectors avoid per-call delegate allocation on the hot path.
    private static readonly ScriptSelector _replaceIfEqualSelector = static x => x.ReplaceIfEqualScript;
    private static readonly ScriptSelector _removeIfEqualSelector = static x => x.RemoveIfEqualScript;
    private static readonly ScriptSelector _incrementSelector = static x => x.IncrementWithExpireScript;
    private static readonly ScriptSelector _setIfLowerSelector = static x => x.SetIfLowerScript;
    private static readonly ScriptSelector _setIfHigherSelector = static x => x.SetIfHigherScript;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private int _version = 1; // Odd = not loaded, Even = loaded
    private bool _eventsSubscribed;
    private readonly AsyncLock _loadScriptsLock = new();

    public LoadedLuaScript? IncrementWithExpireScript { get; private set; }

    public LoadedLuaScript? RemoveIfEqualScript { get; private set; }

    public LoadedLuaScript? ReplaceIfEqualScript { get; private set; }

    public LoadedLuaScript? SetIfHigherScript { get; private set; }

    public LoadedLuaScript? SetIfLowerScript { get; private set; }

    public async ValueTask LoadScriptsAsync(CancellationToken cancellationToken = default)
    {
        if ((Volatile.Read(ref _version) & 1) == 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var timestamp = _timeProvider.GetTimestamp();

        using (await _loadScriptsLock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if ((_version & 1) == 0)
            {
                return;
            }

            logger?.LogPreparingLuaScript();

            var incrementWithExpire = LuaScript.Prepare(RedisScripts.IncrementWithExpire);
            var removeIfEqual = LuaScript.Prepare(RedisScripts.RemoveIfEqual);
            var replaceIfEqual = LuaScript.Prepare(RedisScripts.ReplaceIfEqual);
            var setIfHigher = LuaScript.Prepare(RedisScripts.SetIfHigher);
            var setIfLower = LuaScript.Prepare(RedisScripts.SetIfLower);

            LoadedLuaScript? loadedIncrement = null;
            LoadedLuaScript? loadedRemove = null;
            LoadedLuaScript? loadedReplace = null;
            LoadedLuaScript? loadedSetIfHigher = null;
            LoadedLuaScript? loadedSetIfLower = null;

            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                var server = multiplexer.GetServer(endpoint);

                if (server.IsReplica)
                {
                    continue;
                }

                logger?.LogLoadingLuaScripts(endpoint);

                var loadIncrementScriptTask = incrementWithExpire.LoadAsync(server);
                var loadRemoveScriptTask = removeIfEqual.LoadAsync(server);
                var loadReplaceScriptTask = replaceIfEqual.LoadAsync(server);
                var loadSetIfHigherScriptTask = setIfHigher.LoadAsync(server);
                var loadSetIfLowerScriptTask = setIfLower.LoadAsync(server);

                var whenAll = Task.WhenAll(
                    loadIncrementScriptTask,
                    loadRemoveScriptTask,
                    loadReplaceScriptTask,
                    loadSetIfHigherScriptTask,
                    loadSetIfLowerScriptTask
                );

                var results = await whenAll.WithAggregatedExceptions().ConfigureAwait(false);
                loadedIncrement = results[0];
                loadedRemove = results[1];
                loadedReplace = results[2];
                loadedSetIfHigher = results[3];
                loadedSetIfLower = results[4];
            }

            // Only publish loaded scripts after every master endpoint has loaded successfully.
            // Partial assignment on failure would leave stale scripts visible to callers.
            IncrementWithExpireScript = loadedIncrement;
            RemoveIfEqualScript = loadedRemove;
            ReplaceIfEqualScript = loadedReplace;
            SetIfHigherScript = loadedSetIfHigher;
            SetIfLowerScript = loadedSetIfLower;

            _version++; // Becomes even (loaded)

            // Subscribe to connection events for automatic script reload on reconnection
            if (!_eventsSubscribed)
            {
                multiplexer.ConnectionRestored += _OnConnectionRestored;
                _eventsSubscribed = true;
            }

            var elapsed = _timeProvider.GetElapsedTime(timestamp);
            logger?.LogScriptsLoadedSuccessfully(elapsed);
        }
    }

    /// <summary>Resets the scripts loaded state, forcing a reload on next operation.</summary>
    public void ResetScripts()
    {
        var v = Volatile.Read(ref _version);
        if ((v & 1) == 0) // If even (loaded)
        {
            Interlocked.CompareExchange(ref _version, v + 1, v); // Make it odd (unloaded)
        }
    }

    /// <summary>
    /// Resets the scripts loaded state only if the current version matches the expected version.
    /// This prevents redundant resets in high-concurrency scenarios.
    /// </summary>
    public void ResetScripts(int expectedVersion)
    {
        if ((expectedVersion & 1) == 0) // Only reset if it was even (loaded)
        {
            Interlocked.CompareExchange(ref _version, expectedVersion + 1, expectedVersion);
        }
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
        logger?.LogConnectionRestored();
        ResetScripts();
    }

    public async Task<bool> ReplaceIfEqualAsync(
        IDatabase db,
        RedisKey key,
        string? expectedValue,
        string? newValue,
        TimeSpan? newTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(db);

        var parameters = _GetReplaceIfEqualParameters(key, newValue, expectedValue, newTtl);
        var redisResult = await EvaluateAsync(db, _replaceIfEqualSelector, parameters, cancellationToken)
            .ConfigureAwait(false);
        var result = (int)redisResult;

        return result > 0;
    }

    public async Task<bool> RemoveIfEqualAsync(
        IDatabase db,
        RedisKey key,
        string? expectedValue,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(db);

        var parameters = _GetRemoveIfEqualParameters(key, expectedValue);
        var redisResult = await EvaluateAsync(db, _removeIfEqualSelector, parameters, cancellationToken)
            .ConfigureAwait(false);
        var result = (int)redisResult;

        return result > 0;
    }

    public async Task<long> IncrementAsync(
        IDatabase db,
        string resource,
        long value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(db);
        Argument.IsNotNullOrEmpty(resource);

        var parameters = _GetIncrementParameters(resource, value, ttl);
        var result = await EvaluateAsync(db, _incrementSelector, parameters, cancellationToken).ConfigureAwait(false);

        return (long)result;
    }

    public async Task<double> IncrementAsync(
        IDatabase db,
        string resource,
        double value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(db);
        Argument.IsNotNullOrEmpty(resource);

        var parameters = _GetIncrementParameters(resource, value, ttl);
        var result = await EvaluateAsync(db, _incrementSelector, parameters, cancellationToken).ConfigureAwait(false);

        return (double)result;
    }

    public async Task<long> SetIfLowerAsync(
        IDatabase db,
        string key,
        long value,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(db);
        Argument.IsNotNullOrEmpty(key);

        var parameters = _GetIfParameters(key, value, ttl);
        var result = await EvaluateAsync(db, _setIfLowerSelector, parameters, cancellationToken).ConfigureAwait(false);

        return (long)result;
    }

    public async Task<double> SetIfLowerAsync(
        IDatabase db,
        string key,
        double value,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(db);
        Argument.IsNotNullOrEmpty(key);

        var parameters = _GetIfParameters(key, value, ttl);
        var result = await EvaluateAsync(db, _setIfLowerSelector, parameters, cancellationToken).ConfigureAwait(false);

        return (double)result;
    }

    public async Task<long> SetIfHigherAsync(
        IDatabase db,
        string key,
        long value,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(db);
        Argument.IsNotNullOrEmpty(key);

        var parameters = _GetIfParameters(key, value, ttl);
        var result = await EvaluateAsync(db, _setIfHigherSelector, parameters, cancellationToken).ConfigureAwait(false);

        return (long)result;
    }

    public async Task<double> SetIfHigherAsync(
        IDatabase db,
        string key,
        double value,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(db);
        Argument.IsNotNullOrEmpty(key);

        var parameters = _GetIfParameters(key, value, ttl);
        var result = await EvaluateAsync(db, _setIfHigherSelector, parameters, cancellationToken).ConfigureAwait(false);

        return (double)result;
    }

    public delegate LoadedLuaScript? ScriptSelector(HeadlessRedisScriptsLoader loader);

    /// <summary>
    /// Evaluates a Lua script with automatic recovery from NOSCRIPT errors, which occur when the
    /// server's script cache is invalidated between the caller's <see cref="LoadScriptsAsync"/>
    /// check and the evaluation (failover, server restart, or SCRIPT FLUSH). On NOSCRIPT we reset,
    /// reload, and retry exactly once using the freshly-loaded script reference.
    /// </summary>
    public async Task<RedisResult> EvaluateAsync(
        IDatabase db,
        ScriptSelector scriptSelector,
        object? parameters,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await LoadScriptsAsync(cancellationToken).ConfigureAwait(false);

        var versionAtStart = Volatile.Read(ref _version);
        var script = scriptSelector(this);
        if (script is null)
        {
            throw new InvalidOperationException("Scripts were not loaded correctly.");
        }

        try
        {
            return await db.ScriptEvaluateAsync(script, parameters).ConfigureAwait(false);
        }
        catch (RedisServerException e) when (IsNoScriptError(e))
        {
            logger?.LogNoScriptRetry();

            // Only reset if no one has successfully loaded scripts since we started this call.
            // This prevents redundant reloads in high-concurrency scenarios.
            ResetScripts(versionAtStart);

            await LoadScriptsAsync(cancellationToken).ConfigureAwait(false);

            script = scriptSelector(this);
            if (script is null)
            {
                throw new InvalidOperationException("Scripts were not loaded correctly.");
            }

            return await db.ScriptEvaluateAsync(script, parameters).ConfigureAwait(false);
        }
    }

    public static bool IsNoScriptError(RedisServerException e)
    {
        return e.Message.StartsWith("NOSCRIPT", StringComparison.Ordinal);
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
        var valueStr = value.ToString(format: null, CultureInfo.InvariantCulture);
        return new IncrementParams((RedisKey)resource, valueStr, (int)ttl.TotalMilliseconds);
    }

    private static SetIfParams _GetIfParameters<T>(string key, T value, TimeSpan? ttl)
        where T : INumber<T>
    {
        // Convert value to string to preserve precision for floating-point numbers
        var valueStr = value.ToString(format: null, CultureInfo.InvariantCulture);
        var expiresValue = ttl.HasValue ? (int)ttl.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new SetIfParams((RedisKey)key, valueStr, expiresValue);
    }

    private static bool _IsNoScriptError(RedisServerException e)
    {
        return e.Message.StartsWith("NOSCRIPT", StringComparison.Ordinal);
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
