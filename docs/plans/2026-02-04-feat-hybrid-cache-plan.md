# feat: Add Hybrid Cache with Cross-Instance Invalidation

## Overview

Add a hybrid (two-tier) caching implementation that combines fast in-memory L1 cache with distributed L2 cache (Redis), featuring automatic cross-instance cache invalidation via messaging. Inspired by Foundatio's HybridCacheClient and Microsoft's HybridCache API patterns.

**Key Value Proposition**: Reduce latency by serving from in-memory cache while maintaining consistency across scaled-out instances through pub/sub invalidation.

## Problem Statement / Motivation

Current caching in the framework requires consumers to choose between:
- **In-memory cache** (`IInMemoryCache`): Fast but per-instance, no sharing across servers
- **Distributed cache** (`IDistributedCache`): Shared but slower due to network round-trips

In scaled-out deployments, consumers face a dilemma:
1. Use distributed cache for all reads → higher latency
2. Use in-memory cache → stale data across instances
3. Implement their own hybrid solution → boilerplate, error-prone

**The hybrid cache solves this** by:
- Reading from L1 (in-memory) first for speed
- Falling back to L2 (distributed) on L1 miss
- Broadcasting invalidation messages to all instances when data changes
- Providing stampede protection out-of-the-box

## Proposed Solution

### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         HybridCache                              │
│  ┌─────────────┐    ┌─────────────┐    ┌──────────────────────┐ │
│  │ L1 Cache    │    │ L2 Cache    │    │ Message Bus          │ │
│  │ (InMemory)  │    │ (Redis)     │    │ (Pub/Sub)            │ │
│  │             │    │             │    │                      │ │
│  │ - Fast      │    │ - Shared    │    │ - Invalidation       │ │
│  │ - Per-inst. │    │ - Durable   │    │ - Cross-instance     │ │
│  └─────────────┘    └─────────────┘    └──────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘

Read Path:
  Request → L1 Hit? → Return (sync fast path, no allocation)
              ↓ Miss
            L2 Hit? → Populate L1 → Return
              ↓ Miss
            Lock → Double-check L1+L2 → Factory() → Populate L2 → Populate L1 → Return

Write/Invalidate Path:
  Publish InvalidateCache message (fire first to reduce race window)
              ↓ concurrent
  Remove from L1 → Remove from L2
              ↓
  Other instances receive → Remove from their L1
```

### Package Structure

Following the framework's abstraction + provider pattern:

| Package | Purpose |
|---------|---------|
| `Headless.Caching.Hybrid` | Core implementation, depends on abstractions + messaging |

**Dependencies**:
- `Headless.Caching.Abstractions` (ICache, IInMemoryCache, IDistributedCache)
- `Headless.Messaging.Abstractions` (IMessageBus for pub/sub)
- `Headless.Base` (KeyedAsyncLock, TimeProvider)

### Key Design Decisions

> **Note (dev branch)**: The `ICache` interface already includes `GetOrAddAsync` as an interface method (not extension).
> Both `InMemoryCache` and `RedisCache` already have built-in stampede protection using instance-based `KeyedAsyncLock`.
> The HybridCache will follow the same pattern.

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Interface | Implement `ICache` directly | Consumers use familiar API; drop-in replacement |
| L1 Cache | Use `IInMemoryCache` | Reuse existing tested implementation |
| L2 Cache | Use `IDistributedCache` | Reuse existing Redis implementation |
| Invalidation | `IMessageBus` pub/sub | Already have infrastructure; no Redis Pub/Sub coupling |
| Stampede Protection | `KeyedAsyncLock` (instance-based) | Already exists in Headless.Base; ref counted, instance-based design |
| Serialization | Delegated to L2 cache | L2 already handles serialization |
| Tag Invalidation | Deferred to v2 | Adds complexity; focus on core first |
| Disposal | `IAsyncDisposable` | Proper cleanup of subscriptions and resources |

## Technical Approach

### Core Types

```csharp
// Headless.Caching.Hybrid/HybridCacheOptions.cs
public sealed class HybridCacheOptions : CacheOptions
{
    /// <summary>Default L1 TTL. If null, uses L2 expiration.</summary>
    public TimeSpan? DefaultLocalExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Topic name for invalidation messages.</summary>
    public string InvalidationTopic { get; set; } = "cache:invalidate";

