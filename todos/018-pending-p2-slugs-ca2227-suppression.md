# CA2227 Suppression - Mutable AllowedRanges Collection

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, api-design, dotnet, slugs

---

## Problem Statement

`AllowedRanges` suppresses CA2227 to allow public setter on collection:

```csharp
// SlugOptions.cs:24-32
#pragma warning disable CA2227
public List<UnicodeRange> AllowedRanges { get; set; } =
#pragma warning restore CA2227
    [
        UnicodeRange.Create('A', 'Z'),
        UnicodeRange.Create('a', 'z'),
        UnicodeRange.Create('0', '9'),
        UnicodeRange.Create('a', 'i'),  // Arabic
    ];
```

**Why it matters:**
- `options.AllowedRanges = null` causes NullReferenceException in `IsAllowed()`
- Collection can be replaced mid-use
- Inconsistent with `Replacements` (get-only)
- CA2227 exists for good reason

---

## Proposed Solutions

### Option A: Use `init` Setter
```csharp
public IReadOnlyList<UnicodeRange> AllowedRanges { get; init; } = [...];
```
- **Pros:** Immutable after construction, no CA2227 warning
- **Cons:** Breaking change for consumers adding ranges
- **Effort:** Small
- **Risk:** Medium (breaking)

### Option B: Use IList Backed by Internal List
```csharp
private readonly List<UnicodeRange> _allowedRanges = [...];
public IList<UnicodeRange> AllowedRanges => _allowedRanges;
```
- **Pros:** Allows modification, prevents replacement
- **Cons:** Still mutable, doesn't solve thread safety
- **Effort:** Small
- **Risk:** Low

### Option C: Use Builder Pattern
```csharp
public SlugOptionsBuilder WithAllowedRange(UnicodeRange range) { ... }
public SlugOptions Build() { ... }
```
- **Pros:** Clean immutable construction
- **Cons:** More types, API change
- **Effort:** Medium
- **Risk:** Medium

---

## Recommended Action

**Option A** - Use `init` setter with `IReadOnlyList`. Part of larger immutability fix (see P1 todo).

---

## Technical Details

**Affected Files:**
- `src/Framework.Slugs/SlugOptions.cs` (lines 24-32)

**Related:**
- P1 todo: Mutable SlugOptions.Default Singleton

---

## Acceptance Criteria

- [ ] CA2227 suppression removed
- [ ] `AllowedRanges` is `IReadOnlyList<UnicodeRange>` with `init`
- [ ] Null check in `IsAllowed` as safety net
- [ ] Tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pattern-recognition, strict-dotnet-reviewer |
