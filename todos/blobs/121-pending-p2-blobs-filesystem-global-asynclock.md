# Global AsyncLock Creates Serialization Bottleneck

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, concurrency, dotnet, blobs, filesystem

---

## Problem Statement

A single `AsyncLock` serializes ALL `RenameAsync` and `CopyAsync` operations across the entire storage instance. Completely unrelated file operations block each other.

```csharp
// FileSystemBlobStorage.cs:18
private readonly AsyncLock _lock = new();

// Used in RenameAsync (line 267) and CopyAsync (line 319)
using (await _lock.LockAsync(cancellationToken))
{
    // Only one rename/copy at a time across entire storage!
}
```

**Impact at Scale:**
- 100 concurrent rename operations to different files = effectively single-threaded
- Throughput collapses to O(1) operations at a time
- High latency spikes during contention
- Registered as singleton, so ALL consumers share the same lock

---

## Proposed Solutions

### Option A: Remove Lock Entirely (Recommended)
File system operations are already atomic at OS level. Use `File.Move` with overwrite parameter (.NET 5+):
```csharp
public async ValueTask<bool> RenameAsync(...)
{
    try
    {
        Directory.CreateDirectory(newDirectoryPath);
        File.Move(oldFullPath, newFullPath, overwrite: true);  // Atomic, no lock needed
        return true;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error renaming...");
        return false;
    }
}
```
- **Pros:** Maximum concurrency, simpler code, removes Nito.AsyncEx dependency
- **Cons:** Different semantics if overwriting is not desired
- **Effort:** Small
- **Risk:** Low

### Option B: Per-File Lock Striping
```csharp
private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

private async Task<IDisposable> _LockFileAsync(string path, CancellationToken ct)
{
    var lockObj = _locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
    await lockObj.WaitAsync(ct).AnyContext();
    return new LockReleaser(() => lockObj.Release());
}
```
- **Pros:** Only blocks same-file operations
- **Cons:** More complex, dictionary cleanup needed
- **Effort:** Medium
- **Risk:** Low

### Option C: Lock-Free with Retry
```csharp
public async ValueTask<bool> RenameAsync(...)
{
    for (int retry = 0; retry < 3; retry++)
    {
        try
        {
            File.Move(oldFullPath, newFullPath, overwrite: true);
            return true;
        }
        catch (IOException) when (retry < 2)
        {
            await Task.Delay(100 * (retry + 1), cancellationToken).AnyContext();
        }
    }
    return false;
}
```
- **Pros:** Handles transient failures, no lock
- **Cons:** May hide real errors
- **Effort:** Small
- **Risk:** Medium

---

## Recommended Action

**Option A** - Remove lock entirely. The file system handles concurrency. Use `File.Move(src, dest, overwrite: true)`.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 18, 267, 319)

**Why the lock was probably added:**
The original code has a try-catch retry pattern in `RenameAsync`:
```csharp
try { File.Move(...); }
catch (IOException) { File.Delete(newFullPath); File.Move(...); }
```
This TOCTOU pattern is what the lock protects. But `File.Move(..., overwrite: true)` eliminates the need.

---

## Acceptance Criteria

- [ ] Lock removed or changed to per-file
- [ ] Concurrent operations don't block each other
- [ ] File operations remain atomic
- [ ] Tests verify concurrent access works

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle, pragmatic-dotnet-reviewer |
