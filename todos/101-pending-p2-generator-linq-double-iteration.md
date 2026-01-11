# LINQ Any()/First() Double Iteration Pattern

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, source-generator, linq

---

## Problem Statement

Code uses `Any()` to check existence, then `First()` to retrieve the same item, causing double iteration over `AllInterfaces`.

**Locations:**
- `src/Framework.Generator.Primitives/Parser.cs:54`
- `src/Framework.Generator.Primitives/Emitter.cs:171`

```csharp
// Parser.cs:54 - checks if any interface matches
return symbol?.IsAbstract == false && symbol.AllInterfaces.Any(x => x.IsImplementIPrimitive())
    ? symbol : null;

// Emitter.cs:171 - retrieves the same interface again
var interfaceType = typeSymbol.AllInterfaces.First(x => x.IsImplementIPrimitive());
```

**Why it matters:**
- Iterates `AllInterfaces` twice - once to check, once to retrieve
- For types with many interfaces, this compounds quickly
- Unnecessary allocations from iterator creation

---

## Proposed Solutions

### Option A: Single-Pass Loop Pattern (Recommended)
```csharp
// Parser.cs
INamedTypeSymbol? primitiveInterface = null;
foreach (var iface in symbol.AllInterfaces)
{
    if (iface.IsImplementIPrimitive())
    {
        primitiveInterface = iface;
        break;
    }
}
return primitiveInterface is not null ? symbol : null;

// Then pass primitiveInterface through to Emitter or store in model
```
- **Pros:** Single iteration, no LINQ overhead
- **Cons:** More verbose
- **Effort:** Small
- **Risk:** Low

### Option B: Use FirstOrDefault and Check Result
```csharp
var primitiveInterface = symbol.AllInterfaces.FirstOrDefault(x => x.IsImplementIPrimitive());
return primitiveInterface is not null ? symbol : null;
```
- **Pros:** Single iteration, concise
- **Cons:** Still has LINQ overhead (minor)
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** for hot paths, **Option B** for less critical code.

---

## Technical Details

**Affected Files:**
- `src/Framework.Generator.Primitives/Parser.cs` (line 54)
- `src/Framework.Generator.Primitives/Emitter.cs` (line 171)

---

## Acceptance Criteria

- [ ] AllInterfaces iterated only once per type
- [ ] Interface reference passed through pipeline (not re-queried)
- [ ] All tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From performance-oracle code review |
