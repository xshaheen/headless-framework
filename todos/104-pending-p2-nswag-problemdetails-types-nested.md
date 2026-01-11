# ProblemDetails Types Nested Inside Processor

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, architecture, organization, dotnet

---

## Problem Statement

In `ProblemDetailsOperationProcessor.cs` (lines 239-282), ProblemDetails schema types are defined as nested classes:

```csharp
public sealed class ProblemDetailsOperationProcessor : IOperationProcessor
{
    // ... processor logic ...

    #region ProblemDetails Types
    public class HeadlessProblemDetails { ... }
    public sealed class BadRequestProblemDetails : HeadlessProblemDetails;
    public sealed class EntityNotFoundProblemDetails : HeadlessProblemDetails { ... }
    // etc.
    #endregion
}
```

**Why it matters:**
- Types are public but nested - awkward to reference externally
- Violates single responsibility - processor shouldn't define DTOs
- `Framework.Primitives` already has ProblemDetails types - potential duplication
- Makes testing schema types independently harder

---

## Proposed Solutions

### Option A: Extract to Separate Files
```
src/Framework.OpenApi.Nswag/Models/
  HeadlessProblemDetails.cs
  BadRequestProblemDetails.cs
  ...
```
- **Pros:** Clean separation, easier to maintain
- **Cons:** More files
- **Effort:** Medium
- **Risk:** Low

### Option B: Use Existing Framework.Primitives Types
Check if `Framework.Primitives` already has equivalent types and reuse them.
- **Pros:** No duplication, consistent across framework
- **Cons:** May need to add missing types there
- **Effort:** Medium
- **Risk:** Low

### Option C: Make Internal
If types are only for OpenAPI examples, make them internal nested.
- **Pros:** Minimal change
- **Cons:** Still clutters processor
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option B** - Investigate if `Framework.Primitives` has equivalent types. If yes, reuse. If not, **Option A**.

---

## Technical Details

**Affected Files:**
- `src/Framework.OpenApi.Nswag/OperationProcessors/ProblemDetailsOperationProcessor.cs` (lines 239-282)
- Potentially `src/Framework.Primitives/` types

---

## Acceptance Criteria

- [ ] ProblemDetails types extracted or reused from existing location
- [ ] Processor only contains processor logic
- [ ] No public nested types

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
