# Redis Blob Storage Unbounded Parallelism in Bulk Operations

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, dotnet, redis, blobs

---

## Problem Statement

Bulk operations start ALL tasks immediately with no concurrency limit (lines 112-127, 188-200, 216-226):

```csharp
var tasks = blobs.Select(async blob => { ... });
return await Task.WhenAll(tasks).WithAggregatedExceptions();
```

**Problems:**
- 1000 blob upload = 1000 concurrent Redis operations
- Redis connection pool exhaustion
- Memory spike from all streams held simultaneously
- Potential timeout cascade

---

## Findings

**From performance-oracle:**
- No throttling mechanism
- Azure/FileSystem providers have similar issue

**From strict-dotnet-reviewer:**
- Same pattern in 3 locations: BulkUploadAsync, BulkDeleteAsync, DeleteAllAsync

---

## Proposed Solutions

### Option A: SemaphoreSlim Throttling
```csharp
private static readonly SemaphoreSlim _bulkSemaphore = new(maxDegreeOfParallelism: 10);

var tasks = blobs.Select(async blob =>
{
    await _bulkSemaphore.WaitAsync(cancellationToken);
    try { await UploadAsync(...); }
    finally { _bulkSemaphore.Release(); }
});
```
- **Pros:** Simple, configurable
- **Cons:** Fixed limit may not suit all deployments
- **Effort:** Small
- **Risk:** Low

### Option B: Parallel.ForEachAsync
```csharp
var results = new ConcurrentBag<Result<Exception>>();
await Parallel.ForEachAsync(blobs,
    new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = ct },
    async (blob, ct) => { results.Add(await TryUpload(blob, ct)); });
return results.ToList();
```
- **Pros:** .NET built-in, modern
- **Cons:** ConcurrentBag overhead
- **Effort:** Medium
- **Risk:** Low

### Option C: Configurable in Options
```csharp
public int MaxBulkParallelism { get; set; } = 10;
```
- **Pros:** User-tunable
- **Cons:** Adds option complexity
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A + C** - Add configurable limit via options, default to 10.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Redis/RedisBlobStorage.cs` (lines 112-127, 188-200, 216-226)
- `src/Framework.Blobs.Redis/RedisBlobStorageOptions.cs`

---

## Acceptance Criteria

- [ ] Add `MaxBulkParallelism` option (default 10)
- [ ] Apply throttling to BulkUploadAsync
- [ ] Apply throttling to BulkDeleteAsync
- [ ] Apply throttling to DeleteAllAsync
- [ ] Add load test verifying throttling works

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle, strict-dotnet-reviewer |
