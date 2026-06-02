// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using StackExchange.Redis;

namespace Headless.Redis;

/// <summary>Named Lua script descriptor used by <see cref="HeadlessRedisScriptsLoader"/>.</summary>
public abstract class RedisScriptDefinition
{
    private readonly LuaScript _script;

    protected RedisScriptDefinition(string source)
    {
        Argument.IsNotNullOrWhiteSpace(source);

        Name = GetType().Name;
        _script = LuaScript.Prepare(source);
    }

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
