---
status: pending
priority: p2
issue_id: "102"
tags: [code-review, dotnet, api-mvc, bug]
dependencies: []
---

# Inconsistent ProblemDetailsNormalizer Usage in ConflictProblemDetails

## Problem Statement

In `ApiControllerBase`, one `ConflictProblemDetails` overload calls `ProblemDetailsNormalizer.ApplyProblemDetailsDefaults()` but the other doesn't. This causes inconsistent behavior - one response has traceId and custom configuration, the other doesn't.

## Findings

**Source:** strict-dotnet-reviewer agent

**Location:** `src/Framework.Api.Mvc/Controllers/ApiControllerBase.cs:124-138`

```csharp
// Line 124-129: Does NOT call normalizer
protected ConflictObjectResult ConflictProblemDetails(IEnumerable<ErrorDescriptor> errorDescriptors)
{
    var problemDetails = ProblemDetailsCreator.Conflict(errorDescriptors);
    return base.Conflict(problemDetails); // Missing normalization!
}

// Line 131-138: DOES call normalizer
protected ConflictObjectResult ConflictProblemDetails(ErrorDescriptor errorDescriptor)
{
    var problemDetails = ProblemDetailsCreator.Conflict([errorDescriptor]);
    ProblemDetailsNormalizer.ApplyProblemDetailsDefaults(HttpContext, problemDetails); // Has it
    return base.Conflict(problemDetails);
}
```

## Proposed Solutions

### Option 1: Add Missing Normalization Call (Recommended)
**Pros:** Consistent behavior across overloads
**Cons:** None
**Effort:** Small
**Risk:** Low

```csharp
protected ConflictObjectResult ConflictProblemDetails(IEnumerable<ErrorDescriptor> errorDescriptors)
{
    var problemDetails = ProblemDetailsCreator.Conflict(errorDescriptors);
    ProblemDetailsNormalizer.ApplyProblemDetailsDefaults(HttpContext, problemDetails);
    return base.Conflict(problemDetails);
}
```

## Acceptance Criteria

- [ ] Both `ConflictProblemDetails` overloads call normalization
- [ ] Responses are consistent
- [ ] Tests pass

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Inconsistent API behavior detected |

## Resources

- ApiControllerBase.cs:124-138
