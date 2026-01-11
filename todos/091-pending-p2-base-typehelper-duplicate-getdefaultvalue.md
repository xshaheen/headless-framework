# TypeHelper and TypeExtensions Have Duplicate GetDefaultValue

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, duplication, dotnet

---

## Problem Statement

`GetDefaultValue(Type type)` is implemented identically in two places:

**TypeHelper.cs (lines 68-76):**
```csharp
public static object? GetDefaultValue(Type type)
{
    return type.IsValueType ? Activator.CreateInstance(type) : null;
}
```

**TypeExtensions.cs (lines 144-147):**
```csharp
public static object? GetDefaultValue(this Type type)
{
    return type.IsValueType ? Activator.CreateInstance(type) : null;
}
```

**Why it matters:**
- Duplicate code that must be kept in sync
- Confusing for consumers - which one to use?
- Violates DRY principle

---

## Proposed Solutions

### Option A: TypeExtensions Delegates to TypeHelper
```csharp
// TypeExtensions.cs
public static object? GetDefaultValue(this Type type) => TypeHelper.GetDefaultValue(type);
```
- **Pros:** Single source of truth
- **Cons:** Extra method call
- **Effort:** Small
- **Risk:** Low

### Option B: Remove TypeHelper Version
- Keep only extension method version
- **Pros:** Simpler API
- **Cons:** Breaking change if someone uses `TypeHelper.GetDefaultValue()`
- **Effort:** Small
- **Risk:** Medium

---

## Recommended Action

**Option A** - Have TypeExtensions delegate to TypeHelper.

---

## Technical Details

**Affected Files:**
- `src/Framework.Base/Reflection/TypeHelper.cs` (lines 68-76)
- `src/Framework.Base/Reflection/TypeExtensions.cs` (lines 144-147)

---

## Acceptance Criteria

- [ ] Single implementation of the logic
- [ ] Extension method delegates to helper (or vice versa)
- [ ] Both APIs still work

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pattern-recognition-specialist |
