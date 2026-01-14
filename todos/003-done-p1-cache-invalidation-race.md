---
status: done
priority: p1
issue_id: "003"
tags: [code-review, security, caching, permissions]
dependencies: []
---

# Race Condition in Cache Invalidation

## Problem Statement

There is a TOCTOU (time-of-check-to-time-of-use) race condition between database operations and cache updates in `GrantAsync`/`RevokeAsync`. A brief window exists where revoked permissions remain effective in cache.

## Findings

**Location:** `src/Framework.Permissions.Core/Grants/PermissionGrantStore.cs`

In `RevokeAsync` (lines 215-235):
```csharp
await repository.DeleteAsync(permissionGrant, cancellationToken);  // 1. Delete from DB
await cache.RemoveAsync(...);  // 2. Remove from cache (non-atomic)
```

**Attack Scenario:**
1. Request A: Revokes permission, DB updated, cache removal pending
2. Request B: Checks permission, reads stale cache showing permission granted
3. Request B proceeds with elevated privileges

**Security Impact:** Brief windows where revoked permissions remain effective.

## Proposed Solutions

### Option A: Invalidate Cache Before DB (Recommended)
**Pros:** Fail-safe - permission denied if cache fails
**Cons:** Temporary denial during race window
**Effort:** Small
**Risk:** Low

```csharp
await cache.RemoveAsync(...);  // Invalidate first
await repository.DeleteAsync(permissionGrant, cancellationToken);
```

### Option B: Transactional Outbox Pattern
**Pros:** Eventually consistent, reliable
**Cons:** More complex infrastructure
**Effort:** Large
**Risk:** Medium

### Option C: Distributed Lock for Grant Changes
**Pros:** Serializes changes
**Cons:** Performance impact
**Effort:** Medium
**Risk:** Medium

## Recommended Action

Use Option A: Invalidate cache before DB write. Fail-safe approach - temporary denial is acceptable, stale grant is not.

## Technical Details

**Affected Files:**
- `src/Framework.Permissions.Core/Grants/PermissionGrantStore.cs`

## Acceptance Criteria

- [x] Cache invalidation occurs before or atomically with DB change
- [x] No window where revoked permission appears granted
- [x] Integration test verifies concurrent revoke/check behavior

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Created from code review | Security Sentinel finding |
| 2026-01-14 | Triage approved | Status: ready |

## Resources

- Security Sentinel review findings

### 2026-01-14 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
