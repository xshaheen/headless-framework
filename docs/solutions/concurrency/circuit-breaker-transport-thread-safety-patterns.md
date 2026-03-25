---
title: "Thread Safety and Resilience Patterns in .NET Messaging Circuit Breakers"
category: concurrency
date: 2026-03-21
tags: [threading, circuit-breaker, dotnet, messaging, timer, async, interlocked, cancellation, taskcompletionsource, semaphore, opentelemetry, rabbitmq, nats, pulsar, sqs, redis]
problem_type: concurrency_issue
components:
  - CircuitBreakerStateManager
  - InMemoryConsumerClient
  - PulsarConsumerClient
  - SqsConsumerClient
  - RedisConsumerClient
  - RabbitMqConsumerClient
  - NatsConsumerClient
  - CircuitBreakerMetrics
  - IConsumerRegister
  - Setup
symptoms:
  - ThreadPool starvation from synchronous ManualResetEventSlim.Wait in async transport clients
  - Pause/resume TOCTOU race via non-atomic volatile bool in RabbitMQ client
  - Semaphore leaked in NATS when Task.Run is cancelled before the lambda executes
  - Stale timer callbacks fire after circuit reset or dispose due to missing generation counter
  - Dispose race with in-flight timer callbacks causes use-after-free on group state
  - Off-by-one in escalation level read before vs. after increment
  - Exception detail leaked to broker headers enabling information disclosure
  - OTel metric cardinality unbounded without MaxTrackedGroups cap
  - Fire-and-forget Task.Run exceptions silently swallowed
  - High allocation in OTel gauge callbacks via List<Measurement<int>> per scrape
severity: p1
research:
  agents: [context-analyzer, solution-extractor, related-docs-finder, prevention-strategist, category-classifier]
  documented_at: 2026-03-21T20:00:00Z
  conversation_context: "PR #194 per-group circuit breaker + adaptive retry backpressure — 21 fixes from 7-agent code review"
---

# Thread Safety and Resilience Patterns in .NET Messaging Circuit Breakers

PR #194 added per-consumer-group circuit breakers and adaptive retry backpressure to a .NET messaging framework with 8 transport adapters. A 7-agent code review surfaced 21 fixable concurrency and safety issues. The fixes cluster into 8 recurring patterns documented here for future reference.

## Root Cause Analysis

The dominant failure themes:

1. **Thread blocking in async paths** — sync primitives designed for sync code misused in async/await contexts
2. **Non-atomic check-then-set** — `volatile` used where `Interlocked` operations are required
3. **Resource acquisition outside execution scope** — semaphores acquired before the work that consumes them is guaranteed to run
4. **Stale timer callbacks** — no generation counter to distinguish live callbacks from queued-but-already-cancelled ones
5. **Dispose races** — timer callbacks executing concurrently with teardown, no disposed guard
6. **Silent task failures** — fire-and-forget `Task.Run` swallowing exceptions, leaving circuit stuck
7. **Unbounded OTel cardinality** — metric tag sets growing with untrusted input at runtime
8. **Header data leakage** — exception messages written verbatim to broker headers

## Solution Patterns

### Pattern 1: ManualResetEventSlim.Wait → TaskCompletionSource async gate

`ManualResetEventSlim.Wait()` blocks the calling thread. In async consumers running on ThreadPool threads, many paused consumers can exhaust the ThreadPool, causing starvation across the process.

**Affected:** `InMemoryConsumerClient`, `PulsarConsumerClient`, `SqsConsumerClient`, `RedisConsumerClient`

```csharp
// BEFORE (blocks ThreadPool thread)
private readonly ManualResetEventSlim _pauseGate = new(initialState: true);

// In consume loop:
_pauseGate.Wait(cancellationToken);


// AFTER (yields thread back to pool while paused)
private volatile TaskCompletionSource<bool> _pauseGate = _CreateCompletedGate();

private static TaskCompletionSource<bool> _CreateCompletedGate()
{
    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    tcs.SetResult(true);
    return tcs;
}

public Task PauseAsync(CancellationToken cancellationToken)
{
    _pauseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    return Task.CompletedTask;
}

public Task ResumeAsync(CancellationToken cancellationToken)
{
    _pauseGate.TrySetResult(true);
    return Task.CompletedTask;
}

// In consume loop:
await _pauseGate.Task.WaitAsync(cancellationToken);
```

