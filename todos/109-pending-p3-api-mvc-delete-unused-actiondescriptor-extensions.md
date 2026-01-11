---
status: pending
priority: p3
issue_id: "109"
tags: [code-review, dotnet, api-mvc, cleanup, yagni]
dependencies: []
---

# Delete Unused ActionDescriptorExtensions.cs

## Problem Statement

`ActionDescriptorExtensions.cs` defines 6 extension methods for `ActionDescriptor` but none are used anywhere in the codebase.

## Findings

**Source:** code-simplicity-reviewer agent

**Location:** `src/Framework.Api.Mvc/Extensions/ActionDescriptorExtensions.cs` (56 LOC)

Unused methods:
- `AsControllerActionDescriptor`
- `GetMethodInfo`
- `GetReturnType`
- `IsControllerAction`
- `IsPageAction`
- `AsPageAction`

## Proposed Solutions

### Option 1: Delete Entire File (Recommended)
**Pros:** Removes 56 LOC of dead code
**Cons:** Breaking change if external consumers use it
**Effort:** Small
**Risk:** Low

## Acceptance Criteria

- [ ] File deleted
- [ ] No build errors

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Dead code removal |

## Resources

- ActionDescriptorExtensions.cs
