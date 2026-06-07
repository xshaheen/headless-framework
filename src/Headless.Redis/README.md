# Headless.Redis

Redis utilities and Lua script management for StackExchange.Redis.

## Problem Solved

Provides Redis helper extensions plus definition-first Lua script loading/execution for StackExchange.Redis. Scripts are loaded on demand by default; provider packages can warm their own script bundles through hosted initializers.

## Key Features

- `ConnectionMultiplexerExtensions` - Helper extensions for Redis connections
- `RedisScriptDefinition` - Base type for named Lua script definitions
- `HeadlessRedisScriptsLoader` - Generic Lua script loader and evaluator

## Design Notes

`Headless.Redis` owns the generic loader plus a small set of broadly shared CAS script definitions (`RemoveIfEqualScriptDefinition`, `ReplaceIfEqualScriptDefinition`). Each provider package owns its own script definitions, script grouping, hosted warmup, typed parameters, and result decoding so consumers load only the script definitions they need. Scripts used by a single provider live in that provider's package (for example, the cache CAS and counter scripts live in `Headless.Caching.Redis`, the lock/semaphore scripts in `Headless.DistributedLocks.Redis`, and the membership scripts in `Headless.Coordination.Redis`).

Each concrete `RedisScriptDefinition` type is a singleton contract. Reuse the exposed `Instance` member; the loader rejects multiple instances of the same concrete type because it caches loaded scripts by definition type.

## Installation

```bash
dotnet add package Headless.Redis
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

var redis = await ConnectionMultiplexer.ConnectAsync("localhost");
var scriptsLoader = new HeadlessRedisScriptsLoader(redis);

await scriptsLoader.LoadAsync([ReplaceIfEqualScriptDefinition.Instance]);
```

## Usage

### Script Execution

```csharp
var db = redis.GetDatabase();
var result = await scriptsLoader.EvaluateAsync(
    db,
    ReplaceIfEqualScriptDefinition.Instance,
    new ReplaceIfEqualParams(
        key: (RedisKey)"counter",
        value: "2",
        expected: "1",
        expires: 60_000
    )
);
```

## Configuration

No configuration required.

## Dependencies

- `StackExchange.Redis`

## Side Effects

None.
