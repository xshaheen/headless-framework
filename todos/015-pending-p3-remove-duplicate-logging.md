---
status: pending
priority: p3
issue_id: "015"
tags: [code-review, cleanup]
dependencies: []
---

# Remove Duplicate Logging Statements

## Problem Statement

Several log statements appear twice in the same operation flow.

**Why it matters:** Log noise, disk space.

## Findings

### From simplicity-reviewer:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs`
- Line 85 and 105: "Saving {Path}" logged twice in upload
- Line 338 and 364: "Renaming {Path} to {NewPath}" logged twice in rename

## Proposed Solutions

### Option A: Remove duplicates (Recommended)
Keep only one log per operation.

**Effort:** Trivial
**Risk:** None

## Recommended Action

<!-- Fill after triage -->

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (lines 85, 105, 338, 364)

## Acceptance Criteria

- [ ] No duplicate log messages in same operation
- [ ] Key operations still logged

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via code review | |

## Resources

- N/A
