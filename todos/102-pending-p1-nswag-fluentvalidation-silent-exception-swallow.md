# FluentValidationSchemaProcessor Swallows Exceptions Silently

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, error-handling, logging, dotnet

---

## Problem Statement

In `FluentValidationSchemaProcessor.cs`, exceptions are caught and logged as warnings but processing continues:

```csharp
// Line 56-63
try
{
    _AddRulesFromIncludedValidators(context, validator);
}
catch (Exception e)
{
    _logger.LogWarning(0, e, "Applying IncludeRules for type '{Type}' fails", context.ContextualType.Name);
}

// Line 105-127 and 152-173 - similar pattern
catch (Exception e)
{
    _logger.LogWarning(e, "Error on apply rule '{RuleName}' for property '{TypeName}.{Key}'", ...);
}
```

**Why it matters:**
- Schema generation silently produces incomplete/incorrect schemas
- Validators exist but their constraints don't appear in OpenAPI docs
- Only visible if Debug logging enabled
- Production issues may go unnoticed for extended periods
- API consumers get incorrect schema information

---

## Proposed Solutions

### Option A: Log as Error, Continue Processing
```csharp
_logger.LogError(e, "Failed to apply rule '{RuleName}'...");
```
- **Pros:** More visible, still resilient
- **Cons:** Still silent failure
- **Effort:** Small
- **Risk:** Low

### Option B: Add Strict Mode Option
```csharp
public bool ThrowOnSchemaProcessingError { get; set; } = false;

// In processor:
if (_options.ThrowOnSchemaProcessingError)
    throw;
_logger.LogError(...);
```
- **Pros:** Opt-in strict validation, catches issues in dev
- **Cons:** More code, new option
- **Effort:** Medium
- **Risk:** Low

### Option C: Metrics/Health Check Integration
- **Pros:** Observable without breaking, alertable
- **Cons:** More infrastructure
- **Effort:** Large
- **Risk:** Low

---

## Recommended Action

**Option B** - Add `ThrowOnSchemaProcessingError` option to `HeadlessNswagOptions`, default `false` for backwards compatibility, recommend `true` in development.

---

## Technical Details

**Affected Files:**
- `src/Framework.OpenApi.Nswag/SchemaProcessors/FluentValidationSchemaProcessor.cs` (lines 56-63, 105-127, 152-173)
- `src/Framework.OpenApi.Nswag/HeadlessNswagOptions.cs`

---

## Acceptance Criteria

- [ ] Option to throw on schema processing errors
- [ ] Default behavior unchanged (warnings only)
- [ ] Documentation recommends strict mode in dev
- [ ] Log level upgraded to Error

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
