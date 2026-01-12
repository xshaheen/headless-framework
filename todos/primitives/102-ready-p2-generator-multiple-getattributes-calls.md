# Multiple GetAttributes() Calls on Same Symbol

**Date:** 2026-01-11
**Status:** ready
**Priority:** P2 - Important
**Tags:** code-review, performance, source-generator, allocations

---

## Problem Statement

`GetAttributes()` is called multiple times on the same symbol, and each attribute search iterates the full list with `ToDisplayString()` comparisons.

**Locations:**
- `src/Framework.Generator.Primitives/Emitter.cs:209-225` - two FirstOrDefault searches
- `src/Framework.Generator.Primitives/Emitter.cs:323-331` - called again in nested function
- `src/Framework.Generator.Primitives/Emitter.cs:583-591` - called again

```csharp
// Line 209
var attributes = typeSymbol.GetAttributes();

// Line 211-217 - first search with ToDisplayString
var attributeData = attributes.FirstOrDefault(x =>
    string.Equals(x.AttributeClass?.ToDisplayString(), ...));

// Line 219-225 - second search with ToDisplayString
var serializationAttribute = attributes.FirstOrDefault(x =>
    string.Equals(x.AttributeClass?.ToDisplayString(), ...));
```

**Why it matters:**
- `ToDisplayString()` called for EVERY attribute during EVERY search
- Multiple passes over same attribute collection
- Scales poorly with number of attributes

---

## Proposed Solutions

### Option A: Single-Pass Attribute Extraction (Recommended)
```csharp
var supportedOps = default(AttributeData?);
var serialization = default(AttributeData?);
var stringLength = default(AttributeData?);

foreach (var attr in typeSymbol.GetAttributes())
{
    var name = attr.AttributeClass?.Name;
    switch (name)
    {
        case "SupportedOperationsAttribute":
            supportedOps = attr;
            break;
        case "SerializationFormatAttribute":
            serialization = attr;
            break;
        case "StringLengthAttribute":
            stringLength = attr;
            break;
    }
}
```
- **Pros:** Single iteration, compare by Name (cheap) not ToDisplayString (expensive)
- **Cons:** More verbose
- **Effort:** Medium
- **Risk:** Low

### Option B: Cache ToDisplayString Results
Use dictionary to cache attribute class display strings.
- **Pros:** Reduces duplicate ToDisplayString calls
- **Cons:** Still multiple iterations
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Single-pass extraction is the correct pattern for source generators.

---

## Technical Details

**Affected Files:**
- `src/Framework.Generator.Primitives/Emitter.cs` (lines 209-225, 323-331, 583-591)

---

## Acceptance Criteria

- [ ] GetAttributes() called once per symbol
- [ ] All needed attributes extracted in single pass
- [ ] Use Name comparison instead of ToDisplayString where possible
- [ ] All tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From performance-oracle code review |
| 2026-01-12 | Approved | Triage: pending → ready |
