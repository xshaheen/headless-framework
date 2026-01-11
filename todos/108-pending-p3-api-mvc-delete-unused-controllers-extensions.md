---
status: pending
priority: p3
issue_id: "108"
tags: [code-review, dotnet, api-mvc, cleanup, yagni]
dependencies: []
---

# Delete Unused ControllersExtensions.cs

## Problem Statement

`ControllersExtensions.cs` defines `ChallengeOrForbid` and `Redirect`/`LocalRedirect` with `escapeUrl` parameter. These methods have zero usages anywhere in the codebase - they're dead code.

## Findings

**Source:** code-simplicity-reviewer agent

**Location:** `src/Framework.Api.Mvc/Controllers/ControllersExtensions.cs` (70 LOC)

Methods defined but never used:
- `ChallengeOrForbid(this ControllerBase)` - 0 usages
- `ChallengeOrForbid(this ControllerBase, params string[])` - 0 usages
- `LocalRedirect(this ControllerBase, string, bool)` - 0 usages
- `Redirect(this ControllerBase, string, bool)` - 0 usages

## Proposed Solutions

### Option 1: Delete Entire File (Recommended)
**Pros:** Removes 70 LOC of dead code
**Cons:** Breaking change if external consumers use it
**Effort:** Small
**Risk:** Low (if no external consumers)

## Acceptance Criteria

- [ ] File deleted
- [ ] No build errors
- [ ] No external consumers affected (verify NuGet package usage)

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | YAGNI violation - delete unused code |

## Resources

- ControllersExtensions.cs
