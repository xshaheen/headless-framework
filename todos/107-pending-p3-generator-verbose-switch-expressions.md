# Verbose Switch Expressions in Type Extensions

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, quality, source-generator, code-simplification

---

## Problem Statement

`IsNumeric`, `IsDateOrTime`, `IsByteOrShort`, `IsFloatingPoint` all use verbose `Type => true` patterns.

**Location:** `src/Framework.Generator.Primitives/Extensions/PrimitiveUnderlyingTypeExtensions.cs:48-114`

```csharp
// Current - 17 lines
public static bool IsNumeric(this PrimitiveUnderlyingType underlyingType)
{
    return underlyingType switch
    {
        PrimitiveUnderlyingType.Byte => true,
        PrimitiveUnderlyingType.SByte => true,
        PrimitiveUnderlyingType.Int16 => true,
        PrimitiveUnderlyingType.Int32 => true,
        PrimitiveUnderlyingType.Int64 => true,
        PrimitiveUnderlyingType.UInt16 => true,
        PrimitiveUnderlyingType.UInt32 => true,
        PrimitiveUnderlyingType.UInt64 => true,
        PrimitiveUnderlyingType.Decimal => true,
        PrimitiveUnderlyingType.Double => true,
        PrimitiveUnderlyingType.Single => true,
        _ => false,
    };
}
```

**Why it matters:**
- Verbose and repetitive
- Modern C# `is` pattern is more concise
- Same pattern in 4 methods

---

## Proposed Solutions

### Option A: Use `is` Pattern with `or` (Recommended)
```csharp
// Proposed - 2 lines
public static bool IsNumeric(this PrimitiveUnderlyingType type) =>
    type is PrimitiveUnderlyingType.Byte or PrimitiveUnderlyingType.SByte
        or PrimitiveUnderlyingType.Int16 or PrimitiveUnderlyingType.Int32
        or PrimitiveUnderlyingType.Int64 or PrimitiveUnderlyingType.UInt16
        or PrimitiveUnderlyingType.UInt32 or PrimitiveUnderlyingType.UInt64
        or PrimitiveUnderlyingType.Decimal or PrimitiveUnderlyingType.Double
        or PrimitiveUnderlyingType.Single;
```
- **Pros:** ~30 LOC saved across 4 methods, more idiomatic
- **Cons:** Long line (can break across lines)
- **Effort:** Small
- **Risk:** None

---

## Recommended Action

**Option A** - Use modern C# pattern matching.

---

## Technical Details

**Affected Files:**
- `src/Framework.Generator.Primitives/Extensions/PrimitiveUnderlyingTypeExtensions.cs` (lines 48-114)

**Methods to update:**
- `IsNumeric` (lines 48-66)
- `IsDateOrTime` (lines 68-83)
- `IsFloatingPoint` (lines 85-98)
- `IsByteOrShort` (lines 100-114)

---

## Acceptance Criteria

- [ ] All 4 methods use `is` pattern
- [ ] No functional change
- [ ] All tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code-simplicity code review |
