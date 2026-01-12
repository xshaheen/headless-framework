---
status: pending
priority: p2
issue_id: "139"
tags: [code-review, blobs, aws, performance]
dependencies: []
---

# BulkUploadAsync Unbounded Parallelism - Connection/Memory Exhaustion

## Problem Statement

BulkUploadAsync uses `Task.WhenAll` on lazy LINQ Select without throttling. For 1000 items, starts 1000 concurrent uploads.

## Findings

- **Location:** `src/Framework.Blobs.Aws/AwsBlobStorage.cs:136-150`
```csharp
var tasks = blobs.Select(async blob => { ... });
return await Task.WhenAll(tasks).WithAggregatedExceptions();
```
- Unbounded parallelism
- Combined with memory copy = 1000 MemoryStreams
- S3 may return 503 SlowDown

## Proposed Solutions

### Option 1: Use Parallel.ForEachAsync

```csharp
await Parallel.ForEachAsync(
    blobs,
    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
    async (blob, ct) => { ... }
).AnyContext();
```

**Effort:** 1 hour | **Risk:** Low

## Acceptance Criteria

- [ ] Concurrent uploads limited (4-10)
- [ ] No S3 rate limiting under normal usage
