# Magic Status Code Strings in Operation Processors

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-have
**Tags:** code-review, magic-strings, dotnet

---

## Problem Statement

Status codes are hardcoded as strings throughout:

```csharp
// ForbiddenResponseOperationProcessor.cs
private const string _ForbiddenStatusCode = "403";

// UnauthorizedResponseOperationProcessor.cs
private const string _UnauthorizedStatusCode = "401";

// ProblemDetailsOperationProcessor.cs
case "400":
case "401":
case "403":
// etc.
```

**Why it matters:**
- Inconsistent with using `StatusCodes.Status400BadRequest` for the actual status values
- Could use `StatusCodes` class consistently

---

## Proposed Solutions

### Option A: Use StatusCodes Constants
```csharp
private const string _ForbiddenStatusCode = nameof(StatusCodes.Status403Forbidden)[6..]; // "403"
// Or just keep as "403" but derive from StatusCodes
private static readonly string _ForbiddenStatusCode = StatusCodes.Status403Forbidden.ToString(CultureInfo.InvariantCulture);
```
- **Pros:** Single source of truth
- **Cons:** More complex than simple string
- **Effort:** Small
- **Risk:** Low

### Option B: Create Constants Class
```csharp
internal static class OpenApiStatusCodes
{
    public const string BadRequest = "400";
    public const string Unauthorized = "401";
    // etc.
}
```
- **Pros:** Centralized
- **Cons:** Another file
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

Leave as-is or **Option B** if more status codes are added. Current code is clear enough.

---

## Technical Details

**Affected Files:**
- `src/Framework.OpenApi.Nswag/OperationProcessors/ForbiddenResponseOperationProcessor.cs`
- `src/Framework.OpenApi.Nswag/OperationProcessors/UnauthorizedResponseOperationProcessor.cs`
- `src/Framework.OpenApi.Nswag/OperationProcessors/ProblemDetailsOperationProcessor.cs`

---

## Acceptance Criteria

- [ ] Status codes centralized or documented as acceptable

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
