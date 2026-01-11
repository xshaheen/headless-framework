---
status: pending
priority: p1
issue_id: "100"
tags: [code-review, dotnet, async, api-mvc]
dependencies: []
---

# Missing CancellationToken Propagation in MvcApiExceptionFilter

## Problem Statement

The `_Handle` methods in `MvcApiExceptionFilter` call `Results.Problem(...).ExecuteAsync(context)` but never pass `CancellationToken`. The `HttpContext` has `RequestAborted` which should be passed to honor request cancellation.

## Findings

**Source:** strict-dotnet-reviewer agent

**Location:** `src/Framework.Api.Mvc/Filters/MvcApiExceptionFilter.cs:57-102`

All `_Handle` methods have this pattern:
```csharp
private Task _Handle(HttpContext context, ValidationException exception)
{
    var problemDetails = problemDetailsCreator.UnprocessableEntity(...);
    return Results.Problem(problemDetails).ExecuteAsync(context); // No CancellationToken!
}
```

## Proposed Solutions

### Option 1: Pass RequestAborted Token (Recommended)
**Pros:** Honors request cancellation, follows async best practices
**Cons:** Minor code change
**Effort:** Small
**Risk:** Low

```csharp
// After
return Results.Problem(problemDetails).ExecuteAsync(context, context.RequestAborted);
```

## Technical Details

**Affected Methods:**
- `_Handle(HttpContext, ValidationException)` line 57
- `_Handle(HttpContext, ConflictException)` line 64
- `_Handle(HttpContext, EntityNotFoundException)` line 71
- `_Handle(HttpContext, DbUpdateConcurrencyException)` line 78
- `_Handle(HttpContext, TimeoutException)` line 87
- `_Handle(HttpContext, OperationCanceledException)` line 94
- `_Handle(HttpContext, NotImplementedException)` line 99

## Acceptance Criteria

- [ ] All `ExecuteAsync` calls pass `context.RequestAborted`
- [ ] Tests pass

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Always propagate CancellationToken in async operations |

## Resources

- MvcApiExceptionFilter.cs
