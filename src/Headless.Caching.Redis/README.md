# Headless.Caching.Redis

Redis distributed cache implementation for multi-instance applications.

## Problem Solved

Provides Redis-backed caching through the unified `ICache` abstraction, enabling cache sharing across multiple application instances.

## Key Features

- Full `IRemoteCache` implementation using StackExchange.Redis.
- Can serve as the default `ICache` (`setup.UseRedis(...)`) or as the remote tier of a default hybrid (`setup.AddRedisTier(...)`).
- Supports strongly typed `IRemoteCache<T>`.
- Named cache instances via `setup.AddNamed(name, i => i.UseRedis(...))`, each owning its own scripts loader bound to its own multiplexer.
- Prefix-based key management.
- Atomic operations (increment, compare-and-swap, SetIfHigher/Lower).
- Set/list operations with pagination.
- Lua scripts for atomic multi-key operations.
- Tag invalidation through a script-maintained reverse tag index (not supported on Redis Cluster).
- Redis Cluster support for the non-tag operations.
- Shared `GetOrAddAsync` fail-safe, factory timeout, eager refresh, conditional refresh, and background completion behavior through `Headless.Caching.Core`.

## Design Notes

Scalar write operations (`UpsertAsync`, `TryInsertAsync`, `TryReplaceAsync`, `TryReplaceIfEqualAsync`, `UpsertAllAsync`, `UpsertEntryAsync`) store entries as a versioned binary envelope: a 19-byte fixed header, optional variable sections, then the raw value segment produced by the cache value codec. Physical expiration is mapped to the Redis key TTL; when fail-safe is enabled, Redis retains the key until physical expiration even after logical expiration has passed. Sliding expiration maps the key TTL to the idle deadline and keeps physical expiration in the envelope as the absolute cap. Logical expiration rides in the payload so normal value reads can miss while `GetOrAddAsync` still has a fail-safe reserve. Atomic counters (`Increment`, `SetIfHigher`, `SetIfLower`) bypass framing and write raw Redis-native numeric strings (see below).

The envelope byte layout (version `0x02`) is:

| Offset | Field | Description |
| --- | --- | --- |
| 0 | Magic | `0xFF` — marks a framed entry |
| 1 | Version | `0x02` — current envelope version; any other version (including the retired `0x01`) reads as unframed legacy bytes, i.e. a framed-semantics miss |
| 2 | Flags | bit0 = `isNull`, bit1 = `hasLogicalExpiresAt`, bit2 = `hasPhysicalExpiresAt`, bit3 = `hasSlidingExpiration`, bit4 = `hasEagerRefreshAt`, bit5 = `hasETag`, bit6 = `hasLastModifiedAt`, bit7 = `hasTags` |
| 3–10 | LogicalExpiresAt | `Int64` little-endian Unix milliseconds; bytes always reserved, meaningful only when bit1 is set |
| 11–18 | PhysicalExpiresAt | `Int64` little-endian Unix milliseconds; bytes always reserved, meaningful only when bit2 is set |
| 19+ | Optional sections | In order, each present only when its flag is set: `SlidingExpiration` (`Int64` little-endian milliseconds, bit3), `EagerRefreshAt` (`Int64` little-endian Unix milliseconds, bit4), `LastModifiedAt` (`Int64` little-endian Unix milliseconds, bit6), `ETag` (`UInt16` little-endian byte length + UTF-8 bytes, bit5), `Tags` (`UInt16` little-endian count, then per tag a `UInt16` little-endian byte length + UTF-8 bytes, bit7) |
| rest | ValueSegment | raw codec bytes after the last present section; empty when `isNull` is set |

Note the section order is positional (sliding, eager, last-modified, etag, tags), which differs from the flag bit order. The decoder is defensive: truncated sections, out-of-range timestamps, and non-positive sliding windows all read as unframed legacy bytes rather than throwing, so corrupt or foreign data degrades to a miss.

