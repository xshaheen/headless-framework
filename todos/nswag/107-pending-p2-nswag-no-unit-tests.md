# Framework.OpenApi.Nswag Has No Unit Tests

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, testing, quality, dotnet

---

## Problem Statement

No test project found for `Framework.OpenApi.Nswag`. Glob for `tests/**/Framework.OpenApi*/**/*.cs` returned empty.

**Why it matters:**
- FluentValidation schema rules have complex logic (nullable handling, OneOf cleanup)
- ComparisonRule has a bug (P1) that tests would have caught
- Operation processors have conditional logic for auth attributes
- Regressions easy to introduce without test coverage
- Schema generation correctness is critical for API consumers

---

## Proposed Solutions

### Option A: Add Unit Test Project
```
tests/Framework.OpenApi.Nswag.Tests.Unit/
  SchemaProcessors/
    FluentValidationSchemaProcessorTests.cs
    NullabilityAsRequiredSchemaProcessorTests.cs
  OperationProcessors/
    UnauthorizedResponseOperationProcessorTests.cs
    ForbiddenResponseOperationProcessorTests.cs
  FluentValidation/
    FluentValidationRuleTests.cs
```
- **Pros:** Full coverage, catches regressions
- **Cons:** Effort to create
- **Effort:** Large
- **Risk:** Low

### Option B: Add Integration Tests Only
Test via actual OpenAPI document generation.
- **Pros:** Tests real behavior
- **Cons:** Slower, harder to isolate issues
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** - Prioritize unit tests for `FluentValidationRule` and schema processors.

---

## Technical Details

**Key Test Cases:**
- Each FluentValidation rule generates correct schema
- NotNull/NotEmpty remove nullable flag
- Comparison rules set correct min/max/exclusive
- Between rules handle inclusive vs exclusive
- Operation processors add responses conditionally
- Auth attribute detection works

---

## Acceptance Criteria

- [ ] Test project created
- [ ] FluentValidationRule tests (one per rule)
- [ ] Schema processor tests
- [ ] Operation processor tests
- [ ] CI runs tests

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
