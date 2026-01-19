---
status: done
priority: p1
issue_id: "006"
tags: [code-review, thread-safety, messages, critical]
created: 2026-01-19
dependencies: []
---

# Race Condition in ConsumerRegistry

## Problem Statement

`ConsumerRegistry` uses plain `List<ConsumerMetadata>` without synchronization, causing data corruption if `ScanConsumers()` or `Consumer<T>()` called concurrently during startup.

**Why Critical:** Multi-threaded startup (common in hosted services) corrupts registry state, causing unpredictable consumer routing.

## Evidence from Reviews

**Stephen Toub Review (Agent abf9230):**
```csharp
internal sealed class ConsumerRegistry
{
    private readonly List<ConsumerMetadata> _consumers = [];  // ❌ NOT THREAD-SAFE

    public void Register(ConsumerMetadata metadata)
    {
        _consumers.Add(metadata);  // ❌ Race condition
    }

    public IReadOnlyList<ConsumerMetadata> GetAll() => _consumers;  // ❌ Exposes mutable
}
```

**Issues:**
1. `List<T>.Add()` not thread-safe
2. `GetAll()` returns same mutable list reference
3. No memory barrier guarantees

## Proposed Solutions

### Option 1: ConcurrentBag (Simplest)
**Effort:** Small
**Risk:** Low

```csharp
internal sealed class ConsumerRegistry
{
    private readonly ConcurrentBag<ConsumerMetadata> _consumers = new();

    public void Register(ConsumerMetadata metadata)
    {
        _consumers.Add(metadata);  // Thread-safe
    }

    public IReadOnlyList<ConsumerMetadata> GetAll() => _consumers.ToList();  // Defensive copy
}
```

### Option 2: Freeze Pattern (Best Performance)
**Effort:** Medium
**Risk:** Low

```csharp
internal sealed class ConsumerRegistry
{
    private List<ConsumerMetadata>? _consumers = [];
    private IReadOnlyList<ConsumerMetadata>? _frozen;

    public void Register(ConsumerMetadata metadata)
    {
        if (_frozen != null)
            throw new InvalidOperationException("Registry is frozen");
        _consumers!.Add(metadata);
    }

    public IReadOnlyList<ConsumerMetadata> GetAll()
    {
        if (_frozen == null)
        {
            _frozen = _consumers!.AsReadOnly();
            _consumers = null;  // Release for GC
        }
        return _frozen;
    }
}
```

Assumes registration happens during startup (single-threaded), then frozen before runtime.

### Option 3: Lock-Based (Most Conservative)
**Effort:** Small
**Risk:** Low

```csharp
internal sealed class ConsumerRegistry
{
    private readonly List<ConsumerMetadata> _consumers = [];
    private readonly object _lock = new();

    public void Register(ConsumerMetadata metadata)
    {
        lock (_lock)
        {
            _consumers.Add(metadata);
        }
    }

    public IReadOnlyList<ConsumerMetadata> GetAll()
    {
        lock (_lock)
        {
            return _consumers.ToList();  // Copy under lock
        }
    }
}
```

## Technical Details

**Affected Files:**
- `src/Framework.Messages.Core/ConsumerRegistry.cs:13-31`

**Race Condition Scenario:**
1. Thread A calls `Register()` → reads `_consumers.Count` = 5
2. Thread B calls `Register()` → reads `_consumers.Count` = 5
3. Thread A writes index 5
4. Thread B writes index 5 (overwrites A's write)
5. One consumer lost, registry corrupted

## Acceptance Criteria

- [ ] Choose thread-safety approach (recommend: Freeze Pattern)
- [ ] Implement solution
- [ ] Add XML doc: "Thread-safe for concurrent registration"
- [ ] Add unit test: `should_handle_concurrent_registration`
- [ ] Verify: No `CA2002` analyzer warnings
- [ ] Run full test suite

## Work Log

- **2026-01-19:** Issue identified during Stephen Toub review

## Resources

- Review: Agent abf9230
- File: `src/Framework.Messages.Core/ConsumerRegistry.cs`
- Pattern: .NET's `FrozenDictionary<T>` for inspiration

### 2026-01-19 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-19 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
