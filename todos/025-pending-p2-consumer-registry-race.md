---
status: pending
priority: p2
issue_id: "025"
tags: [threading, race-condition, consumer-registry, initialization]
dependencies: []
---

# Consumer Registry Freeze Check Race Condition

## Problem Statement

ConsumerRegistry freeze check and mutation not atomic, creating race condition during registration.

## Findings

**Vulnerable Code** (ConsumerRegistry.cs:32-42):
```csharp
public void Register(ConsumerMetadata metadata)
{
    if (_frozen != null) throw new InvalidOperationException("...");
    //  ^^^^^^^^^^^^ - Check
    _consumers!.Add(metadata); // Mutation - NOT ATOMIC
}

public IReadOnlyList<ConsumerMetadata> GetAll()
{
    if (_frozen == null)  // Thread A reads null
    {
        _frozen = _consumers!.AsReadOnly();  // Thread B freezes here
        _consumers = null; // Thread A releases - RACE!
    }
    return _frozen;
}
```

**Race Scenario**:
1. Thread A: `Register()` checks `_frozen == null` (passes)
2. Thread B: `GetAll()` sets `_frozen` and nulls `_consumers`
3. Thread A: `_consumers!.Add()` → **NullReferenceException**

**Note**: Issue #006 marked as DONE but only addressed symptom, not root cause.

## Proposed Solutions

### Option 1: Lock Freeze Operations (RECOMMENDED)
**Effort**: 1 hour

```csharp
private readonly object _lock = new();

public void Register(ConsumerMetadata metadata)
{
    lock (_lock)
    {
        if (_frozen != null) throw new InvalidOperationException("...");
        _consumers!.Add(metadata);
    }
}

public IReadOnlyList<ConsumerMetadata> GetAll()
{
    lock (_lock)
    {
        if (_frozen == null)
        {
            _frozen = _consumers!.AsReadOnly();
            _consumers = null;
        }
    }
    return _frozen;
}
```

### Option 2: Interlocked.CompareExchange
**Effort**: 2 hours
**Benefit**: Lock-free

More complex - use Interlocked for atomic freeze.

## Recommended Action

Implement Option 1 - simple, correct, low overhead (only during startup).

## Acceptance Criteria

- [ ] Lock protects freeze check + mutation
- [ ] Concurrency test verifies no races (1000 threads registering)
- [ ] No performance regression (registration is startup-only)
- [ ] Tests verify exception on late registration

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Data Integrity Guardian Agent)

**Actions:**
- Reviewed issue #006 fix (incomplete)
- Identified check-then-act race condition
- Proposed lock-based solution
