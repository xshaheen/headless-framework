---
status: pending
priority: p3
issue_id: "145"
tags: [code-review, blobs, aws, di, logging]
dependencies: []
---

# LoggerFactory in Options Instead of DI Injection

## Problem Statement

LoggerFactory configured via options instead of DI injection. Non-standard pattern.

## Findings

- **Location:** `src/Framework.Blobs.Aws/AwsBlobStorageOptions.cs:16`
```csharp
public ILoggerFactory? LoggerFactory { get; set; }
```

## Proposed Solutions

### Option 1: Inject ILogger<AwsBlobStorage> Directly

```csharp
public AwsBlobStorage(
    IAmazonS3 s3,
    ...,
    ILogger<AwsBlobStorage>? logger = null)
{
    _logger = logger ?? NullLogger<AwsBlobStorage>.Instance;
}
```

**Effort:** 30 minutes | **Risk:** Low

## Acceptance Criteria

- [ ] LoggerFactory removed from options
- [ ] ILogger injected via DI
