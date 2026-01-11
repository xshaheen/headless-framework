# GenerateConvertibles Repetitive Pattern

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, quality, source-generator, code-duplication

---

## Problem Statement

`GenerateConvertibles` contains 17 nearly identical method generations, each appending the same pattern.

**Location:** `src/Framework.Generator.Primitives/Helpers/MethodGeneratorEmitter.cs:702-834`

```csharp
// Pattern repeats 17 times:
builder.AppendInheritDoc();
builder.Append($"bool {TypeNames.IConvertible}.ToBoolean({TypeNames.IFormatProvider}? provider)")
    .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToBoolean(provider);")
    .NewLine();

builder.AppendInheritDoc();
builder.Append($"byte {TypeNames.IConvertible}.ToByte({TypeNames.IFormatProvider}? provider)")
    .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToByte(provider);")
    .NewLine();
// ... 15 more
```

**Why it matters:**
- ~130 lines of repetitive code
- Each method has same structure with different type names
- Hard to maintain consistency

---

## Proposed Solutions

### Option A: Loop Over Method Definitions (Recommended)
```csharp
var methods = new (string ReturnType, string MethodName)[]
{
    ("bool", "ToBoolean"),
    ("byte", "ToByte"),
    ("char", "ToChar"),
    ("DateTime", "ToDateTime"),
    ("decimal", "ToDecimal"),
    ("double", "ToDouble"),
    ("short", "ToInt16"),
    ("int", "ToInt32"),
    ("long", "ToInt64"),
    ("sbyte", "ToSByte"),
    ("float", "ToSingle"),
    ("string", "ToString"),
    ("ushort", "ToUInt16"),
    ("uint", "ToUInt32"),
    ("ulong", "ToUInt64"),
};

foreach (var (returnType, methodName) in methods)
{
    builder.AppendInheritDoc();
    builder.Append($"{returnType} {TypeNames.IConvertible}.{methodName}({TypeNames.IFormatProvider}? provider)")
        .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).{methodName}(provider);")
        .NewLine();
}

// Handle special cases (GetTypeCode, ToType) separately
```
- **Pros:** ~80 LOC saved, easier to maintain
- **Cons:** Slightly less explicit
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** - Extract to loop for maintainability.

---

## Technical Details

**Affected Files:**
- `src/Framework.Generator.Primitives/Helpers/MethodGeneratorEmitter.cs` (lines 702-834)

**Special cases:**
- `GetTypeCode` - no provider parameter
- `ToType` - extra `Type conversionType` parameter

---

## Acceptance Criteria

- [ ] Repetitive method generation consolidated to loop
- [ ] Generated code unchanged
- [ ] All tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code-simplicity code review |
