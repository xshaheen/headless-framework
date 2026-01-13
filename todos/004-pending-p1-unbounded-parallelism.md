---
status: pending
priority: p1
issue_id: "004"
tags: [code-review, performance, critical, scalability]
dependencies: ["001"]
---

# Unbounded Parallelism in Bulk Operations

## Problem Statement

`BulkUploadAsync` and `BulkDeleteAsync` spawn unbounded parallel tasks via `Task.WhenAll`. With 1000 blobs, this launches 1000 concurrent operations over a single SSH connection.

**Why it matters:** Combined with thread-safety issues (#001), this WILL cause failures under load. Even with thread-safety fixed, unbounded parallelism overwhelms the SSH connection.

## Findings

### From strict-dotnet-reviewer:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs:131-145`
```csharp
var tasks = blobs.Select(async blob =>
{
    await UploadAsync(container, blob.FileName, blob.Stream, ...);
});
return await Task.WhenAll(tasks).WithAggregatedExceptions();
```

### From performance-oracle:
- SSH/SFTP clients have limited concurrent channel capacity
- 1,000 files: Connection exhaustion, SSH server rejection
- 10,000 files: Near-certain failure, memory pressure

## Proposed Solutions

### Option A: SemaphoreSlim throttling (Recommended)
Limit concurrent operations with `SemaphoreSlim`.

```csharp
private static readonly SemaphoreSlim _uploadThrottle = new(maxConcurrency: 4);

var tasks = blobs.Select(async blob =>
{
    await _uploadThrottle.WaitAsync(cancellationToken);
    try
    {
        await UploadAsync(...);
        return Result<Exception>.Ok();
    }
    finally { _uploadThrottle.Release(); }
});
```

**Pros:** Simple, effective
**Cons:** Fixed concurrency limit
**Effort:** Small
**Risk:** Low

### Option B: Parallel.ForEachAsync with MaxDegreeOfParallelism
Use built-in .NET API.

**Pros:** .NET standard approach
**Cons:** Slightly different API surface
**Effort:** Small
**Risk:** Low

## Recommended Action

<!-- Fill after triage -->

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (lines 131-145, 203-215)

## Acceptance Criteria

- [ ] Bulk upload with 1000 files completes successfully
- [ ] No more than N concurrent SFTP operations (configurable)
- [ ] Memory usage stays bounded during bulk operations
- [ ] Error handling still aggregates failures properly

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via performance review | SSH.NET has ~10-20 channel limit typically |

## Resources

- Parallel.ForEachAsync docs: https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.parallel.foreachasync
