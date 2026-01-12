---
status: pending
priority: p1
issue_id: "137"
tags: [code-review, blobs, aws, async, dotnet]
dependencies: []
---

# Missing AnyContext() on Multiple Async Calls - Deadlock Risk

## Problem Statement

Multiple async calls missing `.AnyContext()` (ConfigureAwait(false) wrapper). Can cause deadlocks when callers use `.Result` or `.Wait()`.

## Findings

- **Location:** `src/Framework.Blobs.Aws/AwsBlobStorage.cs` - 11+ locations

| Line | Method |
|------|--------|
| 56 | `_CreateBucketAsync` call |
| 61, 486 | `DoesS3BucketExistV2Async` |
| 67 | `PutBucketAsync` |
| 85 | `CreateContainerAsync` call |
| 89 | `stream.CopyToAsync` |
| 150 | `Task.WhenAll` |
| 176 | `DeleteObjectAsync` |
| 222 | `DeleteObjectsAsync` |
| 524 | `_ExistsAsync` call |
| 531 | `GetObjectAsync` |
| 571 | `GetObjectMetadataAsync` |

- Also missing: CancellationToken not passed to `DoesS3BucketExistV2Async`

## Proposed Solutions

### Option 1: Add AnyContext() to All Missing Calls

Systematically add `.AnyContext()` to all async calls.

**Effort:** 30 minutes | **Risk:** Low

## Acceptance Criteria

- [ ] All async calls have `.AnyContext()`
- [ ] All async calls pass CancellationToken where available
