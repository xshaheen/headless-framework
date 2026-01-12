# Code Duplication in BlobInfo Creation

**Date:** 2026-01-11
**Status:** ready
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, duplication, dotnet, blobs, filesystem

---

## Problem Statement

BlobInfo construction appears twice with nearly identical code:

```csharp
// GetBlobInfoAsync (lines 398-404)
var blobInfo = new BlobInfo
{
    BlobKey = Url.Combine([.. container.Skip(1).Append(blobName)]),
    Created = new DateTimeOffset(fileInfo.CreationTimeUtc, TimeSpan.Zero),
    Modified = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
    Size = fileInfo.Length,
};

// _GetFiles (lines 501-507)
var blobInfo = new BlobInfo
{
    BlobKey = blobKey,
    Created = new DateTimeOffset(fileInfo.CreationTimeUtc, TimeSpan.Zero),
    Modified = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
    Size = fileInfo.Length,
};
```

---

## Proposed Solutions

### Option A: Extract Factory Method (Recommended)
```csharp
private static BlobInfo _CreateBlobInfo(FileInfo fileInfo, string blobKey) => new()
{
    BlobKey = blobKey,
    Created = new DateTimeOffset(fileInfo.CreationTimeUtc, TimeSpan.Zero),
    Modified = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
    Size = fileInfo.Length,
};
```
- **Pros:** DRY, single point of change
- **Cons:** Additional method
- **Effort:** Trivial
- **Risk:** None

---

## Recommended Action

**Option A** - Extract factory method.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 398-404, 501-507)

---

## Acceptance Criteria

- [ ] Single factory method for BlobInfo creation
- [ ] Both usages updated to use factory

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pattern-recognition |
