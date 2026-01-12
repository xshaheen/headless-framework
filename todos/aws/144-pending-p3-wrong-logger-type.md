---
status: pending
priority: p3
issue_id: "144"
tags: [code-review, blobs, aws, logging]
dependencies: []
---

# Logger Created with Wrong Type Name

## Problem Statement

Logger created for `AwsBlobStorageOptions` instead of `AwsBlobStorage`.

## Findings

- **Location:** `src/Framework.Blobs.Aws/AwsBlobStorage.cs:46-47`
```csharp
_logger = _options.LoggerFactory?.CreateLogger<AwsBlobStorageOptions>()
    ?? NullLogger<AwsBlobStorageOptions>.Instance;
```

## Proposed Solutions

### Option 1: Fix Logger Type

```csharp
_logger = _options.LoggerFactory?.CreateLogger<AwsBlobStorage>()
    ?? NullLogger<AwsBlobStorage>.Instance;
```

**Effort:** 2 minutes | **Risk:** Low

## Acceptance Criteria

- [ ] Logger uses AwsBlobStorage type
