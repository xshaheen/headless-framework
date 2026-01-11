# Missing CancellationToken in Handler Methods

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, async, cancellation, minimalapi, dotnet

---

## Problem Statement

In `EndpointRouteBuilderExtensions.cs`, the `Map`, `MapPost`, `MapPut`, and `MapDelete` methods do not pass `CancellationToken` to the mediator:

```csharp
// Map, MapPost, MapPut, MapDelete - lines 25-34, 66-75, 86-95, 106-115
static async Task<Result<Ok<TResponse>, ProblemHttpResult>> handler(
    TRequest? request,
    ISender sender,
    IProblemDetailsCreator problemDetailsCreator
)
{
    return request is null
        ? TypedResults.Problem(problemDetailsCreator.MalformedSyntax())
        : TypedResults.Ok(await sender.Send(request));  // NO CancellationToken!
}
```

Only `MapGet` correctly passes the token:

```csharp
// MapGet - lines 45-55
static async Task<Result<Ok<TResponse>, ProblemHttpResult>> handler(
    [FromQuery] TRequest? request,
    ISender sender,
    IProblemDetailsCreator problemDetailsCreator,
    CancellationToken cancellationToken  // Correct!
)
{
    return TypedResults.Ok(await sender.Send(request, cancellationToken));  // Correct!
}
```

**Why it matters:**
- Client disconnect doesn't cancel ongoing work
- Database operations continue after client leaves
- Wasted server resources on abandoned requests
- Inconsistent behavior between GET and other methods

---

## Proposed Solutions

### Option A: Add CancellationToken to All Handlers
```csharp
static async Task<Result<Ok<TResponse>, ProblemHttpResult>> handler(
    TRequest? request,
    ISender sender,
    IProblemDetailsCreator problemDetailsCreator,
    CancellationToken cancellationToken  // Add this
)
{
    return request is null
        ? TypedResults.Problem(problemDetailsCreator.MalformedSyntax())
        : TypedResults.Ok(await sender.Send(request, cancellationToken));  // Pass it
}
```
- **Pros:** Consistent, correct async pattern
- **Cons:** None
- **Effort:** Small
- **Risk:** None

---

## Recommended Action

**Option A** - Add CancellationToken parameter to all handler methods.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api.MinimalApi/Endpoints/EndpointRouteBuilderExtensions.cs` (lines 25-34, 66-75, 86-95, 106-115)

---

## Acceptance Criteria

- [ ] All handler methods accept CancellationToken
- [ ] All Send() calls pass the cancellation token
- [ ] Consistent pattern across Map, MapGet, MapPost, MapPut, MapDelete

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, performance-oracle |
