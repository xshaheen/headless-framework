# Plan: In-Memory Resource Locks Implementation

## Enhancement Summary

**Deepened on:** 2025-01-10
**Research agents used:** best-practices-researcher, framework-docs-researcher, git-history-analyzer, strict-dotnet-reviewer (Stephen Toub), pragmatic-dotnet-reviewer (Scott Hanselman), architecture-strategist, performance-oracle, pattern-recognition-specialist, code-simplicity-reviewer

### Key Insights Discovered

1. **CRITICAL: Architectural mismatch** - AsyncKeyedLock provides local async coordination, but `IResourceLockStorage` needs atomic state storage operations. These are fundamentally different abstractions.

2. **Existing solution exists** - `CacheResourceLockStorage` + `InMemoryCache` (Foundatio) already provides full in-memory lock functionality with zero new packages.

3. **Pattern violation** - Managing internal state (ConcurrentDictionary + Timer) breaks the established "thin adapter" pattern used by Cache/Redis implementations.

---

## Problem Statement

Add in-memory implementation of `Framework.ResourceLocks.Abstractions` for:
- Single-instance deployments
- Development/testing scenarios
- Scenarios where Redis/distributed cache is not needed

## Options Analysis

### Option A: Use Existing Infrastructure (RECOMMENDED)

**Approach:** Document and simplify existing pattern - no new code needed.

```csharp
// Already works today:
services.AddInMemoryCache();  // From Framework.Caching.Foundatio.Memory
services.AddResourceLock<CacheResourceLockStorage>();
```

**Pros:**
- Zero new packages to maintain
- Follows established "thin adapter" pattern
- Battle-tested Foundatio InMemoryCache handles TTL, atomicity, thread-safety
- No new dependencies

**Cons:**
- Requires Foundatio caching dependency

**Research Insights:**
- `CacheResourceLockStorage` is only 32 lines - pure delegation
- Foundatio's `InMemoryCacheClient` already implements `TryInsertAsync`, `TryReplaceIfEqualAsync`, `RemoveIfEqualAsync` with proper atomicity
- No performance benefit from AsyncKeyedLock over existing solution

### Option B: Add DI Extension to Existing Package (SIMPLE ENHANCEMENT)

**Approach:** Add convenience method to `Framework.ResourceLocks.Cache`:

```csharp
// In Framework.ResourceLocks.Cache/AddInMemoryResourceLockExtensions.cs
public static class AddInMemoryResourceLockExtensions
{
    public static IServiceCollection AddInMemoryResourceLock(
        this IServiceCollection services,
        Action<ResourceLockOptions, IServiceProvider>? optionSetupAction = null)
    {
        services.AddInMemoryCache();
        return services.AddResourceLock<CacheResourceLockStorage>(optionSetupAction);
    }
}
```

**Pros:**
- One-liner setup for consumers
- ~10 lines of new code in existing package
- No new packages to version/maintain

**Cons:**
- Couples `Framework.ResourceLocks.Cache` to Foundatio Memory package

### Option C: Create Framework.ResourceLocks.InMemory (ORIGINAL PROPOSAL - NOT RECOMMENDED)

**Approach:** New package with AsyncKeyedLock + ConcurrentDictionary

**Critical Issues Identified by Review:**

| Issue | Severity | Description |
|-------|----------|-------------|
| Wrong abstraction | CRITICAL | AsyncKeyedLock is for coordination, not state storage |
| Pattern violation | HIGH | Breaks thin adapter pattern (32-line implementations become 200+) |
| TTL complexity | HIGH | Must implement timer-based cleanup (Redis/Cache do this for free) |
| Memory management | HIGH | Unbounded growth without proper cleanup |
| Dual data structures | MEDIUM | AsyncKeyedLocker + ConcurrentDictionary = double overhead |
| Testing complexity | MEDIUM | Need TimeProvider injection for TTL testing |

**If Proceeding Despite Concerns (Implementation Guidance from Stephen Toub Review):**

