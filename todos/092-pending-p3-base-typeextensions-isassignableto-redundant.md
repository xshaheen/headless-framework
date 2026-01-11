# TypeExtensions.IsAssignableTo is Redundant

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-have
**Tags:** code-review, modernization, dotnet

---

## Problem Statement

`IsAssignableTo` extension methods duplicate built-in .NET 5+ functionality:

```csharp
// Framework.Base version (lines 247-269)
public static bool IsAssignableTo<TTarget>(this Type type)
{
    return type.IsAssignableTo(typeof(TTarget));
}

public static bool IsAssignableTo(this Type type, Type targetType)
{
    return targetType.IsAssignableFrom(type);
}
```

.NET 5 added `Type.IsAssignableTo(Type)` as a built-in method.

**Why it matters:**
- Redundant with BCL
- Extension shadows instance method (confusing)
- ~25 LOC that serve no purpose on .NET 10

---

## Proposed Solutions

### Option A: Deprecate and Remove
```csharp
[Obsolete("Use Type.IsAssignableTo() directly. Available since .NET 5.")]
public static bool IsAssignableTo<TTarget>(this Type type) => type.IsAssignableTo(typeof(TTarget));
```
- **Pros:** Guides users to BCL
- **Cons:** Breaking change eventually
- **Effort:** Small
- **Risk:** Low

### Option B: Keep Generic Overload Only
- BCL doesn't have generic `IsAssignableTo<T>()` version
- Keep just that overload
- **Pros:** Provides value BCL doesn't
- **Cons:** Still maintaining custom code
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option B** - Keep only `IsAssignableTo<TTarget>()` generic version, remove non-generic.

---

## Technical Details

**Affected Files:**
- `src/Framework.Base/Reflection/TypeExtensions.cs` (lines 247-269)

---

## Acceptance Criteria

- [ ] Non-generic `IsAssignableTo(Type)` removed or deprecated
- [ ] Generic version kept (provides value over BCL)

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - code-simplicity-reviewer |