    /// <summary>Unique identifier for this cache instance (auto-generated if null).</summary>
    public string? InstanceId { get; set; }
}

// Headless.Caching.Hybrid/CacheInvalidationMessage.cs
public sealed record CacheInvalidationMessage
{
    public required string InstanceId { get; init; }
    public string? Key { get; init; }
    public string? Prefix { get; init; }
    public bool FlushAll { get; init; }
}
```

### Read Flow (GetOrAddAsync) - With Fast Path & Complete Double-Check

```csharp
/// <summary>
/// Gets a value from cache or creates it using the factory.
/// Uses sync fast path for L1 hits to avoid state machine allocation.
/// </summary>
public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
    string key,
    Func<CancellationToken, ValueTask<T?>> factory,
    TimeSpan expiration,
    CancellationToken ct = default)
{
    Argument.IsNotNullOrEmpty(key);
    Argument.IsNotNull(factory);
    ct.ThrowIfCancellationRequested();

    // FAST PATH: Sync L1 check - no state machine allocation on L1 hit
    // Note: This requires IInMemoryCache to expose a sync TryGet or we check HasValue
    var l1Result = _l1Cache.TryGetValue<T>(key, out var cachedValue);
    if (l1Result)
    {
        return new ValueTask<CacheValue<T>>(new CacheValue<T>(cachedValue, hasValue: true));
    }

    // SLOW PATH: Async L2 check + factory
    return _GetOrAddSlowPathAsync(key, factory, expiration, ct);
}

private async ValueTask<CacheValue<T>> _GetOrAddSlowPathAsync<T>(
    string key,
    Func<CancellationToken, ValueTask<T?>> factory,
    TimeSpan expiration,
    CancellationToken ct)
{
    // 1. Check L2
    var l2Result = await _l2Cache.GetAsync<T>(key, ct).AnyContext();
    if (l2Result.HasValue)
    {
        var localExpiration = _options.DefaultLocalExpiration ?? expiration;
        await _l1Cache.UpsertAsync(key, l2Result.Value, localExpiration, ct).AnyContext();
        return l2Result;
    }

    // 2. Stampede protection + factory (instance-based KeyedAsyncLock)
    using (await _keyedLock.LockAsync(key, ct).AnyContext())
    {
        // CRITICAL: Double-check BOTH L1 AND L2 after acquiring lock
        // Another process-local thread may have populated L1
        var l1Recheck = await _l1Cache.GetAsync<T>(key, ct).AnyContext();
        if (l1Recheck.HasValue)
        {
            return l1Recheck;
        }

        // Another instance may have populated L2 while we waited for the lock
        var l2Recheck = await _l2Cache.GetAsync<T>(key, ct).AnyContext();
        if (l2Recheck.HasValue)
        {
            var localExpiration = _options.DefaultLocalExpiration ?? expiration;
            await _l1Cache.UpsertAsync(key, l2Recheck.Value, localExpiration, ct).AnyContext();
            return l2Recheck;
        }

        // 3. Execute factory
        var value = await factory(ct).AnyContext();

        // 4. Populate caches with defined exception handling
        var localExp = _options.DefaultLocalExpiration ?? expiration;

        try
        {
            await _l2Cache.UpsertAsync(key, value, expiration, ct).AnyContext();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // L2 failure is non-fatal: log and continue to populate L1
            // Value will be missing from L2 but available locally
            _logger.LogWarning(ex, "Failed to write to L2 cache for key {Key}, L1 will still be populated", key);
        }

        await _l1Cache.UpsertAsync(key, value, localExp, ct).AnyContext();

        return new CacheValue<T>(value, hasValue: true);
    }
}
```

### Invalidation Flow - Race Condition Fixed

```csharp
public async ValueTask<bool> RemoveAsync(string key, CancellationToken ct = default)
{
    Argument.IsNotNullOrEmpty(key);
    ct.ThrowIfCancellationRequested();

    // CRITICAL: Publish invalidation FIRST (or concurrently) to minimize race window
    // This prevents other instances from re-caching stale data during L2 removal
    var publishTask = _PublishInvalidationAsync(new CacheInvalidationMessage
    {
        InstanceId = _instanceId,
        Key = key
    }, ct);

    // Remove from L1 (immediate, this instance)
    await _l1Cache.RemoveAsync(key, ct).AnyContext();

    // Remove from L2 (shared)
    var removed = await _l2Cache.RemoveAsync(key, ct).AnyContext();

    // Ensure publish completed (fire-and-forget with await to catch exceptions)
    await publishTask.AnyContext();

    return removed;
}

