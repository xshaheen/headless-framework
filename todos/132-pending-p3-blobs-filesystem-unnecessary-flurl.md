# Unnecessary Flurl Dependency for Path Combining

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, dependencies, dotnet, blobs, filesystem

---

## Problem Statement

Flurl library is used just for URL path combining:

```csharp
// FileSystemBlobStorage.cs:3
using Flurl;

// Line 400
BlobKey = Url.Combine([.. container.Skip(1).Append(blobName)]),
```

Flurl is a full HTTP client library. Using it just for `Url.Combine` is overkill when simpler alternatives exist.

---

## Proposed Solutions

### Option A: Use String.Join (Recommended)
```csharp
BlobKey = string.Join("/", container.Skip(1).Append(blobName)),
```
- **Pros:** No external dependency, built-in
- **Cons:** Less URL normalization
- **Effort:** Trivial
- **Risk:** None

### Option B: Keep Flurl
If Flurl is used elsewhere in the solution, this isn't a problem.
- **Pros:** Already using it
- **Cons:** Heavy dependency for simple use
- **Effort:** None
- **Risk:** None

---

## Recommended Action

Check if Flurl is used elsewhere. If not, **Option A** - use `string.Join`.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 3, 400)

---

## Acceptance Criteria

- [ ] Flurl dependency removed (if not used elsewhere)
- [ ] BlobKey construction still works

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pragmatic-dotnet-reviewer |
