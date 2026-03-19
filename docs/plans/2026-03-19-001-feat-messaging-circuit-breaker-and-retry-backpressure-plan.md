---
title: "feat: Add circuit breaker and adaptive retry backpressure to messaging"
type: feat
date: 2026-03-19
origin: docs/brainstorms/2026-03-18-messaging-circuit-breaker-and-retry-backpressure-brainstorm.md
---

> **Verification gate:** Before claiming any task or story complete — run the plan's `verification_command` and confirm PASS. Do not mark complete based on reading code alone.

# feat: Add circuit breaker and adaptive retry backpressure to messaging

## Overview

Add two complementary resilience features to Headless.Messaging:

1. **Per-consumer-group circuit breaker** — Detects sustained transient dependency failures and pauses the transport consumer for that group. Messages backlog in the broker instead of flooding the DB with `Failed` rows.
2. **Adaptive retry processor polling** — Doubles the retry polling interval when transient failure rates are high. Recovers after sustained healthy cycles.

Both features address DB pressure during sustained downstream outages — a gap identified when comparing Headless.Messaging to MassTransit's broker-native DLQ approach.

## Problem Statement / Motivation

When a downstream dependency goes down:

1. **Retry processor hammers DB** — Fixed 60s polling with 200-message batches generates ~200 SELECTs + 200 UPDATEs per cycle, all pointless retries against a dead service.
2. **No circuit breaking** — Consumers keep invoking the dead downstream, generating failed messages that accumulate in the DB (up to 720K rows in a 2-hour outage at 100 msg/s).
3. **Collector can't keep up** — Deletes 1,000 rows every 5 minutes (288K/day) vs 720K rows created in 2 hours.

MassTransit avoids this with broker-native error queues (passive, zero polling cost). Our database-resident failure model needs active protection.

## Proposed Solution

### Circuit Breaker Architecture

```
Handler throws exception
        │
        ▼
SubscribeExecutor._SetFailedState
        │
        ▼ calls
ICircuitBreakerStateManager.ReportFailure(groupName, exception)
        │
        ├─ Exception in allowlist? (TimeoutException, HttpRequestException, etc.)
        │   NO → ignore, normal retry/failure handling
        │   YES ↓
        │
        ├─ Increment consecutive failure counter for group
        │
        ├─ Counter >= threshold (5)?
        │   NO → done
        │   YES ↓
        │
        ▼ notifies
ConsumerRegister reads circuit state
        │
        ▼
Pause transport consumer for this group (per-group CTS cancel)
```

**Key architectural decisions** (see brainstorm: `docs/brainstorms/2026-03-18-messaging-circuit-breaker-and-retry-backpressure-brainstorm.md`):

- **Circuit state in `ICircuitBreakerStateManager`** — new internal service injected into both `SubscribeExecutor` and `ConsumerRegister`. Executor reports failures; Register reads state to decide pause.
- **Circuit key = consumer group name** — matches transport consumer granularity. `ConsumerRegister` already iterates by group.
- **Exception predicate** — `Func<Exception, bool>` classifies exceptions. Default `CircuitBreakerDefaults.IsTransient` handles `TimeoutException`, `HttpRequestException` 5xx, `SocketException`, `BrokerConnectionException`, `TaskCanceledException` (timeout-only). Composable per-group. Safe by default — unknown exceptions don't trip.
- **Pause transport** — zero DB writes during outage. Broker handles backlog.
- **Escalating open duration** — 30s → 60s → 120s → 240s on repeated reopens. Prevents flapping.
- **Non-transient probe failure closes circuit** — dependency is presumably fine, message is just bad.

### Adaptive Retry Processor

- Double polling interval when >80% of executed batch are allowlist-matched failures
- Cap at 15 minutes
- Halve after 2 consecutive healthy cycles (>50% success)
- Reset after 3 cycles with 0 transient failures
- Skipped-due-to-open-circuit messages excluded from rate calculation

## Technical Approach

### Architecture

#### New Types

