# Thread Safety: Mutable SlugOptions.Default Singleton

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, thread-safety, dotnet, slugs

---

## Problem Statement

`SlugOptions.Default` is a shared singleton with **mutable state**. Code using `Slug.Create(text)` or `Slug.Create(text, null)` shares the same instance.

```csharp
// SlugOptions.cs:12
internal static SlugOptions Default { get; } = new();

// Mutable properties
public int MaximumLength { get; set; } = DefaultMaximumLength;
public List<UnicodeRange> AllowedRanges { get; set; } = [...];
public Dictionary<string, string> Replacements { get; } = new(...);
```

**Why it matters:**
- Any code modifying `Default` corrupts it for ALL callers
- Race conditions in multi-threaded environments
- `AllowedRanges` can be replaced/modified
- `Replacements` dictionary contents are mutable

**Example attack:**
```csharp
var options = SlugOptions.Default;
options.MaximumLength = 50; // Corrupts singleton for ALL callers!
options.Replacements.Add("@", "at"); // Even worse!
```

---

## Proposed Solutions

### Option A: Use `init` Properties + FrozenCollections
```csharp
public int MaximumLength { get; init; } = DefaultMaximumLength;
public IReadOnlyList<UnicodeRange> AllowedRanges { get; init; } = [...];
public FrozenDictionary<string, string> Replacements { get; init; } = ...;
```
- **Pros:** Immutable after construction, modern C# pattern
- **Cons:** Breaking API change for consumers setting properties
- **Effort:** Medium
- **Risk:** Medium (breaking change)

### Option B: Return Fresh Instance from Default
```csharp
internal static SlugOptions Default => new();  // Fresh each call
```
- **Pros:** Simple, non-breaking
- **Cons:** Allocation per call, mutations still possible
- **Effort:** Small
- **Risk:** Low

### Option C: Separate Immutable/Mutable Types
```csharp
public sealed class SlugOptionsBuilder { /* mutable */ }
public sealed record SlugOptions { /* immutable */ }
```
- **Pros:** Clean separation, builder pattern
- **Cons:** More types, more complexity
- **Effort:** Large
- **Risk:** Medium

---

## Recommended Action

**Option A** - Use `init` properties. This is a library; immutability should be the default.

---

## Technical Details

**Affected Files:**
- `src/Framework.Slugs/SlugOptions.cs` (lines 12-41)
- `src/Framework.Slugs/Slug.cs` (line 17 - usage)

---

## Acceptance Criteria

- [ ] `SlugOptions.Default` cannot be modified after creation
- [ ] All setters converted to `init`
- [ ] `AllowedRanges` uses `IReadOnlyList<UnicodeRange>`
- [ ] `Replacements` uses `FrozenDictionary` or is `init`-only
- [ ] Tests verify immutability
- [ ] Document migration for consumers

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, security-sentinel |
