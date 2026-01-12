# Large Files Loaded Entirely Into Memory in DownloadAsync

**Date:** 2026-01-11
**Status:** ready
**Priority:** P1 - Critical
**Tags:** code-review, performance, memory, dotnet, blobs, filesystem

---

## Problem Statement

`DownloadAsync` copies the entire file into a `MemoryStream` before returning. For large files (100MB+), this causes immediate memory pressure and potential `OutOfMemoryException`.

```csharp
// FileSystemBlobStorage.cs:357-374
public async ValueTask<BlobDownloadResult?> DownloadAsync(...)
{
    var filePath = _BuildBlobPath(container, blobName);

    if (!File.Exists(filePath)) return null;

    await using var fileStream = File.OpenRead(filePath);
    var memoryStream = await fileStream.CopyToMemoryStreamAndFlushAsync(cancellationToken);

    return new BlobDownloadResult(memoryStream!, Path.GetFileName(filePath));
}
```

**Impact at scale:**
- 10 concurrent 50MB downloads = 500MB heap allocation
- 1GB file = `OutOfMemoryException` likely
- No backpressure mechanism
- GC pressure from LOH allocations (>85KB objects)
- DoS vector - attacker uploads large file, requests many concurrent downloads

---

## Proposed Solutions

### Option A: Return FileStream Directly (Recommended)
```csharp
public ValueTask<BlobDownloadResult?> DownloadAsync(...)
{
    var filePath = _BuildBlobPath(container, blobName);

    if (!File.Exists(filePath))
        return ValueTask.FromResult<BlobDownloadResult?>(null);

    var fileStream = File.OpenRead(filePath);
    return ValueTask.FromResult<BlobDownloadResult?>(
        new BlobDownloadResult(fileStream, Path.GetFileName(filePath)));
}
```
- **Pros:** O(1) memory, streaming, no copy
- **Cons:** Stream ownership transfers to caller (must document)
- **Effort:** Small
- **Risk:** Low - may need API documentation update

### Option B: Add Size Check and Threshold
```csharp
public async ValueTask<BlobDownloadResult?> DownloadAsync(...)
{
    var filePath = _BuildBlobPath(container, blobName);
    if (!File.Exists(filePath)) return null;

    var fileInfo = new FileInfo(filePath);

    // For large files, return FileStream directly
    if (fileInfo.Length > 10 * 1024 * 1024) // 10MB threshold
    {
        return new BlobDownloadResult(File.OpenRead(filePath), fileInfo.Name);
    }

    // For small files, MemoryStream is fine
    await using var fileStream = File.OpenRead(filePath);
    var memoryStream = new MemoryStream();
    await fileStream.CopyToAsync(memoryStream, cancellationToken).AnyContext();
    memoryStream.Seek(0, SeekOrigin.Begin);
    return new BlobDownloadResult(memoryStream, fileInfo.Name);
}
```
- **Pros:** Backwards compatible for small files
- **Cons:** Inconsistent behavior based on size
- **Effort:** Small
- **Risk:** Medium - inconsistent stream types

### Option C: Document Limitation
Add XML doc comment warning about memory usage for large files.
- **Pros:** No code change
- **Cons:** Doesn't fix the problem
- **Effort:** Trivial
- **Risk:** None but issue remains

---

## Recommended Action

**Option A** - Return `FileStream` directly. The current behavior is fundamentally wrong for a streaming API. Callers should handle stream disposal (standard .NET pattern).

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 357-374)
- `src/Framework.Blobs.FileSystem/Internals/Helpers.cs` (can be deleted after)

**Memory Analysis:**
| File Size | Current Memory | With FileStream |
|-----------|----------------|-----------------|
| 1 MB | 1 MB | ~4 KB |
| 100 MB | 100 MB | ~4 KB |
| 1 GB | OOM | ~4 KB |

---

## Acceptance Criteria

- [ ] Large file downloads don't exhaust memory
- [ ] Stream ownership documented in API
- [ ] Tests verify streaming behavior
- [ ] Azure blob storage implementation compared for consistency

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle, pragmatic-dotnet-reviewer |
