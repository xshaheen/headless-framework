# Missing Null Check for Separator

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, validation, dotnet, slugs

---

## Problem Statement

`Separator` can be set to null, causing issues:

```csharp
// SlugOptions.cs:18
public string Separator { get; set; } = DefaultSeparator;

// Slug.cs:69-72 - Throws if Separator is null and CanEndWithSeparator is false
while (text.EndsWith(options.Separator, StringComparison.Ordinal))
{
    text = text[..^options.Separator.Length];
}
```

**Why it matters:**
- `ArgumentNullException` at runtime if `Separator = null`
- Inconsistent null checks: line 44 checks, line 69 doesn't
- Empty separator `""` causes infinite loop in trailing removal

---

## Proposed Solutions

### Option A: Add Validation in Setter
```csharp
private string _separator = DefaultSeparator;
public string Separator
{
    get => _separator;
    set => _separator = string.IsNullOrEmpty(value)
        ? throw new ArgumentException("Separator cannot be null or empty")
        : value;
}
```
- **Pros:** Fails fast, clear error
- **Cons:** More code
- **Effort:** Small
- **Risk:** Low

### Option B: Use `required init` Pattern
```csharp
public required string Separator { get; init; } = DefaultSeparator;
```
- **Pros:** Enforced by compiler
- **Cons:** Breaking change
- **Effort:** Trivial
- **Risk:** Medium

### Option C: Guard in Trailing Removal
```csharp
if (options.Separator is { Length: > 0 })
{
    while (text.EndsWith(options.Separator, StringComparison.Ordinal))
    {
        text = text[..^options.Separator.Length];
    }
}
```
- **Pros:** Defensive, non-breaking
- **Cons:** Masks configuration errors
- **Effort:** Trivial
- **Risk:** None

---

## Recommended Action

**Option A** - Validate in setter. Fail fast on bad config.

---

## Technical Details

**Affected Files:**
- `src/Framework.Slugs/SlugOptions.cs` (line 18)
- `src/Framework.Slugs/Slug.cs` (lines 44, 69)

---

## Acceptance Criteria

- [ ] Null separator throws at configuration time
- [ ] Empty separator throws at configuration time
- [ ] Document valid separator values
- [ ] Test edge cases

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - security-sentinel |
