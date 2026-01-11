---
status: pending
priority: p1
issue_id: "101"
tags: [code-review, dotnet, performance, api-mvc]
dependencies: []
---

# Uncached ProblemDetailsNormalizer Service Resolution

## Problem Statement

In `ApiControllerBase`, the `ProblemDetailsNormalizer` property resolves from DI on **every access**, unlike other service properties that use lazy caching with `field ??=` pattern. This causes unnecessary DI lookups on every error path.

## Findings

**Source:** strict-dotnet-reviewer, performance-oracle agents

**Location:** `src/Framework.Api.Mvc/Controllers/ApiControllerBase.cs:32-34`

```csharp
// Current - NO caching
protected MvcProblemDetailsNormalizer ProblemDetailsNormalizer =>
    HttpContext.RequestServices.GetService<MvcProblemDetailsNormalizer>()
    ?? throw new InvalidOperationException(...);

// Compare to other properties - WITH caching
[field: AllowNull, MaybeNull]
protected ISender Sender =>
    field ??= HttpContext.RequestServices.GetService<ISender>()
        ?? throw new InvalidOperationException(...);
```

**Performance Impact:** At 10K errors/sec, this creates ~10K unnecessary DI lookups/sec.

## Proposed Solutions

### Option 1: Add Caching Pattern (Recommended)
**Pros:** Consistent with other properties, eliminates redundant lookups
**Cons:** None
**Effort:** Small
**Risk:** Low

```csharp
[field: AllowNull, MaybeNull]
protected MvcProblemDetailsNormalizer ProblemDetailsNormalizer =>
    field ??= HttpContext.RequestServices.GetService<MvcProblemDetailsNormalizer>()
        ?? throw new InvalidOperationException(...);
```

## Technical Details

**File:** `src/Framework.Api.Mvc/Controllers/ApiControllerBase.cs`
**Lines:** 32-34

## Acceptance Criteria

- [ ] `ProblemDetailsNormalizer` uses `[field: AllowNull, MaybeNull]` pattern
- [ ] Behavior remains the same
- [ ] Tests pass

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Inconsistent caching pattern found |

## Resources

- ApiControllerBase.cs
