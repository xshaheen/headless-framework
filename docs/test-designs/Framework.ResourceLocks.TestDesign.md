# Test Case Design: Framework.ResourceLocks (All Packages)

**Packages:**
- `src/Framework.ResourceLocks.Abstractions`
- `src/Framework.ResourceLocks.Core`
- `src/Framework.ResourceLocks.Cache`
- `src/Framework.ResourceLocks.Redis`
- `src/Framework.ResourceLocks.InMemory`

**Test Projects:** None (new projects needed)
**Generated:** 2026-01-25

## Package Analysis

### Framework.ResourceLocks.Abstractions

| File | Purpose | Testable |
|------|---------|----------|
| `RegularLocks/IResourceLockProvider.cs` | Lock provider interface | Low (interface) |
| `RegularLocks/IResourceLock.cs` | Lock handle interface | Low (interface) |
| `RegularLocks/LockInfo.cs` | Lock information record | Medium |
| `RegularLocks/ResourceLockProviderExtensions.cs` | AcquireAsync extension | High |
| `ThrottlingLocks/IThrottlingResourceLockProvider.cs` | Throttling provider | Low (interface) |
| `ThrottlingLocks/IResourceThrottlingLock.cs` | Throttling lock handle | Low (interface) |

### Framework.ResourceLocks.Core

| File | Purpose | Testable |
|------|---------|----------|
| `RegularLocks/ResourceLockProvider.cs` | IResourceLockProvider impl | High |
| `RegularLocks/DisposableResourceLock.cs` | IResourceLock impl | High |
| `RegularLocks/IResourceLockStorage.cs` | Storage interface | Low (interface) |
| `RegularLocks/ResourceLockOptions.cs` | Lock configuration | Medium |
| `RegularLocks/ResourceLockReleased.cs` | Lock release message | Low |
| `ThrottlingLocks/ThrottlingResourceLockProvider.cs` | Throttling impl | High |
| `ThrottlingLocks/ResourceThrottlingLock.cs` | Throttling lock impl | Medium |
| `ThrottlingLocks/ThrottlingResourceLockOptions.cs` | Throttling options | Medium |
| `ThrottlingLocks/IThrottlingResourceLockStorage.cs` | Throttling storage | Low (interface) |
| `Setup.cs` | DI registration | Low |

### Framework.ResourceLocks.Cache

| File | Purpose | Testable |
|------|---------|----------|
| `CacheResourceLockStorage.cs` | ICache-backed storage | High (integration) |
| `CacheThrottlingResourceLockStorage.cs` | Cache throttling storage | High (integration) |

### Framework.ResourceLocks.Redis

| File | Purpose | Testable |
|------|---------|----------|
| `RedisResourceLockStorage.cs` | Redis-backed storage | High (integration) |
| `RedisThrottlingResourceLockStorage.cs` | Redis throttling storage | High (integration) |
| `Setup.cs` | DI registration | Low |

## Current Test Coverage

**Existing Tests:** None

---

## Missing: ResourceLockProviderExtensions Tests

**File:** `tests/Framework.ResourceLocks.Tests.Unit/ResourceLockProviderExtensionsTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_throw_timeout_when_lock_not_acquired` | AcquireAsync throws TimeoutException |
| `should_return_lock_when_acquired` | Successful acquisition |
| `should_pass_parameters_to_try_acquire` | Parameter forwarding |

---

## Missing: ResourceLockProvider Tests

**File:** `tests/Framework.ResourceLocks.Tests.Unit/RegularLocks/ResourceLockProviderTests.cs`

### TryAcquireAsync Tests

