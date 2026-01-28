# Headless.Redis

Redis utilities and Lua script management for StackExchange.Redis.

## Problem Solved

Provides Redis helper extensions and centralized Lua script loading/execution for StackExchange.Redis, eliminating boilerplate and ensuring consistent script management.

## Key Features

- `ConnectionMultiplexerExtensions` - Helper extensions for Redis connections
- `HeadlessRedisScriptsLoader` - Centralized Lua script management
- `RedisScripts` - Pre-defined script references
- Script caching and execution helpers

## Installation

```bash
dotnet add package Headless.Redis
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

var redis = await ConnectionMultiplexer.ConnectAsync("localhost");
var scriptsLoader = new HeadlessRedisScriptsLoader(redis);

// Load scripts on startup
await scriptsLoader.LoadAsync();
```

## Usage

### Script Execution

```csharp
var db = redis.GetDatabase();
var result = await db.ScriptEvaluateAsync(
    RedisScripts.YourScript,
    keys: [key],
    values: [value]
);
```

## Configuration

No configuration required.

## Dependencies

- `StackExchange.Redis`

## Side Effects

None.
