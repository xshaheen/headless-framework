# Missing Timeout Exception Logging

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, logging, consistency, minimalapi, dotnet

---

## Problem Statement

The `MvcApiExceptionFilter` logs timeout exceptions at Debug level (lines 87-91, 114-120):

```csharp
// MvcApiExceptionFilter.cs
private Task _Handle(HttpContext context, TimeoutException exception)
{
    LogRequestTimeoutException(logger, exception);  // Logs timeout
    return Results.Problem(statusCode: StatusCodes.Status408RequestTimeout).ExecuteAsync(context);
}

[LoggerMessage(EventId = 5004, EventName = "RequestTimeoutException", Level = LogLevel.Debug, ...)]
private static partial void LogRequestTimeoutException(ILogger logger, Exception exception);
```

But `MinimalApiExceptionFilter` does not log timeouts (lines 69-72):

```csharp
// MinimalApiExceptionFilter.cs
catch (TimeoutException)
{
    return TypedResults.StatusCode(StatusCodes.Status408RequestTimeout);  // No logging!
}
```

**Why it matters:**
- Inconsistent behavior between MVC and MinimalApi
- No visibility into timeout patterns
- Harder to diagnose performance issues

---

## Proposed Solutions

### Option A: Add Timeout Logging
```csharp
catch (TimeoutException exception)
{
    LogRequestTimeoutException(logger, exception);
    return TypedResults.StatusCode(StatusCodes.Status408RequestTimeout);
}

[LoggerMessage(EventId = 5004, EventName = "RequestTimeoutException", Level = LogLevel.Debug, Message = "Request was timed out", SkipEnabledCheck = true)]
private static partial void LogRequestTimeoutException(ILogger logger, Exception exception);
```
- **Pros:** Consistent with MVC, aids debugging
- **Cons:** Minor overhead
- **Effort:** Small
- **Risk:** None

---

## Recommended Action

**Option A** - Add the same logging pattern used in MVC.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api.MinimalApi/Filters/MinimalApiExceptionFilter.cs` (lines 69-72)

---

## Acceptance Criteria

- [ ] Timeout exceptions logged at Debug level
- [ ] Consistent with MvcApiExceptionFilter logging

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pattern-recognition-specialist |
