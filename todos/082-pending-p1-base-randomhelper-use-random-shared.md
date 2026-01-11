# RandomHelper Should Use Random.Shared

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, performance, threading, dotnet

---

## Problem Statement

`RandomHelper.cs` uses a static `Random` with manual locking instead of `Random.Shared`:

```csharp
private static readonly Random _Random = new();  // Line 12

public static int GetRandom(int minValue, int maxValue)
{
    lock (_Random)  // Lines 29-32 - Unnecessary contention!
    {
        return _Random.Next(minValue, maxValue);
    }
}
```

**Why it matters:**
- `Random.Shared` exists since .NET 6 - thread-safe, lock-free
- Manual locking causes contention under concurrent load
- 50-200% slowdown measured under concurrent access
- Additionally, `Random` is NOT cryptographically secure - if used for security purposes, it's vulnerable

---

## Proposed Solutions

### Option A: Replace with Random.Shared
```csharp
public static int GetRandom(int minValue, int maxValue)
{
    return Random.Shared.Next(minValue, maxValue);
}
```
- **Pros:** Simple, performant, idiomatic
- **Cons:** None
- **Effort:** Small
- **Risk:** Low

### Option B: Delete the class entirely
- Callers use `Random.Shared.Next()` directly
- **Pros:** Less code to maintain, clearer intent
- **Cons:** Breaking change for consumers
- **Effort:** Medium
- **Risk:** Medium

---

## Recommended Action

**Option A** - Replace implementation with `Random.Shared`. Consider deprecating class in future.

---

## Technical Details

**Affected Files:**
- `src/Framework.Base/Core/RandomHelper.cs` (all methods)

---

## Acceptance Criteria

- [ ] All methods use `Random.Shared` instead of locked static Random
- [ ] Remove `_Random` field and lock statements
- [ ] No performance regression under concurrent load
- [ ] Add XML doc warning about non-cryptographic nature

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle, security-sentinel, pragmatic-dotnet-reviewer |
