# Headless.Caching.Redis

Redis distributed cache implementation for multi-instance applications.

## Problem Solved

Provides Redis-backed caching through the unified `ICache` abstraction, enabling cache sharing across multiple application instances.

## Key Features

- Full `IRemoteCache` implementation using StackExchange.Redis.
- Can serve as the default `ICache` (`setup.UseRedis(...)`) or as the remote tier of a default hybrid (`setup.AddRedisTier(...)`).
- `GetWithExpirationAsync<T>` returns the cached value and its remaining TTL in one round-trip; used internally by `Headless.Caching.Hybrid` to avoid a double L2 read.
- Supports strongly typed `ICache<T>` (the single typed facade; `IRemoteCache<T>` is not registered).
- Named cache instances via `setup.AddNamed(name, i => i.UseRedis(...))`, each owning its own scripts loader bound to its own multiplexer.
- `HeadlessCacheInstanceBuilder.WithSerializer(...)` - per-named-Redis-instance value-codec selection (instance, factory, and generic `<TSerializer>()` overloads); Redis resolves the keyed serializer by cache name and falls back to the global `ISerializer`. Serialization is a Redis-tier concern, so this lives in the Redis package; InMemory stores object references and never serializes, so it is not offered there. On a hybrid instance it governs L2 (Redis) only.
- Prefix-based key management. `FlushAsync` is a **logical** whole-cache flush (FusionCache `Clear(false)` parity): it bumps a reserved remove-generation marker so every entry reads as a hard miss with no fail-safe reserve, rather than a physical `FLUSHDB`/`SCAN`+`UNLINK`. This is cluster-safe (one marker key, not a per-node command) and never touches co-tenant keyspaces; physical memory is reclaimed lazily by each entry's TTL, so `GetCountAsync` may still count logically-removed entries until they age out. (`ClearAsync` is the reserve-preserving logical counterpart; `RemoveByPrefixAsync` still physically removes a prefix via `SCAN`+`UNLINK`.)
- Atomic operations (increment, compare-and-swap, SetIfHigher/Lower). The `SetIfHigher`/`SetIfLower` long overloads compare and compute their returned difference in Lua with IEEE-754 doubles, so results are exact only for magnitudes up to 2^53; larger long values lose precision.
- Set/list operations with pagination.
- Lua scripts for atomic multi-key operations.
- O(1) logical tag invalidation and `ClearAsync` through timestamp markers (Family-2), compared against each entry's birth time on read — one marker key per tag, so tagging works on Redis Cluster.
- Redis Cluster support for all operations, including tagging and clear.
- Implements `IBufferCache` — `TryGetToAsync` writes the decoded value slice into the caller's `IBufferWriter<byte>` and `UpsertRawAsync` splices a `ReadOnlySequence<byte>` payload into the frame buffer, both reusing the same envelope stamping so expiry/tags/sliding/`CreatedAt` match the generic path; the frame is byte-identical and the read exposes the payload as a slice of the received buffer (one copy, no intermediate `byte[]`).
- Shared `GetOrAddAsync` fail-safe, factory timeout, eager refresh, conditional refresh, and background completion behavior through `Headless.Caching.Core`.

## Design Notes

Scalar write operations (`UpsertAsync`, `TryInsertAsync`, `TryReplaceAsync`, `TryReplaceIfEqualAsync`, `UpsertAllAsync`, `UpsertEntryAsync`) store entries as a versioned binary envelope: a 27-byte fixed header, optional variable sections, then the raw value segment produced by the cache value codec. Physical expiration is mapped to the Redis key TTL; when fail-safe is enabled, Redis retains the key until physical expiration even after logical expiration has passed. Sliding expiration maps the key TTL to the idle deadline and keeps physical expiration in the envelope as the absolute cap. Logical expiration rides in the payload so normal value reads can miss while `GetOrAddAsync` still has a fail-safe reserve. `ExpireAsync` rewrites the payload's logical stamp to now while keeping the key TTL (the physical reserve) when the entry carries a genuine fail-safe reserve on a non-sliding entry; a sliding entry's surplus TTL is its absolute cap, not a reserve, so `ExpireAsync` deletes the key instead of preserving it. A raw/legacy non-framed key carries no logical metadata, so it has no reserve and is likewise deleted. Atomic counters (`Increment`, `SetIfHigher`, `SetIfLower`) bypass framing and write raw Redis-native numeric strings (see below).

