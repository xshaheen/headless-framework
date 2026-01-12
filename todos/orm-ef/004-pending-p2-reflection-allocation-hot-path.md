---
status: pending
priority: p2
issue_id: "004"
tags: [code-review, performance, dotnet, reflection]
dependencies: []
---

# Uncached Reflection in Event Publishing Hot Path

## Problem Statement

`MakeGenericType()` and `Activator.CreateInstance()` are called on every entity state change without caching. With large batches, this becomes a significant performance bottleneck.

**Why it matters:** With 100 entities being saved, this results in 400+ reflection operations. Estimated overhead: 50-200ms for large batches.

## Findings

### Location
- **File:** `src/Framework.Orm.EntityFramework/Contexts/HeadlessEntityModelProcessor.cs`
- **Lines:** 594-619

### Evidence
```csharp
private static void _PublishEntityCreated(ILocalMessageEmitter entity)
{
    var eventType = typeof(EntityCreatedEventData<>).MakeGenericType(entity.GetType());
    var eventMessage = (ILocalMessage)Activator.CreateInstance(eventType, entity)!;
    entity.AddMessage(eventMessage);
}

// Same pattern repeated for:
// _PublishEntityUpdated (lines 601-606)
// _PublishEntityDeleted (lines 608-613)
// _PublishEntityChanged (lines 615-620)
```

### Performance Impact
| Entities | Reflection Calls | Estimated Overhead |
|----------|------------------|-------------------|
| 10 | 40 | ~5ms |
| 100 | 400 | ~50ms |
| 1000 | 4000 | ~500ms |

## Proposed Solutions

### Option 1: Cache generic types and use compiled factories (Recommended)
```csharp
private static readonly ConcurrentDictionary<Type, Func<object, ILocalMessage>> _CreatedFactories = new();

private static void _PublishEntityCreated(ILocalMessageEmitter entity)
{
    var factory = _CreatedFactories.GetOrAdd(entity.GetType(), type =>
    {
        var eventType = typeof(EntityCreatedEventData<>).MakeGenericType(type);
        var ctor = eventType.GetConstructor([type])!;
        var param = Expression.Parameter(typeof(object));
        var converted = Expression.Convert(param, type);
        var newExpr = Expression.New(ctor, converted);
        var cast = Expression.Convert(newExpr, typeof(ILocalMessage));
        return Expression.Lambda<Func<object, ILocalMessage>>(cast, param).Compile();
    });

    entity.AddMessage(factory(entity));
}
```

**Pros:** Near-zero overhead after first call per type
**Cons:** Initial compilation cost, more complex code
**Effort:** Medium
**Risk:** Low

### Option 2: Simple type cache only
Cache only `MakeGenericType` result, keep `Activator.CreateInstance`:
```csharp
private static readonly ConcurrentDictionary<Type, Type> _EventTypeCache = new();
```

**Pros:** Simple implementation
**Cons:** Still has Activator overhead (~50% improvement)
**Effort:** Small
**Risk:** Low

## Recommended Action
<!-- To be filled during triage -->

## Technical Details

### Affected Files
- `src/Framework.Orm.EntityFramework/Contexts/HeadlessEntityModelProcessor.cs`

### Affected Components
- SaveChanges performance for ILocalMessageEmitter entities

### Database Changes Required
None

## Acceptance Criteria
- [ ] Reflection overhead reduced by 90%+
- [ ] BenchmarkDotNet tests added for ProcessEntries
- [ ] No functional regression in event publishing

## Work Log
| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-12 | Identified during performance review | Cache reflection results in hot paths |

## Resources
- Expression trees: https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/expression-trees/