/// <summary>
/// Publishes invalidation message with defined exception handling.
/// Publish failures are logged but don't fail the operation.
/// </summary>
private async ValueTask _PublishInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct)
{
    try
    {
        await _messageBus.PublishAsync(message, ct).AnyContext();
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        // Publish failure is non-fatal: other instances may have stale L1 data
        // until their TTL expires. This is acceptable for eventual consistency.
        _logger.LogWarning(
            ex,
            "Failed to publish cache invalidation for {Key}/{Prefix}, other instances may serve stale data until TTL expires",
            message.Key,
            message.Prefix);
    }
}
```

### HybridCache Class with IAsyncDisposable

```csharp
public sealed class HybridCache : ICache, IAsyncDisposable
{
    private readonly IInMemoryCache _l1Cache;
    private readonly IDistributedCache _l2Cache;
    private readonly IMessageBus _messageBus;
    private readonly HybridCacheOptions _options;
    private readonly ILogger<HybridCache> _logger;
    private readonly string _instanceId;
    private readonly KeyedAsyncLock _keyedLock = new();  // Instance-based, not static
    private readonly CancellationTokenSource _disposalCts = new();

    private IDisposable? _subscription;
    private volatile bool _disposed;

    public HybridCache(
        IInMemoryCache l1Cache,
        IDistributedCache l2Cache,
        IMessageBus messageBus,
        IOptions<HybridCacheOptions> options,
        ILogger<HybridCache> logger)
    {
        _l1Cache = l1Cache;
        _l2Cache = l2Cache;
        _messageBus = messageBus;
        _options = options.Value;
        _logger = logger;
        _instanceId = _options.InstanceId ?? Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Starts listening for invalidation messages. Called by hosted service.
    /// </summary>
    internal async Task StartAsync(CancellationToken ct)
    {
        await _messageBus.SubscribeAsync<CacheInvalidationMessage>(
            _OnInvalidationMessageAsync,
            ct).AnyContext();
    }

    private async Task _OnInvalidationMessageAsync(
        IMessageSubscribeMedium<CacheInvalidationMessage> medium,
        CancellationToken ct)
    {
        var msg = medium.Payload;

        // Skip self-originated messages
        if (msg.InstanceId == _instanceId)
        {
            return;
        }

        try
        {
            if (msg.FlushAll)
            {
                await _l1Cache.FlushAsync(ct).AnyContext();
            }
            else if (!string.IsNullOrEmpty(msg.Prefix))
            {
                await _l1Cache.RemoveByPrefixAsync(msg.Prefix, ct).AnyContext();
            }
            else if (!string.IsNullOrEmpty(msg.Key))
            {
                await _l1Cache.RemoveAsync(msg.Key, ct).AnyContext();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during shutdown, don't log as error
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process cache invalidation message: {Message}", msg);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Signal disposal to any pending operations
        await _disposalCts.CancelAsync();

        // Subscription cleanup handled by message bus disposal
        _subscription?.Dispose();

        _keyedLock.Dispose();
        _disposalCts.Dispose();
    }
}
```

### HybridCacheInvalidationSubscriber Hosted Service

```csharp
/// <summary>
/// Hosted service that manages the lifecycle of HybridCache message subscription.
/// Properly handles cancellation during shutdown.
/// </summary>
public sealed class HybridCacheInvalidationSubscriber(HybridCache cache) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await cache.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Disposal handled by DI container calling DisposeAsync on HybridCache
        // The cancellationToken here signals graceful shutdown timeout
        return Task.CompletedTask;
    }
}
```

### DI Registration

```csharp
// Headless.Caching.Hybrid/Setup.cs
public static class HybridCacheSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddHybridCache(
            Action<HybridCacheOptions>? setupAction = null,
            bool isDefault = true)
        {
            services.Configure<HybridCacheOptions>(setupAction ?? (_ => { }));
            services.AddSingletonOptionValue<HybridCacheOptions>();

            // Register as singleton with IAsyncDisposable support
            services.TryAddSingleton<HybridCache>();

            if (isDefault)
            {
                services.TryAddSingleton<ICache>(sp => sp.GetRequiredService<HybridCache>());
            }

            // Register hosted service for subscription lifecycle
            services.AddSingleton<IHostedService, HybridCacheInvalidationSubscriber>();

            return services;
        }
    }
}
```

## Exception Handling Contract

| Scenario | Behavior | Rationale |
|----------|----------|-----------|
| L2 write fails in `GetOrAddAsync` | Log warning, continue to populate L1 | Value available locally; eventual consistency |
| Publish fails in `RemoveAsync` | Log warning, continue | Other instances serve stale until TTL; acceptable |
| L1 write fails | Propagate exception | L1 is in-process, failures indicate serious issues |
| L2 read fails in `GetAsync` | Propagate exception | Caller should know data unavailable |
| Subscription handler fails | Log error, don't crash | Individual message failures shouldn't stop processing |
| `OperationCanceledException` | Always propagate | Respect cancellation requests |

## Stories

| # | Story | Size | Notes |
|---|-------|------|-------|
| 1 | Create `Headless.Caching.Hybrid` project with proper structure | S | csproj, README, namespace |
| 2 | Define `HybridCacheOptions`, `CacheInvalidationMessage` | S | Core types |
| 3 | Implement `HybridCache` read path with fast path + complete double-check | M | L1→L2→factory flow, sync fast path |
| 4 | Implement `HybridCache` write path with exception handling | M | Dual-write, L2 failure tolerance |
| 5 | Implement `HybridCache` remove operations with race-safe invalidation | M | Publish-first pattern |
| 6 | Implement `HybridCacheInvalidationSubscriber` with proper cancellation | S | Message subscription, StopAsync handling |
| 7 | Implement `IAsyncDisposable` on `HybridCache` | S | Cleanup subscriptions, CTS |
| 8 | Add DI registration in `Setup.cs` | S | Extension methods |
| 9 | Add options validation with FluentValidation | XS | Validator class |
| 10 | Write unit tests for HybridCache read path (including fast path) | M | Mock L1/L2/bus |
| 11 | Write unit tests for invalidation flow + exception scenarios | M | Verify broadcast logic, failure handling |
| 12 | Write integration tests with Redis + in-memory message bus | L | Testcontainers |
| 13 | Add README documentation | S | Usage examples |

## Acceptance Criteria

### Functional Requirements

- [ ] [M] `GetOrAddAsync` uses sync fast path for L1 hits (no state machine allocation)
- [ ] [M] `GetOrAddAsync` double-checks BOTH L1 AND L2 after acquiring lock
- [ ] [M] Cache miss populates both L1 and L2
- [ ] [M] L2 write failure logs warning and still populates L1
- [ ] [M] `RemoveAsync` publishes invalidation BEFORE/concurrent with L2 removal
- [ ] [M] Publish failure logs warning but doesn't fail the remove operation
- [ ] [M] Instances receiving invalidation message remove from their L1 only
- [ ] [S] Self-originated invalidation messages are ignored (no echo)
- [ ] [S] `RemoveByPrefixAsync` broadcasts prefix-based invalidation
- [ ] [S] `FlushAsync` broadcasts flush-all message
- [ ] [S] Stampede protection prevents concurrent factory calls for same key

### Non-Functional Requirements

- [ ] [S] L1 cache hit latency < 1ms (no network, sync fast path)
- [ ] [S] Invalidation propagation < 100ms in normal conditions
- [ ] [XS] Thread-safe for concurrent access
- [ ] [XS] Graceful degradation if message bus unavailable
- [ ] [S] `IAsyncDisposable` properly cleans up subscriptions
- [ ] [S] `HybridCacheInvalidationSubscriber.StopAsync` respects cancellation

### Quality Gates

- [ ] [S] Unit test coverage ≥85% line, ≥80% branch
- [ ] [S] Integration tests pass with Testcontainers
- [ ] [XS] XML documentation on all public APIs
- [ ] [XS] README with usage examples

## Success Metrics

1. **Latency Reduction**: L1 cache hits should show 10-100x improvement over direct Redis access
2. **Consistency**: Stale reads after invalidation should be < 1% in normal operation
3. **Adoption**: Drop-in replacement for existing `ICache` usage

## Dependencies & Prerequisites

| Dependency | Status | Notes |
|------------|--------|-------|
| `Headless.Caching.Abstractions` | ✅ Exists | ICache (with GetOrAddAsync), IInMemoryCache, IDistributedCache |
| `Headless.Messaging.Abstractions` | ✅ Exists | IMessageBus, IMessagePublisher |
| `Headless.Base` | ✅ Exists | KeyedAsyncLock (instance-based, ref counting, IDisposable) |
| In-memory cache provider | ✅ Exists | Headless.Caching.Memory (has built-in stampede protection) |
| Distributed cache provider | ✅ Exists | Headless.Caching.Redis (has built-in stampede protection) |
| Message bus provider | ✅ Exists | Headless.Messaging.* (Redis-backed) |

## Risk Analysis & Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Message bus unavailable | Medium | Low | Graceful degradation: log warning, L1 invalidation still works locally |
| Message ordering/loss | Low | Low | Eventual consistency acceptable; TTL provides safety net |
| Memory pressure from L1 | Medium | Medium | Respect existing InMemoryCache MaxItems; configurable local TTL |
| Circular invalidation | High | Low | InstanceId check prevents self-invalidation loops |
| Race between invalidation publish and L2 removal | Medium | Medium | **Mitigated**: Publish-first pattern reduces window |
| L2 failure during write | Low | Low | **Mitigated**: Log and continue, L1 still populated |

## Future Considerations (v2)

1. **Tag-based invalidation**: Add `tags` parameter to write methods, `RemoveByTagAsync`
2. **Per-entry L1 TTL**: Allow `HybridCacheEntryOptions` per call (like Microsoft's HybridCache)
3. **Metrics/telemetry**: L1 hit ratio, L2 hit ratio, invalidation count
4. **IBufferDistributedCache support**: For improved allocation performance
5. **Compression**: Optional compression for large values

## References & Research

### Internal References

- Existing cache abstractions: `src/Headless.Caching.Abstractions/ICache.cs` (includes GetOrAddAsync)
- In-memory implementation: `src/Headless.Caching.Memory/InMemoryCache.cs` (has stampede protection)
- Redis implementation: `src/Headless.Caching.Redis/RedisCache.cs` (has stampede protection)
- Message bus: `src/Headless.Messaging.Abstractions/IMessageBus.cs`
- Keyed locking: `src/Headless.Base/Threading/KeyedAsyncLock.cs` (instance-based, IDisposable)
- DI pattern example: `src/Headless.Caching.Memory/Setup.cs`

### External References

- [Microsoft HybridCache Documentation](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid)
- [Microsoft HybridCache API Reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.hybrid.hybridcache)
- [Foundatio HybridCacheClient](https://github.com/FoundatioFx/Foundatio) - Inspiration for cross-instance invalidation via messaging
- [.NET Blog: HybridCache GA Announcement](https://devblogs.microsoft.com/dotnet/hybrid-cache-is-now-ga/)

### Design Inspirations

| Feature | Source | Adaptation |
|---------|--------|------------|
| L1→L2 read flow | Microsoft HybridCache | Direct port |
| Sync fast path for L1 hits | Stephen Toub patterns | Avoid state machine allocation |
| Message-based invalidation | Foundatio HybridCacheClient | Uses existing IMessageBus instead of separate bus |
| Stampede protection | Both | Uses existing KeyedAsyncLock (instance-based) |
| InstanceId for self-skip | Foundatio | Direct port |
| Publish-first invalidation | Code review feedback | Reduces race window |

## Resolved Questions

| Question | Decision | Rationale |
|----------|----------|-----------|
| L2 writes fire-and-forget? | **No, await** | Simpler, more predictable; failures logged |
| L1-only mode? | **No** | Use `IInMemoryCache` directly if L1-only needed |
| IDirectPublisher vs IOutboxPublisher? | **Fire-and-forget via IMessageBus** | Invalidation is best-effort; TTL is safety net |
| API naming alignment? | **Keep `GetOrAddAsync`** | Matches existing `ICache`; no breaking change |
