---
status: pending
priority: p3
issue_id: "069"
tags: [code-review, yagni, simplification, rabbitmq]
created: 2026-01-20
dependencies: []
---

# YAGNI: IConnectionChannelPool Interface

## Problem

**File:** `src/Framework.Messages.RabbitMQ/IConnectionChannelPool.cs:11-20`

```csharp
public interface IConnectionChannelPool : IDisposable, IAsyncDisposable
{
    IModel Rent();
    bool Return(IModel context);
}
```

**No evidence interface needed:**
- Single implementation (IConnectionChannelPool class at line 22)
- Internal API (not public abstraction)
- No dependency injection (instantiated directly)
- No test mocking required (0 tests exist)

**Costs:**
- Extra file navigation complexity
- False abstraction (YAGNI)

## Solution

**Remove interface:**
- Make class sealed
- Remove interface declaration
- Update references to use concrete type

```csharp
internal sealed class ConnectionChannelPool : IDisposable, IAsyncDisposable
{
    public IModel Rent() { ... }
    public bool Return(IModel context) { ... }
}
```

**LOC savings:** ~10 lines, improved clarity.

## Acceptance Criteria

- [ ] Remove interface
- [ ] Make class sealed
- [ ] Update instantiation sites
- [ ] Verify builds
- [ ] Run tests

**Effort:** 30 min | **Risk:** Very Low
