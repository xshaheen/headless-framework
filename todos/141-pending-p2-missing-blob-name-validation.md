---
status: pending
priority: p2
issue_id: "141"
tags: [code-review, blobs, aws, security]
dependencies: []
---

# Missing Blob Name Validation - Inconsistent with FileSystem Provider

## Problem Statement

AWS implementation does NOT validate blob names for `../` patterns, unlike FileSystem provider. Creates API inconsistency.

## Findings

- **Location:** `src/Framework.Blobs.Aws/AwsBlobNamingNormalizer.cs:57-60`
```csharp
public string NormalizeBlobName(string blobName)
{
    return blobName;  // No validation!
}
```
- S3 stores `foo/../bar` literally (not resolved)
- Different behavior between FileSystem (dev) and AWS (prod)

## Proposed Solutions

### Option 1: Add Validation in NormalizeBlobName

```csharp
public string NormalizeBlobName(string blobName)
{
    if (blobName.Contains("..") || Path.IsPathRooted(blobName))
        throw new ArgumentException("Invalid blob name");
    return blobName;
}
```

**Effort:** 30 minutes | **Risk:** Low

## Acceptance Criteria

- [ ] Blob names with `..` rejected
- [ ] Consistent behavior with FileSystem provider
