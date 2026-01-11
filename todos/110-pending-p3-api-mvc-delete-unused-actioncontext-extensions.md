---
status: pending
priority: p3
issue_id: "110"
tags: [code-review, dotnet, api-mvc, cleanup, yagni]
dependencies: []
---

# Delete Unused ActionContextExtensions.cs

## Problem Statement

`ActionContextExtensions.cs` defines `GetRequiredService<T>` and `GetService<T>` extensions for `FilterContext` but neither are used anywhere.

## Findings

**Source:** code-simplicity-reviewer agent

**Location:** `src/Framework.Api.Mvc/Extensions/ActionContextExtensions.cs` (22 LOC)

Users can call `context.HttpContext.RequestServices.GetRequiredService<T>()` directly - the extension adds no value.

## Proposed Solutions

### Option 1: Delete Entire File (Recommended)
**Pros:** Removes 22 LOC of unnecessary abstraction
**Cons:** Breaking change if external consumers use it
**Effort:** Small
**Risk:** Low

## Acceptance Criteria

- [ ] File deleted
- [ ] No build errors

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Unnecessary convenience extensions |

## Resources

- ActionContextExtensions.cs
