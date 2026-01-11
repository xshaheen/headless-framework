# ClaimsPrincipalExtensions.RemoveAll Collection Modified During Enumeration

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, bug, security, dotnet

---

## Problem Statement

`RemoveAll` may throw `InvalidOperationException` due to collection modification during enumeration:

```csharp
public static ClaimsIdentity RemoveAll(this ClaimsIdentity claimsIdentity, string claimType)
{
    Argument.IsNotNull(claimsIdentity);

    foreach (var x in claimsIdentity.FindAll(claimType))  // Returns IEnumerable
    {
        claimsIdentity.RemoveClaim(x);  // Modifies underlying collection!
    }

    return claimsIdentity;
}
```

Compare with `AddOrReplace` (line 38) which correctly calls `.ToList()` first.

**Why it matters:**
- Can throw `InvalidOperationException` at runtime
- Intermittent failure depending on enumeration implementation
- Security-sensitive code path for claims manipulation

---

## Proposed Solutions

### Option A: Materialize Enumerable First
```csharp
public static ClaimsIdentity RemoveAll(this ClaimsIdentity claimsIdentity, string claimType)
{
    Argument.IsNotNull(claimsIdentity);

    foreach (var x in claimsIdentity.FindAll(claimType).ToList())  // Add .ToList()
    {
        claimsIdentity.RemoveClaim(x);
    }

    return claimsIdentity;
}
```
- **Pros:** Simple fix, matches AddOrReplace pattern
- **Cons:** Extra allocation
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Add `.ToList()` to materialize the enumerable before modification.

---

## Technical Details

**Affected Files:**
- `src/Framework.Base/Security/ClaimsPrincipalExtensions.cs` (lines 48-58)

---

## Acceptance Criteria

- [ ] No exception thrown when removing multiple claims
- [ ] Pattern matches `AddOrReplace` method
- [ ] Unit test with multiple claims of same type

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
