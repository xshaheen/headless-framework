---
status: pending
priority: p1
issue_id: "114"
tags: [code-review, dotnet, async, serilog, middleware]
dependencies: []
---

# Missing AnyContext() on Async Call in SerilogEnrichersMiddleware

## Problem Statement

Per project conventions (CLAUDE.md), all async methods should use `AnyContext()` extension. The `SerilogEnrichersMiddleware.InvokeAsync` method awaits `next(context)` without `AnyContext()`.

In library/middleware code, capturing the synchronization context unnecessarily can cause deadlocks and unnecessary allocations.

## Findings

**Source:** strict-dotnet-reviewer, performance-oracle, architecture-strategist, pattern-recognition-specialist agents

**Affected Files:**
- `src/Framework.Api.Logging.Serilog/SerilogEnrichersMiddleware.cs:38` - `await next(context)` missing `AnyContext()`

## Proposed Solutions

### Option 1: Add AnyContext() (Recommended)
**Pros:** Follows project conventions, prevents potential deadlocks
**Cons:** None
**Effort:** Trivial
**Risk:** Low

```csharp
// Before
await next(context);

// After
await next(context).AnyContext();
```

## Technical Details

**Affected Components:** SerilogEnrichersMiddleware
**Files to Modify:** 1 file

## Acceptance Criteria

- [ ] `await next(context)` uses `AnyContext()`
- [ ] Code compiles without errors
- [ ] Existing tests pass (if any)

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Project convention requires AnyContext() on all async |

## Resources

- CLAUDE.md async conventions
