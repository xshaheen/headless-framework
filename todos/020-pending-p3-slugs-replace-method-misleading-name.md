# Replace Method Name is Misleading

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, naming, dotnet, slugs

---

## Problem Statement

`SlugOptions.Replace()` doesn't replace anything - it applies casing transformation:

```csharp
// SlugOptions.cs:50-64
public string Replace(Rune rune)
{
    rune = CasingTransformation switch
    {
        CasingTransformation.ToLowerCase => Culture is null
            ? Rune.ToLowerInvariant(rune)
            : Rune.ToLower(rune, Culture),
        CasingTransformation.ToUpperCase => Culture is null
            ? Rune.ToUpperInvariant(rune)
            : Rune.ToUpper(rune, Culture),
        _ => rune,
    };

    return rune.ToString();
}
```

**Why it matters:**
- Name implies character substitution
- Actually does casing transformation
- Confusing for maintainers
- `Replacements` dictionary handles actual replacements

---

## Proposed Solutions

### Option A: Rename to TransformCase
```csharp
public string TransformCase(Rune rune) { ... }
```
- **Pros:** Accurate name
- **Cons:** Breaking change for anyone using it
- **Effort:** Trivial
- **Risk:** Low (internal usage)

### Option B: Inline into Slug.Create
```csharp
// In Slug.Create loop:
var transformed = options.CasingTransformation switch
{
    ToLowerCase => Rune.ToLowerInvariant(rune),
    ToUpperCase => Rune.ToUpperInvariant(rune),
    _ => rune,
};
sb.Append(transformed);
```
- **Pros:** Removes method entirely, fixes allocation issue
- **Cons:** Duplicates logic if called elsewhere
- **Effort:** Small
- **Risk:** Low

### Option C: Make Private/Internal
```csharp
internal string ApplyCasing(Rune rune) { ... }
```
- **Pros:** Clear it's internal detail
- **Cons:** Still misleading name
- **Effort:** Trivial
- **Risk:** None

---

## Recommended Action

**Option B** - Inline into Slug.Create. Fixes naming AND allocation (no ToString()).

---

## Technical Details

**Affected Files:**
- `src/Framework.Slugs/SlugOptions.cs` (lines 50-64)
- `src/Framework.Slugs/Slug.cs` (line 38)

---

## Acceptance Criteria

- [ ] Method renamed or removed
- [ ] Logic inlined if removed
- [ ] No public API with misleading name

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, code-simplicity |
