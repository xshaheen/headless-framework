# Silent Exception Swallowing in UploadAsync

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, error-handling, dotnet, blobs, filesystem

---

## Problem Statement

`UploadAsync` catches all exceptions and only logs them - does not rethrow or indicate failure to caller. The method returns `ValueTask` (void), so callers have no way to know the upload failed.

```csharp
// FileSystemBlobStorage.cs:60-68
try
{
    await stream.SaveToLocalFileAsync(blobName, directoryPath, cancellationToken);
}
catch (Exception e)
{
    _logger.LogError(e, "Error uploading {BlobName} to {DirectoryPath}", blobName, directoryPath);
    // Returns normally - caller thinks upload succeeded!
}
```

**Why it matters:**
- Data loss goes undetected - callers proceed believing file was saved when it wasn't
- User thinks their document is safely stored, but it's not
- 3am debugging nightmare - logs show errors but no way to correlate to failed operations
- Inconsistent with `BulkUploadAsync` which returns `Result<Exception>`

---

## Proposed Solutions

### Option A: Let Exception Propagate (Recommended)
```csharp
public async ValueTask UploadAsync(...)
{
    // Remove try-catch entirely
    await stream.SaveToLocalFileAsync(blobName, directoryPath, cancellationToken)
        .AnyContext();
}
```
- **Pros:** Clear failure semantics, matches interface contract expectations
- **Cons:** Breaking change if callers relied on silent failure (unlikely)
- **Effort:** Trivial
- **Risk:** Low

### Option B: Return Result Type
```csharp
public async ValueTask<Result<Exception>> UploadAsync(...)
{
    try
    {
        await stream.SaveToLocalFileAsync(blobName, directoryPath, cancellationToken)
            .AnyContext();
        return Result<Exception>.Success();
    }
    catch (Exception e)
    {
        _logger.LogError(e, "Error uploading...");
        return Result<Exception>.Fail(e);
    }
}
```
- **Pros:** Matches `BulkUploadAsync` pattern, explicit failure handling
- **Cons:** Breaking change to interface `IBlobStorage`
- **Effort:** Medium (interface change)
- **Risk:** Medium

### Option C: Return Boolean Success Indicator
```csharp
public async ValueTask<bool> UploadAsync(...)
```
- **Pros:** Simple success/failure indication
- **Cons:** No exception details, interface change required
- **Effort:** Medium
- **Risk:** Medium

---

## Recommended Action

**Option A** - Let exception propagate. This is the simplest fix and matches expected behavior. The interface contract doesn't promise silent failure handling.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 60-68)

**Comparison with other methods:**
| Method | Error Handling |
|--------|----------------|
| `UploadAsync` | Logs, swallows, returns void ❌ |
| `BulkUploadAsync` | Returns `Result<Exception>` ✓ |
| `DeleteAsync` | No try-catch, may throw ✓ |
| `BulkDeleteAsync` | Returns `Result<bool, Exception>` ✓ |
| `RenameAsync` | Logs, returns false ⚠️ |
| `CopyAsync` | Logs, returns false ⚠️ |

---

## Acceptance Criteria

- [ ] Upload failures propagate exception to caller
- [ ] Error is still logged for diagnostics
- [ ] Tests verify exception propagation
- [ ] Azure blob storage implementation has consistent behavior

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - multiple agents flagged this |
