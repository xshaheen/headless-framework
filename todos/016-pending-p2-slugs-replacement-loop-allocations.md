# Replacement Loop String Allocations

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, dotnet, slugs

---

## Problem Statement

The replacement loop allocates multiple strings per iteration:

```csharp
// Slug.cs:20-26
foreach (var (value, replacement) in options.Replacements)
{
    var newValue = replacement.EndsWith(' ') ? replacement : replacement + " ";  // Alloc
    newValue = replacement.StartsWith(' ') ? newValue : " " + newValue;          // Alloc
    text = text.Replace(value, newValue, StringComparison.Ordinal);              // Alloc
}
```

**Why it matters:**
- 4 default replacements = up to 12 allocations
- `string.Replace()` allocates even when no match found
- Padding logic allocates 2 strings per replacement
- Processing happens before MaximumLength truncation

---

## Proposed Solutions

### Option A: Pre-compute Padded Replacements
```csharp
// In SlugOptions constructor or lazy
private FrozenDictionary<string, string>? _paddedReplacements;

public FrozenDictionary<string, string> GetPaddedReplacements()
{
    return _paddedReplacements ??= Replacements
        .ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => $" {kvp.Value} ");  // Pre-padded
}
```
- **Pros:** Zero runtime padding allocations
- **Cons:** Requires FrozenDictionary or caching
- **Effort:** Small
- **Risk:** Low

### Option B: Store Replacements with Spaces Already
```csharp
public Dictionary<string, string> Replacements { get; } = new(StringComparer.Ordinal)
{
    { "&", " and " },
    { "+", " plus " },
    { ".", " dot " },
    { "%", " percent " },
};
```
- **Pros:** Zero runtime processing
- **Cons:** Less flexible, docs must explain spaces
- **Effort:** Trivial
- **Risk:** Low

### Option C: Single-Pass Replacement with SearchValues
```csharp
// .NET 9+: Use SearchValues<string> for O(n) multi-pattern search
```
- **Pros:** O(n) vs O(n*m), single allocation
- **Cons:** Requires .NET 9+, complex
- **Effort:** Large
- **Risk:** Medium

---

## Recommended Action

**Option B** - Simplest. Store replacements with spaces.

---

## Technical Details

**Affected Files:**
- `src/Framework.Slugs/Slug.cs` (lines 20-26)
- `src/Framework.Slugs/SlugOptions.cs` (lines 34-41)

---

## Acceptance Criteria

- [ ] No runtime string concatenation for padding
- [ ] Replacements stored with spaces already included
- [ ] Documentation updated to explain format
- [ ] Tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, performance-oracle |
