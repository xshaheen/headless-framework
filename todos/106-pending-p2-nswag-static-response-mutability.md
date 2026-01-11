# Static OpenApiResponse Objects May Be Mutable

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, thread-safety, dotnet

---

## Problem Statement

In `ForbiddenResponseOperationProcessor.cs` and `UnauthorizedResponseOperationProcessor.cs`, static readonly responses are used:

```csharp
// ForbiddenResponseOperationProcessor.cs line 16
private static readonly OpenApiResponse _ForbiddenResponse = _CreateForbiddenResponse();

// UnauthorizedResponseOperationProcessor.cs line 16
private static readonly OpenApiResponse _UnauthorizedResponse = _CreateUnauthorizedResponse();
```

Then added to operation responses:
```csharp
responses.Add(_ForbiddenStatusCode, _ForbiddenResponse);  // Same instance shared!
```

**Why it matters:**
- `OpenApiResponse` is mutable (has settable properties)
- Same instance added to multiple operations
- If NSwag or user code modifies response after adding, affects all operations
- Thread safety concern during parallel schema generation

---

## Proposed Solutions

### Option A: Create New Instance Per Operation
```csharp
responses.Add(_ForbiddenStatusCode, _CreateForbiddenResponse());
```
- **Pros:** No shared mutable state
- **Cons:** More allocations (minimal - only during schema gen)
- **Effort:** Small
- **Risk:** Low

### Option B: Verify NSwag Doesn't Mutate
Research if NSwag modifies responses after they're added.
- **Pros:** No change if safe
- **Cons:** Relies on external behavior
- **Effort:** Small
- **Risk:** Medium (behavior may change)

---

## Recommended Action

**Option A** - Create new instances. Schema generation is infrequent, safety > micro-optimization.

---

## Technical Details

**Affected Files:**
- `src/Framework.OpenApi.Nswag/OperationProcessors/ForbiddenResponseOperationProcessor.cs` (line 16)
- `src/Framework.OpenApi.Nswag/OperationProcessors/UnauthorizedResponseOperationProcessor.cs` (line 16)

---

## Acceptance Criteria

- [ ] Each operation gets its own response instance
- [ ] Or documented that shared instance is safe

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
