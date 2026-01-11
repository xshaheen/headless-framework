# Missing AnyContext() on Await Statements

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, async, dotnet, minimalapi

---

## Problem Statement

Per CLAUDE.md conventions: "Use `AnyContext()` extension (replaces `ConfigureAwait(false)`). Always pass `CancellationToken`."

Multiple files have await statements without AnyContext():

**MinimalApiExceptionFilter.cs (lines 33, 38):**
```csharp
return await next(context);  // Missing AnyContext()
```

**MinimalApiValidatorFilter.cs (lines 20, 33-36, 45):**
```csharp
return await next(context);  // Missing AnyContext()
var validationResults = await Task.WhenAll(...);  // Missing AnyContext()
```

**EndpointRouteBuilderExtensions.cs (multiple handlers):**
```csharp
TypedResults.Ok(await sender.Send(request));  // Missing AnyContext()
```

**Why it matters:**
- Library code should use ConfigureAwait(false) to avoid deadlocks
- Callers with SynchronizationContext may deadlock
- Inconsistent with project conventions

---

## Proposed Solutions

### Option A: Add AnyContext() to All Awaits
```csharp
return await next(context).AnyContext();
var validationResults = await Task.WhenAll(...).AnyContext();
TypedResults.Ok(await sender.Send(request).AnyContext());
```
- **Pros:** Follows project conventions, prevents deadlocks
- **Cons:** Verbose
- **Effort:** Small
- **Risk:** None

---

## Recommended Action

**Option A** - Add AnyContext() to all await statements in the package.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api.MinimalApi/Filters/MinimalApiExceptionFilter.cs` (lines 33, 38)
- `src/Framework.Api.MinimalApi/Filters/MinimalApiValidatorFilter.cs` (lines 20, 35, 45)
- `src/Framework.Api.MinimalApi/Endpoints/EndpointRouteBuilderExtensions.cs` (multiple lines)

---

## Acceptance Criteria

- [ ] All await statements use AnyContext()
- [ ] Consistent with project conventions

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