```csharp
public sealed class InMemoryResourceLockStorage : IResourceLockStorage, IDisposable
{
    private readonly record struct LockEntry(string LockId, long ExpirationTicks);

    private readonly ConcurrentDictionary<string, LockEntry> _locks = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly Timer _cleanupTimer;
    private volatile bool _disposed;

    public InMemoryResourceLockStorage(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _cleanupTimer = new Timer(_CleanupExpiredLocks, null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public Task<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null)
    {
        var expiration = _CalculateExpiration(ttl);
        var entry = new LockEntry(lockId, expiration);

        // Atomic insert-if-not-exists
        var added = _locks.TryAdd(key, entry);

        // If key exists but expired, try to replace
        if (!added && _locks.TryGetValue(key, out var existing) && _IsExpired(existing))
        {
            added = _locks.TryUpdate(key, entry, existing);
        }

        return Task.FromResult(added);
    }

    public Task<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null)
    {
        var expiration = _CalculateExpiration(newTtl);
        var newEntry = new LockEntry(newId, expiration);

        while (_locks.TryGetValue(key, out var current))
        {
            if (current.LockId != expectedId || _IsExpired(current))
                return Task.FromResult(false);

            if (_locks.TryUpdate(key, newEntry, current))
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<bool> RemoveIfEqualAsync(string key, string expectedId)
    {
        while (_locks.TryGetValue(key, out var current))
        {
            if (current.LockId != expectedId)
                return Task.FromResult(false);

            // .NET 5+ TryRemove with value comparison
            if (_locks.TryRemove(KeyValuePair.Create(key, current)))
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<TimeSpan?> GetExpirationAsync(string key)
    {
        if (!_locks.TryGetValue(key, out var entry) || _IsExpired(entry))
            return Task.FromResult<TimeSpan?>(null);

        var remaining = entry.ExpirationTicks - _timeProvider.GetUtcNow().Ticks;
        return Task.FromResult<TimeSpan?>(TimeSpan.FromTicks(Math.Max(0, remaining)));
    }

    public Task<bool> ExistsAsync(string key)
    {
        return Task.FromResult(_locks.TryGetValue(key, out var entry) && !_IsExpired(entry));
    }

    private long _CalculateExpiration(TimeSpan? ttl)
        => ttl.HasValue ? _timeProvider.GetUtcNow().Ticks + ttl.Value.Ticks : long.MaxValue;

    private bool _IsExpired(LockEntry entry)
        => entry.ExpirationTicks <= _timeProvider.GetUtcNow().Ticks;

    private void _CleanupExpiredLocks(object? state)
    {
        if (_disposed) return;

        var now = _timeProvider.GetUtcNow().Ticks;
        foreach (var kvp in _locks)
        {
            if (kvp.Value.ExpirationTicks <= now)
                _locks.TryRemove(kvp);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _cleanupTimer.Dispose();
        _locks.Clear();
    }
}
```

**Key Design Decisions (if implementing Option C):**

1. **DO NOT use AsyncKeyedLock** - Use ConcurrentDictionary CAS operations directly
2. **Use `record struct`** - Value type, no heap allocation for entries
3. **Inject TimeProvider** - For testability
4. **Return Task.FromResult** - Operations are synchronous, no async overhead
5. **Timer-based cleanup** - Periodic sweep of expired entries
6. **CAS loops** - For ReplaceIfEqual/RemoveIfEqual thread safety

---

## Recommendation

**Implement Option B** - Add `AddInMemoryResourceLock` extension to existing `Framework.ResourceLocks.Cache` package.

**Rationale:**
1. Simplest solution that meets the need
2. No new packages to maintain (framework already has ~94 packages)
3. Follows established patterns
4. Leverages battle-tested Foundatio InMemoryCache
5. One-liner DI setup for consumers

---

## Implementation (COMPLETED)

### Created Package: Framework.ResourceLocks.InMemory

**File:** `src/Framework.ResourceLocks.InMemory/Setup.cs`

```csharp
// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Caching;
using Framework.Messaging;
using Framework.ResourceLocks.Cache;
using Framework.ResourceLocks.RegularLocks;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.ResourceLocks;

[PublicAPI]
public static class InMemoryResourceLockSetup
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds in-memory resource lock provider using in-memory cache and message bus.
        /// Suitable for single-instance deployments, development, or testing only.
        /// </summary>
        public IServiceCollection AddInMemoryResourceLock(
            Action<ResourceLockOptions, IServiceProvider>? optionSetupAction = null)
        {
            services.AddInMemoryCache();
            services.AddFoundatioInMemoryMessageBus();

            return services.AddResourceLock<CacheResourceLockStorage>(optionSetupAction);
        }

        /// <summary>
        /// Adds in-memory throttling resource lock provider.
        /// </summary>
        public IServiceCollection AddInMemoryThrottlingResourceLock(ThrottlingResourceLockOptions options)
        {
            services.AddInMemoryCache();

            return services.AddThrottlingResourceLock(
                options,
                sp => new CacheThrottlingResourceLockStorage(sp.GetRequiredService<ICache>())
            );
        }
    }
}
```

