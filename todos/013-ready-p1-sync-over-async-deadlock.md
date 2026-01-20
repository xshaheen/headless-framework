---
status: ready
priority: p1
issue_id: "013"
tags: [async, deadlock, threading, sync-over-async]
dependencies: []
---

# Sync-Over-Async Pattern Causing Deadlock Risk

## Problem Statement

**CRITICAL THREADING ISSUE**: Multiple locations use sync-over-async anti-pattern (`.GetAwaiter().GetResult()`), creating deadlock risk in applications with synchronization contexts (ASP.NET Core, WPF, WinForms).

Affected files:
- `src/Framework.Messages.Core/Internal/ICapPublisher.Default.cs:96-97`
- `src/Framework.Messages.RabbitMQ/IConnectionChannelPool.cs:103`

## Findings

**Root Cause**: Synchronous wrapper methods block on async operations, violating async-all-the-way principle.

**Vulnerable Code #1** (ICapPublisher.Default.cs:96-97):
```csharp
public void Publish<T>(string name, T? value, string? callbackName = null)
{
    PublishAsync(name, value, callbackName).ConfigureAwait(false).GetAwaiter().GetResult();
    // ConfigureAwait(false) does NOT prevent deadlock!
}
```

**Vulnerable Code #2** (IConnectionChannelPool.cs:103):
```csharp
lock (_connectionLock)
{
    if (_connection == null || !_connection.IsOpen)
    {
        _connection = _connectionActivator().GetAwaiter().GetResult(); // Inside lock!
    }
}
```

**Deadlock Scenario** (ASP.NET Core):
1. Controller calls `Publish()` (sync method)
2. Request thread blocks on `GetAwaiter().GetResult()`
3. Async continuation tries to resume on request thread
4. Request thread blocked waiting for continuation
5. **DEADLOCK** - thread waits for itself

**Why ConfigureAwait(false) Doesn't Help**:
- Only affects WHERE continuation runs
- Doesn't prevent blocking the calling thread
- Calling thread still blocks waiting for Task completion

## Proposed Solutions

### Option 1: Remove Sync APIs (RECOMMENDED)
**Effort**: 1-2 hours
**Risk**: Low (breaking change, but correct)
**Breaking Change**: YES

```csharp
// DELETE these methods entirely:
// public void Publish<T>(string name, T? value, string? callbackName = null)

// Force callers to use async:
// await publisher.PublishAsync("topic", data);
```

**Migration Path**:
```csharp
// Before (deadlock risk):
publisher.Publish("topic", data);

// After (safe):
await publisher.PublishAsync("topic", data);
```

### Option 2: Use Task.Run() Workaround (NOT RECOMMENDED)
**Effort**: 30 minutes
**Risk**: Medium (hides problem, wastes threads)

```csharp
public void Publish<T>(string name, T? value, string? callbackName = null)
{
    Task.Run(() => PublishAsync(name, value, callbackName)).GetAwaiter().GetResult();
    // Better but still wasteful - queues to thread pool
}
```

**Problems**:
- Wastes thread pool threads
- Adds latency
- Doesn't work in all scenarios (still can deadlock in some contexts)

### Option 3: Rewrite as Truly Sync (COMPLEX)
**Effort**: 1-2 weeks
**Risk**: High (duplicate code paths)

Maintain separate sync and async code paths - NOT worth it for messaging.

## Recommended Action

**Implement Option 1** - remove sync APIs entirely.

**Justification**:
- Messaging is inherently I/O-bound (async-first)
- No legitimate use case for blocking publish
- Matches industry standards (MassTransit, NServiceBus are async-only)
- Prevents entire class of deadlock bugs

**Migration Guide**:
1. Mark sync methods `[Obsolete("Use PublishAsync instead", error: true)]`
2. Add analyzer warning in next minor version
3. Remove in next major version
4. Update documentation and samples

## Acceptance Criteria

- [ ] All sync-over-async methods removed or marked obsolete
- [ ] No `.GetAwaiter().GetResult()` in codebase
- [ ] No `.Wait()` or `.Result` on Tasks
- [ ] All public APIs are async-only or CPU-bound sync
- [ ] Documentation updated to async examples
- [ ] Migration guide published

## Technical Details

**Lock + Async Pattern** (IConnectionChannelPool.cs:103):

This is especially dangerous - combines two anti-patterns:
```csharp
lock (_lock)
{
    _connection = _connectionActivator().GetAwaiter().GetResult(); // NEVER do this!
}
```

**Correct Pattern**:
```csharp
// Use SemaphoreSlim for async locks
private readonly SemaphoreSlim _lock = new(1, 1);

await _lock.WaitAsync();
try
{
    if (_connection == null || !_connection.IsOpen)
    {
        _connection = await _connectionActivator();
    }
}
finally
{
    _lock.Release();
}
```

**Other Locations to Audit**:
```bash
# Find all sync-over-async patterns
rg "\.GetAwaiter\(\)\.GetResult\(\)" src/
rg "\.Wait\(\)" src/
rg "\.Result(?!\s*=)" src/  # .Result property (not assignment)
```

## Resources

- [Async Best Practices](https://docs.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
- [Don't Block on Async Code](https://blog.stephencleary.com/2012/07/dont-block-on-async-code.html)
- [ConfigureAwait FAQ](https://devblogs.microsoft.com/dotnet/configureawait-faq/)

## Notes

**Why This Matters**:
- ASP.NET Core has synchronization context (in some scenarios)
- Desktop apps (WPF, WinForms) ALWAYS have sync context
- Deadlocks are intermittent and hard to debug
- Only appears under load or specific timing

**Industry Consensus**:
- All modern .NET messaging libraries are async-only
- Microsoft guidelines: "async all the way down"
- Thread pool starvation is real concern

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Strict .NET Reviewer Agent)

**Actions:**
- Identified sync-over-async anti-patterns
- Analyzed deadlock scenarios
- Recommended API removal (breaking change justified)

**Priority Justification**:
- P1 because causes production deadlocks
- Simple fix (remove methods)
- Industry standard approach (async-only)

### 2026-01-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
