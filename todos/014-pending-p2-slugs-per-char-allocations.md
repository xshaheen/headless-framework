# Per-Character String Allocations in Hot Path

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, dotnet, slugs

---

## Problem Statement

Two locations allocate strings for **every character** in input:

**1. IsAllowed() - SlugOptions.cs:47**
```csharp
|| Replacements.ContainsKey(character.ToString());  // Allocates!
```

**2. Replace() - SlugOptions.cs:63**
```csharp
return rune.ToString();  // Allocates!
```

**3. Append in loop - Slug.cs:38**
```csharp
sb.Append(options.Replace(rune));  // Replace returns string
```

**Why it matters:**
- 100-char input = ~200 string allocations
- Gen0 GC pressure at scale
- `Rune.ToString()` allocates 2-4 bytes + object header per call

**Impact at scale:**
- 1M slugs/sec = ~200M string allocations/sec
- Significant CPU time in GC

---

## Proposed Solutions

### Option A: Use Codepoint Key for Replacements
```csharp
// Change Replacements to use int (codepoint) keys for single chars
private Dictionary<int, string> _singleCharReplacements;

public bool IsAllowed(Rune rune)
{
    // No allocation for lookup
    return _singleCharReplacements.ContainsKey(rune.Value);
}
```
- **Pros:** Zero allocation for lookup
- **Cons:** Separate handling for multi-char replacements
- **Effort:** Medium
- **Risk:** Low

### Option B: Use StringBuilder.Append(Rune) Directly
```csharp
// Slug.cs - append rune directly after casing
var transformed = CasingTransformation switch
{
    ToLowerCase => Rune.ToLowerInvariant(rune),
    ToUpperCase => Rune.ToUpperInvariant(rune),
    _ => rune,
};
sb.Append(transformed);  // No string allocation!
```
- **Pros:** Zero allocation per character
- **Cons:** Moves casing logic into Slug.Create
- **Effort:** Small
- **Risk:** Low

### Option C: Stackalloc for Rune Lookup
```csharp
Span<char> buffer = stackalloc char[2];
int written = rune.EncodeToUtf16(buffer);
// Use for lookup without allocation
```
- **Pros:** Zero heap allocation
- **Cons:** More complex code
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option B** - Simplest fix. Use `sb.Append(rune)` directly.

---

## Technical Details

**Affected Files:**
- `src/Framework.Slugs/SlugOptions.cs` (lines 47, 50-64)
- `src/Framework.Slugs/Slug.cs` (line 38)

**Expected Improvement:**
- -40% to -50% heap allocations per slug

---

## Acceptance Criteria

- [ ] No `ToString()` on Rune in hot path
- [ ] `sb.Append(rune)` used directly
- [ ] Benchmark before/after shows improvement
- [ ] Tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, performance-oracle |