| Type | Package | Responsibility |
|------|---------|----------------|
| `ICircuitBreakerStateManager` | Core (internal) | Tracks per-group circuit state. Reports failures, manages state machine transitions. Thread-safe via `ConcurrentDictionary<string, CircuitState>`. |
| `CircuitState` | Core (internal) | Per-group state: `Closed`/`Open`/`HalfOpen`, consecutive failure count, escalation level, open-since timestamp. |
| `CircuitBreakerOptions` | Core (public) | Global defaults: `FailureThreshold`, `OpenDuration`, `MaxOpenDuration`, `HalfOpenProbeCount`, `IsTransientException` predicate. |
| `ConsumerCircuitBreakerOptions` | Core (public) | Per-group overrides: `Enabled`, `FailureThreshold`, `OpenDuration`, `IsTransientException` predicate. |
| `CircuitBreakerDefaults` | Core (public static) | Default `IsTransient(Exception)` predicate. Composable — users chain with `||`. |

#### Modified Types

| Type | Change |
|------|--------|
| `IConsumerClient` | Add `PauseAsync()` / `ResumeAsync()` methods |
| `ConsumerRegister` | Refactor `ExecuteAsync` to per-group `CancellationTokenSource`. Track `Dictionary<string, List<IConsumerClient>>`. Read circuit state to pause/resume groups. |
| `ConsumerRegister._isHealthy` | Kept for broker connectivity (transport-level). Circuit breaker is handler-level. `ReStartAsync()` does NOT reset circuit state — orthogonal concerns. |
| `SubscribeExecutor._SetFailedState` | Call `ICircuitBreakerStateManager.ReportFailure()` with exception and group name. |
| `SubscribeExecutor._ExecuteWithoutRetryAsync` | Stop swallowing `OperationCanceledException` unconditionally. Check if it's `TaskCanceledException` from timeout (inner token != app shutdown token) and let it propagate to circuit breaker. |
| `MessageNeedToRetryProcessor` | Add adaptive interval logic. Check circuit state before re-enqueueing — skip groups with open circuits. |
| `MessagingOptions` | Add `CircuitBreaker` and `RetryProcessor` config sections. |
| `IConsumerBuilder<T>` | Add `WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions>)`. |
| `SubscribeExecutor._ExecuteWithoutRetryAsync` | Stop swallowing `OperationCanceledException` unconditionally. Check if it's `TaskCanceledException` from timeout (not app shutdown) and let it propagate to circuit breaker via predicate. |
| `EventCounterSource` | Add circuit breaker metrics: trip count, open duration histogram, state gauge. |
| All 8 transport `IConsumerClient` implementations | Implement `PauseAsync()` / `ResumeAsync()`. |

#### State Machine

```
     [Closed]
        │
        │ N consecutive transient failures (default: 5)
        ▼
     [Open] ── per-group CTS cancelled, consumer tasks stopped
        │        open duration: 30s (escalates 30→60→120→240s on repeated reopens)
        │
        │ timer expires
        ▼
   [HalfOpen] ── drain in-flight, resume with concurrency=1
        │         admit exactly 1 probe message
        │
    ┌───┴───┐
    │       │
 success  failure
    │     (transient only)
    │       │
    ▼       ▼
 [Closed] [Open] ── re-pause, escalate open duration
    │
    │ non-transient probe failure also → [Closed]
    │   (dependency is fine, message is bad)
    │
    │ after 3 healthy cycles: reset escalation
```

#### Exception Classification

```csharp
// Default predicate — shipped as CircuitBreakerDefaults.IsTransient
public static class CircuitBreakerDefaults
{
    public static bool IsTransient(Exception exception) => exception switch
    {
        TimeoutException => true,
        HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } => true,
        HttpRequestException { InnerException: SocketException } => true,
        SocketException => true,
        BrokerConnectionException => true,
        TaskCanceledException tce when !tce.CancellationToken.IsCancellationRequested => true,
        _ => false,
    };
}

// Global override
options.CircuitBreaker.IsTransientException = CircuitBreakerDefaults.IsTransient; // default

// Per-group composition
services.AddConsumer<OrderHandler, OrderEvent>("orders.placed")
    .WithCircuitBreaker(cb =>
    {
        cb.IsTransientException = ex =>
            ex is MyCustomTransientException
            || CircuitBreakerDefaults.IsTransient(ex);
    });
```

**Why predicate over type allowlist:** A `Type[]` can't distinguish `HttpRequestException` 503 (transient) from 404 (permanent), can't match inner exceptions in `AggregateException`, and can't handle `TaskCanceledException` timeout-vs-shutdown. The predicate subsumes type matching with full pattern matching.

`TaskCanceledException` handling: only counts as transient when `CancellationToken.IsCancellationRequested` is `false` (timeout, not app shutdown). `HttpClient` throws `TaskCanceledException` on request timeout — the most common transient failure in microservices.

