---
status: pending
priority: p2
issue_id: "142"
tags: [code-review, blobs, aws, security]
dependencies: []
---

# Default ACL is Null - Potential Public Exposure Risk

## Problem Statement

When `CannedAcl` is null (default), S3 uses bucket defaults. If bucket misconfigured, uploads become public.

## Findings

- **Location:** `src/Framework.Blobs.Aws/AwsBlobStorageOptions.cs:14`
```csharp
public S3CannedACL? CannedAcl { get; set; }  // Nullable, no default
```

## Proposed Solutions

### Option 1: Default to S3CannedACL.Private

```csharp
public S3CannedACL CannedAcl { get; set; } = S3CannedACL.Private;
```

**Effort:** 5 minutes | **Risk:** Low

## Acceptance Criteria

- [ ] CannedAcl defaults to Private
- [ ] Documentation updated
