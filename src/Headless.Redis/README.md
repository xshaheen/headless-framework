# Headless.Redis

Redis utilities and Lua script management for StackExchange.Redis.

## Problem Solved

Provides Redis helper extensions plus definition-first Lua script loading/execution for StackExchange.Redis. Scripts are loaded on demand by default; provider packages can warm their own script bundles through hosted initializers.

## Key Features

- `ConnectionMultiplexerExtensions` - Helper extensions for Redis connections; `CountAllKeysAsync` accepts an optional trailing `CancellationToken`, checks it before endpoint discovery and between endpoint queries, and cannot interrupt an in-flight `DBSIZE` because StackExchange.Redis exposes no cancellation for that command
- `RedisScriptDefinition` - Base type for named Lua script definitions
- `HeadlessRedisScriptsLoader` - Generic Lua script loader and evaluator
- Repository integration tests retain an internal destructive `FlushAllAsync` helper; it is not part of the package's supported public API

## Design Notes

`Headless.Redis` owns only the generic loader and the `RedisScriptDefinition` base type — it ships no concrete script definitions. Each provider package owns its own script definitions, script grouping, hosted warmup, typed parameters, and result decoding so consumers load only the script definitions they need. Scripts live in the package that uses them (for example, the cache CAS and counter scripts in `Headless.Caching.Redis`, the lock/semaphore and compare-and-swap scripts in `Headless.DistributedLocks.Redis`, and the membership scripts in `Headless.Coordination.Redis`).

Each concrete `RedisScriptDefinition` type is a singleton contract. Reuse the exposed `Instance` member; the loader rejects multiple instances of the same concrete type because it caches loaded scripts by definition type.

## Installation

```bash
dotnet add package Headless.Redis
```

## Quick Start

Define a script as a singleton `RedisScriptDefinition`, then load it through the loader. Each concrete type is a singleton contract — expose and reuse a single `Instance`.

```csharp
internal sealed class IncrementScriptDefinition : RedisScriptDefinition
{
    public static IncrementScriptDefinition Instance { get; } = new();

    private IncrementScriptDefinition()
        : base("return redis.call('incrby', @key, @by)") { }
}

var redis = await ConnectionMultiplexer.ConnectAsync("localhost");
var scriptsLoader = new HeadlessRedisScriptsLoader(redis);

await scriptsLoader.LoadAsync([IncrementScriptDefinition.Instance]);
```

### Script Execution

```csharp
var db = redis.GetDatabase();
var result = await scriptsLoader.EvaluateAsync(
    db,
    IncrementScriptDefinition.Instance,
    new { key = (RedisKey)"counter", by = 1 }
);
```

## Configuration

No configuration required.

## Dependencies

- `StackExchange.Redis`

## Side Effects

None.
