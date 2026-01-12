# Double File Enumeration in DeleteAllAsync

**Date:** 2026-01-11
**Status:** ready
**Priority:** P2 - Important
**Tags:** code-review, performance, dotnet, blobs, filesystem

---

## Problem Statement

`DeleteAllAsync` enumerates the directory TWICE - once to count files, then again by `Directory.Delete`. This is O(2n) when O(n) would suffice.

```csharp
// FileSystemBlobStorage.cs:177-178 (and similar at 203-204, 216-217)
var count = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories).Count();
Directory.Delete(directoryPath, recursive: true);
```

The count is only used for logging.

**Impact:** For directories with 10,000 files, this doubles I/O operations. On network shares or slow disks, this is significant.

---

## Proposed Solutions

### Option A: Delete First, Don't Count (Recommended)
```csharp
if (!Directory.Exists(directoryPath))
{
    return ValueTask.FromResult(0);
}

_logger.LogInformation("Deleting {Directory} directory", directoryPath);
Directory.Delete(directoryPath, recursive: true);
_logger.LogTrace("Finished deleting {Directory} directory", directoryPath);

return ValueTask.FromResult(-1);  // Or just don't return count
```
- **Pros:** O(n) instead of O(2n), simpler code
- **Cons:** No file count in logs (rarely useful anyway)
- **Effort:** Trivial
- **Risk:** None

### Option B: Count via Single Enumeration
```csharp
var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
var count = files.Length;  // Already enumerated
Directory.Delete(directoryPath, recursive: true);
```
- **Pros:** Still counts, single enumeration
- **Cons:** `GetFiles` allocates array (memory for large dirs)
- **Effort:** Trivial
- **Risk:** Low

---

## Recommended Action

**Option A** - Don't count. The count in logs provides little value and doubles the work.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 177-178, 203-204, 216-217)

---

## Acceptance Criteria

- [ ] Directory deletion is O(n) not O(2n)
- [ ] Logging still indicates deletion happened

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle |
