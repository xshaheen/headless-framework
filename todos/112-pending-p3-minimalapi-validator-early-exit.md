# Performance: Dictionary Allocation When Validation Passes

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, performance, minimalapi, dotnet

---

## Problem Statement

In `MinimalApiValidatorFilter.cs` lines 38-43, a dictionary is always allocated even when all validations pass:

```csharp
var failures = validationResults
    .Where(x => !x.IsValid)
    .SelectMany(result => result.Errors)
    .Where(failure => failure is not null)
    .GroupBy(x => x.PropertyName, x => x.ErrorMessage, StringComparer.Ordinal)
    .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);

return failures.Count > 0 ? Results.ValidationProblem(failures) : await next(context);
```

**Why it matters:**
- Happy path (validation passes) still allocates empty dictionary
- Multiple LINQ iterator allocations
- Wasted allocations on every successful request

---

## Proposed Solutions

### Option A: Early Exit When All Valid
```csharp
// Early exit if all valid - avoid LINQ chain entirely
if (validationResults.All(x => x.IsValid))
{
    return await next(context).AnyContext();
}

var failures = validationResults
    .Where(x => !x.IsValid)
    .SelectMany(result => result.Errors)
    .Where(failure => failure is not null)
    .GroupBy(x => x.PropertyName, x => x.ErrorMessage, StringComparer.Ordinal)
    .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);

return Results.ValidationProblem(failures);
```
- **Pros:** Zero allocations on happy path
- **Cons:** Extra check for All()
- **Effort:** Small
- **Risk:** None

---

## Recommended Action

**Option A** - Add early exit when all validations pass.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api.MinimalApi/Filters/MinimalApiValidatorFilter.cs` (lines 38-45)

---

## Acceptance Criteria

- [ ] No dictionary allocation when validation passes
- [ ] Early exit on happy path

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle |
