# Repeated ToDisplayString() Calls in Hot Paths

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, source-generator, allocations

---

## Problem Statement

`ToDisplayString()` is an expensive Roslyn operation that allocates strings and performs formatting. It's called multiple times on the same symbol in hot paths.

**Locations:**
- `src/Framework.Generator.Primitives/Extensions/CompilationExtensions.cs:18-22` - `IsImplementIPrimitive`
- `src/Framework.Generator.Primitives/Shared/CompilationExtensions.cs:103-123` - `ImplementsInterface`
- Multiple attribute class comparisons throughout `Emitter.cs`

```csharp
// IsImplementIPrimitive - called for EVERY interface on EVERY type
return x is { IsGenericType: true, Name: AbstractionConstants.Interface }
    && string.Equals(
        x.ContainingNamespace.ToDisplayString(),  // EXPENSIVE
        AbstractionConstants.Namespace,
        StringComparison.Ordinal
    );
```

**Why it matters:**
- With 100 primitive types, each with 5-10 interface checks = 500-1000+ calls
- Each call allocates a new string
- Significant impact on IDE responsiveness

---

## Proposed Solutions

### Option A: Compare Namespace Symbols Directly (Recommended)
```csharp
// Cache the target namespace symbol once
private static INamespaceSymbol? _primitiveNamespace;

public static bool IsImplementIPrimitive(this INamedTypeSymbol x, Compilation compilation)
{
    _primitiveNamespace ??= compilation.GetTypeByMetadataName(
        "Framework.Generator.Primitives.IPrimitive`1")?.ContainingNamespace;

    return x is { IsGenericType: true, Name: AbstractionConstants.Interface }
        && SymbolEqualityComparer.Default.Equals(x.ContainingNamespace, _primitiveNamespace);
}
```
- **Pros:** No string allocation, faster comparison
- **Cons:** Requires compilation reference
- **Effort:** Medium
- **Risk:** Low

### Option B: Cache Display Strings in Dictionary
```csharp
private static readonly ConcurrentDictionary<INamespaceSymbol, string> _namespaceCache = new(SymbolEqualityComparer.Default);

public static string GetCachedDisplayString(this INamespaceSymbol ns) =>
    _namespaceCache.GetOrAdd(ns, n => n.ToDisplayString());
```
- **Pros:** Simple change
- **Cons:** Still allocates once per unique namespace
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** for `IsImplementIPrimitive` (most frequently called), **Option B** for other cases.

---

## Technical Details

**Affected Files:**
- `src/Framework.Generator.Primitives/Extensions/CompilationExtensions.cs` (lines 15-23)
- `src/Framework.Generator.Primitives/Shared/CompilationExtensions.cs` (lines 103-123)
- `src/Framework.Generator.Primitives/Emitter.cs` (multiple attribute comparisons)

---

## Acceptance Criteria

- [ ] `IsImplementIPrimitive` uses symbol comparison instead of string
- [ ] Other ToDisplayString calls are cached
- [ ] No regression in generated code
- [ ] Measurable improvement in generator performance

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From performance-oracle code review |
