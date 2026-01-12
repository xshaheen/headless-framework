# Stream Position Not Validated Before Upload

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, data-integrity, blobs-azure, input-validation

---

## Problem Statement

`UploadAsync` and `BulkUploadAsync` don't check or reset stream position before calling Azure SDK:

```csharp
await blobClient.UploadAsync(stream, httpHeader, metadata, cancellationToken: cancellationToken);
// If stream.Position > 0, only part of the content is uploaded!
```

**Why it matters:**
- If caller reads stream before upload, position is not at start
- Silent data loss - partial or empty blobs uploaded
- No error thrown
- Extremely hard to debug

---

## Proposed Solutions

### Option A: Reset Stream Position if Seekable
```csharp
if (stream.CanSeek && stream.Position != 0)
{
    stream.Seek(0, SeekOrigin.Begin);
}
```
- **Pros:** Handles common case
- **Cons:** Silently modifies input
- **Effort:** Small
- **Risk:** Low

### Option B: Throw if Position Not at Start
```csharp
if (stream.CanSeek && stream.Position != 0)
{
    throw new ArgumentException("Stream position must be at the beginning", nameof(stream));
}
```
- **Pros:** Explicit error, easier debugging
- **Cons:** Breaking change if callers rely on current behavior
- **Effort:** Small
- **Risk:** Medium

### Option C: Log Warning and Reset
```csharp
if (stream.CanSeek && stream.Position != 0)
{
    _logger.LogWarning("Stream position was {Position}, resetting to 0", stream.Position);
    stream.Seek(0, SeekOrigin.Begin);
}
```
- **Pros:** Visible issue, auto-corrects
- **Cons:** May mask caller bugs
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option C** - Log warning and reset. Prevents silent data loss while being backward compatible.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs` (lines 76-105, UploadAsync)

---

## Acceptance Criteria

- [ ] Stream position checked before upload
- [ ] Warning logged if position != 0
- [ ] Stream reset to 0 if seekable
- [ ] Non-seekable streams handled gracefully

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From data integrity review |
