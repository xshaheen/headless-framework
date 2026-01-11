# RemoveHostedService Misses Factory-Based Registrations

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, bug, dotnet, hosting, di

---

## Problem Statement

`RemoveHostedService<T>` only matches when `ImplementationType == typeof(T)`. If hosted service was registered with a factory, `ImplementationType` is `null` and method fails to find it.

```csharp
// Lines 250-262
var hostedServiceDescriptor = services.FirstOrDefault(descriptor =>
    descriptor.ServiceType == typeof(IHostedService) &&
    descriptor.ImplementationType == typeof(T)  // Won't match factory registrations!
);
```

**Example that won't be found:**
```csharp
services.AddHostedService(sp => new MyHostedService(sp.GetRequiredService<IDep>()));
```

---

## Proposed Solutions

### Option A: Also Check ImplementationFactory
```csharp
var hostedServiceDescriptor = services.FirstOrDefault(descriptor =>
    descriptor.ServiceType == typeof(IHostedService) &&
    (descriptor.ImplementationType == typeof(T) ||
     descriptor.ImplementationFactory?.Method.ReturnType == typeof(T))
);
```
- **Pros:** Catches factory registrations
- **Cons:** Relies on reflection, doesn't catch all edge cases
- **Effort:** Small
- **Risk:** Low

### Option B: Document Limitation
- Add XML doc stating only works for type-based registrations
- **Pros:** Honest API
- **Cons:** Limitation remains
- **Effort:** Small
- **Risk:** Low

### Option C: Remove All Matching IHostedService Where Implementation Could Be T
```csharp
services.RemoveAll(d =>
    d.ServiceType == typeof(IHostedService) &&
    (d.ImplementationType == typeof(T) ||
     (d.ImplementationInstance?.GetType() == typeof(T)) ||
     IsFactoryForType<T>(d.ImplementationFactory)));
```
- **Pros:** Most thorough
- **Cons:** Complex, potential false positives
- **Effort:** Medium
- **Risk:** Medium

---

## Recommended Action

**Option B** for now - document the limitation. Option A if factory removal is actually needed.

---

## Technical Details

**Affected Files:**
- `src/Framework.Hosting/DependencyInjection/DependencyInjectionExtensions.cs` (lines 250-263)

---

## Acceptance Criteria

- [ ] Either fix to handle factory registrations OR document limitation clearly
- [ ] XML documentation updated

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
