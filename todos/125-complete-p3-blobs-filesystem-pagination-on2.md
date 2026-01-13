# Pagination Re-Enumerates From Start Every Page (O(n²))

**Date:** 2026-01-11
**Status:** complete
**Priority:** P3 - Nice-to-Have
**Tags:** documentation, performance, dotnet, blobs, filesystem

---

## Problem Statement

Each pagination request re-enumerates the entire directory and skips N items. For page 10 with pageSize=100, this reads 1000 file entries just to get 100.

```csharp
// FileSystemBlobStorage.cs:483-488
foreach (var path in Directory
    .EnumerateFiles(directoryPath, searchPattern, SearchOption.AllDirectories)
    .Skip(skip)    // <-- Re-reads from start every time!
    .Take(pagingLimit))
```

**Complexity:** O(page * pageSize) per page request = O(n²) total for full enumeration

---

## Recommended Action

**Option C** - Document the limitation. Add XML doc warning about performance with large directories. No code change needed.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (GetPagedListAsync method)

---

## Acceptance Criteria

- [x] XML doc comment added warning about O(n²) pagination behavior
- [x] Users informed to use streaming alternatives for large directories

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle |
| 2026-01-13 | Approved | Triage: downgraded to P3, documentation-only fix |
| 2026-01-13 | Resolved | Added XML doc remarks to GetPagedListAsync |
