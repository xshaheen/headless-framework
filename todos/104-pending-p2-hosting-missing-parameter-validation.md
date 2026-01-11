# Missing Parameter Validation Across Multiple Methods

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, quality, dotnet, hosting

---

## Problem Statement

Multiple public methods lack `Argument.IsNotNull` validation for their parameters, inconsistent with rest of codebase.

**Files missing validation:**

### ConfigurationExtensions.cs - ALL methods missing validation
- `GetOptions<TModel>` - no validation of `configuration` or `section`
- `GetRequiredConnectionString` - no validation of `configuration` or `name`
- `GetRequired<T>` overloads - no validation

### DependencyInjectionExtensions.cs
- `Unregister<TService>` (line 185) - no validation of `services`
- `IsAdded<T>` (line 203) - no validation of `services`
- `IsAdded(Type)` (line 208) - no validation of `services` or `type`
- `AddKeyedSingleton/Scoped/Transient` (lines 217-245) - no validation
- `RemoveHostedService<T>` (line 250) - no validation of `services`

### OptionsBuilderFluentValidationExtensions.cs
- `ValidateFluentValidation` (line 17) - no validation of `optionsBuilder`

### DbSeedersExtensions.cs
- `AddPreSeeder<T>` (line 14) - no validation of `services`
- `AddSeeder<T>` (line 21) - no validation of `services`
- `PreSeedAsync` (line 28) - no validation of `services`
- `SeedAsync` (line 64) - no validation of `services`

---

## Proposed Solutions

### Option A: Add Argument.IsNotNull to All Public Methods
```csharp
public static TModel GetOptions<TModel>(this IConfiguration configuration, string section)
    where TModel : new()
{
    Argument.IsNotNull(configuration);
    Argument.IsNotNull(section);
    // ...
}
```
- **Pros:** Consistent, fail-fast, clear errors
- **Cons:** Slight overhead (negligible)
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** - Add validation to all public extension methods.

---

## Technical Details

**Affected Files:**
- `src/Framework.Hosting/Configuration/ConfigurationExtensions.cs`
- `src/Framework.Hosting/DependencyInjection/DependencyInjectionExtensions.cs`
- `src/Framework.Hosting/Options/OptionsBuilderFluentValidationExtensions.cs`
- `src/Framework.Hosting/Seeders/DbSeedersExtensions.cs`

---

## Acceptance Criteria

- [ ] All public methods have `Argument.IsNotNull` for all parameters
- [ ] Consistent with other extension classes in project

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pattern-recognition-specialist |