#### Transport Pause/Resume Contract

```csharp
public interface IConsumerClient
{
    // ... existing methods ...

    /// <summary>
    /// Pauses message consumption. Idempotent — calling on already-paused client is no-op.
    /// In-flight messages (prefetched, being processed) are allowed to complete.
    /// No new messages are pulled from the broker after this call returns.
    /// </summary>
    ValueTask PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes message consumption. Idempotent — calling on already-running client is no-op.
    /// </summary>
    ValueTask ResumeAsync(CancellationToken cancellationToken = default);
}
```

Transport-specific implementations:

| Transport | Pause Mechanism | Resume Mechanism |
|-----------|----------------|------------------|
| **RabbitMQ** | `BasicCancel(consumerTag)` | Re-register consumer with `BasicConsume` |
| **Kafka** | `consumer.Pause(partitions)` | `consumer.Resume(partitions)` |
| **AWS SQS** | Stop polling loop (set flag) | Resume polling loop |
| **Azure Service Bus** | `StopProcessingAsync()` | `StartProcessingAsync()` |
| **Redis Streams** | Stop `XREADGROUP` loop | Resume loop |
| **NATS** | `Unsubscribe()` | Re-subscribe |
| **Pulsar** | `PauseAsync()` (native) | `ResumeAsync()` (native) |
| **InMemory** | Stop dequeue loop | Resume dequeue loop |

#### ConsumerRegister Refactor

Current: single `CancellationTokenSource _cts` for all groups. Fire-and-forget `Task.Factory.StartNew` with no handle.

New: per-group `CancellationTokenSource` stored in `ConcurrentDictionary<string, GroupHandle>`:

```csharp
internal sealed class GroupHandle
{
    public required CancellationTokenSource Cts { get; init; }
    public required List<IConsumerClient> Clients { get; init; }
    public required int OriginalConcurrency { get; init; }
}
```

`ExecuteAsync` creates one `GroupHandle` per group. Pause = cancel group's CTS + call `PauseAsync` on each client. Resume = create new CTS + call `ResumeAsync`.

#### Retry Processor Interaction

```
MessageNeedToRetryProcessor.ProcessAsync
    │
    ├─ Fetch failed messages from DB
    │
    ├─ For each message:
    │   ├─ Resolve group from message.Origin.GetGroup()
    │   ├─ Check ICircuitBreakerStateManager.IsOpen(group)
    │   │   YES → skip, do not re-enqueue
    │   │   NO  → enqueue via Dispatcher
    │   └─ Track execution outcome (for adaptive polling)
    │
    ├─ Calculate transient failure rate (executed messages only, skipped excluded)
    │   ├─ >80% transient → double interval (cap 15min)
    │   ├─ 2 consecutive healthy cycles → halve interval
    │   └─ 3 clean cycles → reset to base
    │
    └─ Wait adaptive interval
```

**Exception classification for retry processor:** The retry processor only has `Headers.Exception` string (`"TypeName-->Message"`). The `IsTransientException` predicate requires an `Exception` object, which is unavailable for persisted messages. For the retry processor's adaptive polling, use string-prefix matching against a static list of known transient type names derived from the default predicate. Accept string matching fragility as V1 limitation — the circuit breaker itself (which has the actual `Exception` object and runs the full predicate) is the primary protection; the retry processor's classification is a secondary optimization.

## System-Wide Impact

### Interaction Graph

- `SubscribeExecutor._SetFailedState` → `ICircuitBreakerStateManager.ReportFailure()` → state transition → `ConsumerRegister` pauses group
- `TransportCheckProcessor` → `ConsumerRegister.IsHealthy()` → `ReStartAsync()` — orthogonal to circuit breaker. `ReStartAsync` does NOT reset circuit state.
- `MessageNeedToRetryProcessor` → checks `ICircuitBreakerStateManager.IsOpen(group)` before re-enqueue
- `EventCounterSource` → new counters: `circuit-breaker-trips`, `circuit-breaker-open-duration`
- `Messaging.OpenTelemetry` → new OTel metrics for circuit state

### Error Propagation