Tagged entries maintain a reverse tag index in the reserved namespace `{KeyPrefix}__cache_tag__:{tag}`: each tag is a Redis HASH whose fields are the full prefixed cache keys carrying the tag and whose values are the entry's `PhysicalExpiresAt` Unix-millisecond stamp — the entry "version" that pins memberships. A tagged write runs one atomic Lua script that SETs the framed value, HSETs the current tags with the version, extends each tag hash TTL with greater-than semantics (the index TTL is only ever extended, so it outlives its longest-lived member and is never shortened by a short-lived write), and HDELs memberships for tags the write drops. Untagged writes with no prior tags keep the plain `SET` path — zero hot-path regression. `RemoveByTagAsync` walks the tag hash and UNLINKs an entry only when its live header (magic, version, physical stamp) matches the recorded version; a key that expired, was plain-`SET` overwritten, or was re-created without the tag carries a different stamp and has its stale membership dropped instead. Consumers must not store cache entries under keys starting with `{KeyPrefix}__cache_tag__:`. The tag scripts construct hash key names from the tag prefix, so the touched keys do not hash to one slot: tag invalidation is NOT supported on Redis Cluster (standalone/replicated deployments are unaffected).

Null scalar values are represented by a header flag with an empty value segment. The literal string `"@@NULL"` is a normal cacheable string when written through Redis cache APIs. Raw legacy keys containing `"@@NULL"` still read as null. Atomic counters remain raw Redis-native numeric strings so Redis can perform native atomic arithmetic; their read path falls back to the raw value codec.

Factory timeouts are enforced in the shared coordinator before provider writes. A soft-timeout background refresh writes through Redis on success and Redis TTL still follows physical expiration. StackExchange.Redis operation timeouts remain configured on `ConfigurationOptions.SyncTimeout` and `AsyncTimeout`; they are separate from `CacheEntryOptions.FactorySoftTimeout` and `FactoryHardTimeout`.

## Installation

```bash
dotnet add package Headless.Caching.Redis
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);
var redis = ConnectionMultiplexer.Connect("localhost:6379");

builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

builder.Services.AddHeadlessCaching(setup =>
    setup.UseRedis(options =>
    {
        options.ConnectionMultiplexer = redis;
        options.KeyPrefix = "myapp:";
    })
);
```

`UseRedis` has no parameterless shape: `ConnectionMultiplexer` is required, and the `IConfiguration`-binding shape still needs the multiplexer supplied through an additional `Configure` call.

Named instances (independent multiplexer, prefix, and scripts loader per name; the setup still needs exactly one default `Use*`):

```csharp
builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options => options.ConnectionMultiplexer = redis);
    setup.AddNamed("sessions", i => i.UseRedis(options =>
    {
        options.ConnectionMultiplexer = sessionsRedis;
        options.KeyPrefix = "sessions:";
    }));
});

public sealed class SessionService(ICacheProvider cacheProvider)
{
    private readonly ICache _cache = cacheProvider.GetCache("sessions");
}
```

Names must be non-empty and must not be reserved: the `CacheConstants` role keys (`Headless.Caching:{memory,remote,hybrid}`), their bare aliases (`memory`, `remote`, `hybrid`), and any name under the `Headless.Caching:` namespace are rejected with `ArgumentException`, and duplicate names throw. Each named instance must select exactly one provider. Named instances never touch the default (unkeyed) `ICache`.

## Configuration

| Option | Default | Description |
| --- | --- | --- |
| `ConnectionMultiplexer` | required | The StackExchange.Redis multiplexer the cache uses; the setup never creates one. |
| `KeyPrefix` | `""` | Prefix for all cache keys (and the tag-index namespace). |
| `DefaultEntryOptions` | `null` | Default `CacheEntryOptions` for the option-less `GetOrAddAsync` extension overloads; when `null` those overloads throw. |
| `ReadMode` | `CommandFlags.None` | StackExchange.Redis command flags applied to read operations (e.g. `PreferReplica`). |

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`
- `Headless.Redis`
- `Headless.Serializer.Json`
- `StackExchange.Redis`

## Side Effects

- Registers `IRemoteCache` as singleton (`setup.UseRedis(...)` and `setup.AddRedisTier(...)`).
- Registers `ICache` as singleton when used as the default provider (`setup.UseRedis(...)`).
- Registers a keyed `ICache` under the `CacheConstants.RemoteCacheProvider` role key (`Headless.Caching:Remote`).
- Registers `IRemoteCache<T>` and `ICache<T>` as singletons.
- Registers `ICacheProvider` (shared, `TryAdd`).
- Registers a keyed `HeadlessRedisScriptsLoader` bound to `RedisCacheOptions.ConnectionMultiplexer`, plus a hosted `IInitializer` that warms the cache Lua scripts on host start.
- `setup.AddNamed(name, i => i.UseRedis(...))` registers a keyed `ICache` under the instance name with a per-instance scripts loader and initializer bound to that instance's multiplexer.
- Tagged writes create/maintain Redis hashes in the reserved `{KeyPrefix}__cache_tag__:{tag}` namespace.
