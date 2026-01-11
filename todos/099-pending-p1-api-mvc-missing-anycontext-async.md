---
status: pending
priority: p1
issue_id: "099"
tags: [code-review, dotnet, async, api-mvc]
dependencies: []
---

# Missing AnyContext() on Async Calls in Framework.Api.Mvc

## Problem Statement

Per project conventions (CLAUDE.md), all async methods should use `AnyContext()` extension (replaces `ConfigureAwait(false)`). None of the async methods in Framework.Api.Mvc filters use it.

In library code, capturing the synchronization context unnecessarily can cause deadlocks when called from synchronous contexts and causes unnecessary allocations.

## Findings

**Source:** strict-dotnet-reviewer agent

**Affected Files:**
- `src/Framework.Api.Mvc/Filters/BlockInEnvironmentAttribute.cs:25-27` - `await next()` and `await Results.Problem(...).ExecuteAsync()`
- `src/Framework.Api.Mvc/Filters/RequireEnvironmentAttribute.cs:25-27` - Same issue
- `src/Framework.Api.Mvc/Filters/NoTrailingSlashAttribute.cs:42` - `await Results.Problem(...).ExecuteAsync()`
- `src/Framework.Api.Mvc/Filters/MvcApiExceptionFilter.cs:52` - `await task`

## Proposed Solutions

### Option 1: Add AnyContext() to All Await Statements (Recommended)
**Pros:** Follows project conventions, prevents potential deadlocks
**Cons:** Minor code change
**Effort:** Small
**Risk:** Low

```csharp
// Before
await next();
await Results.Problem(problemDetails).ExecuteAsync(context.HttpContext);

// After
await next().AnyContext();
await Results.Problem(problemDetails).ExecuteAsync(context.HttpContext).AnyContext();
```

## Technical Details

**Affected Components:** MVC Filters
**Files to Modify:** 4 files in Filters directory

## Acceptance Criteria

- [ ] All `await` statements in Framework.Api.Mvc use `AnyContext()`
- [ ] Code compiles without errors
- [ ] Existing tests pass

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Project convention requires AnyContext() on all async |

## Resources

- CLAUDE.md async conventions