- Transient exceptions (allowlist) → increment circuit counter → may trigger pause
- Non-transient exceptions → normal retry/failure path, circuit counter unchanged
- `TaskCanceledException` (timeout) → classified as transient (HttpClient timeout)
- `TaskCanceledException` (shutdown) → swallowed as today, not classified
- `BrokerConnectionException` in `ConsumerRegister` → sets `_isHealthy = false` (existing global health). Does NOT interact with per-group circuit state — different layer.

### State Lifecycle Risks

- **Circuit state is in-memory only.** App restart resets to Closed. Brief retry storm on restart until circuits re-trip (~5 messages per group).
- **Per-group CTS lifecycle.** Old CTS must be disposed when creating new one on resume. Leak risk if not handled.
- **Concurrent failure counting.** Multiple threads process same group. Use `Interlocked` operations on the counter. State transition (check threshold → open → pause) must be atomic — `lock` or compare-and-swap on `CircuitState`.

### API Surface Parity

- `IConsumerClient` gains `PauseAsync`/`ResumeAsync` — all 8 transport implementations must add these.
- `IConsumerBuilder<T>` gains `WithCircuitBreaker()` — public API.
- `MessagingOptions` gains `CircuitBreaker` and `RetryProcessor` config objects — public API.

### Integration Test Scenarios

1. **Circuit trip and recovery**: Send 5 messages that cause `TimeoutException` → verify circuit opens → verify transport consumer paused → wait open duration → verify half-open probe → send success → verify circuit closes and consumer resumes.
2. **Poison message does NOT trip circuit**: Send message causing `ArgumentException` → verify circuit stays closed.
3. **Retry processor skips open groups**: Trip circuit → verify retry processor does not re-enqueue messages for that group → close circuit → verify retries resume.
4. **Adaptive polling escalation**: Trigger >80% transient failure rate in retry batch → verify interval doubles → trigger healthy cycles → verify interval recovery.
5. **Multi-group isolation**: Trip circuit on group A → verify group B continues consuming normally.

## Stories

> Full story details in companion PRD: [`2026-03-19-001-feat-messaging-circuit-breaker-and-retry-backpressure-plan.prd.json`](./2026-03-19-001-feat-messaging-circuit-breaker-and-retry-backpressure-plan.prd.json)

| ID | Title | Size |
|----|-------|------|
| US-001 | Add `ICircuitBreakerStateManager` and circuit state types | M |
| US-002 | Add `CircuitBreakerOptions` and `ConsumerCircuitBreakerOptions` config | S |
| US-003 | Add `PauseAsync`/`ResumeAsync` to `IConsumerClient` | S |
| US-004 | Refactor `ConsumerRegister` for per-group CTS and client tracking | L |
| US-005 | Integrate circuit breaker into `ConsumerRegister` (pause/resume on state change) | M |
| US-006 | Wire `SubscribeExecutor` failure reporting to `ICircuitBreakerStateManager` | M |
| US-007 | Handle `TaskCanceledException` timeout in default predicate | S |
| US-008 | Implement `PauseAsync`/`ResumeAsync` for RabbitMQ transport | M |
| US-009 | Implement `PauseAsync`/`ResumeAsync` for Kafka transport | M |
| US-010 | Implement `PauseAsync`/`ResumeAsync` for remaining transports (SQS, ASB, Redis, NATS, Pulsar, InMemory) | L |
| US-011 | Add adaptive polling to `MessageNeedToRetryProcessor` | M |
| US-012 | Add retry processor circuit-state awareness (skip open groups) | S |
| US-013 | Add OpenTelemetry metrics and logging for circuit breaker | M |
| US-014 | Add configuration validation for circuit breaker options | S |
| US-015 | Update docs and package READMEs | S |

## Final Acceptance Criteria

### Functional Requirements

- [ ] Circuit breaker trips after N consecutive transient failures (configurable, default 5)
- [ ] Only exceptions where `IsTransientException` predicate returns `true` trip the breaker. Default `CircuitBreakerDefaults.IsTransient` handles `TimeoutException`, `HttpRequestException` 5xx, `SocketException`, `BrokerConnectionException`, `TaskCanceledException` (timeout)
- [ ] Transport consumer pauses when circuit opens — zero new messages pulled
- [ ] Half-open probe admits exactly 1 message after drain. Transient failure re-opens; non-transient or success closes.
- [ ] Open duration escalates on repeated reopens (30s → 60s → 120s → 240s), resets after 3 healthy cycles
- [ ] Circuit breaker configurable globally and per-consumer-group. Disableable per-group.
- [ ] Retry processor skips re-enqueuing for groups with open circuits
- [ ] Adaptive polling doubles interval on >80% transient failure rate, halves after 2 healthy cycles, resets after 3 clean cycles
- [ ] All 8 transport providers implement `PauseAsync`/`ResumeAsync`
- [ ] `TaskCanceledException` from `HttpClient` timeout classified as transient; shutdown cancellation ignored