| Test Case | Description |
|-----------|-------------|
| `should_throw_when_resource_is_null` | Argument validation |
| `should_throw_when_resource_is_whitespace` | Argument validation |
| `should_throw_when_resource_exceeds_max_length` | MaxResourceNameLength |
| `should_acquire_lock_when_not_held` | Happy path |
| `should_return_null_when_already_locked` | Lock contention |
| `should_retry_with_exponential_backoff` | Retry delay pattern |
| `should_return_null_after_acquire_timeout` | Timeout behavior |
| `should_respect_cancellation_token` | Cancellation |
| `should_use_default_time_until_expires` | Default 20 minutes |
| `should_use_custom_time_until_expires` | Custom TTL |
| `should_use_infinite_time_until_expires` | No expiration |
| `should_throw_when_max_waiters_exceeded` | DoS protection |
| `should_throw_when_max_concurrent_resources_exceeded` | DoS protection |

### ReleaseAsync Tests

| Test Case | Description |
|-----------|-------------|
| `should_throw_when_resource_is_null` | Argument validation |
| `should_throw_when_lock_id_is_null` | Argument validation |
| `should_release_lock` | Happy path |
| `should_retry_release_on_transient_error` | Retry logic |
| `should_publish_lock_released_message` | Outbox publish |

### RenewAsync Tests

| Test Case | Description |
|-----------|-------------|
| `should_throw_when_resource_is_null` | Argument validation |
| `should_throw_when_lock_id_is_null` | Argument validation |
| `should_renew_lock_if_held` | Successful renewal |
| `should_return_false_if_lock_not_held` | Not the owner |
| `should_extend_expiration` | TTL extension |

### IsLockedAsync Tests

| Test Case | Description |
|-----------|-------------|
| `should_return_true_when_locked` | Lock exists |
| `should_return_false_when_not_locked` | Lock doesn't exist |

### Observability Tests

| Test Case | Description |
|-----------|-------------|
| `should_get_expiration_for_locked_resource` | GetExpirationAsync |
| `should_return_null_expiration_when_not_locked` | No lock |
| `should_get_lock_info` | GetLockInfoAsync |
| `should_list_active_locks` | ListActiveLocksAsync |
| `should_get_active_locks_count` | GetActiveLocksCountAsync |

---

## Missing: DisposableResourceLock Tests

**File:** `tests/Framework.ResourceLocks.Tests.Unit/RegularLocks/DisposableResourceLockTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_store_resource_and_lock_id` | Properties |
| `should_store_acquired_at` | AcquiredAt property |
| `should_store_time_waited_for_lock` | TimeWaitedForLock |
| `should_release_on_dispose` | DisposeAsync releases |
| `should_only_release_once` | Idempotent dispose |
| `should_calculate_locked_duration` | LockedDuration |

---

## Missing: ThrottlingResourceLockProvider Tests

**File:** `tests/Framework.ResourceLocks.Tests.Unit/ThrottlingLocks/ThrottlingResourceLockProviderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_acquire_when_under_limit` | Happy path |
| `should_return_null_when_at_limit` | Limit reached |
| `should_wait_and_retry_when_at_limit` | Blocking acquire |
| `should_release_decrements_count` | Release behavior |
| `should_expire_slots_after_ttl` | TTL expiration |
| `should_get_available_slots` | GetAvailableSlotsAsync |

---

## Missing: CacheResourceLockStorage Tests (Integration)

**File:** `tests/Framework.ResourceLocks.Cache.Tests.Integration/CacheResourceLockStorageTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_insert_lock` | InsertAsync |
| `should_not_insert_when_exists` | Conflict |
| `should_remove_lock` | RemoveIfEqualAsync |
| `should_not_remove_when_different_id` | Wrong owner |
| `should_expire_after_ttl` | Auto-expiration |
| `should_get_lock_id` | GetAsync |
| `should_check_exists` | ExistsAsync |
| `should_get_expiration` | GetExpirationAsync |

---

## Missing: RedisResourceLockStorage Tests (Integration)

**File:** `tests/Framework.ResourceLocks.Redis.Tests.Integration/RedisResourceLockStorageTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_insert_lock_with_nx` | SET NX behavior |
| `should_use_redis_expiry` | PSETEX |
| `should_remove_with_lua_script` | Atomic compare-and-delete |
| `should_handle_connection_failure` | Redis unavailable |

