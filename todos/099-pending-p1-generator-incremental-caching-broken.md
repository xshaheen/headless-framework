# Generator Incremental Caching Broken

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, performance, source-generator, incremental-compilation

---

## Problem Statement

The semantic transform in `Parser.cs` returns `INamedTypeSymbol?` which is NOT equatable by the incremental generator pipeline. This defeats the entire purpose of incremental generation.

**Location:** `src/Framework.Generator.Primitives/Parser.cs:46-54`

```csharp
internal static INamedTypeSymbol? GetSemanticTargetForGeneration(
    GeneratorSyntaxContext context,
    CancellationToken ct
)
{
    // ...
    return symbol?.IsAbstract == false && symbol.AllInterfaces.Any(x => x.IsImplementIPrimitive())
        ? symbol : null;
}
```

**Why it matters:**
- Every recompilation regenerates ALL outputs even if nothing changed
- IDE performance degrades significantly with more primitive types
- Build times increase unnecessarily
- Defeats the core benefit of incremental generators

**Projected Impact:** With 100 primitive types, generator runs on every keystroke instead of only when files change.

---

## Proposed Solutions

### Option A: Transform to Equatable Record Struct (Recommended)
```csharp
public readonly record struct PrimitiveTypeInfo(
    string FullyQualifiedName,
    string Namespace,
    string ClassName,
    bool IsValueType,
    string UnderlyingTypeName,
    // ... other needed data extracted from INamedTypeSymbol
) : IEquatable<PrimitiveTypeInfo>;

internal static PrimitiveTypeInfo? GetSemanticTargetForGeneration(...)
{
    // Extract all needed data into the record struct
    return new PrimitiveTypeInfo(...);
}
```
- **Pros:** Correct incremental behavior, huge performance win
- **Cons:** Requires significant refactoring of Emitter to use extracted data
- **Effort:** Large
- **Risk:** Medium - need thorough testing

### Option B: Custom IEquatable Implementation
Create a wrapper class that implements proper equality based on symbol identity.
- **Pros:** Less refactoring
- **Cons:** Still carries symbol reference, may have other issues
- **Effort:** Medium
- **Risk:** Medium

---

## Recommended Action

**Option A** - This is the correct Roslyn incremental generator pattern. All data needed for emission should be extracted in the transform phase.

---

## Technical Details

**Affected Files:**
- `src/Framework.Generator.Primitives/Parser.cs` (lines 46-54)
- `src/Framework.Generator.Primitives/Emitter.cs` (must adapt to use extracted data)
- `src/Framework.Generator.Primitives/Models/GeneratorData.cs` (may need restructuring)

**Related Documentation:**
- https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md

---

## Acceptance Criteria

- [ ] Semantic transform returns an equatable value type
- [ ] Generator only re-runs when primitive type definitions change
- [ ] All existing tests pass
- [ ] Verify incremental behavior with breakpoints/logging

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From performance-oracle code review |
