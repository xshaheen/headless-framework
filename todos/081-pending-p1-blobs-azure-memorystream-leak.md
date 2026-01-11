# MemoryStream Leak in DownloadAsync

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, dotnet, blobs-azure, memory-leak, resource-disposal

---

## Problem Statement

In `DownloadAsync` (lines 324-347), a `MemoryStream` is created but never disposed when exceptions occur.

```csharp
var memoryStream = new MemoryStream();
try
{
    await blobClient.DownloadToAsync(memoryStream, cancellationToken);
}
catch (RequestFailedException e)
    when (e.ErrorCode == BlobErrorCode.BlobNotFound || e.ErrorCode == BlobErrorCode.ContainerNotFound)
{
    return null;  // MemoryStream NOT disposed!
}
```

**Why it matters:**
- Memory leak on every failed download attempt
- If exception is NOT 404 (e.g., throttling, network error), stream is also leaked
- Accumulates GC pressure in high-frequency scenarios

---

## Proposed Solutions

### Option A: Dispose in Catch Block
```csharp
catch (RequestFailedException e) when (...)
{
    memoryStream.Dispose();
    return null;
}
```
- **Pros:** Simple fix
- **Cons:** Must handle all catch paths
- **Effort:** Small
- **Risk:** Low

### Option B: Use await using with Conditional Return
```csharp
await using var memoryStream = new MemoryStream();
try { ... }
catch (...) { return null; }
memoryStream.Seek(0, SeekOrigin.Begin);
return new(memoryStream, blobName);  // Problem: stream disposed after return
```
- **Cons:** Doesn't work - stream disposed before caller uses it
- **Effort:** N/A
- **Risk:** N/A

### Option C: Restructure with Explicit Ownership
```csharp
MemoryStream? memoryStream = null;
try
{
    memoryStream = new MemoryStream();
    await blobClient.DownloadToAsync(memoryStream, cancellationToken);
    memoryStream.Seek(0, SeekOrigin.Begin);
    return new(memoryStream, blobName);
}
catch (RequestFailedException e) when (...)
{
    memoryStream?.Dispose();
    return null;
}
catch
{
    memoryStream?.Dispose();
    throw;
}
```
- **Pros:** Handles all exception paths
- **Cons:** More verbose
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option C** - Explicit ownership with disposal in all catch blocks.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs` (lines 324-347)

---

## Acceptance Criteria

- [ ] MemoryStream disposed on BlobNotFound
- [ ] MemoryStream disposed on ContainerNotFound
- [ ] MemoryStream disposed on any other exception
- [ ] Happy path unchanged

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
