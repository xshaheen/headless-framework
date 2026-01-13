---
status: pending
priority: p3
issue_id: "012"
tags: [code-review, code-style, cleanup]
dependencies: []
---

# Remove #region Tags

## Problem Statement

18+ `#region` blocks add visual noise with zero value. Regions are an anti-pattern that hide poor organization.

**Why it matters:** Code cleanliness, modern C# style.

## Findings

### From simplicity-reviewer:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs`
- 18 region blocks throughout file
- Adds ~36 lines of noise

### From pragmatic-dotnet-reviewer:
- "If you need this many regions, the class wants to be smaller"

## Proposed Solutions

### Option A: Remove all regions (Recommended)
Simple find-and-delete.

**Pros:** Cleaner code
**Cons:** Large diff
**Effort:** Trivial
**Risk:** None

## Recommended Action

<!-- Fill after triage -->

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (all #region/#endregion lines)

## Acceptance Criteria

- [ ] No #region tags remain
- [ ] Code still compiles
- [ ] Methods remain logically grouped

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via code review | Modern C# discourages regions |

## Resources

- Modern C# style guides
