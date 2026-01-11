# BulkUploadAsync Has Unbounded Parallelism

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, blobs-azure, concurrency, scalability

---

## Problem Statement

`BulkUploadAsync` fires all uploads concurrently with no throttling:

```csharp
var tasks = blobs.Select(async blob =>
{
    try
    {
        await UploadAsync(container, blob.FileName, blob.Stream, blob.Metadata, cancellationToken);
        return Result<Exception>.Ok();
    }
    catch (Exception e) { return Result<Exception>.Fail(e); }
});
return await Task.WhenAll(tasks).WithAggregatedExceptions();
```

**Why it matters:**
- With 10,000 blobs, starts 10,000 concurrent HTTP connections
- Connection pool exhaustion at ~100+ concurrent uploads
- Azure Storage rate limiting kicks in
- Memory pressure from concurrent stream buffers
- May cause OutOfMemoryException with large files

---

## Proposed Solutions

### Option A: Use Parallel.ForEachAsync with MaxDegreeOfParallelism
```csharp
var options = new ParallelOptions
{
    MaxDegreeOfParallelism = 10,  // or configurable
    CancellationToken = cancellationToken
};
await Parallel.ForEachAsync(blobs, options, async (blob, ct) => ...);
```
- **Pros:** Built-in, efficient
- **Cons:** .NET 6+ required (already using .NET 10)
- **Effort:** Small
- **Risk:** Low

### Option B: SemaphoreSlim Throttling
```csharp
using var semaphore = new SemaphoreSlim(10);
var tasks = blobs.Select(async blob =>
{
    await semaphore.WaitAsync(cancellationToken);
    try { ... }
    finally { semaphore.Release(); }
});
```
- **Pros:** Flexible
- **Cons:** More boilerplate
- **Effort:** Small
- **Risk:** Low

### Option C: Make MaxDegreeOfParallelism Configurable
Add to `AzureStorageOptions`:
```csharp
public int MaxBulkParallelism { get; set; } = 10;
```
- **Pros:** User can tune for their workload
- **Cons:** More options to manage
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A + C** - Use `Parallel.ForEachAsync` with configurable parallelism.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs` (lines 111-136)
- `src/Framework.Blobs.Azure/AzureStorageOptions.cs`

---

## Acceptance Criteria

- [ ] BulkUploadAsync limits concurrent uploads
- [ ] Default parallelism is reasonable (e.g., 10)
- [ ] Parallelism is configurable via options
- [ ] Large bulk uploads don't exhaust connections

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From performance review |
