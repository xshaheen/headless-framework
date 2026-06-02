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

    // 0 = not subscribed, 1 = subscribed. Flipped via Interlocked so the subscribe path and Dispose
    // never race a non-atomic check-then-act (subscribe ran under _loadScriptsLock, Dispose does not).
    private int _eventsSubscribed;

    // Bounds a single SCRIPT LOAD round-trip so a stalled load cannot wedge _loadScriptsLock
    // indefinitely (the load runs while the lock is held and previously ignored the caller's CT).
    private static readonly TimeSpan _ScriptLoadTimeout = TimeSpan.FromSeconds(30);

    private ScriptsLoadState _state = new(1, new Dictionary<Type, LoadedRedisScript>());

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

        var state = Volatile.Read(ref _state);

        if (state.IsLoaded && _ContainsAll(definitions, state))
        {
            return;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var timestamp = _timeProvider.GetTimestamp();

            using (await _loadScriptsLock.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                state = Volatile.Read(ref _state);

                if (state.IsLoaded && _ContainsAll(definitions, state))
                {
                    return;
                }

                var missingDefinitions = _GetMissingDefinitions(definitions, state);

                if (missingDefinitions.Count is 0)
                {
                    return;
                }

                logger?.LogPreparingLuaScript();

                var loadedScripts = new Dictionary<Type, LoadedRedisScript>(state.LoadedScripts);
                var loadedEndpointCount = 0;

                foreach (var endpoint in multiplexer.GetEndPoints())
                {
                    var server = multiplexer.GetServer(endpoint);

                    if (server.IsReplica || !server.IsConnected)
                    {
                        continue;
                    }

                    logger?.LogLoadingLuaScripts(endpoint);

                    var loadTasks = missingDefinitions.Select(script => script.LoadAsync(server)).ToArray();
                    var results = await Task.WhenAll(loadTasks).WithAggregatedExceptions().ConfigureAwait(false);

                    for (var index = 0; index < missingDefinitions.Count; index++)
                    {
                        var definition = missingDefinitions[index];
                        loadedScripts[definition.GetType()] = new LoadedRedisScript(definition, results[index]);
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

                if (!_TryPublishLoadedScripts(state, loadedScripts))
                {
                    continue;
                }

                _SubscribeConnectionRestored();

                var elapsed = _timeProvider.GetElapsedTime(timestamp);
                logger?.LogScriptsLoadedSuccessfully(elapsed);

                return;
            }
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
        var versionAtStart = Volatile.Read(ref _state).Version;

        try
        {
            return await db.ScriptEvaluateAsync(script, parameters).ConfigureAwait(false);
        }
        catch (RedisServerException e) when (IsNoScriptError(e))
        {
            logger?.LogNoScriptRetry();

            // The node that served this EVALSHA is missing the script — typically a replica that
            // was promoted to primary after the initial SCRIPT LOAD, or a flushed/cold cache.
            // Recover by re-running the full body via EVAL, which cannot raise NOSCRIPT and
            // re-populates that node's script cache as a side effect. The version-guarded reset
            // (skipped if another caller already reloaded) drops the stale SHA handles so the next
            // caller reloads against the current topology and resumes the fast EVALSHA path.
            _ResetScripts(versionAtStart);

            return await scriptDefinition.EvaluateWithoutScriptCacheAsync(db, parameters).ConfigureAwait(false);
        }
    }

    /// <summary>Resets the scripts loaded state, forcing a reload on next operation.</summary>
    public void ResetScripts()
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);

            var resetVersion = state.IsLoaded ? state.Version + 1 : state.Version + 2;
            var resetState = new ScriptsLoadState(resetVersion, new Dictionary<Type, LoadedRedisScript>());

            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, resetState, state), state))
            {
                return;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Atomically claim the unsubscribe so a concurrent _SubscribeConnectionRestored cannot leave
        // a handler attached past Dispose (or unhook twice). Only the winner detaches the handler.
        if (Interlocked.Exchange(ref _eventsSubscribed, 0) == 1)
        {
            multiplexer.ConnectionRestored -= _OnConnectionRestored;
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
        var state = Volatile.Read(ref _state);

        if (state.IsLoaded && _GetLoadedScript(scriptDefinition, state) is { } current)
        {
            return current;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (await _loadScriptsLock.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                state = Volatile.Read(ref _state);

                if (state.IsLoaded && _GetLoadedScript(scriptDefinition, state) is { } currentAfterLock)
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

                    // Bound the SCRIPT LOAD: it runs while _loadScriptsLock is held and the StackExchange
                    // call does not honor a CancellationToken, so a stalled load would wedge the lock for
                    // every other caller. A TimeProvider-driven timeout CTS linked with the caller token
                    // (via .WaitAsync) lets a stuck load fail fast and release the lock instead.
                    using (var timeoutCts = _timeProvider.CreateCancellationTokenSource(_ScriptLoadTimeout))
                    using (var loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                    {
                        loadedScript = await scriptDefinition
                            .LoadAsync(server)
                            .WaitAsync(loadCts.Token)
                            .ConfigureAwait(false);
                    }

                    loadedEndpointCount++;
                }

                if (loadedEndpointCount == 0 || loadedScript is null)
                {
                    throw new RedisConnectionException(
                        ConnectionFailureType.UnableToConnect,
                        "No writable Redis endpoints were available for Lua script loading."
                    );
                }

                var loadedScripts = new Dictionary<Type, LoadedRedisScript>(state.LoadedScripts)
                {
                    [scriptDefinition.GetType()] = new LoadedRedisScript(scriptDefinition, loadedScript),
                };

                if (!_TryPublishLoadedScripts(state, loadedScripts))
                {
                    continue;
                }

                _SubscribeConnectionRestored();

                return loadedScript;
            }
        }
    }

    private static LoadedLuaScript? _GetLoadedScript(RedisScriptDefinition scriptDefinition, ScriptsLoadState state)
    {
        if (!state.LoadedScripts.TryGetValue(scriptDefinition.GetType(), out var loadedScript))
        {
            return null;
        }

        _EnsureSameDefinitionInstance(scriptDefinition, loadedScript.Definition);

        return loadedScript.Script;
    }

    private static bool _ContainsAll(IReadOnlyList<RedisScriptDefinition> scriptDefinitions, ScriptsLoadState state)
    {
        return scriptDefinitions.All(scriptDefinition => _GetLoadedScript(scriptDefinition, state) is not null);
    }

    private static IReadOnlyList<RedisScriptDefinition> _GetMissingDefinitions(
        IReadOnlyList<RedisScriptDefinition> definitions,
        ScriptsLoadState state
    )
    {
        return definitions.Where(scriptDefinition => _GetLoadedScript(scriptDefinition, state) is null).ToArray();
    }

    private void _ResetScripts(int expectedVersion)
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);

            if (state.Version != expectedVersion || !state.IsLoaded)
            {
                return;
            }

            var resetState = new ScriptsLoadState(state.Version + 1, new Dictionary<Type, LoadedRedisScript>());

            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, resetState, state), state))
            {
                return;
            }
        }
    }

    private bool _TryPublishLoadedScripts(
        ScriptsLoadState expectedState,
        Dictionary<Type, LoadedRedisScript> loadedScripts
    )
    {
        var loadedVersion = expectedState.IsLoaded ? expectedState.Version : expectedState.Version + 1;
        var loadedState = new ScriptsLoadState(loadedVersion, loadedScripts);

        return ReferenceEquals(Interlocked.CompareExchange(ref _state, loadedState, expectedState), expectedState);
    }

    private void _SubscribeConnectionRestored()
    {
        // Only the caller that flips 0 -> 1 attaches the handler, so a concurrent subscribe cannot
        // double-register and Dispose's Interlocked.Exchange observes a consistent flag.
        if (Interlocked.CompareExchange(ref _eventsSubscribed, 1, 0) == 0)
        {
            multiplexer.ConnectionRestored += _OnConnectionRestored;
        }
    }

    private static IReadOnlyList<RedisScriptDefinition> _GetDistinctDefinitions(
        IEnumerable<RedisScriptDefinition> scriptDefinitions
    )
    {
        var definitions = new List<RedisScriptDefinition>();
        var definitionsByType = new Dictionary<Type, RedisScriptDefinition>();

        foreach (var scriptDefinition in scriptDefinitions)
        {
            Argument.IsNotNull(scriptDefinition);

            var definitionType = scriptDefinition.GetType();

            if (definitionsByType.TryGetValue(definitionType, out var existingDefinition))
            {
                _EnsureSameDefinitionInstance(scriptDefinition, existingDefinition);
                continue;
            }

            definitionsByType.Add(definitionType, scriptDefinition);
            definitions.Add(scriptDefinition);
        }

        return definitions;
    }

    private static void _EnsureSameDefinitionInstance(
        RedisScriptDefinition scriptDefinition,
        RedisScriptDefinition existingDefinition
    )
    {
        if (!ReferenceEquals(scriptDefinition, existingDefinition))
        {
            throw new InvalidOperationException(
                $"Redis script definition type '{scriptDefinition.GetType().FullName}' was provided by multiple instances. "
                    + "Redis script definitions must be immutable singleton instances per concrete type."
            );
        }
    }

    private sealed record ScriptsLoadState(int Version, IReadOnlyDictionary<Type, LoadedRedisScript> LoadedScripts)
    {
        public bool IsLoaded => (Version & 1) == 0;
    }

    private sealed record LoadedRedisScript(RedisScriptDefinition Definition, LoadedLuaScript Script);
}
