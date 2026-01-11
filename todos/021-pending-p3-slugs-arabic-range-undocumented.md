# Arabic Range Hardcoded Without Documentation

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, documentation, dotnet, slugs

---

## Problem Statement

`AllowedRanges` includes Arabic characters without explanation:

```csharp
// SlugOptions.cs:28-32
public List<UnicodeRange> AllowedRanges { get; set; } =
    [
        UnicodeRange.Create('A', 'Z'),
        UnicodeRange.Create('a', 'z'),
        UnicodeRange.Create('0', '9'),
        UnicodeRange.Create('a', 'i'),  // U+0620 to U+064A - Arabic
    ];
```

**Why it matters:**
- Non-obvious to non-Arabic developers
- May not be appropriate for all use cases
- Should this be opt-in rather than default?
- No XML doc explaining the choice

---

## Proposed Solutions

### Option A: Add XML Documentation
```csharp
/// <summary>
/// Unicode ranges allowed in slugs. Default includes:
/// - A-Z, a-z: Latin letters
/// - 0-9: Digits
/// - Arabic (U+0620-U+064A): For Arabic language support
/// </summary>
public List<UnicodeRange> AllowedRanges { get; set; } = ...;
```
- **Pros:** Documents intent
- **Cons:** Still default-on
- **Effort:** Trivial
- **Risk:** None

### Option B: Remove Arabic from Defaults
```csharp
// Only ASCII by default
public List<UnicodeRange> AllowedRanges { get; set; } =
    [
        UnicodeRange.Create('A', 'Z'),
        UnicodeRange.Create('a', 'z'),
        UnicodeRange.Create('0', '9'),
    ];
```
- **Pros:** Universal default, Arabic is opt-in
- **Cons:** Breaking change for Arabic users
- **Effort:** Trivial
- **Risk:** Medium (breaking)

### Option C: Provide Named Presets
```csharp
public static class SlugPresets
{
    public static SlugOptions Latin => new() { AllowedRanges = [...] };
    public static SlugOptions Arabic => new() { AllowedRanges = [...] };
    public static SlugOptions Multilingual => new() { AllowedRanges = [...] };
}
```
- **Pros:** Clear intent, flexible
- **Cons:** More API surface
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** for now - Document it. Consider Option C for v2.

---

## Technical Details

**Affected Files:**
- `src/Framework.Slugs/SlugOptions.cs` (lines 25-32)

---

## Acceptance Criteria

- [ ] XML documentation explains each range
- [ ] Consider if Arabic should be opt-in in future version

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pattern-recognition, architecture-strategist |