### Tests: Framework.ResourceLocks.InMemory.Tests.Unit

All 10 lock provider tests pass:
- `should_lock_with_try_acquire`
- `should_not_acquire_when_already_locked`
- `should_obtain_multiple_locks`
- `should_release_lock_multiple_times`
- `should_timeout_when_try_to_lock_acquired_resource`
- `should_lock_one_at_a_time_async`
- `should_acquire_and_release_locks_async`
- `should_acquire_one_at_a_time_parallel`
- `should_acquire_locks_in_sync`
- `should_acquire_locks_in_parallel`

### Usage

```csharp
// In-Memory Locks (Development/Single-Instance)
services.AddInMemoryResourceLock();

// With custom options
services.AddInMemoryResourceLock((options, sp) =>
{
    options.KeyPrefix = "myapp:locks:";
});

// Throttling locks
services.AddInMemoryThrottlingResourceLock(new ThrottlingResourceLockOptions
{
    MaxHitsPerPeriod = 100,
    ThrottlingPeriod = TimeSpan.FromMinutes(1)
});
```

**Warning:** In-memory locks are process-local. Not suitable for multi-instance deployments.

---

## Unresolved Questions

1. **Is there a specific use case requiring AsyncKeyedLock over Foundatio?**
   - Performance? (No benchmarks showing bottleneck)
   - Dependency size? (AsyncKeyedLock ~50KB, Foundatio similar)

2. **Should throttling lock also get in-memory convenience method?**
   - Added to plan above, but confirm if needed

3. **Package dependency direction:**
   - Option B adds `Framework.Caching.Foundatio.Memory` dependency to `Framework.ResourceLocks.Cache`
   - Alternative: Add extension to a new `Framework.ResourceLocks.Cache.Memory` package

4. **IMessageBus requirement:**
   - `ResourceLockProvider` uses `IMessageBus` for cross-waiter notification
   - In-memory locks need in-memory message bus for full functionality
   - Should `AddInMemoryResourceLock` also register in-memory message bus?

---

## Performance Considerations

### AsyncKeyedLock Pool Configuration (if Option C)

```csharp
// From best-practices research
var locker = new AsyncKeyedLocker<string>(new AsyncKeyedLockOptions
{
    PoolSize = Environment.ProcessorCount * 4,  // 32-64 typical
    PoolInitialFill = Environment.ProcessorCount
});
```

### Striped vs Dictionary AsyncKeyedLock (if Option C)

| Aspect | AsyncKeyedLocker (Dict) | StripedAsyncKeyedLocker |
|--------|------------------------|-------------------------|
| Memory | Dynamic, pooled | Fixed, pre-allocated |
| Collision | Never | Possible (hash-based) |
| Nested locks | Safe | Potential deadlock |
| **Recommendation** | Use for unpredictable keys | Use for high-frequency, well-distributed keys |

### Memory Growth (if Option C)

| Keys | Estimated Memory |
|------|------------------|
| 10K  | ~3-5MB |
| 100K | ~30-50MB |
| 1M   | ~300-500MB |

Cleanup timer essential for long-running processes.

---

## Testing Strategy

### From Existing Test Harness

Reuse `ResourceLockProviderTestsBase` covering:
- Basic acquire/release
- Mutual exclusion (lock contention)
- Timeout behavior
- Parallel stress tests
- Release/re-acquire cycles

### Additional In-Memory Specific Tests

1. TTL expiration (with fake TimeProvider)
2. Cleanup timer behavior
3. Memory growth under churn
4. Single-process limitation documentation

---

## Security Considerations

None identified. In-memory locks have no network exposure.

---

## References

- [AsyncKeyedLock GitHub](https://github.com/MarkCiliaVincenti/AsyncKeyedLock)
- [Keyed Semaphores Comparison](https://github.com/amoerie/keyed-semaphores)
- [Foundatio InMemoryCache](https://github.com/FoundatioFx/Foundatio)
- Existing implementations:
  - `src/Framework.ResourceLocks.Cache/CacheResourceLockStorage.cs`
  - `src/Framework.ResourceLocks.Redis/RedisResourceLockStorage.cs`
