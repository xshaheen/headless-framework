# String Allocations in GetIndentation()

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, performance, source-generator, allocations

---

## Problem Statement

`GetIndentation()` creates new strings via `string.Concat(Enumerable.Repeat(...))` which allocates multiple times.

**Location:** `src/Framework.Generator.Primitives/Shared/SourceCodeBuilder.cs:53-56`

```csharp
public static string GetIndentation(int count = 1)
{
    return string.Concat(Enumerable.Repeat(_IndentationString, count));
}
```

Called frequently in `createInheritedInterfaces()` (line 403) and `AppendIndentation()` (line 71).

**Why it matters:**
- Creates new string for every call
- Allocates IEnumerable iterator
- Common indentation levels (1-4) could be cached

---

## Proposed Solutions

### Option A: Pre-Cache Common Indentation Levels (Recommended)
```csharp
private static readonly string[] _CachedIndentations = Enumerable.Range(0, 11)
    .Select(i => string.Concat(Enumerable.Repeat(_IndentationString, i)))
    .ToArray();

public static string GetIndentation(int count = 1)
{
    return count < _CachedIndentations.Length
        ? _CachedIndentations[count]
        : string.Concat(Enumerable.Repeat(_IndentationString, count));
}
```
- **Pros:** Zero allocations for common cases (0-10 indents)
- **Cons:** Small static memory footprint
- **Effort:** Small
- **Risk:** None

---

## Recommended Action

**Option A** - Simple optimization with clear benefit.

---

## Technical Details

**Affected Files:**
- `src/Framework.Generator.Primitives/Shared/SourceCodeBuilder.cs` (lines 53-56)

---

## Acceptance Criteria

- [ ] Common indentation levels cached
- [ ] No functional change
- [ ] All tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From performance-oracle code review |
