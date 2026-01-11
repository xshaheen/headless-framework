# Inconsistent Array.Empty vs Collection Expression

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, dotnet, blobs-azure, code-style

---

## Problem Statement

Project convention is to use collection expressions `[]`, but code mixes both patterns:

```csharp
// Line 547 - Array.Empty
ExtraLoadedBlobs = hasExtraLoadedBlobs ? blobs.Skip(pageSize).ToList() : Array.Empty<BlobInfo>(),

// Lines 434, 522-523 - Collection expression
ExtraLoadedBlobs = [],
Blobs = [],
```

**Why it matters:**
- Inconsistent code style
- CLAUDE.md says: "Collection expressions: `[]`"

---

## Proposed Solutions

### Option A: Replace All Array.Empty with []
```csharp
ExtraLoadedBlobs = hasExtraLoadedBlobs ? blobs.Skip(pageSize).ToList() : [],
```
- **Pros:** Consistent, follows convention
- **Cons:** Trivial change
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Use `[]` consistently.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs` (line 547)

---

## Acceptance Criteria

- [ ] All empty collections use `[]` syntax
- [ ] No `Array.Empty<T>()` remaining

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