`TaskCreationOptions.RunContinuationsAsynchronously` ensures continuations don't run inline on the `TrySetResult` caller's thread, avoiding secondary starvation on resume.

---

### Pattern 2: volatile bool → Interlocked.CompareExchange for pause/resume

`volatile bool` guarantees memory visibility but not atomicity of compound operations. Two concurrent callers can both observe `_paused == false` and both proceed to pause, creating duplicate state transitions.

**Affected:** `RabbitMqConsumerClient`

```csharp
// BEFORE (TOCTOU race)
private volatile bool _paused;
if (!_paused) { _paused = true; /* pause */ }


// AFTER (atomic compare-and-swap)
private int _paused; // 0 = running, 1 = paused

public Task PauseAsync(CancellationToken cancellationToken)
{
    if (Interlocked.CompareExchange(ref _paused, 1, 0) == 0)
    {
        // won the race — exactly one caller reaches here
        _pauseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    return Task.CompletedTask;
}

public Task ResumeAsync(CancellationToken cancellationToken)
{
    if (Interlocked.CompareExchange(ref _paused, 0, 1) == 1)
    {
        // won the race — exactly one caller reaches here
        _pauseGate.TrySetResult(true);
    }
    return Task.CompletedTask;
}
```

---

### Pattern 3: Semaphore acquired before Task.Run → schedule without cancellation

When a `CancellationToken` is cancelled between `await semaphore.WaitAsync()` and the `Task.Run` lambda starting, the semaphore slot is consumed but `Release()` in the `finally` block never executes. The concurrency limiter is permanently decremented.

**Affected:** `NatsConsumerClient`

```csharp
// BEFORE (semaphore leaked if Task.Run never starts)
await _semaphore.WaitAsync(cancellationToken);
await Task.Run(async () =>
{
    try { /* work */ }
    finally { _semaphore.Release(); }
}, cancellationToken);


// AFTER (scheduling ignores cancellation so finally always runs)
await _semaphore.WaitAsync(cancellationToken);
_ = RunConcurrentHandlerIgnoringCancellation(async () =>
{
    try { /* work */ }
    finally { _semaphore.Release(); }
}, cancellationToken);

internal static Task RunConcurrentHandlerIgnoringCancellation(
    Func<Task> handler,
    CancellationToken cancellationToken)
{
    _ = cancellationToken;
    return Task.Run(handler);
}
```

---

### Pattern 4: Timer generation counter to prevent stale callbacks

`System.Threading.Timer` queues callbacks to the ThreadPool. `Change(Timeout.Infinite, Timeout.Infinite)` prevents future firings but cannot dequeue an already-queued callback. Without a guard, reset or re-armed timers execute old callbacks against new state.

**Affected:** `CircuitBreakerStateManager`

```csharp
// In GroupCircuitState:
public int TimerGeneration; // incremented on each arm/reset/dispose

// When arming the timer:
int generation = Interlocked.Increment(ref state.TimerGeneration);
state.OpenTimer.Change(delay, Timeout.InfiniteTimeSpan);
// Capture (groupName, generation) in closure or timer state object

// Timer callback:
private void _OnOpenTimerElapsed(object? timerState)
{
    var (groupName, expectedGeneration) = ((string, int))timerState!;
    if (!_states.TryGetValue(groupName, out var state)) return;

    lock (state.SyncLock)
    {
        if (state.TimerGeneration != expectedGeneration) return; // stale callback, ignore
        // proceed with HalfOpen transition
    }
}
```

---

### Pattern 5: Dispose race — hold lock and check disposed guard in callback

Timer callbacks can race with `Dispose()`. Without synchronization, the callback reads fields (`OpenTimer`, state objects) that `Dispose()` has already nulled or freed.

**Affected:** `CircuitBreakerStateManager`

