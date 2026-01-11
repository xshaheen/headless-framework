# Parallel Seeding Race Conditions - Shared Scope

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, security, concurrency, hosting, seeders

---

## Problem Statement

When `runInParallel=true`, seeders execute concurrently but share a single DI scope. This can cause:
1. Race conditions on shared DbContext (EF Core is not thread-safe)
2. Unpredictable behavior when seeders have dependencies on shared state

```csharp
// Lines 30, 44-51
await using var scope = services.CreateAsyncScope();
// ...
if (runInParallel)
{
    await Parallel.ForEachAsync(
        preSeeders,
        cancellationToken,
        async (x, _) => await x.Seeder.SeedAsync(cancellationToken)
        // All seeders resolve from SAME scope!
    );
}
```

**Why it matters:**
- Database corruption if seeders modify same data
- EF Core DbContext is not thread-safe
- Scoped services like DbContext are shared across parallel executions

---

## Proposed Solutions

### Option A: Create Separate Scope Per Seeder in Parallel Mode
```csharp
if (runInParallel)
{
    await Parallel.ForEachAsync(seederTypes, cancellationToken, async (type, ct) =>
    {
        await using var innerScope = services.CreateAsyncScope();
        var seeder = (ISeeder)innerScope.ServiceProvider.GetRequiredService(type);
        await seeder.SeedAsync(ct).AnyContext();
    });
}
```
- **Pros:** Each seeder gets isolated scope, thread-safe
- **Cons:** More complex, stores types instead of instances
- **Effort:** Medium
- **Risk:** Low

### Option B: Document That Parallel Mode Shares Scope
- Add warning in XML docs
- State seeders must be stateless/thread-safe for parallel
- **Pros:** Quick fix
- **Cons:** Easy to misuse
- **Effort:** Small
- **Risk:** Medium (users may still misuse)

---

## Recommended Action

**Option A** - Create separate scope per seeder for parallel execution.

---

## Technical Details

**Affected Files:**
- `src/Framework.Hosting/Seeders/DbSeedersExtensions.cs` (lines 28-62, 64-98)

---

## Acceptance Criteria

- [ ] Each parallel seeder gets its own DI scope
- [ ] Sequential mode behavior unchanged
- [ ] Test with DbContext-dependent seeders in parallel

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - security-sentinel |
