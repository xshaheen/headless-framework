---
title: Circuit Breaker & Retry Processor Backpressure
type: brainstorm
date: 2026-03-18
research:
  repo_patterns:
    - src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs
    - src/Headless.Messaging.Core/Internal/IConsumerRegister.cs
    - src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs
    - src/Headless.Messaging.Core/Configuration/MessagingOptions.cs
    - src/Headless.Messaging.Core/IConsumeFilter.cs
    - src/Headless.Messaging.Core/Processor/IProcessor.TransportCheck.cs
    - src/Headless.Messaging.Core/Retry/ExponentialBackoffStrategy.cs
    - src/Headless.Messaging.Abstractions/IRetryBackoffStrategy.cs
  learnings_applied: []
  timestamp: 2026-03-18T00:00:00Z
---

# Circuit Breaker & Retry Processor Backpressure

## What We're Building

Two complementary improvements to Headless.Messaging that reduce database pressure and wasted work during sustained downstream failures:

1. **Per-consumer-group circuit breaker** — Detects when a downstream dependency is unresponsive and pauses the transport consumer for that group. Messages backlog in the broker (designed for this) instead of flooding the DB with `Failed` rows.

2. **Adaptive retry processor polling** — When the retry processor detects high transient failure rates, it doubles its polling interval (60s -> 120s -> 240s, capped at 15min). Resets to base interval after sustained healthy cycles.

**Scope boundary:** The circuit breaker protects against **dependency/infrastructure degradation** only. Poison messages, bad payloads, and business rule failures are handled by existing retry/dead-letter/failure policy — not the circuit breaker.

## Failure Classification

Only **transient dependency failures** contribute to circuit breaker state. This is the most critical design decision — without it, a single bad payload can pause a healthy consumer group.

### Failures that trip the breaker

- Network timeouts and connection failures
- HTTP 5xx / gateway errors from downstream services
- DB connection pool exhaustion (when the DB is the downstream being protected)
- Broker publish failures for callback responses
- Transient infrastructure exceptions (e.g., `TimeoutException`, `HttpRequestException`, `SocketException`)

### Failures that do NOT trip the breaker

- Deserialization / serialization failures (bad payload)
- Validation failures (`ArgumentException`, `ArgumentNullException`)
- Business rule rejections
- Idempotency duplicate detection
- `InvalidOperationException`, `NotSupportedException` (consumer bugs or unsupported scenarios)
- Permanent 4xx-style downstream errors

### Classification mechanism: Predicate function

The circuit breaker uses a **predicate** (`Func<Exception, bool>`) to classify exceptions. Only exceptions where the predicate returns `true` count toward tripping the breaker. Everything else is ignored by the breaker and handled by existing retry/failure policy.

**Why predicate over type allowlist:** A type list can't distinguish `HttpRequestException` with 503 (transient) from 404 (permanent), can't match inner exceptions inside `AggregateException`, and can't handle `TaskCanceledException` timeout-vs-shutdown logic. The predicate subsumes the type list and handles all these cases with pattern matching.

**Why predicate over `IRetryBackoffStrategy.ShouldRetry()`:** `ShouldRetry = true` means "not a known permanent failure" — it does not mean "downstream is unhealthy." `NullReferenceException` (consumer bug) and custom `BusinessRuleException` both return `ShouldRetry = true` but should never trip the breaker.

**Default predicate** (shipped as `CircuitBreakerDefaults.IsTransient`):

```csharp
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
```

**Composable per-group:**
```csharp
services.AddConsumer<OrderHandler, OrderEvent>("orders.placed")
    .WithCircuitBreaker(cb =>
    {
        cb.IsTransientException = ex =>
            ex is MyCustomTransientException
            || CircuitBreakerDefaults.IsTransient(ex);
    });
```

**Replaceable globally:**
```csharp
options.CircuitBreaker.IsTransientException = ex => ex switch
{
    TimeoutException => true,
    HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } => true,
    SocketException => true,
    BrokerConnectionException => true,
    MyInfraException => true,
    _ => false,
};
```

## Why This Approach

### Circuit Breaker in `ConsumerRegister` (not `IConsumeFilter` or Decorator)

- **Transport-level gating belongs before message execution.** The circuit breaker's primary action is pausing/resuming transport consumers. This must happen before messages are pulled and deserialized, not inside the execution pipeline. This argument holds regardless of whether `IConsumeFilter` becomes a pipeline later.
- **Decision and action colocated.** `ConsumerRegister` already owns transport consumer lifecycle (`ReStartAsync()`, `IsHealthy()`, per-group thread management). Adding circuit state there avoids split-brain between "who decides to trip" and "who pauses the consumer."
- **Filter role is reporting only.** `IConsumeFilter.OnSubscribeExceptionAsync` increments the failure counter. It doesn't make decisions. This avoids conflicting with user-provided filters.

### Pause Transport (not skip-and-mark or reject)