```csharp
private int _disposed; // 0 = live, 1 = disposed

public void Dispose()
{
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return; // idempotent

    foreach (var (_, state) in _states)
    {
        lock (state.SyncLock)
        {
            state.OpenTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            state.OpenTimer = null; // null inside lock — callback sees null and exits
        }
        state.OpenTimer?.Dispose(); // safe: already stopped and nulled
    }

    _states.Clear();
}

// In timer callback:
private void _OnOpenTimerElapsed(object? timerState)
{
    if (Volatile.Read(ref _disposed) != 0) return; // early exit before lock

    var (groupName, expectedGeneration) = ((string, int))timerState!;
    if (!_states.TryGetValue(groupName, out var state)) return;

    lock (state.SyncLock)
    {
        if (state.OpenTimer is null) return; // disposed inside lock
        if (state.TimerGeneration != expectedGeneration) return;
        // proceed
    }
}
```

---

### Pattern 6: Fire-and-forget Task.Run must observe exceptions

An unobserved faulting `Task` silently discards the exception. A circuit breaker background task that throws leaves the circuit in a permanent state with no diagnostic signal.

**Affected:** `CircuitBreakerStateManager`

```csharp
// BEFORE (exception swallowed, circuit stuck)
_ = Task.Run(async () =>
{
    // work that might throw
}, cancellationToken);


// AFTER (exception surfaced to logs)
_ = Task.Run(async () =>
{
    // work that might throw
}, cancellationToken)
    .ContinueWith(
        t => _logger.LogError(
            t.Exception,
            "Circuit breaker background task failed for group {Group}",
            groupName),
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted,
        TaskScheduler.Default);
```

`CancellationToken.None` on the continuation ensures the log fires even when the original token is cancelled.

---

### Pattern 7: OTel cardinality guard — register known groups at startup

If metric tag sets are created lazily per unique group name read from message headers, an adversary (or misconfiguration) can send arbitrarily many distinct group names, creating unbounded cardinality that overwhelms OTel exporters and can OOM the process.

**Affected:** `CircuitBreakerStateManager`, `ICircuitBreakerStateManager`, `IConsumerRegister`

```csharp
// Interface addition:
/// <summary>
/// Pre-registers known consumer groups to bound metric cardinality.
/// Call once at startup before processing begins.
/// </summary>
void RegisterKnownGroups(IEnumerable<string> groups);


// Implementation:
private const int MaxTrackedGroups = 1000;

public void RegisterKnownGroups(IEnumerable<string> groups)
{
    foreach (var group in groups)
    {
        if (_trackedGroups.Count >= MaxTrackedGroups)
        {
            _logger.LogWarning(
                "MaxTrackedGroups ({Max}) reached. Group {Group} will not be tracked.",
                MaxTrackedGroups, group);
            break;
        }
        _trackedGroups.TryAdd(group, _CreateMetricsForGroup(group));
    }
}


// Consumer host startup (ExecuteAsync):
_circuitBreakerStateManager?.RegisterKnownGroups(groupingMatches.Keys);
```

At runtime, reject metric tracking for unregistered group names rather than creating new instruments.

---

### Pattern 8: Exception info in message headers → type name only

Writing `ex.GetType().Name + "-->" + ex.Message` to broker message headers leaks internal details — connection strings, server hostnames, and sensitive data that appear in exception messages — to any consumer that reads those headers.

**Affected:** `IConsumerRegister`, `Messages/Message.cs`

```csharp
// BEFORE (leaks ex.Message to broker)
headers["x-exception"] = $"{ex.GetType().Name}-->{ex.Message}";


// AFTER (type name only; correlate via structured logs server-side)
headers["x-exception"] = ex.GetType().Name;

// Log full exception server-side with correlation ID for post-mortem
_logger.LogError(ex,
    "Circuit breaker recorded failure for group {Group}",
    groupName);
```

---

## Files Affected

