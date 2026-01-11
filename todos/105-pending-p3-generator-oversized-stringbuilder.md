# Oversized StringBuilder Allocation

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, performance, source-generator, allocations

---

## Problem Statement

`createInheritedInterfaces()` allocates an 8KB StringBuilder for every type processed.

**Location:** `src/Framework.Generator.Primitives/Helpers/PrimitiveSourceFilesGeneratorEmitter.cs:296`

```csharp
static string createInheritedInterfaces(GeneratorData data, string className)
{
    var sb = new StringBuilder(8096);  // 8KB allocation per type
    // ...
}
```

**Why it matters:**
- 8KB is likely oversized for interface lists
- Allocated for every primitive type
- Typical output is ~500-1500 characters

---

## Proposed Solutions

### Option A: Reduce Initial Capacity (Recommended)
```csharp
var sb = new StringBuilder(512);  // More reasonable estimate
```
- **Pros:** Reduced memory pressure
- **Cons:** May need to grow for complex types (minor)
- **Effort:** Trivial
- **Risk:** None

### Option B: Use StringBuilder Pooling
```csharp
var sb = StringBuilderPool.Get();
try { /* use sb */ return sb.ToString(); }
finally { StringBuilderPool.Return(sb); }
```
- **Pros:** Reuses StringBuilder instances
- **Cons:** More complex, needs pool implementation
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** - Simple fix with clear benefit.

---

## Technical Details

**Affected Files:**
- `src/Framework.Generator.Primitives/Helpers/PrimitiveSourceFilesGeneratorEmitter.cs` (line 296)

---

## Acceptance Criteria

- [ ] StringBuilder initial capacity reduced to reasonable size
- [ ] No functional change
- [ ] All tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From performance-oracle code review |
