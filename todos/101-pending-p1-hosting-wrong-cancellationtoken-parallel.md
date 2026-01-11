# Wrong CancellationToken Used in Parallel Seeding

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, bug, async, hosting, seeders

---

## Problem Statement

In `DbSeedersExtensions.cs`, the `CancellationToken` from `Parallel.ForEachAsync` is discarded and the outer token is used instead.

```csharp
// Line 49 and 85
await Parallel.ForEachAsync(
    preSeeders,
    cancellationToken,
    async (x, _) => await x.Seeder.SeedAsync(cancellationToken)
    //         ^ The inner CancellationToken is IGNORED!
);
```

**Why it matters:**
- `Parallel.ForEachAsync` provides its own linked token for coordination
- Inner token combines the passed `cancellationToken` with internal cancellation logic
- Ignoring it means parallel execution can't coordinate cancellation properly
- If Parallel decides to stop (max degree reached, exception thrown), seeders won't see it

---

## Proposed Solutions

### Option A: Use Inner Token
```csharp
async (x, ct) => await x.Seeder.SeedAsync(ct).AnyContext()
```
- **Pros:** Correct pattern, proper coordination
- **Cons:** None
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Use the inner token provided by `Parallel.ForEachAsync`.

---

## Technical Details

**Affected Files:**
- `src/Framework.Hosting/Seeders/DbSeedersExtensions.cs` (lines 49, 85)

---

## Acceptance Criteria

- [ ] Inner CancellationToken from Parallel.ForEachAsync is used
- [ ] Also add `.AnyContext()` per convention

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, security-sentinel |