```
src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs  (11 fixes)
src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerStateManager.cs
src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerMetrics.cs
src/Headless.Messaging.Core/Internal/IConsumerRegister.cs
src/Headless.Messaging.Core/Messages/Message.cs
src/Headless.Messaging.Core/Setup.cs
src/Headless.Messaging.RabbitMq/RabbitMqConsumerClient.cs
src/Headless.Messaging.Nats/NatsConsumerClient.cs
src/Headless.Messaging.InMemory/InMemoryConsumerClient.cs
src/Headless.Messaging.Pulsar/PulsarConsumerClient.cs
src/Headless.Messaging.Sqs/SqsConsumerClient.cs
src/Headless.Messaging.Redis/RedisConsumerClient.cs
docs/llms/messaging.md
tests/Headless.Messaging.Core.Tests.Unit/CircuitBreaker/CircuitBreakerStateManagerTests.cs
```

---

## Prevention Checklist

1. **No blocking waits in async code.** Never call `.Wait()`, `.Result`, `.GetAwaiter().GetResult()`, or `ManualResetEventSlim.Wait()` on ThreadPool threads. Use `await semaphore.WaitAsync(ct)` / `TaskCompletionSource<T>` exclusively.

2. **No `volatile bool` as a gate for pause/resume.** `volatile` only guarantees visibility, not atomicity of read-modify-write. Use `Interlocked.CompareExchange` or `SemaphoreSlim(1,1)` with `WaitAsync`/`Release`.

3. **Acquire semaphore inside `Task.Run`, not before it.** If you acquire before scheduling, a cancellation between acquire and execution leaks the slot permanently.

4. **Capture a generation counter when scheduling timer callbacks.** Before every timer callback acts, compare its captured generation with the current field. If mismatched, return immediately.

5. **Hold the state lock for the full Dispose sequence.** Dispose must null the timer inside the lock so a concurrent callback sees null and exits.

6. **Observe all `Task.Run` continuations.** Never bare fire-and-forget inside a state machine. Attach `.ContinueWith(t => { … }, TaskContinuationOptions.OnlyOnFaulted)`.

7. **Pre-register all OTel metric tag combinations at startup.** Never create tag sets at runtime from dynamic header values. Assert `TagCount ≤ N` in integration tests.

8. **Never write exception messages to broker headers.** Write only `ex.GetType().Name`. Exception messages may contain PII, secrets, or connection strings.

9. **Dispose order: cancel → stop timer → null references → release locks.** Cover idempotent double-Dispose in a unit test.

10. **Pass `CancellationToken` through everywhere in async state machines.** Stale callbacks often survive because they hold no reference to the current cancellation token.

---

## Test Patterns

**Pattern 1 — Async gate (no ThreadPool blocking)**
```csharp
[Fact]
public async Task PauseAsync_DoesNotBlockThreadPoolThread()
{
    var client = CreateClient();
    var pauseTask = client.PauseAsync(CancellationToken.None);

    // Must complete without stalling; give it a generous but finite window
    var completed = await Task.WhenAny(pauseTask, Task.Delay(TimeSpan.FromSeconds(3)));
    completed.Should().Be(pauseTask);
}
```

**Pattern 2 — Atomic pause/resume (no double-entry)**
```csharp
[Fact]
public async Task ConcurrentPauseAsync_OnlyOneCallerCreatesGate()
{
    var client = CreateClient();
    int gateCreations = 0; // spy on internal gate creation

    var tasks = Enumerable.Range(0, 1000)
        .Select(_ => Task.Run(() => client.PauseAsync(CancellationToken.None)));
    await Task.WhenAll(tasks);

    gateCreations.Should().Be(1, "only one CompareExchange winner should create the gate");
}
```

**Pattern 3 — Semaphore not leaked on cancellation**
```csharp
[Fact]
public async Task RunConcurrentHandlerIgnoringCancellation_runs_even_when_token_is_canceled()
{
    using var cts = new CancellationTokenSource();
    await cts.CancelAsync();
    var ran = false;

    await NatsConsumerClient.RunConcurrentHandlerIgnoringCancellation(
        () =>
        {
            ran = true;
            return Task.CompletedTask;
        },
        cts.Token);

    ran.Should().BeTrue();
}
```

**Pattern 4 — Stale timer callback ignored**
```csharp
[Fact]
public async Task Timer_StaleCallback_DoesNotTransitionState()
{
    var stateManager = CreateStateManager();
    stateManager.Trip("group-a"); // → Open, timer armed with generation 1
    stateManager.Reset("group-a"); // generation becomes 2

    await Task.Delay(200); // let old callback fire
    stateManager.GetState("group-a").Should().Be(CircuitState.Closed,
        "stale HalfOpen callback from gen 1 must not win");
}
```

