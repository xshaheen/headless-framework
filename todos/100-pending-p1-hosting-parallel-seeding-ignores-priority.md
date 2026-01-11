# Parallel Seeding Ignores Priority Ordering

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, bug, async, hosting, seeders

---

## Problem Statement

`DbSeedersExtensions.cs` orders seeders by `SeederPriorityAttribute`, but when `runInParallel=true`, the ordering is **completely ignored**.

```csharp
// Lines 34-50
var preSeeders = scope
    .ServiceProvider.GetServices<IPreSeeder>()
    .Select(x => (Seeder: x, Type: x.GetType()))
    .OrderBy(x => x.Type.GetCustomAttribute<SeederPriorityAttribute>()?.Priority ?? 0);

if (runInParallel)
{
    await Parallel.ForEachAsync(
        preSeeders,  // OrderBy is meaningless here!
        cancellationToken,
        async (x, _) => await x.Seeder.SeedAsync(cancellationToken)
    );
}
```

**Why it matters:**
- User sets `[SeederPriority(1)]` expecting seeder to run first
- Passing `runInParallel=true` silently ignores priority
- Semantic bug that violates principle of least surprise
- Wasted CPU cycles sorting when parallel execution ignores order

---

## Proposed Solutions

### Option A: Document That Priority Is Ignored in Parallel Mode
- Add XML doc warning
- **Pros:** Quick fix, honest API
- **Cons:** Surprising behavior remains
- **Effort:** Small
- **Risk:** Low

### Option B: Group By Priority, Parallelize Within Groups
```csharp
if (runInParallel)
{
    var groups = preSeeders.GroupBy(x =>
        x.Type.GetCustomAttribute<SeederPriorityAttribute>()?.Priority ?? 0)
        .OrderBy(g => g.Key);

    foreach (var group in groups)
    {
        await Parallel.ForEachAsync(group, cancellationToken,
            async (x, ct) => await x.Seeder.SeedAsync(ct).AnyContext());
    }
}
```
- **Pros:** Priority respected between groups, parallelism within
- **Cons:** More complex
- **Effort:** Medium
- **Risk:** Low

### Option C: Throw If Parallel + Non-Zero Priority
```csharp
if (runInParallel && preSeeders.Any(x =>
    x.Type.GetCustomAttribute<SeederPriorityAttribute>() != null))
{
    throw new InvalidOperationException(
        "Cannot use runInParallel with prioritized seeders");
}
```
- **Pros:** Explicit failure, forces decision
- **Cons:** Breaking change
- **Effort:** Small
- **Risk:** Medium

---

## Recommended Action

**Option B** - Group by priority and parallelize within groups. Best of both worlds.

---

## Technical Details

**Affected Files:**
- `src/Framework.Hosting/Seeders/DbSeedersExtensions.cs` (lines 44-50, 80-86)

---

## Acceptance Criteria

- [ ] Priority ordering is respected even in parallel mode
- [ ] Seeders with same priority can run in parallel
- [ ] Documentation reflects actual behavior
- [ ] Unit test verifying priority is respected

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, performance-oracle, security-sentinel |