- **Zero DB writes during outage.** The whole problem is DB pressure from failed messages. Pausing the consumer means no messages enter the DB as `Failed` during the open period.
- **Broker-native backpressure.** RabbitMQ queues grow, Kafka consumer lag increases, SQS visibility timeouts expire. Ops teams already monitor these signals.
- **Clean recovery.** When half-open probe succeeds, consumer resumes and processes the backlog normally. No retry processor involvement needed.
- **Transport-specific pause semantics.** "Pause" means different things per transport — RabbitMQ can cancel the consumer tag, Kafka can call `Pause()` on partition assignment, SQS can stop polling. The implementation must account for transport-specific behavior (prefetch buffers, in-flight messages, ack timing). Detailed per-transport behavior belongs in the implementation plan.

### Adaptive Polling (not batch sizing)

- Simpler than reducing batch size. One knob (interval) vs two (interval + size).
- Doubling the interval on high transient failure rate is a well-understood pattern (exponential backoff at the processor level).
- Recovery requires sustained healthy cycles to avoid flapping.

## Key Decisions

### Circuit Breaker

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Granularity | Per consumer group (topic + handler) | Maps to `ConsumerMetadata`. One failing handler doesn't stop unrelated handlers. |
| Location | `ConsumerRegister` | Owns transport lifecycle. Filter reports failures only. |
| Open behavior | Pause transport consumer | Zero DB writes. Broker handles backlog. |
| Default thresholds | 5 consecutive transient failures -> open, 30s initial open duration, 1 probe on half-open | Conservative. Trips fast, recovers fast. |
| Failure counting | Consecutive transient failures only | Only exceptions where `IsTransientException` predicate returns `true` count. Resets on any success. Unknown exceptions, poison messages, and business failures do not trip the breaker. |
| Escalating open duration | 30s -> 60s -> 120s -> 240s on repeated reopens | Fixed 30s causes flapping during long outages. Resets after sustained healthy period (3 consecutive successful cycles after closing). |
| Configuration | Framework defaults + per-consumer-group opt-in override | Sensible defaults via `MessagingOptions`. Per-group override via `AddConsumer<T>().WithCircuitBreaker(cb => ...)`. |
| Disableable | Per-group opt-out via `cb.Enabled = false` | For handlers that must never pause (audit logging, compliance). |
| Observability | Logging (Warning level) + OpenTelemetry metrics | State transition logs + OTel gauge for circuit state, counter for trips, histogram for open duration. Fits existing `Messaging.OpenTelemetry` package. |
| Cluster scope | **Per-process only** (see Cluster Scope section) | V1 limitation. |

### Circuit Breaker State Machine

```
     [Closed]
        |
        | N consecutive transient failures (default: 5)
        v
     [Open] ── pause transport consumer for group
        |        open duration: 30s (escalates on repeated reopens)
        |
        | open duration expires
        v
   [HalfOpen] ── drain in-flight, then admit exactly 1 probe message
        |
    +---+---+
    |       |
 success  failure (transient)
    |       |
    v       v
 [Closed] [Open] ── re-pause, escalate open duration (30s -> 60s -> 120s -> 240s)
```

**Half-open probe semantics:**
- No new work admitted until open duration expires
- Transition to half-open: drain any in-flight messages for the group
- Admit exactly one new message through the handler pipeline
- All other deliveries remain paused/unacked at transport level
- **Success** = handler completed without transient exception through the full consume pipeline
- If probe message triggers an internal retry, that retry's outcome determines circuit state
- On success: restore original concurrency, reset escalation after 3 healthy cycles
- On failure: re-enter Open with escalated duration

### Retry Processor Interaction

**Critical rule: the retry processor respects open circuits.**

- If a consumer group's circuit is open, the retry processor **skips re-enqueuing work for that group**
- This prevents one subsystem (retry processor) from manufacturing work toward a bottleneck that another subsystem (circuit breaker) has identified
- When the circuit closes, the retry processor resumes normal behavior for that group
- The message that triggered the circuit open follows normal retry/failure persistence — it was already pulled and processed

### Adaptive Retry Processor

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Strategy | Double polling interval on high transient failure rate | Simple exponential backoff. One config knob. |
| Trigger | >80% of batch resulted in predicate-matched failures | Same `IsTransientException` predicate as circuit breaker. Permanent failures, business rejections, and unknown exceptions don't trigger backoff. |
| Interval range | Base: `FailedRetryInterval` (60s) -> Max: 15min | 60 -> 120 -> 240 -> 480 -> 900 (capped). |
| Recovery | Halve interval after 2 consecutive healthy cycles (>50% succeed) | Requires sustained improvement to avoid flapping. Single good cycle is not enough. |
| Reset | Return to base interval after 3 consecutive cycles with 0 transient failures | Gradual recovery, not instant reset. |
| Scope | **Processor-wide** (not per-group) | Per-group adaptive polling adds significant complexity. V1 is global. One noisy failing group may slow retries for healthy groups — acknowledged as a V1 limitation. |

### Configuration Surface

