# Race Condition (TOCTOU) in RenameAsync

**Date:** 2026-01-11
**Status:** ready
**Priority:** P2 - Important
**Tags:** code-review, concurrency, dotnet, blobs, filesystem

---

## Problem Statement

The rename operation has a Time-Of-Check-Time-Of-Use (TOCTOU) vulnerability. Between `File.Delete` and the retry `File.Move`, another process could create a file at the target path.

```csharp
// FileSystemBlobStorage.cs:271-280
try
{
    File.Move(oldFullPath, newFullPath);
}
catch (IOException)
{
    File.Delete(newFullPath);  // Delete existing file
    _logger.LogTrace("Renaming {Path} to {NewPath}", oldFullPath, newFullPath);
    File.Move(oldFullPath, newFullPath);  // Another process could recreate file here!
}
```

**Impact:** Potential data loss or inconsistent state in concurrent environments.

---

## Proposed Solutions

### Option A: Use File.Move with Overwrite (Recommended)
.NET 5+ supports overwrite parameter:
```csharp
File.Move(oldFullPath, newFullPath, overwrite: true);  // Atomic, no TOCTOU
```
- **Pros:** Atomic operation, no race window, simpler code
- **Cons:** Different semantics (always overwrites)
- **Effort:** Trivial
- **Risk:** None (if overwrite behavior is desired)

### Option B: Check Overwrite Intent Explicitly
```csharp
public async ValueTask<bool> RenameAsync(
    ...,
    bool overwrite = false,  // New parameter
    ...)
{
    if (overwrite)
    {
        File.Move(oldFullPath, newFullPath, overwrite: true);
    }
    else if (File.Exists(newFullPath))
    {
        throw new IOException($"Target file already exists: {newFullPath}");
    }
    else
    {
        File.Move(oldFullPath, newFullPath);
    }
}
```
- **Pros:** Explicit control over behavior
- **Cons:** Interface change
- **Effort:** Medium
- **Risk:** Medium - breaking change

---

## Recommended Action

**Option A** - Use `File.Move` with overwrite. The current code already deletes existing files, so overwrite behavior is intended.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 271-280)

---

## Acceptance Criteria

- [ ] No TOCTOU race window
- [ ] Rename with existing target works atomically
- [ ] Tests verify concurrent rename safety

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - security-sentinel, strict-dotnet-reviewer |