The envelope byte layout (version `0x03`) is:

| Offset | Field | Description |
| --- | --- | --- |
| 0 | Magic | `0xFF` — marks a framed entry |
| 1 | Version | `0x03` — current envelope version; any other version (including the retired `0x01`/`0x02`) reads as unframed legacy bytes, i.e. a framed-semantics miss |
| 2 | Flags | bit0 = `isNull`, bit1 = `hasLogicalExpiresAt`, bit2 = `hasPhysicalExpiresAt`, bit3 = `hasSlidingExpiration`, bit4 = `hasEagerRefreshAt`, bit5 = `hasETag`, bit6 = `hasLastModifiedAt`, bit7 = `hasTags` |
| 3–10 | LogicalExpiresAt | `Int64` little-endian Unix milliseconds; bytes always reserved, meaningful only when bit1 is set |
| 11–18 | PhysicalExpiresAt | `Int64` little-endian Unix milliseconds; bytes always reserved, meaningful only when bit2 is set |
| 19–26 | CreatedAt | `Int64` little-endian Unix milliseconds — the entry's birth time, an always-present v3 fixed field (the flags byte is full at 8 bits, so presence is implied by the version rather than a flag bit). `long.MinValue` is the sentinel meaning "no birth time known"; the Family-2 read-time predicate compares this against tag/clear markers |
| 27+ | Optional sections | In order, each present only when its flag is set: `SlidingExpiration` (`Int64` little-endian milliseconds, bit3), `EagerRefreshAt` (`Int64` little-endian Unix milliseconds, bit4), `LastModifiedAt` (`Int64` little-endian Unix milliseconds, bit6), `ETag` (`UInt16` little-endian byte length + UTF-8 bytes, bit5), `Tags` (`UInt16` little-endian count, then per tag a `UInt16` little-endian byte length + UTF-8 bytes, bit7) |
| rest | ValueSegment | raw codec bytes after the last present section; empty when `isNull` is set |

Note the section order is positional (sliding, eager, last-modified, etag, tags), which differs from the flag bit order. The decoder is defensive: truncated sections, out-of-range timestamps, and non-positive sliding windows all read as unframed legacy bytes rather than throwing, so corrupt or foreign data degrades to a miss.

**Rolling-upgrade contract:** a key written by an older node (version `0x01`/`0x02` or unframed raw bytes) or by a future node carrying a version byte other than `0x03` is decoded as `Unframed` — a cache miss, not an error. The reading node re-populates the entry using the `GetOrAddAsync` factory, so mixed-version deployments self-heal without any explicit migration step. No special deployment ordering is required. A v2 entry has no `CreatedAt`, which is one reason it reads as a miss rather than being re-interpreted.

Tagging is Family-2 logical invalidation: `RemoveByTagAsync(tag)` writes one timestamp marker at `{KeyPrefix}\0__tag:{tag}` (Unix-ms) and `ClearAsync` writes the reserved clear-generation marker at `{KeyPrefix}\0__clear`. Both are O(1) `StringSet` writes that enumerate nothing, and because there is one marker key per tag (plus one clear key) rather than a multi-slot reverse index, tagging and clear work on Redis Cluster. On read, the cache resolves the newest marker applicable to an entry — the max of the clear marker and every per-tag marker the entry's frame carries — and compares it against the frame's `CreatedAt`; an older entry is a miss for direct reads and a fail-safe reserve under the factory coordinator. Tags ride in the entry frame, not a separate index; tagged writes that derive from an existing physical entry use the `CacheTaggedSetScriptDefinition` compare-and-set Lua script (verify the expected `ConcurrencyStamp`, then `SET` with TTL), while plain writes use a direct `SET`. The reserved namespace is prefixed with a NUL byte (U+0000) that ordinary cache keys never contain, so consumer keys cannot collide with the markers; do not embed a NUL byte in your own cache keys.

