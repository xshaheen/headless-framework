---
status: pending
priority: p2
issue_id: "024"
tags: [resource-leak, dispose, async, semaphore]
dependencies: []
---

# Undisposed SemaphoreSlim Instances (Resource Leak)

## Problem Statement

SemaphoreSlim instances created but never disposed, causing resource leaks in long-running applications.

## Findings

**Affected Locations**:
- Dispatcher components using SemaphoreSlim for throttling
- Connection pools using SemaphoreSlim for concurrency control

**Impact**:
- Memory leak (small but accumulates)
- Handle leak on Windows
- May cause perf degradation over time

## Proposed Solutions

### Option 1: Implement IDisposable/IAsyncDisposable (RECOMMENDED)
**Effort**: 2-3 hours

```csharp
public sealed class Dispatcher : IDispatcher, IAsyncDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
    }
}
```

### Option 2: Use ObjectPool<SemaphoreSlim>
**Effort**: 3-4 hours
**Benefit**: Reduces allocations

## Recommended Action

Implement Option 1 - proper disposal pattern.

## Acceptance Criteria

- [ ] All classes with SemaphoreSlim implement IAsyncDisposable
- [ ] Dispose called in hosted service shutdown
- [ ] Integration test verifies no leaks (heap snapshot)
- [ ] Documentation explains disposal requirements

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Strict .NET Reviewer Agent)

**Actions:**
- Identified undisposed SemaphoreSlim instances
- Proposed IAsyncDisposable implementation
