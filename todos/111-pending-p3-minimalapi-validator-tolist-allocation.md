# Performance: ToList() Allocation on Every Request

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, performance, minimalapi, dotnet

---

## Problem Statement

In `MinimalApiValidatorFilter.cs` line 16, a new List is allocated on every request:

```csharp
var validators = context.HttpContext.RequestServices.GetService<IEnumerable<IValidator<TRequest>>>()?.ToList();

if (validators is null || validators.Count == 0)
{
    return await next(context);
}
```

**Why it matters:**
- Allocates List<T> on EVERY request
- Even when validators exist and validation passes
- At 10K RPS: 10K List allocations per second

---

## Proposed Solutions

### Option A: Lazy Materialization
```csharp
var validators = context.HttpContext.RequestServices.GetService<IEnumerable<IValidator<TRequest>>>();

if (validators is null || !validators.Any())
{
    return await next(context).AnyContext();
}

// Only materialize when needed
var validatorList = validators as IList<IValidator<TRequest>> ?? validators.ToList();
```
- **Pros:** No allocation when no validators
- **Cons:** Two-pass enumeration risk if underlying is lazy
- **Effort:** Small
- **Risk:** Low

### Option B: Use TryGetNonEnumeratedCount (.NET 6+)
```csharp
var validators = context.HttpContext.RequestServices.GetService<IEnumerable<IValidator<TRequest>>>();

if (validators is null || (validators.TryGetNonEnumeratedCount(out var count) && count == 0))
{
    return await next(context).AnyContext();
}
```
- **Pros:** No materialization, checks count efficiently
- **Cons:** TryGetNonEnumeratedCount may return false
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** with lazy materialization.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api.MinimalApi/Filters/MinimalApiValidatorFilter.cs` (lines 16-21)

---

## Acceptance Criteria

- [ ] No List allocation when validators are empty/null
- [ ] Materialization deferred until needed

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle |
