# Incorrect [Pure] Attributes on Side-Effectful Methods

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, dotnet, blobs-abstractions, api-design

---

## Problem Statement

`IBlobStorage` interface methods are marked with `[Pure]` attributes but have side effects:

```csharp
[SystemPure]
[JetBrainsPure]
ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default);

[SystemPure]
[JetBrainsPure]
ValueTask UploadAsync(...)
```

**Affected methods:**
- `CreateContainerAsync` - creates containers
- `UploadAsync` - writes data
- `DeleteAsync` - deletes data
- `BulkUploadAsync` - writes data
- `BulkDeleteAsync` - deletes data
- `RenameAsync` - modifies data location
- `CopyAsync` - writes data

**Why it matters:**
- `[Pure]` means no side effects - these methods clearly have side effects
- Misleading to analyzers and developers
- Semantically incorrect
- Could lead to incorrect assumptions about caching/memoization

---

## Proposed Solutions

### Option A: Remove All [Pure] Attributes
- **Pros:** Correct semantics
- **Cons:** Breaking if anyone relies on attributes (unlikely)
- **Effort:** Small
- **Risk:** Low

### Option B: Only Mark Query Methods as Pure
Keep `[Pure]` only on:
- `ExistsAsync`
- `DownloadAsync`
- `GetBlobInfoAsync`
- `GetPagedListAsync`
- **Pros:** Semantically correct for read-only methods
- **Cons:** Still questionable - these do I/O
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Remove `[Pure]` attributes from all methods. Blob operations inherently have side effects or perform I/O.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Abstractions/IBlobStorage.cs` (multiple methods)

---

## Acceptance Criteria

- [ ] `[Pure]` attributes removed from mutating methods
- [ ] Consider removing from all methods (I/O is a side effect)
- [ ] No breaking changes to method signatures

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
