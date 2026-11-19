---
domain: Caching
packages: Caching.Bcl, Caching.OutputCache
---

# Byte payloads and ASP.NET Core adapters

> Part of [Caching](../caching.md). The `IBufferCache` path for opaque bytes, the BCL `IDistributedCache` adapter, and the ASP.NET Core output-cache store.

## Byte payloads

`byte[]` and `string` are the cache's native wire types. The cache stores them as they are and never passes them through the configured serializer, so a `byte[]` value is never JSON or base64 encoded, under any serializer.

For opaque bytes, use `IBufferCache` instead of `GetAsync<byte[]>` and `UpsertEntryAsync<byte[]>`. Redis, InMemory, and Hybrid implement it. It reads the payload straight into a caller's `IBufferWriter<byte>` and writes from a `ReadOnlySequence<byte>`, which removes the intermediate `byte[]` copies of the generic path: a read drops from three payload copies to one, and a write from two to one. The one copy left is the network I/O itself.

```csharp
public interface IBufferCache
{
    ValueTask<bool> TryGetToAsync(string key, IBufferWriter<byte> destination, CancellationToken ct = default);
    ValueTask UpsertRawAsync(
        string key,
        ReadOnlySequence<byte> value,
        CacheEntryOptions options,
        CancellationToken ct = default
    );
}
```

- Call it through `BufferCacheExtensions.TryGetToOrFallbackAsync(key, IBufferWriter<byte>)` and `UpsertRawOrFallbackAsync(key, ReadOnlySequence<byte>, options)` on `ICache`. They use `IBufferCache` when the cache implements it and fall back to the `byte[]` path otherwise, which allocates one `byte[]`.
- `PipeWriter` is an `IBufferWriter<byte>`, so a response or body pipe is a valid destination.
- `TryGetToAsync` writes nothing on a miss and returns `false`.
- `UpsertRawAsync` reads the whole `ReadOnlySequence<byte>` before its first await, so you may pass pooled buffers that stay valid only for the call.
- Expiry, fail-safe retention, tags, sliding expiration, and birth time behave exactly as with `UpsertEntryAsync`. The raw path and the typed `byte[]` path read and write the same bytes, so you can mix them.

---

## Headless.Caching.Bcl

Adapter that exposes a named Headless cache as `Microsoft.Extensions.Caching.Distributed.IDistributedCache`, for framework components that require the BCL contract, such as ASP.NET Core Session.

### Setup

```bash
dotnet add package Headless.Caching.Bcl
```

```csharp
var builder = WebApplication.CreateBuilder(args);
var redis = ConnectionMultiplexer.Connect("localhost:6379");

builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options => options.ConnectionMultiplexer = redis);
    setup.UseBclCache(
        options =>
        {
            options.CacheName = "aspnet-session";
            options.DefaultAbsoluteExpiration = TimeSpan.FromHours(8);
        },
        instance =>
            instance.UseRedis(options =>
            {
                options.ConnectionMultiplexer = redis;
                options.KeyPrefix = "session:";
            })
    );
});

builder.Services.AddSession(options => options.IdleTimeout = TimeSpan.FromMinutes(20));
```

Inject `ICache` in code you own. Inject `IDistributedCache` only where a component demands it.

**Verify:** options validate at startup, so an empty or reserved `CacheName` or a non-positive `DefaultAbsoluteExpiration` fails the start with `OptionsValidationException`. With a Redis backing instance, the `cache-redis-{CacheName}` readiness check reports it.

### Configuration

| Option | Default | Description |
| --- | --- | --- |
| `CacheName` | `"bcl-distributed-cache"` | Named cache instance the adapter uses. Must be non-empty, not a reserved role key, and not under the `Headless.Caching:` namespace. |
| `DefaultAbsoluteExpiration` | 1 day | Absolute lifetime cap when a caller sets only a sliding expiration, or no expiration. Must be greater than zero. |

The `configureCache` callback of `UseBclCache(...)` picks exactly one backing provider for the named cache, usually `instance.UseRedis(...)`. Configuring a serializer there is rejected at registration, because the adapter stores `byte[]`.

### Runtime behavior

