# Code Duplication in DeleteAllAsync - 4 Similar Blocks

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, duplication, dotnet, blobs, filesystem

---

## Problem Statement

`DeleteAllAsync` has 4 nearly identical code blocks for deleting directories:
- Lines 167-183: No pattern (delete entire container)
- Lines 189-208: Pattern ends with separator
- Lines 211-222: Pattern is a directory
- Lines 224-237: Pattern matches files

Each block does: check exists → log info → count files → delete → log trace.

```csharp
// Repeated pattern:
if (!Directory.Exists(directoryPath))
{
    return ValueTask.FromResult(0);
}

_logger.LogInformation("Deleting {Directory} directory", directoryPath);

var count = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories).Count();
Directory.Delete(directoryPath, recursive: true);

_logger.LogTrace("Finished deleting {Directory} directory with {FileCount} files", directoryPath, count);

return ValueTask.FromResult(count);
```

---

## Proposed Solutions

### Option A: Extract Helper Method (Recommended)
```csharp
private int _DeleteDirectoryWithLogging(string directoryPath)
{
    if (!Directory.Exists(directoryPath))
        return 0;

    _logger.LogInformation("Deleting {Directory} directory", directoryPath);

    // Note: Consider removing count for performance (see todo #126)
    var count = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories).Count();
    Directory.Delete(directoryPath, recursive: true);

    _logger.LogTrace("Finished deleting {Directory} with {FileCount} files", directoryPath, count);
    return count;
}
```
- **Pros:** DRY, easier maintenance
- **Cons:** Additional method
- **Effort:** Small
- **Risk:** None

---

## Recommended Action

**Option A** - Extract helper method. Estimated ~30 LOC reduction.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 157-238)

---

## Acceptance Criteria

- [ ] Duplication eliminated
- [ ] Behavior unchanged
- [ ] Single point of change for deletion logic

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - code-simplicity-reviewer |
