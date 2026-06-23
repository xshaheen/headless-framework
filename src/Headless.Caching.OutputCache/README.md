# Headless.Caching.OutputCache

Adapter that backs ASP.NET Core's `IOutputCacheStore` with a named Headless `ICache`, making `services.AddOutputCache()` distributed and tag-aware.

## Problem Solved

ASP.NET Core's output-cache middleware ships only an in-memory store; its own guidance states that `IDistributedCache` is **not** a valid output-cache store because it lacks the atomic tag operations the middleware needs for `EvictByTagAsync`. This package fills that gap: it backs an `IOutputCacheStore` (and the optional `IOutputCacheBufferStore`) with the Headless cache engine, so output-cache entries become distributed and tag eviction rides the engine's distributed tag index — without forcing an ASP.NET dependency onto framework-agnostic `Headless.Caching.Bcl` consumers.

## Key Features

- Registers `Microsoft.AspNetCore.OutputCaching.IOutputCacheStore` over a named Headless `ICache`; the same instance also implements the optional `IOutputCacheBufferStore` that the middleware's formatter pattern-matches (only `IOutputCacheStore` is registered as a service).
- The `IOutputCacheBufferStore` members stream through the `IBufferCache` fast path: read slices the entry into the response `PipeWriter`, write frames the response-body `ReadOnlySequence<byte>` — no intermediate `byte[]` on the hot path when the backing cache is `Headless.Caching.Redis`/`Headless.Caching.InMemory`/`Headless.Caching.Hybrid`, with a transparent `byte[]` fallback otherwise. The remaining copy per side is the unavoidable network I/O; zero-copy across a distributed cache is impossible.
- `setup.UseOutputCache(...)` provisions a dedicated named cache and wires it as the output-cache store.
- `EvictByTagAsync(tag)` delegates to `ICache.RemoveByTagAsync` — O(1) logical (Family-2) tag-marker invalidation. Backed by Redis, the tag marker is a single shared Redis key every instance reads, so eviction is cluster-wide.
- `validFor` maps directly to the entry's `Duration` (a single relative TTL); a non-positive `validFor` falls back to `DefaultExpiration`.
- Tags pass straight through to the engine, persisted on the entry via `UpsertEntryAsync`; tag-count/length limits are enforced by the engine's write-time check.
- Uses `services.Replace` for `IOutputCacheStore`, so the Headless store wins regardless of whether `AddOutputCache()` runs before or after `AddHeadlessCaching(...)`.
- Stores the middleware's `byte[]` output-cache entries in the value segment unchanged rather than JSON/base64 encoded, because `byte[]` is the cache's native wire format (stored verbatim, never through a serializer); no serializer is wired.

## Design Notes

This package only provides the **store**. The consumer still calls `services.AddOutputCache()` and declares output-cache policies — vary-by, expiration strategy — and tags via `[OutputCache(Tags = "...")]` on controllers or `.CacheOutput(p => p.Tag("..."))` on minimal APIs. Policy stays ASP.NET's concern; this adapter changes only where entries live and how tag eviction propagates.

It is a separate package from `Headless.Caching.Bcl` because an `IOutputCacheStore` references the ASP.NET shared framework (`Microsoft.AspNetCore.App`). Keeping it out of the framework-agnostic BCL adapter means a non-web consumer of `IDistributedCache` never pulls an ASP.NET dependency.

