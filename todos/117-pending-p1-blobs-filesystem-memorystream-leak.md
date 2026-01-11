# MemoryStream Leak on Exception in CopyToMemoryStreamAndFlushAsync

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, memory-leak, dotnet, blobs, filesystem

---

## Problem Statement

The `CopyToMemoryStreamAndFlushAsync` helper creates a `MemoryStream` but does not dispose it on exception. If `CopyToAsync` or `FlushAsync` throws, the `MemoryStream` leaks.

```csharp
// Internals/Helpers.cs:7-25
internal static async ValueTask<Stream?> CopyToMemoryStreamAndFlushAsync(
    this Stream? stream,
    CancellationToken token = default
)
{
    if (stream is null) return null;

    var memoryStream = new MemoryStream();

    await stream.CopyToAsync(memoryStream, token);  // If this throws, memoryStream leaks
    memoryStream.Seek(0, SeekOrigin.Begin);

    await stream.FlushAsync(token);  // If this throws, memoryStream leaks

    return memoryStream;
}
```

**Why it matters:**
- Memory leaks under failure conditions (cancellation, IO errors)
- For large files, significant memory wasted
- No cleanup mechanism if exception occurs

---

## Proposed Solutions

### Option A: Try-Catch with Cleanup (Recommended)
```csharp
internal static async ValueTask<Stream?> CopyToMemoryStreamAndFlushAsync(
    this Stream? stream,
    CancellationToken token = default
)
{
    if (stream is null) return null;

    var memoryStream = new MemoryStream();
    try
    {
        await stream.CopyToAsync(memoryStream, token).AnyContext();
        memoryStream.Seek(0, SeekOrigin.Begin);
        return memoryStream;
    }
    catch
    {
        await memoryStream.DisposeAsync().AnyContext();
        throw;
    }
}
```
- **Pros:** Clean disposal on failure, maintains existing contract
- **Cons:** Slightly more code
- **Effort:** Trivial
- **Risk:** None

### Option B: Inline into DownloadAsync
This helper is only used once. Inline it and use proper disposal:
```csharp
public async ValueTask<BlobDownloadResult?> DownloadAsync(...)
{
    var filePath = _BuildBlobPath(container, blobName);
    if (!File.Exists(filePath)) return null;

    await using var fileStream = File.OpenRead(filePath);
    var memoryStream = new MemoryStream();
    try
    {
        await fileStream.CopyToAsync(memoryStream, cancellationToken).AnyContext();
        memoryStream.Seek(0, SeekOrigin.Begin);
        return new BlobDownloadResult(memoryStream, Path.GetFileName(filePath));
    }
    catch
    {
        await memoryStream.DisposeAsync().AnyContext();
        throw;
    }
}
```
- **Pros:** Removes unnecessary helper file, clearer ownership
- **Cons:** Slightly longer method
- **Effort:** Small
- **Risk:** None

---

## Recommended Action

**Option B** - Inline and remove Helpers.cs. The helper is only used once and adds indirection.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/Internals/Helpers.cs` (entire file)
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (line 371)

**Note:** The `FlushAsync` call on line 22 is also unnecessary - flushing a source stream after reading has no effect.

---

## Acceptance Criteria

- [ ] MemoryStream disposed on exception
- [ ] Unnecessary FlushAsync removed
- [ ] Helpers.cs deleted (if Option B chosen)
- [ ] Tests verify no memory leak on cancellation

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, pattern-recognition |
