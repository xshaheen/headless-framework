# Overly Complex Pagination Logic

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, simplification, blobs-azure

---

## Problem Statement

Pagination logic maintains complex `ExtraLoadedBlobs` buffer to handle Azure SDK's variable page sizes:

```csharp
public required IReadOnlyCollection<BlobInfo> ExtraLoadedBlobs { get; init; }
```

This leads to ~40 lines of complex state management across `_GetFilesAsync` method.

**Why it matters:**
- Hard to understand and maintain
- Bug-prone (HasMore logic at line 448 appears incorrect)
- `FileSystemBlobStorage` uses simpler +1 approach

---

## Proposed Solutions

### Option A: Simplify to +1 Approach
Instead of tracking `ExtraLoadedBlobs`, fetch `pageSize + 1`:
```csharp
var pageSizeToLoad = pageSize + 1;
// ...fetch blobs...
var hasMore = blobs.Count > pageSize;
return new AzureNextPageResult
{
    Blobs = blobs.Take(pageSize).ToList(),
    HasMore = hasMore,
    // No ExtraLoadedBlobs needed
};
```
- **Pros:** Simpler, matches FileSystemBlobStorage
- **Cons:** Refactoring needed
- **Effort:** Medium
- **Risk:** Low

### Option B: Remove AzureNextPageResult, Use NextPageResult Directly
- **Pros:** Less custom code
- **Cons:** May lose Azure-specific features
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** - Simplify to +1 approach. Eliminates ~35 lines of complexity.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs` (lines 413-553)
- `src/Framework.Blobs.Azure/Internals/AzureNextPageResult.cs`

---

## Acceptance Criteria

- [ ] `ExtraLoadedBlobs` removed
- [ ] Pagination uses simple +1 approach
- [ ] All pagination tests pass
- [ ] LOC reduced

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From simplicity review |
