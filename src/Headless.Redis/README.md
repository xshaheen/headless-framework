# Headless.Redis

Redis utilities and Lua script management for StackExchange.Redis.

## Problem Solved

Provides Redis helper extensions plus definition-first Lua script loading/execution for StackExchange.Redis. Scripts are loaded on demand by default, with optional feature bundles for warmup.

## Key Features

- `ConnectionMultiplexerExtensions` - Helper extensions for Redis connections
- `RedisScriptDefinition` - Base type for named Lua script definitions
- `HeadlessRedisScriptsLoader` - Generic Lua script loader and evaluator
- `RedisCacheScripts` - Cache script bundle for optional preload
- `RedisDistributedLockScripts` - Distributed lock script bundles for optional preload

## Design Notes

`Headless.Redis` owns script definitions and generic loading only. Provider packages own typed parameters and result decoding so consumers load only the script definitions they need.

## Installation

```bash
dotnet add package Headless.Redis
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

var redis = await ConnectionMultiplexer.ConnectAsync("localhost");
var scriptsLoader = new HeadlessRedisScriptsLoader(redis);

// Optional warmup for a feature bundle.
await scriptsLoader.LoadAsync(RedisCacheScripts.Definitions);
```

## Usage

### Script Execution

```csharp
var db = redis.GetDatabase();
var result = await scriptsLoader.EvaluateAsync(
    db,
    IncrementWithExpireScriptDefinition.Instance,
    new
    {
        key = (RedisKey)"counter",
        value = (RedisValue)1,
        expires = 60_000,
    }
);
```

## Configuration

No configuration required.

## Dependencies

- `StackExchange.Redis`

## Side Effects

None.
