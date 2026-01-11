# MapPut Method Calls MapPost (Copy-Paste Bug)

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, bug, minimalapi, dotnet

---

## Problem Statement

In `EndpointRouteBuilderExtensions.cs` line 97, the `MapPut` method incorrectly calls `endpoints.MapPost()` instead of `endpoints.MapPut()`:

```csharp
public static RouteHandlerBuilder MapPut<TRequest, TResponse>(...)
{
    static async Task<Result<Ok<TResponse>, ProblemHttpResult>> handler(...)
    {
        // ...
    }

    return endpoints.MapPost(pattern, handler).Validate<TRequest>();  // BUG: Should be MapPut!
}
```

**Why it matters:**
- PUT endpoints are registered as POST endpoints
- HTTP method semantics violated (PUT is idempotent, POST is not)
- API consumers expecting PUT will get 405 Method Not Allowed
- Breaks REST conventions

---

## Proposed Solutions

### Option A: Fix the Method Call
```csharp
return endpoints.MapPut(pattern, handler).Validate<TRequest>();
```
- **Pros:** Simple fix, one character change
- **Cons:** None
- **Effort:** Trivial
- **Risk:** None

---

## Recommended Action

**Option A** - Change `MapPost` to `MapPut` on line 97.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api.MinimalApi/Endpoints/EndpointRouteBuilderExtensions.cs` (line 97)

---

## Acceptance Criteria

- [ ] MapPut method calls endpoints.MapPut()
- [ ] PUT endpoints are correctly registered

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, pattern-recognition-specialist |
