# EntityFrameworkCore Dependency for Single Catch Block

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, architecture, dependencies, minimalapi, dotnet

---

## Problem Statement

The `Framework.Api.MinimalApi` package has a dependency on `Microsoft.EntityFrameworkCore` in the .csproj:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" />
```

This dependency exists solely to catch `DbUpdateConcurrencyException` in `MinimalApiExceptionFilter.cs` (lines 60-67):

```csharp
catch (DbUpdateConcurrencyException exception)
{
    LogDbConcurrencyException(logger, exception);
    var details = problemDetailsCreator.Conflict([GeneralMessageDescriber.ConcurrencyFailure()]);
    return TypedResults.Problem(details);
}
```

**Why it matters:**
- Violates Dependency Inversion Principle
- Forces EF Core on consumers who don't use it
- Package bloat for minimal functionality
- Inconsistent with framework's abstraction pattern

---

## Proposed Solutions

### Option A: Remove EF Core Dependency (Type Name Matching)
```csharp
catch (Exception e) when (e.GetType().Name == "DbUpdateConcurrencyException")
{
    // Handle without direct type reference
}
```
- **Pros:** No dependency, works with any EF version
- **Cons:** Slightly hacky, no compile-time safety
- **Effort:** Small
- **Risk:** Low

### Option B: Create Separate EF Package
Create `Framework.Api.MinimalApi.EntityFramework` with EF-specific exception handling.
- **Pros:** Clean separation, follows existing pattern
- **Cons:** Additional package, user must configure
- **Effort:** Medium
- **Risk:** Low

### Option C: Use Generic Concurrency Exception
Add `ConcurrencyException` to `Framework.Exceptions` and have EF packages wrap/rethrow.
- **Pros:** Clean abstraction, reusable
- **Cons:** Requires changes in multiple packages
- **Effort:** Medium
- **Risk:** Medium

---

## Recommended Action

**Option A** for quick fix, **Option B** for proper architecture.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api.MinimalApi/Framework.Api.MinimalApi.csproj` (line 8)
- `src/Framework.Api.MinimalApi/Filters/MinimalApiExceptionFilter.cs` (lines 10, 60-67)

---

## Acceptance Criteria

- [ ] No direct EF Core dependency in MinimalApi package
- [ ] Concurrency exceptions still handled appropriately
- [ ] Consumers can use package without EF Core

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - architecture-strategist, pragmatic-dotnet-reviewer |
