# Redis Blob Storage Non-Atomic Operations Cause Data Loss

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, data-integrity, dotnet, redis, blobs

---

## Problem Statement

Upload stores blob data and metadata in separate Redis operations via `Task.WhenAll` (lines 67, 82-85). These are not transactional.

```csharp
var saveBlobTask = database.HashSetAsync(blobsContainer, blobPath, memory.ToArray());
// ...
var saveInfoTask = database.HashSetAsync(infoContainer, blobPath, memory.ToArray());
await Task.WhenAll(saveBlobTask, saveInfoTask);
```

**Data Corruption Scenario:**
1. Client uploads file "invoice.pdf"
2. `saveBlobTask` succeeds - blob data written
3. `saveInfoTask` fails - network timeout, Redis OOM
4. Result: Orphaned blob data exists without metadata
5. `ExistsAsync` returns false (checks info only)
6. Blob data is unreachable, consuming memory indefinitely

---

## Findings

**From data-integrity-guardian:**
- Retry logic wraps both tasks together
- If one succeeds and one fails, retry re-executes both
- Blob write is not idempotent in partial failure scenarios

**From architecture-strategist:**
- No compensating transaction or cleanup on partial failure
- Delete has same issue: `return result[0] || result[1]` (line 168)

---

## Proposed Solutions

### Option A: Redis MULTI/EXEC Transaction
```csharp
var transaction = database.CreateTransaction();
transaction.HashSetAsync(blobsContainer, blobPath, blobData);
transaction.HashSetAsync(infoContainer, blobPath, infoData);
await transaction.ExecuteAsync();
```
- **Pros:** Atomic, all-or-nothing
- **Cons:** Transactions have limitations in Redis Cluster
- **Effort:** Medium
- **Risk:** Low

### Option B: Lua Script
```csharp
var script = LuaScript.Prepare(@"
    redis.call('HSET', @blobsKey, @path, @blobData)
    redis.call('HSET', @infoKey, @path, @infoData)
    return 1
");
```
- **Pros:** Atomic, works in cluster mode
- **Cons:** More complex to maintain
- **Effort:** Medium
- **Risk:** Low

### Option C: Compensating Delete on Failure
```csharp
try {
    await saveBlobTask;
    await saveInfoTask;
} catch {
    await database.HashDeleteAsync(blobsContainer, blobPath); // Cleanup
    throw;
}
```
- **Pros:** Simple
- **Cons:** Not truly atomic, cleanup can also fail
- **Effort:** Small
- **Risk:** Medium

---

## Recommended Action

**Option A** - Redis transactions are the right tool for multi-key atomicity within same database.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Redis/RedisBlobStorage.cs` (lines 67-89, 158-166)

**Affected Methods:**
- `UploadAsync`
- `_DeleteAsync`

**Additional Fix Needed:**
Line 168: Change `return result[0] || result[1]` to `return result[0] && result[1]`

---

## Acceptance Criteria

- [ ] Upload uses Redis transaction for atomicity
- [ ] Delete returns true only if BOTH operations succeed
- [ ] Add integration test for partial failure scenario
- [ ] Document transactional guarantees in README

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - data-integrity-guardian |
