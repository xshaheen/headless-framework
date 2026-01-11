---
status: pending
priority: p2
issue_id: "103"
tags: [code-review, dotnet, api-mvc, reliability]
dependencies: []
---

# Debug.Assert in Production Code - MvcProblemDetailsNormalizer

## Problem Statement

`Debug.Assert` is compiled out in Release builds. If `Status` is null in production, line 22 will throw `NullReferenceException` on `.Value` access instead of a meaningful error.

## Findings

**Source:** strict-dotnet-reviewer agent

**Location:** `src/Framework.Api.Mvc/Controllers/MvcProblemDetailsNormalizer.cs:20-22`

```csharp
public void ApplyProblemDetailsDefaults(HttpContext httpContext, ProblemDetails problemDetails)
{
    Debug.Assert(problemDetails.Status is not null); // Compiled out in Release!

    if (_apiOptions.ClientErrorMapping.TryGetValue(problemDetails.Status.Value, out var clientErrorData))
    // problemDetails.Status.Value throws NRE if Status is null
```

## Proposed Solutions

### Option 1: Use Null-Conditional Pattern (Recommended)
**Pros:** Safe in all builds, no exception
**Cons:** Silently skips if null
**Effort:** Small
**Risk:** Low

```csharp
if (problemDetails.Status is { } status &&
    _apiOptions.ClientErrorMapping.TryGetValue(status, out var clientErrorData))
```

### Option 2: Guard Clause with Exception
**Pros:** Fails fast with meaningful message
**Cons:** Throws in production
**Effort:** Small
**Risk:** Low

```csharp
if (problemDetails.Status is null)
    throw new ArgumentException("Status must be set", nameof(problemDetails));
```

## Acceptance Criteria

- [ ] No `Debug.Assert` used for production invariants
- [ ] Null status handled gracefully or with clear exception
- [ ] Tests pass

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Debug.Assert not appropriate for production checks |

## Resources

- MvcProblemDetailsNormalizer.cs:20
