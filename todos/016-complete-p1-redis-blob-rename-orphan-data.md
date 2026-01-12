# Redis Blob Storage Rename Operation Leaves Orphaned Data

**Date:** 2026-01-11
**Status:** complete
**Priority:** P1 - Critical
**Tags:** code-review, data-integrity, dotnet, redis, blobs

---

## Problem Statement

`RenameAsync` (lines 258-266) is implemented as Copy+Delete without transactional guarantees. On failure, no cleanup occurs.

```csharp
var result = await CopyAsync(blobContainer, blobName, newBlobContainer, newBlobName, cancellationToken);
if (!result) return false;
return await DeleteAsync(blobContainer, blobName, cancellationToken).AnyContext();
```

**Data Loss Scenarios:**

**Scenario 1 - Partial Rename:**
1. `RenameAsync("old.pdf" -> "new.pdf")`
2. `CopyAsync` succeeds - "new.pdf" now exists
3. `DeleteAsync` fails (network issue)
4. Exception caught, returns false (line 268-273)
5. **"new.pdf" persists as orphan** - never cleaned up

**Scenario 2 - Concurrent Access:**
1. Thread A: `RenameAsync("file.pdf" -> "archived.pdf")`
2. Thread B: `DownloadAsync("file.pdf")`
3. Thread A: Copy completes
4. Thread B: Returns old blob successfully
5. Thread A: Delete succeeds
6. Thread B: User re-uploads modified "file.pdf"
7. Result: Inconsistent state

---

## Findings

**From data-integrity-guardian:**
- No cleanup on failure in catch block (lines 268-273)
- Exception swallowed, returns false
- Caller cannot distinguish "blob didn't exist" from "rename failed"

**From strict-dotnet-reviewer:**
- Same swallow pattern in `CopyAsync` (lines 307-314)

---

## Proposed Solutions

### Option A: Cleanup on Failure
```csharp
try
{
    var result = await CopyAsync(...);
    if (!result) return false;

    var deleted = await DeleteAsync(blobContainer, blobName, cancellationToken);
    if (!deleted)
    {
        // Rollback: delete the copy
        await DeleteAsync(newBlobContainer, newBlobName, cancellationToken);
        return false;
    }
    return true;
}
catch (Exception e)
{
    // Attempt cleanup of partial copy
    await DeleteAsync(newBlobContainer, newBlobName, cancellationToken).ConfigureAwait(false);
    _logger.LogError(e, "Error renaming...");
    throw; // Don't swallow - let caller know
}
```
- **Pros:** Cleans up orphans, surfaces errors
- **Cons:** Cleanup can also fail
- **Effort:** Small
- **Risk:** Medium (breaking change - now throws)

### Option B: Lua Script Atomic Rename
```lua
-- Server-side atomic rename
local blobData = redis.call('HGET', KEYS[1], ARGV[1])
local infoData = redis.call('HGET', KEYS[2], ARGV[1])
if not blobData then return 0 end
redis.call('HSET', KEYS[3], ARGV[2], blobData)
redis.call('HSET', KEYS[4], ARGV[2], infoData)
redis.call('HDEL', KEYS[1], ARGV[1])
redis.call('HDEL', KEYS[2], ARGV[1])
return 1
```
- **Pros:** True atomicity, no partial state
- **Cons:** More complex, Lua knowledge required
- **Effort:** Medium
- **Risk:** Low

### Option C: Redis COPY Command (6.2+)
- Use native COPY for same-database operations
- **Pros:** Server-side, efficient
- **Cons:** Requires Redis 6.2+, cross-hash copy not supported
- **Effort:** Medium
- **Risk:** Medium (version dependency)

---

## Recommended Action

**Option B: Lua Script Atomic Rename** - Implement server-side atomic rename for true atomicity with no partial state possible.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Redis/RedisBlobStorage.cs` (lines 240-274, 280-315)

**Affected Methods:**
- `RenameAsync`
- `CopyAsync`

---

## Acceptance Criteria

- [x] Implement Lua script for atomic rename operation
- [x] RenameAsync uses Lua script instead of Copy+Delete
- [x] CopyAsync also uses Lua script for atomicity
- [x] Add integration test for rename operation (existing tests pass)
- [x] Verify no orphaned data on failure scenarios

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - data-integrity-guardian, strict-dotnet-reviewer |
| 2026-01-13 | Approved | Triage: Option B selected - Lua script for atomic rename |
| 2026-01-13 | Resolved | Implemented Lua scripts for RenameAsync and CopyAsync, all 30 tests pass |