**Pattern 5 — Dispose race safety**
```csharp
[Fact]
public async Task Dispose_ConcurrentWithTimerCallback_IsIdempotentAndSafe()
{
    for (int i = 0; i < 200; i++)
    {
        var sm = CreateStateManager();
        sm.Trip("g"); // arms 1 ms timer

        await Task.WhenAll(
            Task.Run(() => sm.Dispose()),
            Task.Run(() => sm.Dispose())); // no exception = pass
    }
}
```

**Pattern 6 — Fire-and-forget exception surfaced**
```csharp
[Fact]
public async Task BackgroundTask_WhenWorkerThrows_ExceptionIsLogged()
{
    var logger = Substitute.For<ILogger<CircuitBreakerStateManager>>();
    var sm = CreateStateManager(logger, workerFactory: _ => throw new InvalidOperationException("test"));

    sm.Trip("group-a");
    await Task.Delay(50);

    logger.Received().Log(
        LogLevel.Error,
        Arg.Any<EventId>(),
        Arg.Any<object>(),
        Arg.Is<Exception>(e => e is InvalidOperationException),
        Arg.Any<Func<object, Exception?, string>>());
}
```

**Pattern 7 — OTel cardinality bounded**
```csharp
[Fact]
public void RegisterKnownGroups_AtCap_LogsWarningAndStops()
{
    var logger = Substitute.For<ILogger<CircuitBreakerStateManager>>();
    var sm = CreateStateManager(logger);

    sm.RegisterKnownGroups(Enumerable.Range(0, 1001).Select(i => $"group-{i}"));

    sm.TrackedGroupCount.Should().Be(1000);
    logger.Received().Log(LogLevel.Warning, Arg.Any<EventId>(), Arg.Any<object>(),
        null, Arg.Any<Func<object, Exception?, string>>());
}
```

**Pattern 8 — No exception message in headers**
```csharp
[Fact]
public async Task DeadLetter_OnFailure_HeaderContainsOnlyTypeName()
{
    var capturedHeaders = new Dictionary<string, string>();
    var client = CreateClientWithHeaderSpy(capturedHeaders);

    var ex = new InvalidOperationException("Server=prod;Password=s3cr3t");
    await client.SimulateFailureAsync(ex);

    capturedHeaders["x-exception"].Should().Be("InvalidOperationException");
    capturedHeaders.Values.Should().NotContainMatch("*s3cr3t*");
}
```

---

## Code Review Gate

When a PR touches `IConsumerClient` implementations or state machine transitions:

1. **No sync-over-async.** Search diff for `.Wait()`, `.Result`, `.GetAwaiter().GetResult()`, `ManualResetEventSlim.Wait()`. Every hit is a blocker.

2. **No `volatile bool` gates.** Any `volatile bool _paused/stopped/running` used as a branch guard must be replaced with `Interlocked` or `SemaphoreSlim`.

3. **Semaphores acquired inside `Task.Run`, not before.** Confirm `WaitAsync` is inside the delegate, in `try/finally` that always releases.

4. **Timer callbacks check generation.** Every timer driving state transitions must capture and validate a generation counter that is incremented on Reset/Dispose.

5. **No bare fire-and-forget `Task.Run`.** Every result must be awaited or have a fault-only `ContinueWith` observer.

6. **OTel tags pre-registered at startup.** No runtime-derived strings in the metrics hot path. Tag cardinality is bounded.

7. **Broker headers write type name only.** `ex.GetType().Name`, never `.Message`, `.ToString()`, or `.StackTrace`.

---

## Related Docs

- `docs/brainstorms/2026-03-18-messaging-circuit-breaker-and-retry-backpressure-brainstorm.md` — design source for the circuit breaker state machine; half-open probe section underspecifies the generation-counter mechanism required for safe stale callback handling
- `docs/reviews/2026-03-21-pr-194-review.md` — Pass 3 review that surfaced findings 051–075
- PR #194: https://github.com/xshaheen/headless-framework/pull/194