To avoid a Redis round-trip on every read, each instance keeps a process-local marker cache: a resolved tag/clear marker is reused for `TagMarkerRefreshWindow` (default 2s) before the next read that needs it refreshes it via a single pipelined `MGET`. The instance that issues a bump updates its own cached marker immediately, so it self-invalidates on its next read; another instance observes the bump only after its window elapses — the documented Family-2 cross-instance visibility lag — unless the marker is pushed out-of-band: a backplane-connected hybrid seeds this marker cache via `ISeedableTagMarkerCache` (which `RedisCache` implements) the instant the invalidation notification arrives, closing the gap to backplane latency. `RedisCache` implements the interface's durable `Write{Tag,Clear,Remove}MarkerAsync` writers too — backed by the **set-if-higher Lua script** (raise-only: a write never lowers a newer stored marker) — and `RemoveByTagAsync`/`ClearAsync`/`FlushAsync` route through them, so the live invalidation and a hybrid's auto-recovery replay share one raise-only write that can safely re-assert an original timestamp. So the window bounds cross-instance visibility only for no-backplane (pure-Redis, multi-instance) deployments and for recovery after a missed backplane message. The physical key TTL still backstops staleness if a marker is ever lost.

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
    setup.AddNamed(
        "sessions",
        i =>
            i.UseRedis(options =>
            {
                options.ConnectionMultiplexer = sessionsRedis;
                options.KeyPrefix = "sessions:";
            })
    );
});

public sealed class SessionService(ICacheProvider cacheProvider)
{
    private readonly ICache _cache = cacheProvider.GetCache("sessions");
}
```

Names must be non-empty and must not be reserved: the `CacheConstants` role keys (`Headless.Caching:{Memory,Remote,Hybrid}`) and any name under the `Headless.Caching:` namespace are rejected with `ArgumentException`, and duplicate names throw. Each named instance must select exactly one provider. Named instances never touch the default (unkeyed) `ICache`.

A named Redis instance can override its value serializer without affecting the default cache (`WithSerializer` and `UseRedis` chain in either order):

```csharp
builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options => options.ConnectionMultiplexer = redis);
    setup.AddNamed(
        "binary-values",
        instance =>
        {
            instance.WithSerializer<MyBinarySerializer>();
            instance.UseRedis(options => options.ConnectionMultiplexer = redis);
        }
    );
});
```

## Configuration

| Option | Default | Description |
| --- | --- | --- |
| `ConnectionMultiplexer` | required | The StackExchange.Redis multiplexer the cache uses; the setup never creates one. |
| `KeyPrefix` | `""` | Prefix for all cache keys (and the NUL-prefixed `\0__tag:` / `\0__clear` marker namespaces). |
| `DefaultEntryOptions` | `null` | Default `CacheEntryOptions` for the option-less `GetOrAddAsync` extension overloads; when `null` those overloads throw. |
| `ReadMode` | `CommandFlags.None` | StackExchange.Redis command flags applied to read operations (e.g. `PreferReplica`). |
| `TagMarkerRefreshWindow` | `2 seconds` | How long a Family-2 tag/clear marker fetched from Redis is reused from the process-local marker cache before the next read that needs it re-fetches it (pipelined `MGET`). A larger window cuts marker round-trips at the cost of a longer cross-instance visibility lag for a marker another instance bumped (the physical TTL still backstops staleness); the bumping instance self-invalidates immediately. A backplane-connected hybrid seeds the marker on the invalidation notification (via `ISeedableTagMarkerCache`), so this lag applies only to no-backplane (pure-Redis) deployments and missed-message recovery. Must be greater than zero. |

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
- Registers `ICache<T>` as singleton when used as the default provider.
- Registers `ICacheProvider` (shared, `TryAdd`).
- Registers a keyed `HeadlessRedisScriptsLoader` bound to `RedisCacheOptions.ConnectionMultiplexer`, plus a hosted `IInitializer` that warms the cache Lua scripts on host start.
- `setup.AddNamed(name, i => i.UseRedis(...))` registers a keyed `ICache` under the instance name with a per-instance scripts loader and initializer bound to that instance's multiplexer.
- Named Redis instances use the keyed `ISerializer` configured by `HeadlessCacheInstanceBuilder.WithSerializer(...)` when present; otherwise they use the global `ISerializer`.
- `RemoveByTagAsync`/`ClearAsync` write timestamp marker keys in the reserved `{KeyPrefix}\0__tag:{tag}` / `{KeyPrefix}\0__clear` namespaces (one `StringSet` per call; no key enumeration).
