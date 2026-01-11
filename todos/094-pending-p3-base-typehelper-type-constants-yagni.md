# TypeHelper Type Constants are YAGNI

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-have
**Tags:** code-review, simplification, yagni, dotnet

---

## Problem Statement

`TypeHelper.cs` has 20+ type constants that provide no value:

```csharp
public static readonly Type ObjectType = typeof(object);
public static readonly Type StringType = typeof(string);
public static readonly Type BoolType = typeof(bool);
public static readonly Type Int16Type = typeof(short);
public static readonly Type Int32Type = typeof(int);
// ... 15+ more
```

**Why it matters:**
- `typeof(string)` is already simple and is a compiler constant
- These constants add indirection without benefit
- ~25 LOC of noise
- Clutters API with rarely-used members

---

## Proposed Solutions

### Option A: Delete All Type Constants
- Remove constants, callers use `typeof(X)` directly
- **Pros:** Simpler, cleaner
- **Cons:** Breaking change
- **Effort:** Small
- **Risk:** Medium

### Option B: Deprecate
```csharp
[Obsolete("Use typeof(string) directly")]
public static readonly Type StringType = typeof(string);
```
- **Pros:** Gradual migration
- **Cons:** Keeps noise
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** if usage is minimal, **Option B** otherwise.

---

## Technical Details

**Affected Files:**
- `src/Framework.Base/Reflection/TypeHelper.cs` (lines 11-30)

---

## Acceptance Criteria

- [ ] Type constants removed or deprecated
- [ ] Callers use `typeof(X)` directly

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - code-simplicity-reviewer |
