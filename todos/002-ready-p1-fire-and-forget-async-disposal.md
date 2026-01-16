---
status: ready
priority: p1
issue_id: "002"
tags: [async-patterns, disposal, masstransit, critical-bug]
dependencies: []
---

# Fix Fire-and-Forget Async in Subscription Cleanup

CRITICAL async anti-pattern that violates fundamental async/await principles.

## Problem Statement

`_RemoveSubscriptionSync` method fires-and-forgets async work from a synchronous callback, violating .NET async best practices. The async cleanup operation (`_RemoveSubscriptionAsync`) is started but not awaited, leading to unobserved exceptions and race conditions.

**Location:** `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:100-124`

**Why Critical:**
- Exceptions can be lost despite `ContinueWith` error logging
- Disposal racing with cleanup creates undefined behavior
- Violates async contract (no awaiting async work)
- ThreadPool saturation under high churn

## Findings

**From strict-dotnet-reviewer:**

```csharp
private void _RemoveSubscriptionSync(Type payloadType)
{
    if (!_subscriptions.TryGetValue(payloadType, out var state))
    {
        return;
    }

    // Track removal task for proper cleanup and error handling
    var removalTask = _RemoveSubscriptionAsync(payloadType);  // STARTS ASYNC WORK
    state.PendingRemovalTask = removalTask;                   // RACE CONDITION

    // Continue task to log unobserved exceptions
    _ = removalTask.ContinueWith(
        static (t, s) =>
        {
            var (log, type) = ((ILogger, Type))s!;
            if (t.Exception is not null)
            {
                log.LogError(t.Exception, "Unhandled error removing subscription for {Type}", type.Name);
            }
        },
        (logger, payloadType),
        TaskScheduler.Default
    );
}
```

**Multiple problems identified:**

1. **Fire-and-forget async**: Starting async work from sync callback without awaiting
2. **Race condition**: Setting `PendingRemovalTask` has no synchronization with `DisposeAsync` readers
3. **Broken exception handling**: `ContinueWith` won't see exceptions because `_RemoveSubscriptionAsync` catches internally
4. **Discard abuse**: `_ = ` doesn't make fire-and-forget safe

**Why dangerous:**
- If `_RemoveSubscriptionAsync` throws before continuation attaches → unobserved exception
- If `DisposeAsync` runs before `PendingRemovalTask` set → won't wait for cleanup
- Cancellation during disposal → overlapping async cleanup operations

## Proposed Solutions

### Option 1: Track Pending Cleanups in ConcurrentBag (Recommended)

**Approach:** Replace per-state tracking with global bag of cleanup tasks.

```csharp
private readonly ConcurrentBag<Task> _pendingCleanups = new();

private void _RemoveSubscriptionSync(Type payloadType)
{
    if (!_subscriptions.TryGetValue(payloadType, out var state))
    {
        return;
    }

    var removalTask = Task.Run(async () =>
    {
        try
        {
            await _RemoveSubscriptionAsync(payloadType).AnyContext();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing subscription for {Type}", payloadType.Name);
        }
    });

    _pendingCleanups.Add(removalTask);
}

public async ValueTask DisposeAsync()
{
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

    // Existing disposal logic...
    var subscriptions = _subscriptions.ToArray();
    _subscriptions.Clear();

    var stopTasks = /* existing cleanup */;
    await Task.WhenAll(stopTasks).AnyContext();

    // NEW: Wait for all pending cleanups
    await Task.WhenAll(_pendingCleanups).AnyContext();
}
```

**Pros:**
- Eliminates race with per-state tracking
- All async work properly coordinated
- Simple to reason about
- Thread-safe bag handles concurrent adds

**Cons:**
- Unbounded bag growth (tasks never removed)
- Memory overhead proportional to subscription churn

**Effort:** 1 hour

**Risk:** Low - straightforward pattern

---

### Option 2: Redesign to Avoid Sync Callbacks (Better)

**Approach:** Use `CancellationTokenSource.CreateLinkedTokenSource` and poll for cancellation asynchronously instead of sync callback.

