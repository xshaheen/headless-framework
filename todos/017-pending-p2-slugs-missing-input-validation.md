# Missing Input Length Validation

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, security, performance, dotnet, slugs

---

## Problem Statement

`Slug.Create()` accepts input of any length before applying MaximumLength:

```csharp
// Slug.cs:18 - Normalization on unbounded input
text = text.Normalize(NormalizationForm.FormD);

// Slug.cs:20-26 - Replacement loop on full string
foreach (var (value, replacement) in options.Replacements)
{
    text = text.Replace(value, newValue, StringComparison.Ordinal);
}
```

**Why it matters:**
- 100MB+ string could cause OutOfMemoryException
- FormD normalization can expand certain characters (e.g., Korean Hangul)
- CPU exhaustion during normalization of pathological Unicode
- DoS vector for public-facing APIs

**Example attack:**
```csharp
var hugeInput = new string('a', 100_000_000);
Slug.Create(hugeInput);  // Memory/CPU exhaustion
```

---

## Proposed Solutions

### Option A: Early Input Length Check
```csharp
public static string? Create(string? text, SlugOptions? options = null)
{
    if (text is null) return null;
    if (text.Length > MaxInputLength)
        throw new ArgumentException($"Input exceeds {MaxInputLength} chars", nameof(text));
    // ...
}
```
- **Pros:** Fails fast, clear error
- **Cons:** New exception type needed
- **Effort:** Small
- **Risk:** Low (breaking for abusers only)

### Option B: Truncate Before Processing
```csharp
if (text.Length > options.MaximumLength * 4)  // Account for expansion
    text = text[..(options.MaximumLength * 4)];
```
- **Pros:** Non-breaking, graceful
- **Cons:** Silent truncation may surprise
- **Effort:** Trivial
- **Risk:** Low

### Option C: Configurable MaxInputLength
```csharp
public int MaxInputLength { get; init; } = 10_000;  // 10KB default
```
- **Pros:** Flexible, user-configurable
- **Cons:** Another option to configure
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Throw early. Library should protect itself from abuse.

---

## Technical Details

**Affected Files:**
- `src/Framework.Slugs/Slug.cs` (add validation near line 12)
- `src/Framework.Slugs/SlugOptions.cs` (optional: add MaxInputLength)

---

## Acceptance Criteria

- [ ] Input > 10KB (or configurable) throws ArgumentException
- [ ] Exception message is clear
- [ ] Document the limit
- [ ] Test edge cases

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - security-sentinel |
