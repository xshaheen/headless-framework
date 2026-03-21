---
status: pending
priority: p1
issue_id: "076"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix NATS ManualResetEventSlim.Wait blocking ThreadPool thread on pause

## Problem Statement

NatsConsumerClient._SubscriptionMessageHandler is an async void push callback running on a ThreadPool thread. It calls _pauseGate.Wait(_cancellationToken) — a synchronous blocking call. When the circuit opens, every in-flight NATS delivery pins a ThreadPool thread indefinitely. Under load with many subscriptions, this exhausts the ThreadPool and starves the entire process, including the timer callback that would fire the HalfOpen transition. All other 5 transports (InMemory, Pulsar, SQS, Redis, Azure Service Bus) correctly use TaskCompletionSource<bool> + await _pauseGate.Task.WaitAsync(ct). NATS is the sole outlier.

## Findings

- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs:4704 (_pauseGate field), 4757 (.Wait call)
- **Risk:** P1 — process-wide ThreadPool starvation DoS when circuit opens
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, security-sentinel, performance-oracle

## Proposed Solutions

### Replace ManualResetEventSlim with TaskCompletionSource pattern (matches other transports)
- **Pros**: Consistent with all other 5 transports, fully async, zero thread blocking
- **Cons**: Minor refactor of 6 lines
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace `private readonly ManualResetEventSlim _pauseGate = new(true)` with `private volatile TaskCompletionSource<bool> _pauseGate = _CreateCompletedGate()`. Replace `.Wait(_cancellationToken)` with `await _pauseGate.Task.WaitAsync(_cancellationToken).ConfigureAwait(false)`. In PauseAsync: `_pauseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)`. In ResumeAsync: `_pauseGate.TrySetResult(true)`. In DisposeAsync: `_pauseGate.TrySetResult(true)` before dispose. This is the identical pattern in InMemoryConsumerClient and SqsConsumerClient.

## Acceptance Criteria

- [ ] _pauseGate field is volatile TaskCompletionSource<bool>
- [ ] _SubscriptionMessageHandler awaits _pauseGate.Task.WaitAsync(ct)
- [ ] PauseAsync creates new TCS with RunContinuationsAsynchronously
- [ ] ResumeAsync calls TrySetResult(true)
- [ ] DisposeAsync completes gate before disposing
- [ ] Unit test added verifying no thread blocking during pause

## Notes

Pattern documented in docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md (Pattern 1). PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
