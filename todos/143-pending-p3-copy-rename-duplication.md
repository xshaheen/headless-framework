---
status: pending
priority: p3
issue_id: "143"
tags: [code-review, blobs, aws, code-quality]
dependencies: ["138"]
---

# CopyAsync and RenameAsync Code Duplication

## Problem Statement

RenameAsync duplicates 90% of CopyAsync code. ~25 lines of duplicated code.

## Findings

- **Location:**
  - `src/Framework.Blobs.Aws/AwsBlobStorage.cs:364-404` (CopyAsync)
  - `src/Framework.Blobs.Aws/AwsBlobStorage.cs:410-466` (RenameAsync)

## Proposed Solutions

### Option 1: RenameAsync Calls CopyAsync

```csharp
public async ValueTask<bool> RenameAsync(...)
{
    if (!await CopyAsync(blobContainer, blobName, newBlobContainer, newBlobName, cancellationToken))
        return false;

    var (oldBucket, oldKey) = _BuildObjectKey(blobName, blobContainer);
    var deleteRequest = new DeleteObjectRequest { BucketName = oldBucket, Key = oldKey };
    var response = await _s3.DeleteObjectAsync(deleteRequest, cancellationToken).AnyContext();
    return response.HttpStatusCode.IsSuccessStatusCode();
}
```

**Effort:** 30 minutes | **Risk:** Low

## Acceptance Criteria

- [ ] RenameAsync uses CopyAsync
- [ ] ~25 LOC reduction
