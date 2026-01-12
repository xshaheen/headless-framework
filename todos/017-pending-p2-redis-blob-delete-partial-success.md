# Redis Blob Delete Returns True on Partial Success

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, data-integrity, dotnet, redis, blobs

---

## Problem Statement

`_DeleteAsync` (line 168) returns true if EITHER blob OR info is deleted:

```csharp
return result[0] || result[1];  // Should be &&
```

**Data Integrity Problem:**
1. `DeleteAsync("document.pdf")`
2. `deleteInfoTask` succeeds - metadata removed
3. `deleteBlobTask` fails - blob data remains
4. Returns TRUE (indicating successful deletion)
5. Caller believes file is gone
6. Blob data persists in Redis consuming memory forever

---

## Proposed Solutions

### Option A: Require Both Deletions
```csharp
return result[0] && result[1];
```
- **Pros:** Simple, correct behavior
- **Cons:** Returns false if blob already partially deleted
- **Effort:** Trivial
- **Risk:** Low

### Option B: Return Detailed Result
```csharp
public record DeleteResult(bool BlobDeleted, bool InfoDeleted);
return new DeleteResult(result[0], result[1]);
```
- **Pros:** Full visibility into what happened
- **Cons:** Breaking API change
- **Effort:** Medium
- **Risk:** Medium

---

## Recommended Action

**Option A** - Single character fix.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Redis/RedisBlobStorage.cs` (line 168)

---

## Acceptance Criteria

- [ ] Change `||` to `&&` on line 168
- [ ] Add test for partial delete scenario

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - data-integrity-guardian |
