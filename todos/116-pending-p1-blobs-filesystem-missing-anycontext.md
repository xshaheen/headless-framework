# Missing AnyContext() on Async Calls

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, async, dotnet, blobs, filesystem

---

## Problem Statement

Per project conventions, all async calls must use `.AnyContext()` (replaces `ConfigureAwait(false)`). Multiple async awaits in FileSystemBlobStorage are missing this:

**FileSystemBlobStorage.cs:**
- Line 62: `await stream.SaveToLocalFileAsync(...)` - missing
- Line 85-87: `await blobs.Select(...).SaveToLocalFileAsync(...)` - missing
- Line 267: `await _lock.LockAsync(cancellationToken)` - missing
- Line 319: `await _lock.LockAsync(cancellationToken)` - missing
- Line 371: `await fileStream.CopyToMemoryStreamAndFlushAsync(...)` - missing

**Internals/Helpers.cs:**
- Line 19: `await stream.CopyToAsync(memoryStream, token)` - missing
- Line 22: `await stream.FlushAsync(token)` - missing

Only line 453 correctly uses `.AnyContext()`.

**Why it matters:**
- Potential deadlocks in synchronous-over-async contexts
- Thread pool starvation from unnecessary sync context capture
- Inconsistent with codebase conventions (CLAUDE.md explicitly requires this)

---

## Proposed Solutions

### Option A: Add AnyContext() to All Async Calls (Recommended)
```csharp
// Before
await stream.SaveToLocalFileAsync(blobName, directoryPath, cancellationToken);

// After
await stream.SaveToLocalFileAsync(blobName, directoryPath, cancellationToken)
    .AnyContext();
```
- **Pros:** Consistent with codebase, prevents deadlocks
- **Cons:** None
- **Effort:** Trivial
- **Risk:** None

---

## Recommended Action

**Option A** - Add `.AnyContext()` to all 7 locations. This is a mechanical fix.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 62, 85-87, 267, 319, 371)
- `src/Framework.Blobs.FileSystem/Internals/Helpers.cs` (lines 19, 22)

---

## Acceptance Criteria

- [ ] All async awaits use `.AnyContext()`
- [ ] Codebase grep confirms no missing calls
- [ ] Build passes

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
