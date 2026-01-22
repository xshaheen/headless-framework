---
status: pending
priority: p1
issue_id: "005"
tags: [code-review, dotnet, memory-leak]
dependencies: []
---

# Diagnostic Subscription Memory Leak

## Problem Statement

`DiagnosticRegister.StartAsync` subscribes to `DiagnosticListener.AllListeners` but never disposes the subscription, causing a memory leak.

## Findings

**File:** `src/Headless.Messaging.SqlServer/Diagnostics/IProcessingServer.DiagnosticRegister.cs:10-21`

```csharp
public class DiagnosticRegister(DiagnosticProcessorObserver diagnosticProcessorObserver) : IProcessingServer
{
    public void Dispose()
    {
        GC.SuppressFinalize(this);  // No subscription disposal!
    }

    public ValueTask StartAsync(CancellationToken stoppingToken)
    {
        DiagnosticListener.AllListeners.Subscribe(diagnosticProcessorObserver);
        return ValueTask.CompletedTask;
    }
}
```

**Problems:**
1. `Subscribe()` returns `IDisposable` that is discarded
2. Observer remains subscribed indefinitely after `Dispose()`
3. `GC.SuppressFinalize` is called but there's no finalizer
4. `DiagnosticProcessorObserver.TransBuffer` (ConcurrentDictionary) can grow unbounded if transactions aren't properly cleaned

**Additional concern in `DiagnosticProcessorObserver.cs:12`:**
```csharp
public ConcurrentDictionary<Guid, SqlServerOutboxTransaction> TransBuffer { get; } = new();
```
No size limit or cleanup mechanism for stale entries.

## Proposed Solutions

### Option 1: Store and Dispose Subscription (Recommended)

**Approach:** Store the IDisposable and dispose on cleanup.

```csharp
public sealed class DiagnosticRegister(DiagnosticProcessorObserver diagnosticProcessorObserver) : IProcessingServer
{
    private IDisposable? _subscription;

    public void Dispose()
    {
        _subscription?.Dispose();
    }

    public ValueTask StartAsync(CancellationToken stoppingToken)
    {
        _subscription = DiagnosticListener.AllListeners.Subscribe(diagnosticProcessorObserver);
        return ValueTask.CompletedTask;
    }
}
```

**Pros:**
- Proper cleanup
- No memory leak
- Simple fix

**Cons:**
- None

**Effort:** 15 minutes

**Risk:** Low

---

### Option 2: Add TransBuffer Cleanup

**Approach:** Add periodic cleanup for stale transaction entries.

```csharp
// In DiagnosticProcessorObserver
public void CleanupStaleEntries(TimeSpan maxAge)
{
    // Remove entries older than maxAge based on creation time
}
```

**Pros:**
- Prevents unbounded growth
- Handles edge cases where events are missed

**Cons:**
- Need to track entry creation time
- More complex

**Effort:** 1 hour

**Risk:** Low

## Recommended Action

Implement Option 1 immediately. Consider Option 2 as a follow-up for robustness.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.SqlServer/Diagnostics/IProcessingServer.DiagnosticRegister.cs`
- `src/Headless.Messaging.SqlServer/Diagnostics/DiagnosticProcessorObserver.cs` (for Option 2)

## Acceptance Criteria

- [ ] Subscription IDisposable is stored
- [ ] Dispose() cleans up subscription
- [ ] Remove unnecessary GC.SuppressFinalize
- [ ] Add sealed keyword to class
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Strict .NET Reviewer Agent

**Actions:**
- Identified subscription not being disposed
- Found TransBuffer unbounded growth risk
- Documented cleanup approach
