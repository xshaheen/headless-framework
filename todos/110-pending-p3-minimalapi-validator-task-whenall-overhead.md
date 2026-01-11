# Performance: Task.WhenAll Overhead for Single Validator

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, performance, minimalapi, dotnet

---

## Problem Statement

In `MinimalApiValidatorFilter.cs` lines 33-36, `Task.WhenAll` is used even when there's only one validator:

```csharp
var validationResults = await Task.WhenAll(
        validators.Select(v => v.ValidateAsync(validationContext, context.HttpContext.RequestAborted))
    )
    .WithAggregatedExceptions();
```

**Why it matters:**
- Most endpoints have only 1 validator
- Task.WhenAll allocates Task<T[]> and internal arrays
- Select() creates an iterator
- At 100K RPS: ~200K+ unnecessary allocations/sec

---

## Proposed Solutions

### Option A: Fast Path for Single Validator
```csharp
ValidationResult[] validationResults;

if (validatorList.Count == 1)
{
    // Fast path for single validator
    var result = await validatorList[0].ValidateAsync(validationContext, context.HttpContext.RequestAborted).AnyContext();
    validationResults = [result];
}
else
{
    // Parallel path for multiple validators
    validationResults = await Task.WhenAll(
            validatorList.Select(v => v.ValidateAsync(validationContext, context.HttpContext.RequestAborted))
        )
        .WithAggregatedExceptions()
        .AnyContext();
}
```
- **Pros:** Eliminates overhead for common case
- **Cons:** More code
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Add fast path for single validator case.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api.MinimalApi/Filters/MinimalApiValidatorFilter.cs` (lines 33-36)

---

## Acceptance Criteria

- [ ] Single validator case avoids Task.WhenAll
- [ ] Multiple validator case still uses parallel execution
- [ ] Benchmark shows reduced allocations

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle |
