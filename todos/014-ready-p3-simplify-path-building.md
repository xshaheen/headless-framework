---
status: ready
priority: p3
issue_id: "014"
tags: [code-review, simplification, performance]
dependencies: []
---

# Simplify Path Building Methods

## Problem Statement

`_BuildBlobPath` and `_BuildContainerPath` use StringBuilder for simple string joins. This adds unnecessary allocations.

**Why it matters:** Code simplicity, minor performance improvement.

## Findings

### From simplicity-reviewer:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs:796-830`
- StringBuilder overkill for 1-3 string segments
- 26 LOC can become 2 LOC

Current:
```csharp
var sb = new StringBuilder();
sb.AppendJoin('/', container);
if (!string.IsNullOrEmpty(blobName)) { sb.Append('/'); }
sb.Append(blobName);
return sb.ToString();
```

Proposed:
```csharp
return container.Length == 0 ? blobName : $"{string.Join('/', container)}/{blobName}";
```

## Proposed Solutions

### Option A: Use string interpolation (Recommended)
Replace StringBuilder with `string.Join` and interpolation.

**Pros:** Simpler, ~50% fewer allocations
**Cons:** None
**Effort:** Trivial
**Risk:** Very Low

## Recommended Action

Option A: Use string interpolation

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (lines 796-830)

## Acceptance Criteria

- [ ] Path building uses simpler approach
- [ ] All tests pass
- [ ] No behavior change

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via simplicity review | StringBuilder has overhead for small strings |

## Resources

- String interpolation docs
