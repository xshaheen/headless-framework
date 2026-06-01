// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
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
    private readonly AsyncLock _loadScriptsLock = new();
    private int _version = 1; // Odd = not loaded, Even = at least one script loaded
    private bool _eventsSubscribed;
    private Dictionary<Type, LoadedLuaScript> _loadedScripts = [];

    public async ValueTask LoadAsync(
        IEnumerable<RedisScriptDefinition> scriptDefinitions,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(scriptDefinitions);

        var definitions = _GetDistinctDefinitions(scriptDefinitions);
        if (definitions.Count is 0)
        {
            return;
        }

        if ((Volatile.Read(ref _version) & 1) == 0 && _ContainsAll(definitions))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var timestamp = _timeProvider.GetTimestamp();

        using (await _loadScriptsLock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if ((_version & 1) == 0 && _ContainsAll(definitions))
            {
                return;
            }

            logger?.LogPreparingLuaScript();

            var loadedScripts = new Dictionary<Type, LoadedLuaScript>(_loadedScripts);
            var loadedEndpointCount = 0;

            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                var server = multiplexer.GetServer(endpoint);

                if (server.IsReplica || !server.IsConnected)
                {
                    continue;
                }

                logger?.LogLoadingLuaScripts(endpoint);

                var loadTasks = definitions.Select(script => script.LoadAsync(server)).ToArray();
                var results = await Task.WhenAll(loadTasks).WithAggregatedExceptions().ConfigureAwait(false);

                for (var index = 0; index < definitions.Count; index++)
                {
                    loadedScripts[definitions[index].GetType()] = results[index];
                }

                loadedEndpointCount++;
            }

            if (loadedEndpointCount == 0)
            {
                throw new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect,
                    "No writable Redis endpoints were available for Lua script loading."
                );
            }

            _loadedScripts = loadedScripts;
            _MarkLoaded();
            _SubscribeConnectionRestored();

            var elapsed = _timeProvider.GetElapsedTime(timestamp);
            logger?.LogScriptsLoadedSuccessfully(elapsed);
        }
    }

    /// <summary>
    /// Evaluates a Lua script definition with automatic recovery from NOSCRIPT errors.
    /// </summary>
    public async Task<RedisResult> EvaluateAsync(
        IDatabase db,
        RedisScriptDefinition scriptDefinition,
        object? parameters,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(db);
        Argument.IsNotNull(scriptDefinition);

        cancellationToken.ThrowIfCancellationRequested();

        var script = await _GetOrLoadScriptAsync(scriptDefinition, cancellationToken).ConfigureAwait(false);
        var versionAtStart = Volatile.Read(ref _version);

        try
        {
            return await db.ScriptEvaluateAsync(script, parameters).ConfigureAwait(false);
        }
        catch (RedisServerException e) when (IsNoScriptError(e))
        {
            logger?.LogNoScriptRetry();

            // Only reset if no one has successfully loaded scripts since we started this call.
            // This prevents redundant reloads in high-concurrency scenarios.
            _ResetScripts(versionAtStart);

            script = await _GetOrLoadScriptAsync(scriptDefinition, cancellationToken).ConfigureAwait(false);

            return await db.ScriptEvaluateAsync(script, parameters).ConfigureAwait(false);
        }
    }

    /// <summary>Resets the scripts loaded state, forcing a reload on next operation.</summary>
    public void ResetScripts()
    {
        var version = Volatile.Read(ref _version);

        if ((version & 1) == 0 && Interlocked.CompareExchange(ref _version, version + 1, version) == version)
        {
            _loadedScripts = [];
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

    /// <summary>Returns <c>true</c> when <paramref name="e"/> is a NOSCRIPT error from Redis.</summary>
    public static bool IsNoScriptError(RedisServerException e)
    {
        return e.Message.StartsWith("NOSCRIPT", StringComparison.Ordinal);
    }

    private void _OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        logger?.LogConnectionRestored();
        ResetScripts();
    }

    private async Task<LoadedLuaScript> _GetOrLoadScriptAsync(
        RedisScriptDefinition scriptDefinition,
        CancellationToken cancellationToken
    )
    {
        if ((Volatile.Read(ref _version) & 1) == 0 && _GetLoadedScript(scriptDefinition) is { } current)
        {
            return current;
        }

        using (await _loadScriptsLock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if ((_version & 1) == 0 && _GetLoadedScript(scriptDefinition) is { } currentAfterLock)
            {
                return currentAfterLock;
            }

            logger?.LogPreparingLuaScript();

            LoadedLuaScript? loadedScript = null;
            var loadedEndpointCount = 0;

            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                var server = multiplexer.GetServer(endpoint);

                if (server.IsReplica || !server.IsConnected)
                {
                    continue;
                }

                logger?.LogLoadingLuaScripts(endpoint);

                loadedScript = await scriptDefinition.LoadAsync(server).ConfigureAwait(false);
                loadedEndpointCount++;
            }

            if (loadedEndpointCount == 0 || loadedScript is null)
            {
                throw new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect,
                    "No writable Redis endpoints were available for Lua script loading."
                );
            }

            var loadedScripts = new Dictionary<Type, LoadedLuaScript>(_loadedScripts)
            {
                [scriptDefinition.GetType()] = loadedScript,
            };

            _loadedScripts = loadedScripts;
            _MarkLoaded();
            _SubscribeConnectionRestored();

            return loadedScript;
        }
    }

    private LoadedLuaScript? _GetLoadedScript(RedisScriptDefinition scriptDefinition)
    {
        return _loadedScripts.GetValueOrDefault(scriptDefinition.GetType());
    }

    private bool _ContainsAll(IReadOnlyList<RedisScriptDefinition> scriptDefinitions)
    {
        return scriptDefinitions.All(scriptDefinition => _loadedScripts.ContainsKey(scriptDefinition.GetType()));
    }

    private void _ResetScripts(int expectedVersion)
    {
        if (
            (expectedVersion & 1) == 0
            && Interlocked.CompareExchange(ref _version, expectedVersion + 1, expectedVersion) == expectedVersion
        )
        {
            _loadedScripts = [];
        }
    }

    private void _MarkLoaded()
    {
        if ((_version & 1) != 0)
        {
            _version++;
        }
    }

    private void _SubscribeConnectionRestored()
    {
        if (!_eventsSubscribed)
        {
            multiplexer.ConnectionRestored += _OnConnectionRestored;
            _eventsSubscribed = true;
        }
    }

    private static IReadOnlyList<RedisScriptDefinition> _GetDistinctDefinitions(
        IEnumerable<RedisScriptDefinition> scriptDefinitions
    )
    {
        var definitions = new List<RedisScriptDefinition>();
        var types = new HashSet<Type>();

        foreach (var scriptDefinition in scriptDefinitions)
        {
            Argument.IsNotNull(scriptDefinition);

            if (types.Add(scriptDefinition.GetType()))
            {
                definitions.Add(scriptDefinition);
            }
        }

        return definitions;
    }
}
