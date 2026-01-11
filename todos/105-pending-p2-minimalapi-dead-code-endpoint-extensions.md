# Dead Code: EndpointRouteBuilderExtensions Never Used

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, dead-code, yagni, minimalapi, dotnet

---

## Problem Statement

The entire `EndpointRouteBuilderExtensions.cs` file (119 lines) is never used anywhere in the codebase:

```csharp
public static class EndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder Map<TRequest, TResponse>(...) { }
    public static RouteHandlerBuilder MapGet<TRequest, TResponse>(...) { }
    public static RouteHandlerBuilder MapPost<TRequest, TResponse>(...) { }
    public static RouteHandlerBuilder MapPut<TRequest, TResponse>(...) { }
    public static RouteHandlerBuilder MapDelete<TRequest, TResponse>(...) { }
}
```

No usages found in:
- Main codebase
- Test projects
- Demo applications

The file also contains a bug (MapPut calls MapPost) that was never caught because nobody uses it.

**Why it matters:**
- Dead code increases maintenance burden
- Contains unfixed bugs
- Violates YAGNI principle
- Increases package size unnecessarily

---

## Proposed Solutions

### Option A: Delete the File
```bash
rm src/Framework.Api.MinimalApi/Endpoints/EndpointRouteBuilderExtensions.cs
```
- **Pros:** Eliminates dead code, reduces complexity
- **Cons:** Breaking change if any external consumers use it
- **Effort:** Trivial
- **Risk:** Low (check NuGet download consumers first)

### Option B: Mark as Obsolete
```csharp
[Obsolete("This API is deprecated and will be removed in a future version.")]
public static RouteHandlerBuilder Map<TRequest, TResponse>(...) { }
```
- **Pros:** Non-breaking, gives consumers time to migrate
- **Cons:** Still maintaining dead code
- **Effort:** Small
- **Risk:** None

---

## Recommended Action

**Option A** - Delete the file. If this is a new package with no external consumers yet, delete immediately. Otherwise, Option B first.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api.MinimalApi/Endpoints/EndpointRouteBuilderExtensions.cs` (DELETE entire file)
- `src/Framework.Api.MinimalApi/Filters/RouteBuilderExtensions.cs` (remove `Validate<T>()` if only used by above)

---

## Acceptance Criteria

- [ ] EndpointRouteBuilderExtensions.cs removed
- [ ] No build errors
- [ ] No runtime errors in tests

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - code-simplicity-reviewer |