- Maps `DistributedCacheEntryOptions` absolute, relative, and sliding expiration onto `CacheEntryOptions`. A `Set` whose absolute expiration is already past becomes an immediate eviction, matching `Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache`, rather than throwing.
- `Refresh` and `RefreshAsync` call `ICache.RefreshAsync`, so a sliding entry re-arms without returning its value.
- The adapter also implements `IBufferDistributedCache`: its `TryGet(IBufferWriter<byte>)` and `Set(ReadOnlySequence<byte>)` members use the [byte payload path](#byte-payloads).
- The synchronous methods block on the async implementation with `GetAwaiter().GetResult()`, as Microsoft's Redis adapter does. Use the async methods in ASP.NET Core code.
- Adds a named cache through `setup.AddNamed(...)`, so entries live in their own namespace. Registers `IDistributedCache` as a singleton (`TryAdd`), validates `HeadlessDistributedCacheAdapterOptions` at startup, and registers `TimeProvider.System` when no `TimeProvider` is registered.

---

## Headless.Caching.OutputCache

Adapter that backs ASP.NET Core's `IOutputCacheStore` with a named Headless cache, making `services.AddOutputCache()` distributed and tag-aware.

### Setup

```bash
dotnet add package Headless.Caching.OutputCache
```

Do not back `AddOutputCache()` with `IDistributedCache`; ASP.NET Core rejects that store because it has no atomic tag operations. Use this package. It supplies only the store: still call `services.AddOutputCache()`, and declare policies and tags with `[OutputCache(Tags = "...")]` or `.CacheOutput(p => p.Tag("..."))`.

Redis-backed store, distributed and tag-aware across instances:

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

Declare tags where output caching applies, then evict through the standard ASP.NET Core API:

```csharp
app.MapGet("/products", GetProducts).CacheOutput(p => p.Tag("products"));

// elsewhere — IOutputCacheStore.EvictByTagAsync delegates to the Headless engine
await outputCacheStore.EvictByTagAsync("products", cancellationToken);
```

**Verify:** options validate at startup, so an empty or reserved `CacheName` or a non-positive `DefaultExpiration` fails the start with `OptionsValidationException`. With a Redis backing instance, the `cache-redis-{CacheName}` readiness check reports it. In a test, two requests to an endpoint with `.CacheOutput(...)` return the same cached body.

### Configuration

| Option | Default | Description |
| --- | --- | --- |
| `CacheName` | `"output-cache"` | Named cache instance the store uses. Must be non-empty and not a reserved role key. |
| `DefaultExpiration` | 1 minute | Duration used only when ASP.NET Core passes a non-positive `validFor`. A positive `validFor` is the entry's `Duration` as it is. Must be greater than zero. |

The `configureCache` callback of `UseOutputCache(...)` picks exactly one backing provider, usually `instance.UseRedis(...)`. A hybrid backing instance also needs the setup's `Use*Backplane` choice. Configuring a serializer there is rejected at registration. `UseOutputCache` does not replace the default provider: `AddHeadlessCaching` still needs its own `Use*`.

### Runtime behavior

- **Distribution follows the backing provider.** Backed by Redis, values and tag markers live in Redis, so `EvictByTagAsync` on any instance is a miss on every instance's next read, with no backplane. Backed by InMemory only, eviction is single-node. The application's default cache can be a hybrid independently.
- `EvictByTagAsync` calls `ICache.RemoveByTagAsync`: logical eviction, so entries stay physically present until their TTL ends. It works on Redis Cluster. See [Invalidation](invalidation.md#what-a-logical-invalidation-does).
- Tags pass through to the entry; the engine's tag limits apply.
- With tenant catalog resolution on, the resolved tenant joins every output-cache key, whichever store backs it; see [Multi-tenancy](../multi-tenancy/identifier-resolution.md#hardening-the-pre-auth-path).
- Registers the store with `services.Replace`, so it wins whether `AddOutputCache()` runs before or after `AddHeadlessCaching(...)`. Only `IOutputCacheStore` is registered; the same instance implements `IOutputCacheBufferStore`, which the output-cache formatter finds by type check and serves through the [byte payload path](#byte-payloads).
- Adds a named cache through `setup.AddNamed(...)`, validates `HeadlessOutputCacheStoreOptions` at startup, and registers `TimeProvider.System` when no `TimeProvider` is registered.
- It is a separate package from `Headless.Caching.Bcl` because it references the ASP.NET Core shared framework, which a non-web `IDistributedCache` consumer should not pull in.
