# Pagination Re-Enumerates From Start Every Page (O(n²))

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, dotnet, blobs, filesystem

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

**Projected Impact:**
- 1M files, 100 per page = Page 10,000 reads 1M entries
- Severe performance degradation as pagination progresses
- Each page gets slower than the previous one

---

## Proposed Solutions

### Option A: Use IAsyncEnumerable with Continuation (Recommended)
```csharp
public async IAsyncEnumerable<BlobInfo> GetBlobsAsync(
    string[] container,
    string? blobSearchPattern = null,
    [EnumeratorCancellation] CancellationToken cancellationToken = default
)
{
    var directoryPath = _GetDirectoryPath(container);
    var pattern = blobSearchPattern ?? "*";

    foreach (var path in Directory.EnumerateFiles(directoryPath, pattern, SearchOption.AllDirectories))
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fileInfo = new FileInfo(path);
        if (fileInfo.Exists)
        {
            yield return _CreateBlobInfo(fileInfo, directoryPath);
        }
    }
}
```
- **Pros:** O(n) total, streaming, memory efficient
- **Cons:** Interface change
- **Effort:** Medium
- **Risk:** Medium - new API pattern

### Option B: Store Enumerator Across Pages
Keep the `IEnumerator` alive between page requests using a token:
```csharp
private readonly ConcurrentDictionary<string, IEnumerator<string>> _enumerators = new();

public async ValueTask<PagedFileListResult> GetPagedListAsync(
    string[] container,
    string? continuationToken = null,  // Token to resume enumeration
    int pageSize = 100,
    CancellationToken cancellationToken = default
)
```
- **Pros:** O(n) total, backwards compatible API shape
- **Cons:** Complex state management, memory for active enumerators
- **Effort:** Large
- **Risk:** Medium

### Option C: Document Limitation
Add warning about performance with large directories.
- **Pros:** No code change
- **Cons:** Problem remains
- **Effort:** Trivial
- **Risk:** None

---

## Recommended Action

**Option A** - Add `IAsyncEnumerable` overload for streaming. Keep existing `GetPagedListAsync` for backwards compatibility but document its O(n²) nature.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 458-532)

---

## Acceptance Criteria

- [ ] New streaming API available
- [ ] Performance scales linearly with directory size
- [ ] Existing API documented with limitations

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle |
