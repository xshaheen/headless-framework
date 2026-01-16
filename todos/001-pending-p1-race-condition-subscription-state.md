---
status: pending
priority: p1
issue_id: "001"
tags: [concurrency, thread-safety, masstransit, critical-bug]
dependencies: []
---

# Fix Race Condition in SubscriptionState Mutable Properties

CRITICAL concurrency bug that will cause issues in production under normal concurrent usage.

## Problem Statement

`SubscriptionState` has mutable properties (`Registration`, `PendingRemovalTask`) that are set from cancellation callbacks (ThreadPool) and read in `DisposeAsync` without synchronization. This creates a race condition where concurrent disposal and cancellation can lead to undefined behavior.

**Location:** `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:203-208`

**Why Critical:** Will fail under normal concurrent usage (not just extreme load). When subscription cancellation fires during disposal, the lack of memory barriers can cause:
- Stale reads of state
- Double-disposal of handles
- Leaked resources
- ObjectDisposedException

## Findings

**From strict-dotnet-reviewer (Stephen Toub review):**

```csharp
private sealed class SubscriptionState(HostReceiveEndpointHandle handle)
{
    public HostReceiveEndpointHandle Handle { get; } = handle;
    public CancellationTokenRegistration? Registration { get; set; }  // MUTABLE
    public Task? PendingRemovalTask { get; set; }                     // MUTABLE
}
```

**Race Timeline:**
1. Thread A (CancellationToken callback): Sets `PendingRemovalTask` in `_RemoveSubscriptionSync` (line 109)
2. Thread B (DisposeAsync): Reads `PendingRemovalTask` simultaneously (lines 173-178)
3. **RACE**: No memory barrier, no lock, no volatile read

**Evidence:**
- Line 109: `state.PendingRemovalTask = removalTask;` (write from ThreadPool)
- Line 173: `if (kvp.Value.PendingRemovalTask is not null)` (read from disposal thread)
- No synchronization primitives between these operations

## Proposed Solutions

### Option 1: Use Volatile Read/Write (Minimal Change)

**Approach:** Add `Volatile` wrappers to ensure memory visibility.

```csharp
private sealed class SubscriptionState(HostReceiveEndpointHandle handle)
{
    private Task? _pendingRemovalTask;
    private CancellationTokenRegistration? _registration;

    public HostReceiveEndpointHandle Handle { get; } = handle;

    public CancellationTokenRegistration? Registration
    {
        get => Volatile.Read(ref _registration);
        set => Volatile.Write(ref _registration, value);
    }

    public Task? PendingRemovalTask
    {
        get => Volatile.Read(ref _pendingRemovalTask);
        set => Interlocked.Exchange(ref _pendingRemovalTask, value);
    }
}
```

**Pros:**
- Minimal code changes
- Ensures memory visibility
- Low performance overhead

**Cons:**
- Still has mutable shared state
- Requires understanding of memory models
- Doesn't prevent logical races (just makes them deterministic)

**Effort:** 30 minutes

**Risk:** Low - well-understood pattern

---

### Option 2: Redesign to Eliminate Mutable State (Recommended)

**Approach:** Restructure disposal to not rely on reading mutable state. Dispose registrations first, then handles.

```csharp
public async ValueTask DisposeAsync()
{
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

    var subscriptions = _subscriptions.ToArray();

    // Phase 1: Dispose all cancellation registrations FIRST
    // This prevents new callbacks from starting
    foreach (var kvp in subscriptions)
    {
        if (kvp.Value.Registration.HasValue)
        {
            await kvp.Value.Registration.Value.DisposeAsync();
        }
    }

    // Phase 2: NOW safe to clear and stop handles
    // No more callbacks can fire
    _subscriptions.Clear();

    foreach (var kvp in subscriptions)
    {
        try
        {
            await kvp.Value.Handle.StopAsync().AnyContext();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Error stopping {Type}", kvp.Key.Name);
        }
    }
}

// Remove PendingRemovalTask tracking entirely
```

**Pros:**
- Eliminates race condition at architectural level
- Simpler to reason about
- No need for volatile/interlocked
- Removes unnecessary complexity

**Cons:**
- Larger code change
- Need to verify CancellationTokenRegistration.DisposeAsync stops callbacks

**Effort:** 1-2 hours

**Risk:** Medium - requires testing disposal behavior

---

### Option 3: Use Lock-Based Synchronization

**Approach:** Add lock around state mutations.

**Pros:**
- Straightforward correctness

**Cons:**
- Performance overhead
- Can introduce deadlocks if not careful
- Over-engineered for this problem

**Effort:** 1 hour

**Risk:** Medium - potential deadlock issues

**Not recommended** - Option 2 is cleaner

## Recommended Action

**Implement Option 2** - Redesign disposal to eliminate mutable state reading.

1. Modify `DisposeAsync` to dispose registrations first (stops callbacks)
2. Then clear subscriptions and stop handles
3. Remove `PendingRemovalTask` property entirely
4. Simplifies code while fixing race condition
5. Add concurrent stress test to verify fix

## Technical Details

**Affected files:**
- `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:109` - write to PendingRemovalTask
- `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:160-200` - DisposeAsync reads state
- `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:203-208` - SubscriptionState class

**Related components:**
- `_RemoveSubscriptionSync` method (fire-and-forget cleanup)
- `CancellationToken.Register` callbacks (ThreadPool execution)

**Testing requirements:**
- Add concurrent disposal stress test
- Verify no ObjectDisposedException under concurrent cancel+dispose
- Validate resource cleanup (no leaks)

## Resources

- **PR:** #136 (MassTransit adapter)
- **Review:** strict-dotnet-reviewer (Stephen Toub review)
- **Related:** Issue #002 (fire-and-forget async issue)

## Acceptance Criteria

- [ ] Race condition eliminated (no mutable shared state without sync)
- [ ] Concurrent disposal + cancellation stress test passes
- [ ] No ObjectDisposedException thrown
- [ ] No resource leaks (verify handle cleanup)
- [ ] Code review confirms thread safety
- [ ] All existing tests still pass

## Notes

- **Severity:** CRITICAL - will fail in production under normal concurrent usage
- **Impact:** Resource leaks, crashes, undefined behavior
- **Timeline:** MUST fix before merging PR #136
- **Related:** Also affects issue #002 (fire-and-forget), should coordinate fixes

## Work Log

### 2026-01-16 - Initial Discovery

**By:** Claude Code (strict-dotnet-reviewer agent)

**Actions:**
- Identified race condition during comprehensive code review
- Analyzed memory visibility issues in SubscriptionState
- Traced concurrent access patterns (cancellation callback vs disposal)
- Drafted 3 solution approaches with effort/risk assessment

**Learnings:**
- Stephen Toub would immediately flag this in code review
- Not "extreme load" issue - normal concurrent usage triggers it
- Mutable properties without synchronization = undefined behavior
- Option 2 (eliminate mutable state) is architecturally cleanest