```csharp
// Global defaults
builder.Services.AddHeadlessMessaging(options =>
{
    // Circuit breaker defaults (apply to all consumer groups)
    options.CircuitBreaker.FailureThreshold = 5;        // consecutive transient failures to trip
    options.CircuitBreaker.OpenDuration = TimeSpan.FromSeconds(30);  // initial; escalates on repeated reopens
    options.CircuitBreaker.MaxOpenDuration = TimeSpan.FromMinutes(4); // escalation cap
    options.CircuitBreaker.HalfOpenProbeCount = 1;
    // Default: CircuitBreakerDefaults.IsTransient (handles TimeoutException,
    // HttpRequestException 5xx, SocketException, BrokerConnectionException,
    // TaskCanceledException from timeout)
    options.CircuitBreaker.IsTransientException = CircuitBreakerDefaults.IsTransient;

    // Adaptive retry processor
    options.RetryProcessor.AdaptivePolling = true;       // default: true
    options.RetryProcessor.MaxPollingInterval = 900;     // seconds, default: 900 (15min)
    options.RetryProcessor.TransientFailureRateThreshold = 0.8; // default: 80%
});

// Per-consumer-group override
services.AddConsumer<OrderHandler, OrderEvent>("orders.placed")
    .WithCircuitBreaker(cb =>
    {
        cb.FailureThreshold = 10;  // more tolerant for this handler
        cb.OpenDuration = TimeSpan.FromMinutes(2);
        cb.IsTransientException = ex =>
            ex is MyCustomTransientException
            || CircuitBreakerDefaults.IsTransient(ex);
    });

// Disable for specific group
services.AddConsumer<AuditHandler, AuditEvent>("audit.events")
    .WithCircuitBreaker(cb => cb.Enabled = false);
```

## Cluster Scope

**Circuit breaker state is per-process and per-consumer-group. It is not coordinated across application instances in V1.**

This means in a 3-node deployment:
- Node A may open the circuit after detecting 5 failures
- Nodes B and C continue consuming and failing independently until they each independently trip their own breakers
- DB pressure is reduced partially (by ~1/3) until all nodes trip

This is acceptable for V1 because:
- Each node will independently trip within seconds if the downstream is truly down (5 failures at typical message rates)
- Cross-instance coordination would require shared state (DB or distributed cache), adding schema changes, latency, and failure modes
- The benefit of faster convergence doesn't justify the complexity for the initial implementation

**V2 consideration:** If operational experience shows that the convergence lag is problematic, shared circuit state via the outbox DB (a simple `circuit_state` table) could be added without breaking the V1 API surface.

## Resolved Questions

1. **Should the circuit breaker be disableable per-group?** Yes — `cb.Enabled = false` for handlers that must never pause (audit logging, compliance). Per-group opt-out via `WithCircuitBreaker(cb => cb.Enabled = false)`.
2. **How does the half-open probe interact with consumer concurrency?** Drain in-flight messages, then admit exactly one probe. Concurrency=1 is necessary but not sufficient alone — prefetch buffers and in-flight messages must be drained first. On success, restore original concurrency.
3. **Should circuit state survive app restart?** No, in-memory only. App restart resets to Closed. If downstream is still down, circuit re-trips within seconds (5 failures). Persistence adds schema/migration complexity for minimal benefit. Trade-off: brief retry storm on restart in multi-node deployments.

## Rejected Alternatives

- **`IConsumeFilter` as circuit breaker owner**: Transport-level gating must happen before execution pipeline, not inside it. Also a single-slot registration that conflicts with user filters.
- **Decorator around `ISubscribeExecutor`**: Clean separation but adds indirection. The action (pause transport) still needs to call into `ConsumerRegister`, so the decorator becomes a pass-through.
- **Failure-aware batch sizing for retry processor**: Two knobs instead of one. Adaptive interval achieves the same DB pressure reduction more simply.
- **Global circuit breaker**: Too coarse. One failing downstream stops all message processing.
- **Per-topic circuit breaker**: Multiple handlers on the same topic would share state. A poison handler trips the circuit for healthy handlers on the same topic.
- **Skip-and-mark-for-retry on circuit open**: Defeats the purpose — still writes Failed rows to DB.
- **Reject with CircuitOpenException**: Same problem — generates more Failed rows.
- **Counting all failures toward breaker**: Dangerous — a single bad payload would pause a healthy consumer group. Only transient dependency failures should trip the breaker.
- **Using `IRetryBackoffStrategy.ShouldRetry()` as classification**: `ShouldRetry = true` means "not a known permanent failure," not "downstream is unhealthy." `NullReferenceException` and business exceptions return `true` but shouldn't trip the breaker. Predicate is safe by default — unknown exceptions are ignored.
- **Exception type allowlist (`Type[]`) instead of predicate**: Can't distinguish `HttpRequestException` 503 (transient) from 404 (permanent), can't match inner exceptions in `AggregateException`, and can't handle `TaskCanceledException` timeout-vs-shutdown. Predicate subsumes the type list with pattern matching.
- **Fixed open duration without escalation**: Causes flapping during long outages. 30s open -> probe fails -> 30s open -> probe fails, indefinitely.
- **Single-cycle recovery for adaptive polling**: Flaps under mixed outcomes. Requiring 2-3 healthy cycles provides stability.
- **Per-group adaptive polling**: Adds significant complexity (per-group interval tracking, timer management). Global is sufficient for V1 since the circuit breaker already handles per-group protection at the transport level.
