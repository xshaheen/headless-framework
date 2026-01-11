# ProblemDetailsOperationProcessor Timestamp Set at Construction

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, openapi, thread-safety, dotnet

---

## Problem Statement

In `ProblemDetailsOperationProcessor.cs` (lines 132-235), example ProblemDetails objects have `DateTimeOffset.UtcNow` set at construction time:

```csharp
private readonly BadRequestProblemDetails _status400ProblemDetails = new()
{
    // ...
    Timestamp = DateTimeOffset.UtcNow,  // Set once at processor construction!
};
```

**Why it matters:**
- Processor is instantiated once during startup
- All OpenAPI docs show same stale timestamp (e.g., from 3 days ago)
- Example timestamps don't represent "example" realistic data
- Minor but creates confusing documentation

---

## Proposed Solutions

### Option A: Make Examples Static with Fixed Timestamp
```csharp
private static readonly BadRequestProblemDetails _status400ProblemDetails = new()
{
    Timestamp = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
};
```
- **Pros:** Deterministic, clearly "example" data
- **Cons:** Not dynamic
- **Effort:** Small
- **Risk:** Low

### Option B: Create Examples Lazily Per Request
```csharp
private static BadRequestProblemDetails _CreateStatus400Example() => new()
{
    Timestamp = DateTimeOffset.UtcNow,
};
```
- **Pros:** Always current timestamp
- **Cons:** Creates new objects per schema generation, more allocations
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** - Use fixed, obviously-example timestamps. More appropriate for documentation.

---

## Technical Details

**Affected Files:**
- `src/Framework.OpenApi.Nswag/OperationProcessors/ProblemDetailsOperationProcessor.cs` (lines 132-235)

---

## Acceptance Criteria

- [ ] Example timestamps are deterministic
- [ ] OpenAPI docs show reasonable example timestamps

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
