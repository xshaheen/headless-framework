# RandomHelper.GenerateRandomizedList Mutates Input

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, bug, api-design, dotnet

---

## Problem Statement

`GenerateRandomizedList` has a severe side effect - it empties the caller's input list:

```csharp
public static List<T> GenerateRandomizedList<T>(IList<T> items)
{
    Argument.IsNotNullOrEmpty(items);

    List<T> randomList = [];

    while (items.Count > 0)
    {
        var randomIndex = GetRandom(0, items.Count);
        randomList.Add(items[randomIndex]);
        items.RemoveAt(randomIndex);  // Line 108 - MUTATES INPUT!
    }

    return randomList;
}
```

**Why it matters:**
- Caller passes a list expecting shuffled copy, gets empty original list
- Violates principle of least astonishment
- Can cause data loss if caller doesn't expect mutation
- Bug waiting to happen

---

## Proposed Solutions

### Option A: Create Copy First
```csharp
public static List<T> GenerateRandomizedList<T>(IList<T> items)
{
    Argument.IsNotNullOrEmpty(items);
    var source = items.ToList();  // Work on copy
    var randomList = new List<T>(source.Count);

    while (source.Count > 0)
    {
        var randomIndex = Random.Shared.Next(0, source.Count);
        randomList.Add(source[randomIndex]);
        source.RemoveAt(randomIndex);
    }
    return randomList;
}
```
- **Pros:** Non-breaking fix, preserves original
- **Cons:** Extra allocation
- **Effort:** Small
- **Risk:** Low

### Option B: Use Fisher-Yates Shuffle
```csharp
public static List<T> GenerateRandomizedList<T>(IList<T> items)
{
    var result = items.ToList();
    Random.Shared.Shuffle(result.AsSpan());  // .NET 8+
    return result;
}
```
- **Pros:** O(n) instead of O(n^2), uses BCL
- **Cons:** .NET 8+ only (you're on .NET 10, so fine)
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option B** - Use `Random.Shared.Shuffle()` which is built into .NET 8+.

---

## Technical Details

**Affected Files:**
- `src/Framework.Base/Core/RandomHelper.cs` (lines 98-112)

---

## Acceptance Criteria

- [ ] Original input list is NOT modified
- [ ] Returns properly shuffled copy
- [ ] Unit test verifying input preservation

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
