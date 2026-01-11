# Duplicate Nullable Handling in NotNull and NotEmpty Rules

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, duplication, refactor, dotnet

---

## Problem Statement

In `FluentValidationRule.cs`, `NotNullRule` (lines 42-66) and `NotEmptyRule` (lines 68-94) have identical nullable handling logic:

```csharp
// NotNullRule (lines 46-65)
propertySchema.IsNullableRaw = false;
if (propertySchema.Type.HasFlag(JsonObjectType.Null))
{
    propertySchema.Type &= ~JsonObjectType.Null;
}
var oneOfsWithReference = propertySchema.OneOf.Where(x => x.Reference is not null).ToList();
if (oneOfsWithReference.Count == 1)
{
    propertySchema.Reference = oneOfsWithReference.Single();
    propertySchema.OneOf.Clear();
}

// NotEmptyRule (lines 74-93) - EXACT SAME CODE
propertySchema.IsNullableRaw = false;
if (propertySchema.Type.HasFlag(JsonObjectType.Null))
{
    propertySchema.Type &= ~JsonObjectType.Null;
}
// ... same OneOf handling ...
```

**Why it matters:**
- DRY violation - 15 lines duplicated
- Bug fixes must be applied twice
- Easy to diverge accidentally

---

## Proposed Solutions

### Option A: Extract Helper Method
```csharp
private static void _RemoveNullability(JsonSchema propertySchema)
{
    propertySchema.IsNullableRaw = false;
    if (propertySchema.Type.HasFlag(JsonObjectType.Null))
        propertySchema.Type &= ~JsonObjectType.Null;

    var oneOfsWithReference = propertySchema.OneOf.Where(x => x.Reference is not null).ToList();
    if (oneOfsWithReference.Count == 1)
    {
        propertySchema.Reference = oneOfsWithReference.Single();
        propertySchema.OneOf.Clear();
    }
}
```
- **Pros:** Single source of truth, cleaner rules
- **Cons:** Minor indirection
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Extract to private static helper method.

---

## Technical Details

**Affected Files:**
- `src/Framework.OpenApi.Nswag/SchemaProcessors/FluentValidation/Models/FluentValidationRule.cs` (lines 42-94)

---

## Acceptance Criteria

- [ ] Duplicate code extracted to helper
- [ ] Both rules use shared helper
- [ ] Behavior unchanged

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
