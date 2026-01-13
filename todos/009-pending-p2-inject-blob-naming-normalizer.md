---
status: pending
priority: p2
issue_id: "009"
tags: [code-review, architecture, consistency]
dependencies: ["002"]
---

# Inject and Use IBlobNamingNormalizer

## Problem Statement

`IBlobNamingNormalizer` is registered in DI but NOT injected or used by `SshBlobStorage`. This is inconsistent with FileSystem implementation and may contribute to path traversal vulnerability.

**Why it matters:** Inconsistent behavior, potential security gap, code not following established patterns.

## Findings

### From pattern-recognition-specialist:
- **File:** `src/Framework.Blobs.SshNet/SshSetup.cs:37`
```csharp
services.TryAddSingleton<IBlobNamingNormalizer, CrossOsNamingNormalizer>();
```
Registered but never injected into SshBlobStorage.

### From architecture-strategist:
- FileSystem implementation uses normalizer for path sanitization
- SshNet skips this step entirely

## Proposed Solutions

### Option A: Inject and use normalizer (Recommended)
Add to constructor and use in path building.

```csharp
public sealed class SshBlobStorage(
    IOptions<SshBlobStorageOptions> optionsAccessor,
    IBlobNamingNormalizer normalizer,
    ILogger<SshBlobStorage> logger
) : IBlobStorage
```

**Pros:** Consistent, helps with security
**Cons:** Slight behavior change
**Effort:** Small
**Risk:** Low

## Recommended Action

<!-- Fill after triage -->

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (constructor, path methods)

## Acceptance Criteria

- [ ] Normalizer injected via constructor
- [ ] Blob names normalized before use
- [ ] Consistent with FileSystem implementation
- [ ] Tests pass

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via pattern review | Already registered, just not used |

## Resources

- FileSystem implementation for reference
