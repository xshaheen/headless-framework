# AddSeeder/AddPreSeeder Return Void Instead of IServiceCollection

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-have
**Tags:** code-review, quality, dotnet, hosting, seeders

---

## Problem Statement

`AddPreSeeder<T>` and `AddSeeder<T>` return `void` instead of `IServiceCollection`, breaking fluent API pattern.

```csharp
// Lines 14-19
public static void AddPreSeeder<T>(this IServiceCollection services)
    where T : class, IPreSeeder
{
    services.TryAddEnumerable(ServiceDescriptor.Transient<IPreSeeder, T>());
    services.TryAddTransient<T>();
}
```

Other extension methods in project return `IServiceCollection` for chaining.

---

## Proposed Solutions

### Option A: Return IServiceCollection
```csharp
public static IServiceCollection AddPreSeeder<T>(this IServiceCollection services)
    where T : class, IPreSeeder
{
    services.TryAddEnumerable(ServiceDescriptor.Transient<IPreSeeder, T>());
    services.TryAddTransient<T>();
    return services;
}
```
- **Pros:** Enables fluent chaining, consistent API
- **Cons:** None
- **Effort:** Small
- **Risk:** Low (non-breaking)

---

## Recommended Action

**Option A** - Return `IServiceCollection` for consistency.

---

## Technical Details

**Affected Files:**
- `src/Framework.Hosting/Seeders/DbSeedersExtensions.cs` (lines 14-26)

---

## Acceptance Criteria

- [ ] Both methods return `IServiceCollection`
- [ ] Tests updated if needed

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pattern-recognition-specialist |