### Non-Functional Requirements

- [ ] Thread-safe circuit state management (`Interlocked`/CAS for counters, atomic state transitions)
- [ ] Zero allocations on the happy path (circuit closed, no transient exceptions)
- [ ] Per-group pause does not affect other groups' consumers
- [ ] `TransportCheckProcessor.ReStartAsync()` does not reset circuit state

### Quality Gates

- [ ] Unit tests for `ICircuitBreakerStateManager` state machine (all transitions, concurrency, escalation)
- [ ] Unit tests for exception classification (allowlist matching, `TaskCanceledException` timeout vs shutdown)
- [ ] Unit tests for adaptive retry interval logic
- [ ] Integration tests with InMemory transport for circuit trip/recovery flow
- [ ] Integration tests verifying retry processor respects open circuits
- [ ] Line coverage ≥85%, branch coverage ≥80%

## Cluster Scope

**Circuit breaker state is per-process and per-consumer-group. Not coordinated across app instances.**

In a 3-node deployment, each node trips independently. DB pressure is reduced by ~1/3 per node until all converge (typically within seconds for a truly down dependency). This is acceptable for V1. Cross-instance coordination (shared DB table) could be added later without breaking the API surface.

## Alternative Approaches Considered

See brainstorm for full rejected alternatives list. Key rejections:
- **`IConsumeFilter` as circuit breaker owner** — fires after message pulled, single-slot conflicts with user filters
- **`IRetryBackoffStrategy.ShouldRetry()` as classification** — `NullReferenceException` and business exceptions return `true` but shouldn't trip breaker
- **Exception type allowlist (`Type[]`)** — can't distinguish `HttpRequestException` 503 vs 404, can't match inner exceptions, can't handle `TaskCanceledException` timeout-vs-shutdown. Predicate subsumes type matching.
- **Skip-and-mark-for-retry on circuit open** — defeats purpose, still writes Failed rows to DB
- **Per-group adaptive polling** — too complex for V1; circuit breaker handles per-group protection at transport level

## Risk Analysis & Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| Transport `PauseAsync` semantics differ significantly | High — 8 implementations, some may not support clean pause | Design contract as idempotent and best-effort. InMemory + RabbitMQ + Kafka first; others can have no-op stubs initially. |
| Concurrent failure counting race condition | Medium — can cause premature or delayed trips | `Interlocked` for counter, `lock` or CAS for state transitions |
| `ConsumerRegister` refactor breaks existing behavior | High — core component | Extensive unit tests for existing behavior before refactoring. Keep `_isHealthy` global mechanism intact. |
| Adaptive polling penalizes healthy groups | Low — circuit breaker is the primary per-group protection | Document as V1 limitation. Processor-wide adaptive polling is a secondary safety net. |

## Sources & References

### Origin

- **Brainstorm document:** [docs/brainstorms/2026-03-18-messaging-circuit-breaker-and-retry-backpressure-brainstorm.md](../brainstorms/2026-03-18-messaging-circuit-breaker-and-retry-backpressure-brainstorm.md) — Key decisions carried forward: exception predicate (not type allowlist, not `ShouldRetry`), transport pause (not skip-and-mark), `ICircuitBreakerStateManager` as signaling path, group name as circuit key.

### Internal References

- `ConsumerRegister`: `src/Headless.Messaging.Core/Internal/IConsumerRegister.cs`
- `SubscribeExecutor`: `src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs`
- `ConsumeExecutionPipeline`: `src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs`
- `MessageNeedToRetryProcessor`: `src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs`
- `MessagingOptions`: `src/Headless.Messaging.Core/Configuration/MessagingOptions.cs`
- `IConsumerClient`: `src/Headless.Messaging.Core/Transport/IConsumerClient.cs`
- `EventCounterSource`: `src/Headless.Messaging.Core/Diagnostics/EventCounterSource.Message.cs`
- `ExponentialBackoffStrategy`: `src/Headless.Messaging.Core/Retry/ExponentialBackoffStrategy.cs`