---

## Test Infrastructure

### Mock Storage

```csharp
public sealed class FakeResourceLockStorage : IResourceLockStorage
{
    private readonly ConcurrentDictionary<string, (string LockId, DateTimeOffset? Expiry)> _locks = new();

    public Task<bool> InsertAsync(string key, string lockId, TimeSpan? expiry)
    {
        var expiration = expiry.HasValue ? DateTimeOffset.UtcNow + expiry.Value : (DateTimeOffset?)null;
        return Task.FromResult(_locks.TryAdd(key, (lockId, expiration)));
    }

    public Task<bool> RemoveIfEqualAsync(string key, string lockId)
    {
        if (_locks.TryGetValue(key, out var existing) && existing.LockId == lockId)
        {
            return Task.FromResult(_locks.TryRemove(key, out _));
        }
        return Task.FromResult(false);
    }

    // ... other methods
}
```

---

## Test Summary

| Component | Existing | New Unit | New Integration | Total |
|-----------|----------|----------|-----------------|-------|
| ResourceLockProviderExtensions | 0 | 3 | 0 | 3 |
| ResourceLockProvider | 0 | 24 | 0 | 24 |
| DisposableResourceLock | 0 | 6 | 0 | 6 |
| ThrottlingResourceLockProvider | 0 | 6 | 0 | 6 |
| CacheResourceLockStorage | 0 | 0 | 8 | 8 |
| RedisResourceLockStorage | 0 | 0 | 4 | 4 |
| **Total** | **0** | **39** | **12** | **51** |

---

## Priority Order

1. **ResourceLockProvider** - Core locking logic with backoff and metrics
2. **DisposableResourceLock** - Lock handle management
3. **ThrottlingResourceLockProvider** - Rate limiting
4. **Storage implementations** - Integration tests

---

## Notes

1. **Exponential backoff** - 50ms, 100ms, 200ms... capped at 3s with ±25% jitter
2. **DoS protection** - MaxResourceNameLength, MaxConcurrentWaitingResources, MaxWaitersPerResource
3. **Metrics** - Counter for failed locks, histogram for wait time
4. **Message-based wake** - ResourceLockReleased message via outbox signals waiters
5. **Auto-reset events** - Per-resource AsyncAutoResetEvent with ref counting
6. **Disposable lock** - Automatically releases on dispose

---

## ResourceLockProvider Architecture

```
IResourceLockProvider
├── TryAcquireAsync() → IResourceLock?
├── ReleaseAsync()
├── RenewAsync() → bool
├── IsLockedAsync() → bool
├── GetExpirationAsync() → TimeSpan?
├── GetLockInfoAsync() → LockInfo?
├── ListActiveLocksAsync() → IReadOnlyList<LockInfo>
└── GetActiveLocksCountAsync() → int

ResourceLockProvider
├── IResourceLockStorage (pluggable backend)
├── IOutboxPublisher (for lock release messages)
├── ILongIdGenerator (lock IDs)
├── Metrics (counter, histogram)
├── DoS limits
└── Exponential backoff with jitter

Storage Implementations:
├── CacheResourceLockStorage (ICache)
├── RedisResourceLockStorage (direct Redis)
└── InMemory (ConcurrentDictionary)

Lock Lifecycle:
1. TryAcquireAsync() → InsertAsync() with TTL
2. Returns DisposableResourceLock
3. lock.DisposeAsync() → ReleaseAsync()
4. ReleaseAsync() → RemoveIfEqualAsync() + Publish
5. Waiters wake on message
```

---

## Recommendation

**High Priority** - Resource locking is critical infrastructure. Unit tests should cover:
- Acquire/Release flow
- Exponential backoff timing
- DoS protection limits
- Metrics recording
- Dispose behavior

Integration tests for storage backends are medium priority.
