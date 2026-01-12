---
status: pending
priority: p1
issue_id: "136"
tags: [code-review, blobs, aws, data-integrity]
dependencies: []
---

# RenameAsync Non-Atomic - Copy+Delete Creates Data Duplication Risk

## Problem Statement

RenameAsync uses a copy-then-delete pattern without transactional guarantees. If delete fails after successful copy, blob exists in BOTH locations.

## Findings

- **Location:** `src/Framework.Blobs.Aws/AwsBlobStorage.cs:410-466`
```csharp
// Line 442: Copy succeeds
response = await _s3.CopyObjectAsync(request, cancellationToken).AnyContext();
// Line 463: Delete may fail after copy succeeded
var deleteResponse = await _s3.DeleteObjectAsync(deleteRequest, cancellationToken).AnyContext();
```
- **Data Corruption Scenario:**
  1. Copy succeeds
  2. Network error during delete
  3. Method returns `false` but blob exists in BOTH locations
  4. Retry creates duplicates

## Proposed Solutions

### Option 1: Return Detailed Result Object

Return result indicating partial success state (CopySucceeded, DeleteSucceeded).

**Effort:** 2 hours | **Risk:** Medium (breaking change)

### Option 2: Compensating Transaction

If delete fails, delete the copy to restore original state.

```csharp
if (!deleteResponse.HttpStatusCode.IsSuccessStatusCode())
{
    var compensate = new DeleteObjectRequest { BucketName = newBucket, Key = newKey };
    await _s3.DeleteObjectAsync(compensate, cancellationToken).AnyContext();
    return false;
}
```

**Effort:** 1 hour | **Risk:** Low

## Acceptance Criteria

- [ ] Partial success scenario handled
- [ ] No silent data duplication
- [ ] Behavior documented
