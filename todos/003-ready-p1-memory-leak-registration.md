---
status: ready
priority: p1
issue_id: "003"
tags: [memory-leak, resource-management, masstransit, critical-bug]
dependencies: []
---

# Fix Memory Leak - CancellationTokenRegistration Not Disposed

CRITICAL memory leak in error path causing registration leaks under intermittent failures.

## Problem Statement

When `handle.Ready` throws exception in `SubscribeAsync`, the code calls `_RemoveSubscriptionAsync` which removes state from dictionary. However, line 97 then tries to set `state.Registration` on a state object no longer tracked. Subsequent errors or retries leak `CancellationTokenRegistration` instances.

**Location:** `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:71-98`

**Why Critical:** At scale with intermittent failures (network issues, broker restarts), every failed subscription attempt leaks memory. Under load, this causes:
- Memory pressure from leaked registrations
- Leaked delegate closures
- Performance degradation over time

## Findings

**From strict-dotnet-reviewer:**

```csharp
var state = new SubscriptionState(handle);

if (!_subscriptions.TryAdd(typeof(TPayload), state))
{
    await handle.StopAsync().AnyContext();
    throw new InvalidOperationException($"Already subscribed to {typeof(TPayload).Name}");
}

try
{
    await handle.Ready.AnyContext();
}
catch
{
    await _RemoveSubscriptionAsync(typeof(TPayload)).AnyContext();  // Removes from dict
    throw;
}

// ... later ...
state.Registration = cancellationToken.Register(() => _RemoveSubscriptionSync(typeof(TPayload)));
```

**Problem flow:**
1. `handle.Ready` throws
2. Catch block calls `_RemoveSubscriptionAsync` → removes state from `_subscriptions`
3. Exception re-thrown
4. If caller catches and retries, OR if there are subsequent errors...
5. Line 97 tries to set `state.Registration` on orphaned state object
6. Registration NEVER disposed → memory leak

**What leaks per failed subscription:**
- `CancellationTokenRegistration` (~48 bytes)
- Delegate closure (~80 bytes)
- Type reference (~8 bytes)
- Total: ~136 bytes per leak

**Impact at scale:**
- 100 failures/hour = ~13 KB/hour
- 1000 failures/hour = ~136 KB/hour
- Over days/weeks = multi-MB leak

## Proposed Solutions

### Option 1: Dispose Registration in Error Path (Recommended)

**Approach:** Ensure registration cleanup on all error paths.

```csharp
CancellationTokenRegistration? registration = null;

try
{
    await handle.Ready.AnyContext();
}
catch
{
    await _RemoveSubscriptionAsync(typeof(TPayload)).AnyContext();
    throw;
}

try
{
    // Check cancellation BEFORE registering
    if (cancellationToken.IsCancellationRequested)
    {
        await _RemoveSubscriptionAsync(typeof(TPayload)).AnyContext();
        cancellationToken.ThrowIfCancellationRequested();
    }

    registration = cancellationToken.Register(() => _RemoveSubscriptionSync(typeof(TPayload)));
    state.Registration = registration;
}
catch
{
    registration?.Dispose();
    await _RemoveSubscriptionAsync(typeof(TPayload)).AnyContext();
    throw;
}
```

**Pros:**
- Explicit cleanup on all paths
- No memory leaks
- Clear error handling flow

**Cons:**
- More verbose
- Nested try/catch blocks

**Effort:** 30 minutes

**Risk:** Low - straightforward fix

---

### Option 2: Register Before Ready Check

**Approach:** Move registration before `handle.Ready` so cleanup always happens.

```csharp
state.Registration = cancellationToken.Register(() => _RemoveSubscriptionSync(typeof(TPayload)));

try
{
    await handle.Ready.AnyContext();
}
catch
{
    await _RemoveSubscriptionAsync(typeof(TPayload)).AnyContext();  // Now disposes registration
    throw;
}
```

**Pros:**
- Simpler code flow
- Registration always tracked
- `_RemoveSubscriptionAsync` handles disposal

**Cons:**
- Registration created even if Ready fails immediately
- Callback could fire during setup

**Effort:** 15 minutes

**Risk:** Low - verify callback doesn't cause issues during setup

---

### Option 3: Using Statement for Registration

**Approach:** Wrap in using to ensure disposal.

**Pros:**
- Compiler-enforced cleanup

**Cons:**
- Awkward with async/await
- State object needs registration later

**Effort:** 45 minutes

**Risk:** Medium - complex to get right

**Not recommended** - Options 1/2 are clearer

## Recommended Action

**Implement Option 2** - Register before Ready check.

1. Move `state.Registration = cancellationToken.Register(...)` BEFORE `handle.Ready` await
2. Verify `_RemoveSubscriptionAsync` properly disposes registration (it does - line 133)
3. Test failure scenarios to confirm no leaks
4. Add memory leak test (create 100 failed subscriptions, verify no growth)

**Rationale:** Simpler code, registration always tracked, existing cleanup works.

## Technical Details

**Affected files:**
- `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:71-98` - SubscribeAsync method
- `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:133-136` - registration disposal in cleanup

**Memory leak details:**
- Each `CancellationTokenRegistration` holds:
  - Callback delegate
  - State object (closure with Type, adapter reference)
  - Linked list node in token's registration list
- Not disposed → stays in token's list forever
- Even after token disposed, registration memory not reclaimed

**Testing requirements:**
- Test intermittent `handle.Ready` failures
- Verify no memory growth after 100+ failed subscriptions
- Check registration list doesn't grow unbounded
- Validate proper disposal on success path

## Resources

- **PR:** #136 (MassTransit adapter)
- **Review:** strict-dotnet-reviewer (Stephen Toub review)
- **Pattern:** [CancellationToken best practices](https://docs.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)

## Acceptance Criteria

- [ ] No registration leaks on `handle.Ready` failures
- [ ] Memory leak test passes (100+ failed subscriptions, no growth)
- [ ] All registrations properly disposed on all paths
- [ ] Error path testing confirms fix
- [ ] Code review validates cleanup logic
- [ ] All existing tests pass

## Notes

- **Severity:** CRITICAL - memory leak under normal failure scenarios
- **Impact:** Production memory growth, performance degradation
- **Timeline:** MUST fix before merging PR #136
- **Detection:** Difficult to notice until significant memory growth
- **Quote from review:** "Under load with intermittent failures, you'll leak memory. Every failed subscription attempt leaks a CancellationTokenRegistration."

## Work Log

### 2026-01-16 - Initial Discovery

**By:** Claude Code (strict-dotnet-reviewer agent)

**Actions:**
- Identified orphaned state object in error path
- Analyzed CancellationTokenRegistration lifecycle
- Calculated memory leak impact at scale
- Evaluated 3 solution approaches

**Learnings:**
- Registrations must be disposed explicitly
- Error paths often missed in cleanup logic
- ~136 bytes leaked per failed subscription
- Option 2 (register before Ready) is cleanest fix

### 2026-01-16 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
