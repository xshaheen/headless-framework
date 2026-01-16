---
status: ready
priority: p1
issue_id: "004"
tags: [async-patterns, cancellation, contracts, critical-bug]
dependencies: []
---

# Fix Incorrect Async Cancellation Pattern

CRITICAL violation of async cancellation contract - silent return instead of throw.

## Problem Statement

When `cancellationToken.IsCancellationRequested` is true, the code returns silently instead of throwing `OperationCanceledException`. This violates the async/await cancellation contract that all .NET APIs follow.

**Location:** `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:89-94`

**Why Critical:**
- Breaks async combinators (`Task.WhenAll`, `Task.WhenAny`)
- Callers cannot distinguish success from cancellation
- Violates .NET Framework conventions
- Makes debugging cancellation issues impossible

## Findings

**From strict-dotnet-reviewer:**

```csharp
// Check if already cancelled to avoid race condition
if (cancellationToken.IsCancellationRequested)
{
    await _RemoveSubscriptionAsync(typeof(TPayload)).AnyContext();
    return;  // WRONG: Silent cancellation
}
```

**The async cancellation contract:**
When async method accepts `CancellationToken`, it MUST throw `OperationCanceledException` when cancelled, not silently return.

**Why it matters:**
```csharp
// Caller code that breaks with silent cancellation:
try
{
    await messageBus.SubscribeAsync<MyMessage>(handler, cts.Token);
    Console.WriteLine("Subscribed successfully");  // PRINTS even when cancelled!
}
catch (OperationCanceledException)
{
    Console.WriteLine("Subscription cancelled");  // NEVER REACHED
}

// Task combinators that break:
var tasks = new[]
{
    messageBus.SubscribeAsync<Message1>(h1, cts.Token),
    messageBus.SubscribeAsync<Message2>(h2, cts.Token),
};

try
{
    await Task.WhenAll(tasks);
    // Silent return = Task.WhenAll thinks ALL succeeded
    // But some were actually cancelled
}
catch (OperationCanceledException)
{
    // Never caught because no exception thrown
}
```

**API contract documentation:**
> When a CancellationToken is cancelled, the method MUST throw OperationCanceledException (or TaskCanceledException which inherits from it). This allows callers to distinguish successful completion from cancellation.

## Proposed Solutions

### Option 1: Throw After Cleanup (Recommended)

**Approach:** Perform cleanup, then throw `OperationCanceledException`.

```csharp
if (cancellationToken.IsCancellationRequested)
{
    await _RemoveSubscriptionAsync(typeof(TPayload)).AnyContext();
    cancellationToken.ThrowIfCancellationRequested();  // ADD THIS
}
```

**Pros:**
- Minimal code change (one line)
- Follows .NET conventions
- Preserves cleanup logic
- Clear cancellation signal to caller

**Cons:**
- None

**Effort:** 2 minutes

**Risk:** None - this is the correct pattern

---

### Option 2: Remove Check Entirely

**Approach:** Let registration handle cancellation, remove pre-check.

```csharp
// Remove lines 89-94 entirely
state.Registration = cancellationToken.Register(() => _RemoveSubscriptionSync(typeof(TPayload)));
```

**Pros:**
- Even simpler
- One less code path
- Token registration handles cancelled tokens correctly

**Cons:**
- Loses early-exit optimization
- Small window where work done before discovering cancellation

**Effort:** 1 minute

**Risk:** Low - registration handles this correctly

---

### Option 3: Combine with Token Check

**Approach:** Use token's built-in helper.

```csharp
cancellationToken.ThrowIfCancellationRequested();

await _RemoveSubscriptionAsync(typeof(TPayload)).AnyContext();
return;
```

**Pros:**
- Explicit check

**Cons:**
- Still does cleanup after throwing (dead code)

**Not recommended** - Option 1 is better

## Recommended Action

**Implement Option 1** - Add `cancellationToken.ThrowIfCancellationRequested()`.

1. Change line 94 from `return;` to `cancellationToken.ThrowIfCancellationRequested();`
2. Add test to verify exception is thrown on cancelled token
3. Verify async combinators work correctly

**Alternative:** If removing the pre-check entirely (Option 2), also acceptable and simpler.

## Technical Details

**Affected files:**
- `src/Framework.Messaging.MassTransit/MassTransitMessageBusAdapter.cs:89-94` - pre-cancellation check

**Async cancellation contract:**
- `Task<T>` returning method with `CancellationToken` parameter
- If token is cancelled → throw `OperationCanceledException`
- Allows `Task.Status == TaskStatus.Canceled`
- Enables proper exception handling and combinators

**Related .NET APIs following this pattern:**
- `HttpClient.GetAsync()`
- `Stream.ReadAsync()`
- `DbContext.SaveChangesAsync()`
- ALL async .NET Framework APIs

**Testing requirements:**
- Test subscription with pre-cancelled token
- Verify `OperationCanceledException` thrown
- Test `Task.WhenAll` with cancelled subscription
- Verify task status is Canceled

## Resources

- **PR:** #136 (MassTransit adapter)
- **Review:** strict-dotnet-reviewer (Stephen Toub review)
- **Docs:** [Task-based Asynchronous Pattern](https://docs.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap)
- **Pattern:** [Cancellation in Managed Threads](https://docs.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)

## Acceptance Criteria

- [ ] `OperationCanceledException` thrown when token is cancelled
- [ ] Test verifies exception on pre-cancelled token
- [ ] Task combinators work correctly (`Task.WhenAll`)
- [ ] Task status is `Canceled` (not `RanToCompletion`)
- [ ] Code review confirms async contract compliance
- [ ] All existing tests pass

## Notes

- **Severity:** CRITICAL - violates fundamental async contract
- **Impact:** Breaks caller exception handling and async combinators
- **Timeline:** MUST fix before merging PR #136
- **Fix complexity:** Trivial (one line change)
- **Quote from review:** "When an async method accepts a CancellationToken, it should throw OperationCanceledException when cancelled, not silently return. This is the async contract that all .NET APIs follow."

## Work Log

### 2026-01-16 - Initial Discovery

**By:** Claude Code (strict-dotnet-reviewer agent)

**Actions:**
- Identified silent cancellation pattern during code review
- Analyzed async cancellation contract requirements
- Demonstrated broken caller scenarios
- Confirmed one-line fix

**Learnings:**
- Async methods with CancellationToken MUST throw on cancellation
- Silent return breaks Task.WhenAll/WhenAny
- .NET Framework APIs universally follow this pattern
- Fix is trivial but critical for correctness

### 2026-01-16 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
