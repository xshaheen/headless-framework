# Unnecessary Allocation in Unregister<T>

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-have
**Tags:** code-review, performance, dotnet, hosting, di

---

## Problem Statement

`Unregister<TService>` uses LINQ + `ToList()` to find and remove services, causing unnecessary allocation.

```csharp
// Lines 185-197
foreach (var descriptor in services.Where(d => d.ServiceType == typeof(TService)).ToList())
{
    services.Remove(descriptor);
    unregistered = true;
}
```

**Issues:**
- `Where()` creates iterator
- `ToList()` allocates new `List<ServiceDescriptor>` and copies all matches
- Called from `AddOrReplace*` methods, potentially multiple times during startup

**Note:** The `ToList()` IS necessary here since we can't modify collection while iterating.

---

## Proposed Solutions

### Option A: Use Reverse For Loop
```csharp
public static bool Unregister<TService>(this IServiceCollection services)
{
    var unregistered = false;
    for (var i = services.Count - 1; i >= 0; i--)
    {
        if (services[i].ServiceType == typeof(TService))
        {
            services.RemoveAt(i);
            unregistered = true;
        }
    }
    return unregistered;
}
```
- **Pros:** No allocation, same behavior
- **Cons:** Slightly less readable
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Use reverse for loop. Zero allocation pattern.

---

## Technical Details

**Affected Files:**
- `src/Framework.Hosting/DependencyInjection/DependencyInjectionExtensions.cs` (lines 185-197)

---

## Acceptance Criteria

- [ ] No list allocation
- [ ] Same behavior (removes all matching)
- [ ] Tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, performance-oracle |
