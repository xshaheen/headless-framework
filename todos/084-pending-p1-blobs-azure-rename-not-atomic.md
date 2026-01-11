# RenameAsync is Non-Atomic (Data Duplication/Loss Risk)

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, data-integrity, blobs-azure, distributed-systems

---

## Problem Statement

`RenameAsync` uses copy-then-delete pattern without transaction semantics:

```csharp
var copyResult = await CopyAsync(...);  // SUCCESS - blob now in two places
if (!copyResult) { return false; }

var deleteResult = await DeleteAsync(...);  // FAILS - original remains
if (!deleteResult) {
    _logger.LogWarning("Unable to delete {BlobName}", blobName);
    return false;  // Both blobs exist - data duplication!
}
```

**Issues:**

1. **Data Duplication**: If delete fails after successful copy, blob exists in both locations. No cleanup of the copied blob.

2. **Misleading Failure**: `DeleteIfExistsAsync` returns `false` when blob doesn't exist. If source was already deleted (race condition), method returns `false` even though destination has the data.

3. **No Rollback**: Copy is not rolled back on delete failure.

**Why it matters:**
- Storage cost increase from duplicated blobs
- Caller may retry, creating more duplicates
- Caller may interpret failure incorrectly and lose track of data
- Classic distributed systems anti-pattern

---

## Proposed Solutions

### Option A: Add Compensating Transaction
```csharp
var copyResult = await CopyAsync(...);
if (!copyResult) return false;

var deleteResult = await DeleteAsync(sourceContainer, sourceName, cancellationToken);
if (!deleteResult)
{
    // Rollback: delete the copy
    await DeleteAsync(destContainer, destName, cancellationToken);
    _logger.LogWarning("Rename failed, rolled back copy");
    return false;
}
return true;
```
- **Pros:** Ensures atomicity (best effort)
- **Cons:** Rollback can also fail
- **Effort:** Small
- **Risk:** Low

### Option B: Document Non-Atomic Behavior
- **Pros:** Non-breaking, clear expectations
- **Cons:** Doesn't fix the issue
- **Effort:** Small
- **Risk:** Low

### Option C: Check Delete Returns False for Already-Deleted
```csharp
var deleteResult = await DeleteAsync(...);
// DeleteIfExistsAsync returns false if blob doesn't exist - that's ok
// Only log warning if delete fails for other reasons
```
- **Pros:** Handles race condition correctly
- **Cons:** Harder to distinguish "not found" from "failed"
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** - Add compensating transaction to clean up copy on delete failure.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs` (lines 270-303)

---

## Acceptance Criteria

- [ ] If delete fails, copied blob is deleted (rollback)
- [ ] Logging indicates rollback occurred
- [ ] Return false only if both original and copy operations fail
- [ ] Document that rename is not truly atomic

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From data integrity review |
