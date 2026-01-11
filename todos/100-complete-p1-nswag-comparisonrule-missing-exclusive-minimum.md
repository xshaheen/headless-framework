# ComparisonRule GreaterThan Missing ExclusiveMinimum

---
status: complete
priority: p1
issue_id: "100"
tags: [openapi, bug]
dependencies: []
---

**Date:** 2026-01-11
**Status:** complete
**Priority:** P1 - Critical
**Tags:** code-review, openapi, bug, dotnet

---

## Problem Statement

In `FluentValidationRule.cs` (lines 150-158), the `ComparisonRule` handles `GreaterThan` incorrectly:

```csharp
case Comparison.GreaterThanOrEqual:
    propertySchema.Minimum = valueToCompare;
    break;
case Comparison.GreaterThan:
    propertySchema.Minimum = valueToCompare;  // BUG: Should be ExclusiveMinimum!
    break;
```

**Why it matters:**
- OpenAPI 3.0 semantics: `minimum` means `>=`, `exclusiveMinimum` means `>`
- `GreaterThan(5)` generates schema allowing `5`, but validation rejects `5`
- Client code generators produce incorrect validation
- API consumers get unexpected 422 errors

---

## Proposed Solutions

### Option A: Set ExclusiveMinimum for GreaterThan
```csharp
case Comparison.GreaterThan:
    propertySchema.ExclusiveMinimum = valueToCompare;
    break;
```
- **Pros:** Correct fix, matches OpenAPI semantics
- **Cons:** None
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Simple one-line fix.

---

## Technical Details

**Affected Files:**
- `src/Framework.OpenApi.Nswag/SchemaProcessors/FluentValidation/Models/FluentValidationRule.cs` (lines 156-158)

---

## Acceptance Criteria

- [ ] `GreaterThan` validator sets `exclusiveMinimum` not `minimum`
- [ ] Unit test verifying correct schema generation
- [ ] Generated OpenAPI spec validated against FluentValidation behavior

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
| 2026-01-11 | Approved | Triage: ready to work on |
| 2026-01-12 | Resolved | Changed Minimum to ExclusiveMinimum for GreaterThan case |
