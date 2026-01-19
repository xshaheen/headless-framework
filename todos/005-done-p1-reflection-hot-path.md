---
status: done
priority: p1
issue_id: "005"
tags: [code-review, performance, messages, critical]
created: 2026-01-19
dependencies: []
---

# Reflection in Hot Path - ConsumeContext Construction

## Problem Statement

`SubscribeInvoker._BuildConsumeContext` uses `Activator.CreateInstance` + 6 reflection property sets **per message**, causing 5-10x slower than compiled approach.

**Why Critical:** At 100K msg/sec, wastes 50-60ms CPU per second (5-6% overhead). Defeats purpose of `CompiledMessageDispatcher`.

## Evidence from Reviews

**Performance Oracle (Agent af6ca5e):**
```csharp
// Line 151-170: Reflection-heavy instantiation
var instance = Activator.CreateInstance(consumeContextType);  // ~300ns
messageProperty.SetValue(instance, messageInstance);  // ~50ns × 6 = 300ns
// Total: ~600ns per message vs 60ns compiled
```

**Stephen Toub Review (Agent abf9230):**
> "You already have CompiledMessageDispatcher that uses compiled expressions - why not use the same approach here?"

## Performance Impact

**Current Performance:**
- `Activator.CreateInstance`: 200-300ns
- `PropertyInfo.SetValue` × 6: 300ns
- **Total:** 500-600ns per message

**At Scale:**
- 10K msg/sec: 5-6ms/sec wasted (negligible)
- 100K msg/sec: 50-60ms/sec wasted (5-6% CPU)
- 1M msg/sec: 500-600ms/sec = 50-60% CPU overhead

## Proposed Solutions

### Option 1: Compiled Expression Factory (Recommended)
**Effort:** Medium
**Risk:** Low

```csharp
internal sealed class SubscribeInvoker : ISubscribeInvoker
{
    private readonly ConcurrentDictionary<Type, Delegate> _compiledFactories = new();

    private object _BuildConsumeContext(object messageInstance, MediumMessage mediumMessage, Type messageType)
    {
        var factory = (Func<object, MediumMessage, object>)
            _compiledFactories.GetOrAdd(messageType, _CompileFactory);

        return factory(messageInstance, mediumMessage);
    }

    private static Delegate _CompileFactory(Type messageType)
    {
        var consumeContextType = typeof(ConsumeContext<>).MakeGenericType(messageType);
        var messageParam = Expression.Parameter(typeof(object), "message");
        var mediumParam = Expression.Parameter(typeof(MediumMessage), "medium");

        // Build: new ConsumeContext<T> {
        //     Message = (T)message,
        //     MessageId = Guid.Parse(medium.GetId()),
        //     ...
        // }

        var bindings = new[]
        {
            Expression.Bind(
                consumeContextType.GetProperty("Message")!,
                Expression.Convert(messageParam, messageType)
            ),
            // ... 5 more bindings
        };

        var newExpr = Expression.MemberInit(
            Expression.New(consumeContextType),
            bindings
        );

        var lambda = Expression.Lambda<Func<object, MediumMessage, object>>(
            newExpr, messageParam, mediumParam
        );

        return lambda.CompileFast();
    }
}
```

### Option 2: Static Factory Method
**Effort:** Small
**Risk:** Medium - requires ConsumeContext redesign

```csharp
// In ConsumeContext.cs
internal static ConsumeContext<TMessage> Create(
    TMessage message,
    MediumMessage mediumMessage)
{
    var messageId = Guid.Parse(mediumMessage.Origin.GetId());
    // ... parse other fields

    return new ConsumeContext<TMessage>(message, messageId, ...);
}
```

Then call: `ConsumeContext<T>.Create(msg, medium)`

## Technical Details

**Affected Files:**
- `src/Framework.Messages.Core/Internal/ISubscribeInvoker.Default.cs:130-173`

**Pattern to Follow:**
- `/Users/xshaheen/Dev/framework/headless-framework/src/Framework.Messages.Core/Internal/CompiledMessageDispatcher.cs:98-177`

Already does this correctly for consumer invocation!

## Acceptance Criteria

- [ ] Implement compiled expression factory
- [ ] Cache delegates in `ConcurrentDictionary<Type, Delegate>`
- [ ] Use `FastExpressionCompiler` (already in project)
- [ ] Add benchmark: `CompiledFactory_vs_Reflection`
- [ ] Verify 5-10x speedup (600ns → 60-80ns)
- [ ] Run full test suite (75 tests must pass)

## Work Log

- **2026-01-19:** Issue identified in performance review
- **2026-01-19:** Confirmed pattern exists in `CompiledMessageDispatcher`

## Resources

- Performance Review: Agent af6ca5e
- Stephen Toub Review: Agent abf9230
- Example: `CompiledMessageDispatcher.cs:98-177`
- Library: `FastExpressionCompiler` (already referenced)

### 2026-01-19 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-19 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
