# Unhandled Exceptions Can Leak Sensitive Information

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, security, exception-handling, minimalapi, dotnet

---

## Problem Statement

In `MinimalApiExceptionFilter.cs`, the exception filter only catches specific exception types. Unhandled exceptions propagate without sanitization:

```csharp
try
{
    return await next(context);
}
catch (ConflictException exception) { /* handled */ }
catch (ValidationException exception) { /* handled */ }
catch (EntityNotFoundException exception) { /* handled */ }
catch (DbUpdateConcurrencyException exception) { /* handled */ }
catch (TimeoutException) { /* handled */ }
catch (NotImplementedException) { /* handled */ }
catch (OperationCanceledException) { /* handled */ }
catch (Exception exception) when (exception.InnerException is OperationCanceledException) { /* handled */ }
// NO catch-all - unhandled exceptions leak through!
```

**Why it matters:**
- Stack traces exposed to attackers
- Internal file paths revealed
- Database connection strings could leak
- Exception details aid reconnaissance for attacks
- Violates OWASP security guidelines

---

## Proposed Solutions

### Option A: Add Catch-All Handler
```csharp
catch (Exception exception)
{
    _LogUnhandledException(logger, exception);
    return TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError);
}
```
- **Pros:** Prevents info leakage, logs for debugging
- **Cons:** Must ensure global handler not double-handling
- **Effort:** Small
- **Risk:** Low

### Option B: Rely on Global Exception Handler
Document that a global exception handler middleware must be configured and ensure it sanitizes responses.
- **Pros:** Centralized handling
- **Cons:** Requires external configuration, easy to misconfigure
- **Effort:** None
- **Risk:** Medium (relies on consumer configuration)

---

## Recommended Action

**Option A** - Add catch-all handler with generic error response.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api.MinimalApi/Filters/MinimalApiExceptionFilter.cs` (after line 87)

---

## Acceptance Criteria

- [ ] All exceptions return sanitized responses
- [ ] No stack traces in production responses
- [ ] Unhandled exceptions logged for debugging
- [ ] Generic 500 response for unknown exceptions

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - security-sentinel |
