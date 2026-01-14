---
status: done
priority: p1
issue_id: "004"
tags: [code-review, performance, permissions]
dependencies: []
---

# Sequential Cache Removal in RevokeAsync

## Problem Statement

The batch `RevokeAsync` method uses a sequential loop with `await cache.RemoveAsync()` for each permission. This results in O(n) network round trips when O(1) is possible.

## Findings

**Location:** `src/Framework.Permissions.Core/Grants/PermissionGrantStore.cs` (lines 264-270)

```csharp
foreach (var name in names)
{
    await cache.RemoveAsync(
        cacheKey: PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey),
        cancellationToken
    );
}
```

**Impact:**
- Revoking 50 permissions = 50 sequential network round trips
- Latency compounds linearly with permission count
- Under load, this becomes a significant bottleneck

## Proposed Solutions

### Option A: Batch Remove (Recommended)
**Pros:** O(1) network calls, simple change
**Cons:** Requires ICache.RemoveAllAsync support
**Effort:** Small
**Risk:** Low

```csharp
var cacheKeys = names.Select(name =>
    PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey));
await cache.RemoveAllAsync(cacheKeys, cancellationToken);
```

### Option B: Parallel Remove
**Pros:** Works with existing API
**Cons:** Still multiple round trips
**Effort:** Small
**Risk:** Low

```csharp
await Task.WhenAll(names.Select(name =>
    cache.RemoveAsync(PermissionGrantCacheItem.CalculateCacheKey(name, providerName, providerKey), cancellationToken)));
```

## Recommended Action

Use Option A: Add new `RemoveAllAsync` batch method to `ICache` abstraction and use it here. This provides O(1) network calls instead of O(n).

## Technical Details

**Affected Files:**
- `src/Framework.Permissions.Core/Grants/PermissionGrantStore.cs`

## Acceptance Criteria

- [x] Batch revoke uses single cache operation or parallel operations
- [x] Performance test shows improvement for 50+ permission revokes

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Created from code review | Strict .NET & Performance Oracle findings |
| 2026-01-14 | Triage approved | Status: ready. Add RemoveAllAsync to ICache |
| 2026-01-14 | Implemented | Added RemoveAllAsync to ICache<T>, updated RevokeAsync to use batch removal. All tests pass. |

## Resources

- Performance Oracle review findings

### 2026-01-14 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
