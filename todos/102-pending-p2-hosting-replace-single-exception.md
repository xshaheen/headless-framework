# Replace<T> Throws Unhelpful Exception from Single()

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, quality, dotnet, hosting, di

---

## Problem Statement

`DependencyInjectionExtensions.Replace<TService>` uses `Single()` which throws cryptic exceptions.

```csharp
// Line 169
var descriptor = services.Single(descriptor => descriptor.ServiceType == typeof(TService));
```

**Problems:**
- If no service registered: `InvalidOperationException: Sequence contains no matching element`
- If multiple registered: `InvalidOperationException: Sequence contains more than one matching element`
- Neither message mentions the service type
- `Single()` enumerates entire collection even after finding match (to verify uniqueness)

---

## Proposed Solutions

### Option A: Use For Loop with Better Error
```csharp
public static void Replace<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory)
    where TService : class
{
    for (var i = 0; i < services.Count; i++)
    {
        if (services[i].ServiceType == typeof(TService))
        {
            var lifetime = services[i].Lifetime;
            services[i] = new ServiceDescriptor(typeof(TService), factory, lifetime);
            return;
        }
    }
    throw new InvalidOperationException($"Service '{typeof(TService).Name}' is not registered");
}
```
- **Pros:** Clear error, replaces first match, no allocation
- **Cons:** Changes behavior if multiple registrations exist
- **Effort:** Small
- **Risk:** Medium (behavior change)

### Option B: Use SingleOrDefault with Explicit Check
```csharp
var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TService))
    ?? throw new InvalidOperationException($"No service of type '{typeof(TService).Name}' is registered.");
```
- **Pros:** Clear error, keeps uniqueness check
- **Cons:** Still allocates closure, still fails on duplicates
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Use for loop. Replaces first match, better error, better performance.

---

## Technical Details

**Affected Files:**
- `src/Framework.Hosting/DependencyInjection/DependencyInjectionExtensions.cs` (line 169)

---

## Acceptance Criteria

- [ ] Clear error message including type name
- [ ] No closure allocation
- [ ] Test for missing service error message

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, security-sentinel, performance-oracle |
