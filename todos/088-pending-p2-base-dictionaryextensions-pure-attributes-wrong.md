# DictionaryExtensions [Pure] Attributes on Mutating Methods

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, attributes, api-design, dotnet

---

## Problem Statement

`GetOrAdd` and `TryUpdate` are marked with `[Pure]` attributes but they mutate the dictionary:

```csharp
[SystemPure]
[JetBrainsPure]
public static TValue? GetOrAdd<TKey, TValue>(...)  // MUTATES dictionary!

[SystemPure]
[JetBrainsPure]
public static bool TryUpdate<TKey, TValue>(...)  // MUTATES dictionary!
```

**Why it matters:**
- Misleads static analyzers
- Developers expect pure methods to have no side effects
- Could cause bugs if analyzer optimizes away "unused" calls

---

## Proposed Solutions

### Option A: Remove [Pure] Attributes
```csharp
// Remove both [SystemPure] and [JetBrainsPure] from these methods
public static TValue? GetOrAdd<TKey, TValue>(...)
public static bool TryUpdate<TKey, TValue>(...)
```
- **Pros:** Correct semantics
- **Cons:** None
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Remove the `[Pure]` attributes from methods that mutate state.

---

## Technical Details

**Affected Files:**
- `src/Framework.Base/Collections/DictionaryExtensions.cs` (lines 92, 141)

---

## Acceptance Criteria

- [ ] `GetOrAdd` does not have Pure attributes
- [ ] `TryUpdate` does not have Pure attributes
- [ ] Review all other methods for similar issues

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