```csharp
// No CancellationToken.Register - use linked token instead
var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

// Background task polls for cancellation
_ = Task.Run(async () =>
{
    try
    {
        await linkedCts.Token.WaitHandle.WaitOneAsync();
        await _RemoveSubscriptionAsync(payloadType);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error in cancellation cleanup");
    }
});
```

**Pros:**
- Avoids sync callback context entirely
- Fully async cleanup path
- No fire-and-forget issues

**Cons:**
- More complex architecture
- Additional CancellationTokenSource allocations
- Background task overhead

**Effort:** 2-3 hours

**Risk:** Medium - requires careful testing

---

### Option 3: Accept Fire-and-Forget with Improved Tracking

**Approach:** Keep current design but fix race conditions and exception handling.

**Pros:**
- Minimal changes

**Cons:**
- Still fundamentally flawed pattern
- Doesn't address core async anti-pattern

**Effort:** 30 minutes

**Risk:** High - doesn't fix root cause

**Not recommended**

## Recommended Action

**Implement Option 1** - Track pending cleanups in `ConcurrentBag`.

1. Add `ConcurrentBag<Task> _pendingCleanups` field
2. In `_RemoveSubscriptionSync`, wrap async call in `Task.Run` with error handling
3. Add cleanup task to bag
4. In `DisposeAsync`, await all tasks in bag
5. Remove `PendingRemovalTask` property from `SubscriptionState`
6. Add stress test for concurrent subscribe/cancel/dispose

**Alternative:** If willing to invest more time, Option 2 is architecturally superior but requires more extensive refactoring.

## Technical Details

**Affected files:**
- `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:18` - add `_pendingCleanups` field
- `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:100-124` - `_RemoveSubscriptionSync` method
- `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:160-200` - `DisposeAsync` method
- `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:207` - remove `PendingRemovalTask` property

**Related components:**
- `CancellationToken.Register` callback execution (ThreadPool)
- `_RemoveSubscriptionAsync` async cleanup
- `DisposeAsync` coordination

**Testing requirements:**
- Stress test: 100 concurrent subscribe → cancel cycles
- Verify all cleanup tasks complete before disposal finishes
- Check for unobserved task exceptions
- Validate no ThreadPool saturation

## Resources

- **PR:** #136 (MassTransit adapter)
- **Review:** strict-dotnet-reviewer (Stephen Toub review)
- **Related:** Issue #001 (race condition in SubscriptionState)
- **Pattern:** [Task.Run for fire-and-forget](https://blog.stephencleary.com/2014/06/fire-and-forget-on-asp-net.html)

## Acceptance Criteria

- [ ] No fire-and-forget async operations remain
- [ ] All async cleanup properly tracked and awaited
- [ ] Concurrent stress test passes (100+ subscribe/cancel/dispose cycles)
- [ ] No unobserved task exceptions
- [ ] Disposal waits for all pending work to complete
- [ ] Code review confirms async patterns correct
- [ ] All existing tests still pass

## Notes

- **Severity:** CRITICAL - violates fundamental async principles
- **Impact:** Unobserved exceptions, race conditions, resource leaks
- **Timeline:** MUST fix before merging PR #136
- **Related:** Coordinate with issue #001 (both affect SubscriptionState)
- **Quote from review:** "Stephen Toub would say: 'If you need fire-and-forget here, you have deeper concurrency issues. Fix the design.'"

## Work Log

### 2026-01-16 - Initial Discovery

**By:** Claude Code (strict-dotnet-reviewer agent)

**Actions:**
- Identified fire-and-forget async anti-pattern during code review
- Analyzed exception handling flow (`ContinueWith` vs internal catch)
- Traced race conditions between cleanup and disposal
- Evaluated 3 solution approaches with effort/risk

**Learnings:**
- Discard operator (`_ = `) does NOT make fire-and-forget safe
- `ContinueWith` is pointless when method catches internally
- Sync callbacks can't await - need architectural change or task tracking
- Option 1 (ConcurrentBag) is pragmatic; Option 2 (redesign) is ideal

### 2026-01-16 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
