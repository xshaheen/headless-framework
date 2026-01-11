# ProblemDetails Examples Expose Build/Commit Information

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, security, information-disclosure, dotnet

---

## Problem Statement

In `ProblemDetailsOperationProcessor.cs` (lines 132-235), example ProblemDetails expose sensitive build information:

```csharp
private readonly BadRequestProblemDetails _status400ProblemDetails = new()
{
    // ...
    BuildNumber = "1.0.0",
    CommitNumber = "abc123def",
    TraceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
};
```

**Why it matters:**
- OpenAPI docs often publicly accessible
- Build/commit info helps attackers identify vulnerable versions
- Trace ID format reveals internal tracing infrastructure
- While these are "examples", they establish the expectation that real responses include this info

---

## Proposed Solutions

### Option A: Use Generic Example Values
```csharp
BuildNumber = "<version>",
CommitNumber = "<commit>",
TraceId = "<trace-id>",
```
- **Pros:** Clear these are placeholders
- **Cons:** Less realistic examples
- **Effort:** Small
- **Risk:** Low

### Option B: Remove from Examples, Document Separately
Remove build/commit from examples, add description noting these fields exist.
- **Pros:** No info disclosure at all
- **Cons:** Examples less complete
- **Effort:** Small
- **Risk:** Low

### Option C: Make Configurable
Allow disabling these fields in examples via options.
- **Pros:** Flexible
- **Cons:** More complexity
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** - Use obvious placeholder values like `<version>` and `<commit>`.

---

## Technical Details

**Affected Files:**
- `src/Framework.OpenApi.Nswag/OperationProcessors/ProblemDetailsOperationProcessor.cs` (lines 132-235)

---

## Acceptance Criteria

- [ ] Example build/commit info uses placeholder values
- [ ] TraceId uses example format that doesn't reveal infrastructure

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
