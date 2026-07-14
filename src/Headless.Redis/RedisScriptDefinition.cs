// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using StackExchange.Redis;

namespace Headless.Redis;

/// <summary>
/// Named immutable Lua script descriptor used by <see cref="HeadlessRedisScriptsLoader"/>.
/// </summary>
/// <remarks>
/// Each concrete definition type must be represented by a single shared instance. The loader caches
/// scripts by concrete type and rejects multiple instances of the same type to avoid ambiguous script
/// source or parameter contracts.
/// </remarks>
[PublicAPI]
public abstract class RedisScriptDefinition
{
    private readonly LuaScript _script;

    /// <summary>Initializes a new script definition from raw Lua source.</summary>
    /// <param name="source">The Lua script body. Must not be null, empty, or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="source"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    protected RedisScriptDefinition(string source)
    {
        Argument.IsNotNullOrWhiteSpace(source);

        Name = GetType().Name;
        _script = LuaScript.Prepare(source);
    }

    /// <summary>Gets the name of this script definition, derived from the concrete type name.</summary>
    public string Name { get; }

    internal Task<LoadedLuaScript> LoadAsync(IServer server)
    {
        return _script.LoadAsync(server);
    }

    internal Task<RedisResult> EvaluateWithoutScriptCacheAsync(IDatabase db, object? parameters)
    {
        // The LuaScript overload carries the full body; NoScriptCache forces EVAL instead of
        // EVALSHA, so this call cannot raise NOSCRIPT. The loader's recovery path uses it when a
        // node is missing the cached script (e.g. a freshly promoted primary).
        return db.ScriptEvaluateAsync(_script, parameters, CommandFlags.NoScriptCache);
    }
}
