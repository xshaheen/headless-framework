# FluentValidationSchemaProcessor Reflection Without Caching

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, reflection, dotnet

---

## Problem Statement

In `FluentValidationSchemaProcessor.cs` (lines 193-221), reflection calls are made without caching:

```csharp
foreach (var adapter in childAdapters)
{
    var adapterType = adapter.GetType();
    var adapterMethod = adapterType.GetMethod(nameof(ChildValidatorAdaptor<,>.GetValidator));
    // Called for every included validator, every time schema is processed
}
```

**Why it matters:**
- `GetMethod()` is expensive (~1-5 microseconds per call)
- Called repeatedly for same types during schema generation
- With many validators, adds measurable latency to startup
- OpenAPI generation typically happens at startup but can be triggered on-demand

---

## Proposed Solutions

### Option A: Cache MethodInfo by Type
```csharp
private static readonly ConcurrentDictionary<Type, MethodInfo?> _methodCache = new();

var adapterMethod = _methodCache.GetOrAdd(adapterType, t =>
    t.GetMethod(nameof(ChildValidatorAdaptor<,>.GetValidator)));
```
- **Pros:** Simple, effective, thread-safe
- **Cons:** Small memory overhead
- **Effort:** Small
- **Risk:** Low

### Option B: Use Expression Trees for Faster Invocation
- **Pros:** Faster than reflection invoke
- **Cons:** Complex, overkill for startup-only code
- **Effort:** Large
- **Risk:** Medium

---

## Recommended Action

**Option A** - Simple `ConcurrentDictionary` cache for `MethodInfo`.

---

## Technical Details

**Affected Files:**
- `src/Framework.OpenApi.Nswag/SchemaProcessors/FluentValidationSchemaProcessor.cs` (lines 193-221)

---

## Acceptance Criteria

- [ ] Reflection results cached
- [ ] No repeated GetMethod calls for same type
- [ ] Benchmark shows improvement with many validators

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
