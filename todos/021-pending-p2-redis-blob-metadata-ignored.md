# Redis Blob Storage Metadata Parameter Silently Ignored

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, api-design, dotnet, redis, blobs

---

## Problem Statement

`UploadAsync` accepts `metadata` parameter (line 51) but never stores it:

```csharp
public async ValueTask UploadAsync(
    string[] container,
    string blobName,
    Stream stream,
    Dictionary<string, string?>? metadata = null,  // ACCEPTED
    CancellationToken cancellationToken = default
)
{
    // ...
    var blobInfo = new BlobInfo
    {
        BlobKey = blobPath,
        Created = DateTimeOffset.UtcNow,
        Modified = DateTimeOffset.UtcNow,
        Size = fileSize,
        // NO METADATA STORED
    };
}
```

And `CopyAsync` explicitly passes `metadata: null` (line 303).

**Problems:**
- Violates Liskov Substitution Principle
- Callers expect metadata storage (works in Azure/AWS providers)
- Silent data loss - no warning, no exception

---

## Findings

**From architecture-strategist:**
- Azure provider stores metadata
- Redis implementation discards it silently

**From simplicity-reviewer:**
- Either implement or remove from this provider

---

## Proposed Solutions

### Option A: Store Metadata in BlobInfo
```csharp
var blobInfo = new BlobInfo
{
    BlobKey = blobPath,
    Created = DateTimeOffset.UtcNow,
    Modified = DateTimeOffset.UtcNow,
    Size = fileSize,
    Metadata = metadata ?? new Dictionary<string, string?>(),
};
```
- **Pros:** Feature parity with other providers
- **Cons:** Requires BlobInfo schema change
- **Effort:** Medium
- **Risk:** Low

### Option B: Throw NotSupportedException
```csharp
if (metadata is { Count: > 0 })
    throw new NotSupportedException("Redis blob storage does not support custom metadata");
```
- **Pros:** Explicit failure, no silent data loss
- **Cons:** Breaking for consumers using metadata
- **Effort:** Small
- **Risk:** Medium

### Option C: Document Limitation
- Add warning in README and XML docs
- **Pros:** Non-breaking
- **Cons:** Silent failure continues
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Implement proper metadata storage for feature parity.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Redis/RedisBlobStorage.cs` (lines 47-97, 303)
- `src/Framework.Blobs.Abstractions/Contracts/BlobInfo.cs` (may need Metadata property)

---

## Acceptance Criteria

- [ ] Metadata stored in BlobInfo
- [ ] Metadata preserved on Copy operation
- [ ] Metadata returned in GetBlobInfoAsync
- [ ] Add test for metadata round-trip

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - architecture-strategist |