`EvictByTagAsync` is **logical** eviction, not physical deletion. `RemoveByTagAsync` bumps a per-tag timestamp marker so matching entries read as misses (the marker's timestamp postdates their `CreatedAt`); they are not physically removed until their TTL lapses. This satisfies the ASP.NET output-cache contract and is cluster-safe — one marker key per tag, no key enumeration, works on Redis Cluster. With a Redis-backed store the marker lives in Redis, so a tag evicted on node A becomes a miss on node B on its next read (no L1 to invalidate, no backplane required).

`byte[]` is the cache's native wire format, stored verbatim (never through a serializer), so the middleware's `byte[]` entries land as raw bytes under any serializer — no serializer is wired, and the `configureCache` callback selects **only** the backing provider (for example `instance.UseRedis(...)`). Because `byte[]` is native, the `IBufferCache` fast path and the typed `byte[]` path are always byte-consistent, so the buffer-oriented read/write is safe here without any serializer configuration.

Distribution is a function of the backing provider the consumer composes, not the adapter. With an InMemory-only backing cache (which stores object references and never serializes) eviction is single-node only. Back the named instance with Redis (`instance.UseRedis(...)`) for distributed, cluster-wide output caching: the value blobs and the tag markers both live in Redis, shared by every instance.

When the named instance is **Hybrid** rather than Redis-only, cross-node eviction additionally rides the backplane to drop peers' L1 copies, and that publish is best-effort: with the engine default `ReThrowBackplaneExceptions = false`, a failed publish is logged but not surfaced, so a successful `EvictByTagAsync` guarantees only local-node invalidation — peers then rely on the shared L2 marker (and, worst case, the entry's TTL). A Redis-only backing — the Quick Start default — has no L1 and no backplane, so this caveat does not apply.

## Installation

```bash
dotnet add package Headless.Caching.OutputCache
```

## Quick Start

Redis-backed store — distributed and tag-aware across instances:

```csharp
var builder = WebApplication.CreateBuilder(args);
var mux = ConnectionMultiplexer.Connect("localhost:6379");

builder.Services.AddOutputCache(); // ASP.NET; still declare [OutputCache(Tags = ...)] / .CacheOutput(p => p.Tag(...))
builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options => options.ConnectionMultiplexer = mux);
    setup.UseOutputCache(
        options => options.CacheName = "output-cache",
        instance =>
            instance.UseRedis(options =>
            {
                options.ConnectionMultiplexer = mux;
                options.KeyPrefix = "output-cache:";
            })
    );
});
```

Declare tags where output caching is applied, then evict them through the standard ASP.NET API:

```csharp
app.MapGet("/products", GetProducts).CacheOutput(p => p.Tag("products"));

// elsewhere — IOutputCacheStore.EvictByTagAsync delegates to the Headless engine
await outputCacheStore.EvictByTagAsync("products", cancellationToken);
```

Because the Redis-backed store keeps both the value blobs and the tag markers in Redis, eviction is already cluster-wide: a tag evicted through any instance's `IOutputCacheStore` becomes a miss on every instance's next read of a matching entry — no backplane or extra wiring required. Back the named instance with a single remote provider (`instance.UseRedis(...)`); the application's own default cache can still be Hybrid independently.

## Configuration

| Option | Default | Description |
| --- | --- | --- |
| `CacheName` | `"output-cache"` | Named cache instance used by the store. Must be non-empty and must not be a reserved Headless cache provider key. The store uses a dedicated named cache instance; key-level isolation from the default cache is the backing provider's responsibility — give the instance a distinct `KeyPrefix` (or its own connection/database) when it shares Redis infrastructure with the default cache. |
| `DefaultExpiration` | `1 minute` | Duration applied only when ASP.NET hands the store a non-positive `validFor`; a positive `validFor` always passes through unchanged as the entry `Duration`. Must be greater than zero. |

Options are validated through the Hosting FluentValidation pipeline with startup validation. The `configureCache` callback passed to `UseOutputCache(...)` selects only the backing provider for the named cache — exactly one, usually `instance.UseRedis(...)`. `byte[]` is the cache's native wire format, so no serializer configuration is needed — configuring one on the named instance throws at registration. `UseOutputCache` is a consumer of Headless caching, so `AddHeadlessCaching` still requires a default provider (`UseInMemory`/`UseRedis`/`UseHybrid`) alongside it.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`
- `Microsoft.AspNetCore.App` (framework reference)

## Side Effects

- Adds a named cache instance through `setup.AddNamed(...)`.
- Replaces (`services.Replace`) the `IOutputCacheStore` registration with the Headless store as singleton. Only `IOutputCacheStore` is registered; the same instance also implements `IOutputCacheBufferStore`, which the formatter discovers by pattern-matching the resolved store (no separate registration).
- Registers `HeadlessOutputCacheStoreOptions` with FluentValidation and startup validation.
- Registers `TimeProvider.System` when no `TimeProvider` is already registered.
