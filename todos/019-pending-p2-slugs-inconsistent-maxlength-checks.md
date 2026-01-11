# Inconsistent MaximumLength Checks

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, correctness, dotnet, slugs

---

## Problem Statement

MaximumLength is checked inconsistently:

```csharp
// Slug.cs:54-57 - uses >=
if (options.MaximumLength > 0 && sb.Length >= options.MaximumLength)
{
    break;
}

// Slug.cs:62-65 - uses >
if (options.MaximumLength > 0 && text.Length > options.MaximumLength)
{
    text = text[..options.MaximumLength];
}
```

**Why it matters:**
- First check breaks at `>=`, second truncates at `>`
- Breaking mid-loop could leave partial multi-char replacements
- Output length could vary between MaximumLength and MaximumLength-1
- Confusing behavior for users expecting exact length

---

## Proposed Solutions

### Option A: Consistent >= Check
```csharp
// Both checks use >=
if (sb.Length >= options.MaximumLength) break;
// ...
if (text.Length >= options.MaximumLength) text = text[..options.MaximumLength];
```
- **Pros:** Consistent behavior
- **Cons:** Minor - always produces <= MaximumLength
- **Effort:** Trivial
- **Risk:** None

### Option B: Single Post-Loop Truncation
```csharp
// Remove in-loop check, truncate once at end
var result = sb.ToString();
if (options.MaximumLength > 0 && result.Length > options.MaximumLength)
    result = result[..options.MaximumLength];
```
- **Pros:** Single location, clear logic
- **Cons:** Processes extra chars then discards
- **Effort:** Small
- **Risk:** Low

### Option C: Smart Truncation (Don't Break Mid-Word)
```csharp
// Find last separator before MaximumLength
var cutoff = result.LastIndexOf(options.Separator, MaximumLength);
if (cutoff > 0) result = result[..cutoff];
```
- **Pros:** Cleaner URLs, user-friendly
- **Cons:** More complex, may produce much shorter slugs
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option B** - Single post-loop truncation. Clear and predictable.

---

## Technical Details

**Affected Files:**
- `src/Framework.Slugs/Slug.cs` (lines 54-57, 62-65)

---

## Acceptance Criteria

- [ ] Single, clear truncation logic
- [ ] Output never exceeds MaximumLength
- [ ] Document exact behavior
- [ ] Test boundary cases (exact length, length+1, etc.)

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
