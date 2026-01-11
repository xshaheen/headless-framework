# ValidationRuleContext.IsCollectionRule Never Used

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-have
**Tags:** code-review, dead-code, dotnet

---

## Problem Statement

In `ValidationRuleContext.cs`:

```csharp
public readonly record struct ValidationRuleContext(IValidationRule ValidationRule, bool IsCollectionRule);
```

And in `ValidationExtensions.cs` (lines 37-42):

```csharp
public static IEnumerable<ValidationRuleContext> GetPropertyRules(this IEnumerable<IValidationRule> validationRules)
{
    return from validationRule in validationRules
        let isCollectionRule = validationRule.GetType() == typeof(ICollectionRule<,>)  // Computed but...
        select new ValidationRuleContext(validationRule, isCollectionRule);
}
```

But `IsCollectionRule` is never read anywhere in the codebase.

**Why it matters:**
- Dead code adds confusion
- Computing `isCollectionRule` is wasted effort (albeit minimal)

---

## Proposed Solutions

### Option A: Remove IsCollectionRule
Simplify to just return `IValidationRule` directly since the flag isn't used.
- **Pros:** Cleaner code
- **Cons:** May have been intended for future use
- **Effort:** Small
- **Risk:** Low

### Option B: Document Future Intent
Add TODO comment explaining planned usage.
- **Pros:** Preserves intent
- **Cons:** Still dead code
- **Effort:** Trivial
- **Risk:** Low

---

## Recommended Action

**Option A** - Remove unless there's documented future need.

---

## Technical Details

**Affected Files:**
- `src/Framework.OpenApi.Nswag/SchemaProcessors/FluentValidation/Models/ValidationRuleContext.cs`
- `src/Framework.OpenApi.Nswag/SchemaProcessors/FluentValidation/ValidationExtensions.cs` (lines 37-42)

---

## Acceptance Criteria

- [ ] IsCollectionRule removed or documented

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
