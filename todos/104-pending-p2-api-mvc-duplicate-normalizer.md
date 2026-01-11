---
status: pending
priority: p2
issue_id: "104"
tags: [code-review, dotnet, api-mvc, architecture, duplication]
dependencies: []
---

# Duplicate MvcProblemDetailsNormalizer vs ProblemDetailsCreator.Normalize()

## Problem Statement

Two separate normalizers exist with overlapping but divergent functionality:
- `MvcProblemDetailsNormalizer` adds traceId, applies ClientErrorMapping, invokes CustomizeProblemDetails
- `ProblemDetailsCreator.Normalize()` does all of the above PLUS buildNumber, commitNumber, timestamp, instance

This is technical debt - the MVC normalizer is a subset of `ProblemDetailsCreator`.

## Findings

**Source:** pragmatic-dotnet-reviewer, architecture-strategist agents

**Files:**
- `src/Framework.Api.Mvc/Controllers/MvcProblemDetailsNormalizer.cs` (32 lines)
- `src/Framework.Api/Abstractions/IProblemDetailsCreator.cs:151-206` (has `Normalize()`)

| Feature | MvcProblemDetailsNormalizer | ProblemDetailsCreator.Normalize() |
|---------|----------------------------|----------------------------------|
| Sets Title from ClientErrorMapping | Yes | Yes |
| Sets Type from ClientErrorMapping | Yes | Yes |
| Adds traceId | Yes | Yes |
| Adds buildNumber | No | Yes |
| Adds commitNumber | No | Yes |
| Adds timestamp | No | Yes |
| Sets Instance | No | Yes |

## Proposed Solutions

### Option 1: Delete MvcProblemDetailsNormalizer (Recommended)
**Pros:** Eliminates duplication, single source of truth
**Cons:** Need to update ApiControllerBase to use IProblemDetailsCreator.Normalize()
**Effort:** Medium
**Risk:** Low

### Option 2: Have MvcProblemDetailsNormalizer Delegate
**Pros:** Keeps API stable
**Cons:** Extra indirection
**Effort:** Small
**Risk:** Low

## Technical Details

Note: `ProblemDetailsCreator.MalformedSyntax()` and similar methods already call `_Normalize()` internally, so the explicit `ApplyProblemDetailsDefaults` calls in `ApiControllerBase` may be redundant for those cases.

## Acceptance Criteria

- [ ] Only one normalization path exists
- [ ] All ProblemDetails responses have consistent extensions
- [ ] Tests pass

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Duplication identified between packages |

## Resources

- MvcProblemDetailsNormalizer.cs
- IProblemDetailsCreator.cs
