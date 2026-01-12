---
status: pending
priority: p1
issue_id: "138"
tags: [code-review, blobs, aws, data-integrity]
dependencies: []
---

# CopyAsync/RenameAsync Loses Metadata - MetadataDirective Missing

## Problem Statement

CopyObjectRequest doesn't set `MetadataDirective = S3MetadataDirective.COPY`. Custom metadata silently lost including `uploadDate` and `extension`.

## Findings

- **Location:** `src/Framework.Blobs.Aws/AwsBlobStorage.cs:383-390` (CopyAsync), `429-436` (RenameAsync)
```csharp
var request = new CopyObjectRequest
{
    CannedACL = _options.CannedAcl,
    SourceBucket = oldBucket,
    SourceKey = oldKey,
    DestinationBucket = newBucket,
    DestinationKey = newKey,
    // Missing: MetadataDirective = S3MetadataDirective.COPY
};
```

## Proposed Solutions

### Option 1: Add MetadataDirective.COPY

```csharp
MetadataDirective = S3MetadataDirective.COPY,
```

**Effort:** 15 minutes | **Risk:** Low

## Acceptance Criteria

- [ ] CopyObjectRequest includes MetadataDirective.COPY
- [ ] Metadata preserved after copy/rename
- [ ] Integration test verifies metadata round-trip
