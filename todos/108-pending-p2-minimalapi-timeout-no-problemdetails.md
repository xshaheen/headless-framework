# Timeout/NotImplemented Responses Lack ProblemDetails Structure

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, api-consistency, minimalapi, dotnet

---

## Problem Statement

In `MinimalApiExceptionFilter.cs`, timeout and not-implemented exceptions return bare status codes without ProblemDetails:

```csharp
// Lines 69-77
catch (TimeoutException)
{
    return TypedResults.StatusCode(StatusCodes.Status408RequestTimeout);  // No body
}
catch (NotImplementedException)
{
    return TypedResults.StatusCode(StatusCodes.Status501NotImplemented);  // No body
}
```

All other exception types return structured ProblemDetails responses.

**Why it matters:**
- Inconsistent API response format
- Agents/clients can't parse these errors programmatically
- Breaks RFC 7807 compliance pattern
- Harder to debug on client side

---

## Proposed Solutions

### Option A: Return ProblemDetails for All Errors
```csharp
catch (TimeoutException)
{
    return TypedResults.Problem(
        statusCode: StatusCodes.Status408RequestTimeout,
        title: "Request Timeout",
        detail: "The request timed out"
    );
}
catch (NotImplementedException)
{
    return TypedResults.Problem(
        statusCode: StatusCodes.Status501NotImplemented,
        title: "Not Implemented",
        detail: "This functionality is not implemented"
    );
}
```
- **Pros:** Consistent response format, machine-parseable
- **Cons:** Slightly more verbose
- **Effort:** Small
- **Risk:** None

---

## Recommended Action

**Option A** - Return ProblemDetails for all error responses.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api.MinimalApi/Filters/MinimalApiExceptionFilter.cs` (lines 69-77)

---

## Acceptance Criteria

- [ ] All error responses return ProblemDetails
- [ ] Consistent response structure across all exception types

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - agent-native-reviewer |
