# LINQ Allocation in Hot Path (List.Exists)

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, dotnet, slugs

---

## Problem Statement

`IsAllowed` uses `List<T>.Exists()` with a lambda capturing the `character` parameter:

```csharp
// SlugOptions.cs:45-46
return AllowedRanges.Count == 0
    || AllowedRanges.Exists(range => _IsInRange(range, character))  // Lambda captures!
```

**Why it matters:**
- Lambda captures `character` - cannot be cached by compiler
- Delegate allocation on **every call**
- Called for **every rune** in input
- 100-char input = 100 delegate allocations

---

## Proposed Solutions

### Option A: Use Simple For Loop
```csharp
public bool IsAllowed(Rune character)
{
    if (AllowedRanges.Count == 0) return true;

    for (int i = 0; i < AllowedRanges.Count; i++)
    {
        if (_IsInRange(AllowedRanges[i], character))
            return true;
    }

    return Replacements.ContainsKey(character.ToString());
}
```
- **Pros:** Zero allocation, clear intent
- **Cons:** More lines of code
- **Effort:** Small
- **Risk:** None

### Option B: Use Span<UnicodeRange> with CollectionsMarshal
```csharp
var span = CollectionsMarshal.AsSpan(AllowedRanges);
foreach (var range in span)
{
    if (_IsInRange(range, character)) return true;
}
```
- **Pros:** Zero allocation, efficient iteration
- **Cons:** Requires System.Runtime.InteropServices
- **Effort:** Small
- **Risk:** Low

### Option C: Pre-compute BitVector for ASCII
```csharp
// Pre-compute allowed ASCII in constructor
private readonly UInt128 _allowedAscii;

public bool IsAllowed(Rune rune)
{
    if (rune.Value < 128)
        return (_allowedAscii & (1UL << rune.Value)) != 0;
    return _IsInRangesSlow(rune);
}
```
- **Pros:** O(1) for ASCII (95%+ of typical input)
- **Cons:** More complex, two code paths
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** - Simple for loop. Clearest, no dependencies.

---

## Technical Details

**Affected Files:**
- `src/Framework.Slugs/SlugOptions.cs` (lines 43-48)

---

## Acceptance Criteria

- [ ] No `List.Exists` with lambda in hot path
- [ ] Use simple for loop or span iteration
- [ ] Benchmark shows no regression
- [ ] Tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, performance-oracle |
