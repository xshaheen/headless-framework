---
status: pending
priority: p2
issue_id: "011"
tags: [code-review, api-design, documentation]
dependencies: []
---

# Metadata Parameter Accepted But Ignored

## Problem Statement

`UploadAsync` accepts a `metadata` parameter but SFTP doesn't support blob metadata. The parameter is silently ignored, creating API dishonesty.

**Why it matters:** Callers may expect metadata to be stored and retrieved, but it's discarded.

## Findings

### From pragmatic-dotnet-reviewer:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs:76`
```csharp
public async ValueTask UploadAsync(
    ...
    Dictionary<string, string?>? metadata = null,  // Never used
    ...
)
```

## Proposed Solutions

### Option A: Throw NotSupportedException if metadata provided
```csharp
if (metadata?.Count > 0)
    throw new NotSupportedException("SFTP does not support blob metadata.");
```

**Pros:** Clear behavior, fails fast
**Cons:** Breaking change for callers passing metadata
**Effort:** Trivial
**Risk:** Medium (breaking)

### Option B: Document that metadata is ignored
Add XML doc warning.

**Pros:** No breaking change
**Cons:** Silently loses data
**Effort:** Trivial
**Risk:** Low

### Option C: Store metadata in sidecar file
Create `{blobName}.metadata.json` alongside blob.

**Pros:** Full metadata support
**Cons:** Complex, extra files
**Effort:** Medium
**Risk:** Medium

## Recommended Action

<!-- Fill after triage -->

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (line 76)

## Acceptance Criteria

- [ ] Clear documentation or exception for metadata
- [ ] No silent data loss

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via code review | API contract violation |

## Resources

- Interface definition in IBlobStorage.cs
