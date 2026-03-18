---
title: Saga Pattern Support
type: brainstorm
date: 2026-03-18
research:
  repo_patterns:
    - src/Headless.Messaging.Abstractions/IMessagePublisher.cs
    - src/Headless.Messaging.Abstractions/IConsume.cs
    - src/Headless.Messaging.Abstractions/IOutboxTransaction.cs
    - src/Headless.Messaging.Abstractions/ConsumeContext.cs
    - src/Headless.Messaging.Abstractions/Headers.cs
    - src/Headless.Messaging.Abstractions/IRuntimeSubscriber.cs
    - src/Headless.Messaging.Core/Persistence/IDataStorage.cs
    - src/Headless.Messaging.Core/Persistence/IStorageInitializer.cs
    - src/Headless.Messaging.Core/Internal/OutboxPublisher.cs
    - src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs
    - src/Headless.Messaging.Core/Transport/IDispatcher.cs
    - src/Headless.Messaging.Core/Messages/MediumMessage.cs
    - src/Headless.Messaging.Core/Configuration/MessagingOptions.cs
  external_research:
    - MassTransit (Automatonymous state machine DSL)
    - Rebus (handler + correlation sagas)
    - NServiceBus (enterprise sagas with first-class timeouts)
    - Wolverine (convention-based minimal-ceremony sagas)
    - DotNetCore.CAP (outbox only, no saga support)
    - Dapr Workflows (code-as-workflow with replay)
    - Eventuate Tram Sagas (builder DSL, local/participant steps, command/reply)
  timestamp: 2026-03-18T00:00:00Z
---

# Saga Pattern Support

## What We're Building

Orchestration-based saga support for `headless-framework`. A saga is a sequence of steps that either all complete or compensate in reverse. Steps can invoke services directly (DI) or send commands via messaging and wait for replies.

**Design principles:**

- One class, one state record, no ceremony
- Builder DSL: step sequence + compensation visible in one place
- Explicit compensation (never auto-generated)
- Three timeout levels (step, wait, saga-wide)
- Reuses existing messaging infrastructure for command/reply
- Persistence alongside existing messaging storage
- Single runtime shape: `Command()` is syntactic sugar, not a separate engine

**Non-goals (v1):**

- Choreography-based sagas (event-driven, no central orchestrator)
- Parallel step execution (fan-out/fan-in)
- Visual saga designer / diagram generation
- Saga versioning / migration

## Why Builder DSL

Evaluated six API styles across .NET and Go ecosystems:

| Style | Library | Why not |
|-------|---------|---------|
| Fluent state machine | MassTransit | Steep learning curve, overkill for linear orchestration |
| Handler + interfaces | Rebus, NServiceBus | Multiple classes per saga, correlation ceremony |
| Convention methods | Wolverine | Naming magic, hard to enforce at compile time |
| **Builder DSL** | **Eventuate Tram** | **Selected: explicit, one place, composable** |
| Code-as-workflow | Temporal, Dapr | Requires replay engine, different execution model |
| Annotations | Axon | Reflection-heavy, poor discoverability |

Builder DSL wins because:
1. Step sequence and compensation pairs are visible in one definition
2. No naming conventions or interface ceremony
3. Strongly typed with lambda intellisense
4. Composable with `configure:` delegates for per-step options

## API Contract

### Core Definition

```csharp
public interface ISagaDefinition<TState> where TState : class
{
    void Build(ISagaBuilder<TState> builder);
}
```

### Builder

```csharp
public interface ISagaBuilder<TState> where TState : class
{
    /// Local/direct step: invoke a service via DI or run in-process logic.
    ISagaBuilder<TState> Step(
        string name,
        Func<ISagaContext<TState>, CancellationToken, ValueTask> execute,
        Func<ISagaContext<TState>, CancellationToken, ValueTask>? compensate = null,
        Action<IStepOptionsBuilder<TState>>? configure = null);

    /// Command/reply step: send a command via messaging, wait for reply.
    ISagaBuilder<TState> Command<TCommand>(
        string name,
        string destination,
        Func<ISagaContext<TState>, TCommand> buildCommand,
        Action<ICommandStepBuilder<TState>>? configure = null)
        where TCommand : class;

    /// Wait for an external event correlated by key.
    ISagaBuilder<TState> WaitFor<TEvent>(
        string name,
        Func<TState, string> sagaKey,
        Func<TEvent, string> eventKey,
        Func<ISagaContext<TState>, TEvent, CancellationToken, ValueTask> apply,
        Action<IWaitStepOptionsBuilder<TState>>? configure = null)
        where TEvent : class;

    /// Global saga timeout.
    ISagaBuilder<TState> Timeout(
        TimeSpan timeout,
        Func<ISagaContext<TState>, CancellationToken, ValueTask> onTimeout);

    /// Lifecycle: all steps completed successfully.
    ISagaBuilder<TState> Completed(
        Func<ISagaContext<TState>, CancellationToken, ValueTask>? onCompleted = null);

    /// Lifecycle: saga failed (step failure after compensation).
    ISagaBuilder<TState> Failed(
        Func<ISagaContext<TState>, Exception, CancellationToken, ValueTask>? onFailed = null);
}
```

### Saga Context

```csharp
public interface ISagaContext<TState> where TState : class
{
    string SagaId { get; }
    TState State { get; }
    IServiceProvider Services { get; }
    string CurrentStepName { get; }
    int CurrentStepIndex { get; }

    /// Publish a domain event via the existing messaging infrastructure.
    ValueTask PublishAsync<TMessage>(TMessage message, CancellationToken ct = default)
        where TMessage : class;

    /// Store keyed step-scoped data for compensation context.
    /// Persisted in CompletedStepLog.CompensationDataJson as a keyed dictionary.
    /// Scoping: data set during step N is readable during compensation of step N only.
    /// Written to persistence after successful step completion (not during).
    void SetStepData<T>(string key, T data);
    T? GetStepData<T>(string key);

    /// Force-fail the saga (triggers compensation).
    ValueTask FailAsync(string reason, CancellationToken ct = default);
}
```

### Step Options

```csharp
public interface IStepOptionsBuilder<TState> where TState : class
{
    IStepOptionsBuilder<TState> Retry(int maxAttempts, Func<int, TimeSpan>? delay = null);
    IStepOptionsBuilder<TState> Timeout(TimeSpan timeout);
    IStepOptionsBuilder<TState> IdempotencyKey(Func<ISagaContext<TState>, string> factory);
    IStepOptionsBuilder<TState> When(Func<TState, bool> predicate);
    IStepOptionsBuilder<TState> CompensationRetry(int maxAttempts, Func<int, TimeSpan>? delay = null);
}
```

### Command/Reply Step Options

```csharp
public interface ICommandStepBuilder<TState> where TState : class
{
    /// Handle a success reply type. Multiple OnReply calls for different types.
    ICommandStepBuilder<TState> OnReply<TReply>(
        Func<ISagaContext<TState>, TReply, CancellationToken, ValueTask> handler)
        where TReply : class;

    /// Handle a failure reply type. Handler may mutate saga state (e.g., store
    /// failure code, decline reason, audit data). After handler completes,
    /// saga transitions to compensation.
    ICommandStepBuilder<TState> OnFailure<TFailure>(
        Func<ISagaContext<TState>, TFailure, CancellationToken, ValueTask> handler)
        where TFailure : class;

    /// Compensation: send a compensating command via messaging.
    ICommandStepBuilder<TState> CompensateWith<TCompensation>(
        string destination,
        Func<ISagaContext<TState>, TCompensation> buildCommand)
        where TCompensation : class;

    /// Compensation: run local logic.
    ICommandStepBuilder<TState> Compensate(
        Func<ISagaContext<TState>, CancellationToken, ValueTask> action);

    ICommandStepBuilder<TState> Retry(int maxAttempts, Func<int, TimeSpan>? delay = null);
    ICommandStepBuilder<TState> Timeout(TimeSpan timeout);
    ICommandStepBuilder<TState> When(Func<TState, bool> predicate);
    ICommandStepBuilder<TState> CompensationRetry(int maxAttempts, Func<int, TimeSpan>? delay = null);
}
```

### Wait Step Options

```csharp
public interface IWaitStepOptionsBuilder<TState> where TState : class
{
    IWaitStepOptionsBuilder<TState> Timeout(TimeSpan timeout);
    IWaitStepOptionsBuilder<TState> OnTimeout(
        Func<ISagaContext<TState>, CancellationToken, ValueTask> onTimeout);
}
```

### Orchestrator (Runtime)

```csharp
public interface ISagaOrchestrator
{
    Task<string> StartAsync<TSaga, TState>(
        TState state,
        CancellationToken ct = default)
        where TSaga : ISagaDefinition<TState>
        where TState : class;

    /// Business-key correlation: runtime finds matching sagas via
    /// (event type, eventKey) == (WaitingForEventType, WaitingForEventKey).
    Task RaiseEventAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : class;

    /// Direct targeted delivery by saga ID. Bypasses key matching.
    Task RaiseEventToSagaAsync<TEvent>(
        string sagaId,
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : class;

    Task CancelAsync(
        string sagaId,
        string? reason = null,
        CancellationToken ct = default);

    Task<SagaStatusInfo?> GetStatusAsync(
        string sagaId,
        CancellationToken ct = default);
}
```

### Management (Operations)

```csharp
public interface ISagaManagement
{
    /// Retry failed compensation for a stuck saga.
    /// Resumes reverse execution from the failed compensation step.
    Task RetryCompensationAsync(string sagaId, CancellationToken ct = default);

    /// Skip the current failed compensation step and continue rollback.
    /// Only valid for sagas in Stuck status during compensation.
    /// Does NOT skip forward steps — forward failures always trigger compensation.
    Task SkipFailedCompensationAsync(string sagaId, CancellationToken ct = default);

    /// Mark a saga as resolved by operator intervention.
    /// Terminal state is `Resolved` (distinct from `Completed`).
    /// No remaining steps are executed. No compensation is performed.
    /// Intended for manual remediation after external recovery.
    /// Stores audit metadata: reason, timestamp, operator identity.
    Task MarkResolvedAsync(string sagaId, string reason, CancellationToken ct = default);

    /// Find sagas stuck in a failed/compensating state.
    Task<IReadOnlyList<SagaInstance>> GetStuckSagasAsync(
        TimeSpan? olderThan = null,
        CancellationToken ct = default);
}
```

## Execution Guarantees

Step execution, reply handling, and compensation are all **at least once**. The runtime may internally retry due to optimistic concurrency conflicts. Users must:

- **Make step handlers idempotent** — the same step may execute more than once if the runtime retries after a transient failure or concurrency conflict.
- **Make compensation handlers idempotent** — compensation may be retried after failure (via `CompensationRetry` or manual `RetryCompensationAsync`).
- **Use outbox integration** for outgoing messages — `PublishAsync` in `ISagaContext` should go through the transactional outbox where possible to avoid dual-write issues.
- **Not assume exactly-once** — the framework guarantees at-least-once execution with idempotency support (`IdempotencyKey`), not exactly-once.

## Definition Model

### Immutability

`Build()` constructs an **immutable step graph** at startup. Runtime execution uses compiled step descriptors. Runtime lambdas execute against `ISagaContext<TState>`, which is a per-execution instance — never mutate shared state in the definition class.

Separation:
- **Definition time** (`Build()`) — produces metadata: step names, types, lambda references, options
- **Runtime** — creates `ISagaContext<TState>` per execution, invokes compiled lambdas, manages persistence

### Build-Time Validation

The runtime validates the step graph at startup and throws if any rule is violated:

- Step names must be **unique** within a saga definition
- At most **one global timeout** per saga
- `Command()` steps must have at least one `OnReply<T>()` or `OnFailure<T>()` handler
- No duplicate reply type handlers within the same `Command()` step
- `destination` on `Command()` / `CompensateWith()` must not be null or empty
- Compensation cannot be configured via both `Compensate()` and `CompensateWith()` on the same `Command()` step
- `WaitFor()` must have both `sagaKey` and `eventKey` (non-null)

## Reply and Event Safety

### Rules

The runtime accepts replies/events **only when the saga is in the expected waiting state**. All other arrivals are ignored or dead-lettered.

| Scenario | Behavior |
|----------|----------|
| **Duplicate reply** | `SagaId + SagaStepIndex` must match current expected step. If saga has already advanced, the reply is ignored. |
| **Reply after timeout** | Saga has transitioned to `Compensating`. Late reply is ignored. |
| **Reply for old step index** | `SagaStepIndex` doesn't match `CurrentStepIndex`. Ignored. |
| **Late success after compensation started** | Saga status is `Compensating`. Reply is ignored — compensation cannot be reversed. |
| **Duplicate external event** | `WaitFor` match succeeds only if saga is in `WaitingForEvent` for that type/key. If already advanced, event is ignored. |
| **Event for non-waiting saga** | Saga is `Running` or `Completed`. Event is ignored. |

Dead-lettering of ignored messages is configurable via `options.OnIgnoredMessage`.

## Step Types

All step types compile into the same internal runtime shape:

- **execute action** — what happens on forward execution
- **transition handler** — how success/failure is determined
- **compensation handler** — what happens on rollback

`Command()` is syntactic sugar over this contract, not a separate execution engine.

### 1. Local/Direct Step (`Step`)

Calls a service directly via DI or runs in-process logic. The saga orchestrator awaits the lambda and advances immediately on success.

```
Orchestrator --execute--> Lambda (calls IPaymentService via DI)
                          |
                          +-- success: advance to next step
                          +-- exception: begin compensation
```

### 2. Command/Reply Step (`Command`)

Sends a command message to a participant service via the existing messaging infrastructure. The saga enters a waiting state until the reply arrives.

```
Orchestrator --send command--> Message Broker --deliver--> Participant Service
     |                                                           |
     +-- waiting (persisted) <-----reply message-----------------+
     |
     +-- OnReply handler: update state, advance
     +-- OnFailure handler: update state, begin compensation
     +-- Timeout: begin compensation
```

### 3. Wait Step (`WaitFor`)

Waits for an external event not triggered by a command. The runtime matches incoming events by type + business key.

```
Orchestrator --enters wait state (persisted)
     |
External System --publishes event--> Message Broker
     |
     +-- Runtime matches: event type + eventKey(event) == sagaKey(state)
     +-- apply handler: update state, advance
     +-- Timeout: invoke onTimeout or fail
```

## Correlation Model

Two distinct correlation modes. Do not mix them.

### 1. Runtime Correlation (Command/Reply)

For `Command()` steps. Deterministic, header-based.

The saga runtime attaches dedicated headers to outgoing commands:

- `Headers.SagaId` — saga instance ID
- `Headers.SagaStepIndex` — current step index
The participant service echoes these headers in its reply. The runtime uses `SagaId` + `SagaStepIndex` to route the reply to the exact saga instance and step. The actual message CLR type identifies the reply type — no additional type header needed for routing. No business-key matching involved.

These headers are separate from the existing `Headers.CorrelationId` to avoid collision with application-level correlation.

### 2. Business Correlation (WaitFor)

For `WaitFor()` steps. Key-based, event-driven.

- `sagaKey(state)` produces the key stored in the saga instance (`WaitingForEventKey`)
- `eventKey(event)` extracts the key from the incoming event
- The runtime matches by `(event type name, key value)`

The saga instance stores `WaitingForEventType` + `WaitingForEventKey` while blocked. When an event arrives, the runtime queries: `WHERE waiting_event = @type AND waiting_key = @key`.

`RaiseEventToSagaAsync(sagaId, event)` on `ISagaOrchestrator` bypasses key matching and delivers directly by saga ID. `RaiseEventAsync(event)` uses business-key correlation to find matching sagas.

## Execution Model

### Saga Instance (Persistence)

```csharp
public sealed record SagaInstance
{
    public required string Id { get; init; }
    public required string SagaName { get; init; }
    public required string StateJson { get; set; }
    public required SagaRuntimeStatus Status { get; set; }
    public int CurrentStepIndex { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string? WaitingForEventType { get; set; }
    public string? WaitingForEventKey { get; set; }
    public string? FailureReason { get; set; }
    public string? ExceptionInfo { get; set; }
    public int Version { get; set; }
    // Operator override audit (populated by MarkResolvedAsync)
    public string? ResolvedReason { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public string? ResolvedBy { get; set; }
}

public enum SagaRuntimeStatus
{
    /// Forward execution in progress.
    Running,

    /// Blocked on an external event (WaitFor step).
    WaitingForEvent,

    /// Blocked on a command reply (Command step).
    WaitingForReply,

    /// Compensation in progress (reverse execution).
    Compensating,

    /// Cancel requested, compensation in progress.
    Cancelling,

    /// Terminal: all steps completed successfully.
    Completed,

    /// Terminal: forward execution failed, compensation completed successfully.
    /// Business result is unsuccessful but runtime is done.
    Failed,

    /// Terminal: compensation itself failed, manual intervention required.
    Stuck,

    /// Terminal: cancelled by operator, compensation completed successfully.
    Cancelled,

    /// Terminal: manually resolved by operator after external remediation.
    /// Distinct from Completed — dashboard must not treat as natural success.
    Resolved,
}

public sealed record CompletedStepLog
{
    public required long Id { get; init; }
    public required string SagaId { get; init; }
    public required string StepName { get; init; }
    public required int StepIndex { get; init; }
    public required StepLogStatus Status { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
    /// Keyed dictionary of step-scoped data, serialized as JSON.
    /// Set via SetStepData<T>(key, data) during step execution.
    /// Readable via GetStepData<T>(key) during compensation of this step only.
    public string? CompensationDataJson { get; init; }
    public int? DurationMs { get; init; }
    public string? ExceptionInfo { get; init; }
}

public enum StepLogStatus
{
    Completed,
    CompensationFailed,
    Compensated,
}

public sealed record SagaStatusInfo
{
    public required string Id { get; init; }
    public required string SagaName { get; init; }
    public required SagaRuntimeStatus Status { get; init; }
    public required int CurrentStepIndex { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public string? WaitingForEventType { get; init; }
    public string? FailureReason { get; init; }
    public IReadOnlyList<CompletedStepLog> StepLog { get; init; } = [];
}
```

### Execution Flow

```
Start(state)
  |
  v
[Step 0] --success--> [Step 1] --success--> [Step 2 (wait)] --event--> [Step 3] --> Completed
  |                      |                      |
  |                      |                      +--timeout--> Compensation
  |                      +--failure--> Compensation
  +--failure--> Failed (no prior steps to compensate)

Compensation:
  [Compensate Step 2] --> [Compensate Step 1] --> [Compensate Step 0] --> Failed
       |
       +--failure--> Stuck (compensation retry / dead-letter)
```

**Compensation order**: Reverse of completed steps. Only steps with a `compensate` handler are invoked. Steps without compensation are skipped.

**Cancellation flow**: `CancelAsync()` → status becomes `Cancelling` → compensation runs in reverse → final status is `Cancelled` (if compensation succeeds) or `Stuck` (if compensation fails).

## Timeout Design

Three levels, aligned with NServiceBus timeout model but using persisted timers (not in-memory):

| Level | Scope | Trigger | Default |
|-------|-------|---------|---------|
| **Step timeout** | Single step execution | Step exceeds duration | None (no timeout) |
| **Wait timeout** | WaitFor / Command reply | Event/reply not received | None |
| **Saga timeout** | Entire saga lifetime | Saga not completed | None |

All timeouts are persisted (stored as `ExpiresAtUtc` on the saga instance or step). The runtime uses a durable timeout processor. The default implementation polls over persisted expiration timestamps via a hosted service. Alternative implementations (transport-based delayed messages, external scheduler integration) can be swapped without changing the contract. No in-memory `Task.Delay`.

### Step Timeout Semantics

Step timeout is **cooperative**, not preemptive. .NET does not support safe hard preemption of async code.

1. The runtime creates a `CancellationTokenSource` linked to the step's timeout duration and passes the token via the `CancellationToken ct` parameter to the step lambda.
2. When the timeout fires, the runtime cancels the token.
3. If the step lambda honors cancellation promptly, it throws `OperationCanceledException` and the runtime treats the step as failed → compensation begins.
4. If the step lambda does **not** honor cancellation, the runtime still treats the step as timed out for saga state purposes (transitions to `Compensating`). The lambda may continue running in the background — the runtime does not await it indefinitely.

Users must ensure that timed-out local operations are safe, idempotent, and abortable. The framework cannot forcibly stop in-flight HTTP calls, database transactions, or other non-cooperative work.

**Wait/saga timeouts** are different: these are persisted timers checked by the timeout processor, not cooperative cancellation. They fire regardless of whether any code is running.

## Conditional Steps

Steps can be skipped based on current state via `When(predicate)`:

```csharp
.Step("charge-payment",
    execute: async (ctx, ct) => { ... },
    configure: step => step.When(state => state.RequiresPayment))
```

Exact semantics:

- `When()` is evaluated **once**, immediately before forward execution of that step
- If `false`: step is skipped, no `saga_step_log` entry is written, step index advances
- Skipped steps **never participate in compensation** — they have no completion record
- If state changes during a retry (e.g., optimistic concurrency retry re-evaluates the step), the predicate is re-evaluated against the current state
- During cancellation/compensation, `When()` is irrelevant — compensation walks the `saga_step_log`, which only contains steps that actually executed

## Compensation Design

### Principles

1. **Compensation is explicit** — never auto-generated
2. **Reverse-order execution** — compensate completed steps in LIFO order
3. **Step data available** — `GetStepData<T>(key)` provides keyed snapshots from when the step executed
4. **Three compensation modes per step:**
   - No compensation (step is skipped during rollback)
   - Local compensation (lambda via `compensate:` parameter)
   - Command compensation (send compensating command via `CompensateWith<T>()`)

### Compensation Retry

When a compensating action fails, the saga enters `Stuck` status. The runtime retries with configurable backoff:

```csharp
// Per-step compensation retry
configure: step => step.CompensationRetry(
    maxAttempts: 5,
    delay: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)))

// Global default (in AddHeadlessSagas options)
options.CompensationRetry = new CompensationRetryOptions
{
    MaxAttempts = 5,
    Delay = attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
};
```

After exhausting retries, the saga remains in `Stuck` status. The `ISagaManagement` interface provides manual intervention:

- `RetryCompensationAsync` — retry from the failed compensation step
- `SkipFailedCompensationAsync` — skip the stuck compensation step, continue rollback (compensation only, never forward)
- `MarkResolvedAsync` — transition to `Resolved` terminal state (distinct from `Completed`); stores audit: reason, timestamp, operator identity
- `GetStuckSagasAsync` — query for stuck sagas (for alerting/dashboards)

### Dead-Letter

Optionally, stuck sagas can publish a dead-letter event for external monitoring:

```csharp
options.OnSagaStuck = async (sagaInstance, exception, ct) =>
{
    await publisher.PublishAsync(new SagaStuckEvent(sagaInstance.Id, exception.Message), ct);
};
```

## Testing

### SagaTestHarness

A test harness that simulates the saga runtime without real messaging or persistence. Provides fluent assertions for step execution, state mutations, and compensation.

#### Testing Direct Steps

```csharp
[Fact]
public async Task Order_saga_completes_when_all_steps_succeed()
{
    var harness = SagaTestHarness
        .For<OrderSaga, OrderSagaState>(new OrderSagaState { OrderId = "order-1" })
        .WithService<IPaymentService>(mock =>
            mock.CaptureAsync("order-1", Arg.Any<CancellationToken>())
                .Returns(new PaymentResult("pay-1")))
        .WithService<IInventoryService>(mock =>
            mock.ReserveAsync("order-1", Arg.Any<CancellationToken>())
                .Returns(new ReservationResult("res-1")));

    var result = await harness.RunToCompletionAsync();

    result.Status.Should().Be(SagaRuntimeStatus.Completed);
    result.State.PaymentId.Should().Be("pay-1");
    result.State.ReservationId.Should().Be("res-1");
    result.CompletedSteps.Should().HaveCount(2);
    result.PublishedMessages.Should().ContainSingle<OrderCompleted>();
}
```

#### Testing Compensation

```csharp
[Fact]
public async Task Order_saga_compensates_when_inventory_fails()
{
    var harness = SagaTestHarness
        .For<OrderSaga, OrderSagaState>(new OrderSagaState { OrderId = "order-1" })
        .WithService<IPaymentService>(mock =>
            mock.CaptureAsync("order-1", Arg.Any<CancellationToken>())
                .Returns(new PaymentResult("pay-1")))
        .WithService<IInventoryService>(mock =>
            mock.ReserveAsync("order-1", Arg.Any<CancellationToken>())
                .ThrowsAsync(new InsufficientStockException()));

    var result = await harness.RunToCompletionAsync();

    result.Status.Should().Be(SagaRuntimeStatus.Failed);
    result.CompensatedSteps.Should().ContainSingle("capture-payment");
    result.PublishedMessages.Should().ContainSingle<OrderFailed>();
}
```

#### Testing Command/Reply Steps

```csharp
[Fact]
public async Task Order_saga_sends_commands_and_handles_replies()
{
    var result = await SagaTestHarness
        .For<OrderSaga, OrderSagaState>(new OrderSagaState { OrderId = "order-1" })
        .Start()
        .ExpectCommand<ChargePaymentCommand>("payment-service")
        .ReplyWith(new PaymentCharged { PaymentId = "pay-1" })
        .ExpectCommand<ReserveInventoryCommand>("inventory-service")
        .ReplyWith(new InventoryReserved { ReservationId = "res-1" })
        .CompleteAsync();

    result.Status.Should().Be(SagaRuntimeStatus.Completed);
    result.State.PaymentId.Should().Be("pay-1");
}
```

#### Testing Command/Reply Compensation

```csharp
[Fact]
public async Task Order_saga_sends_compensating_commands_on_failure()
{
    var result = await SagaTestHarness
        .For<OrderSaga, OrderSagaState>(new OrderSagaState { OrderId = "order-1" })
        .Start()
        .ExpectCommand<ChargePaymentCommand>("payment-service")
        .ReplyWith(new PaymentCharged { PaymentId = "pay-1" })
        .ExpectCommand<ReserveInventoryCommand>("inventory-service")
        .ReplyWithFailure(new InsufficientStock())
        .ExpectCompensationCommand<RefundPaymentCommand>("payment-service")
        .ReplyWith(new PaymentRefunded())
        .CompleteAsync();

    result.Status.Should().Be(SagaRuntimeStatus.Failed);
}
```

#### Testing Wait Steps

```csharp
[Fact]
public async Task Order_saga_waits_for_shipment_event()
{
    var result = await SagaTestHarness
        .For<OrderSaga, OrderSagaState>(new OrderSagaState { OrderId = "order-1" })
        .WithService<IPaymentService>(/* ... */)
        .Start()
        // ... steps execute ...
        .ExpectWaitFor<ShipmentBooked>()
        .RaiseEvent(new ShipmentBooked { OrderId = "order-1", TrackingId = "track-1" })
        .CompleteAsync();

    result.Status.Should().Be(SagaRuntimeStatus.Completed);
}
```

#### Testing Timeouts

```csharp
[Fact]
public async Task Order_saga_times_out_when_shipment_not_received()
{
    var result = await SagaTestHarness
        .For<OrderSaga, OrderSagaState>(new OrderSagaState { OrderId = "order-1" })
        .WithService<IPaymentService>(/* ... */)
        .Start()
        .ExpectWaitFor<ShipmentBooked>()
        .SimulateTimeout()
        .CompleteAsync();

    result.Status.Should().Be(SagaRuntimeStatus.Failed);
}
```

## Full Example: Mixed Step Types

```csharp
public sealed record OrderSagaState
{
    public required string OrderId { get; init; }
    public string? PaymentId { get; set; }
    public string? ReservationId { get; set; }
    public string? ShipmentTrackingId { get; set; }
    public bool PaymentCaptured { get; set; }
    public bool InventoryReserved { get; set; }
}

public sealed class OrderSaga : ISagaDefinition<OrderSagaState>
{
    public void Build(ISagaBuilder<OrderSagaState> saga)
    {
        saga
            // Step 1: Direct invocation via DI
            .Step(
                "validate-order",
                async (ctx, ct) =>
                {
                    var validator = ctx.Services.GetRequiredService<IOrderValidator>();
                    await validator.ValidateAsync(ctx.State.OrderId, ct);
                })

            // Step 2: Command/reply via messaging
            .Command<ChargePaymentCommand>(
                "charge-payment",
                destination: "payment-service",
                buildCommand: ctx => new ChargePaymentCommand(ctx.State.OrderId),
                configure: cmd => cmd
                    .OnReply<PaymentCharged>((ctx, reply, ct) =>
                    {
                        ctx.State.PaymentId = reply.PaymentId;
                        ctx.State.PaymentCaptured = true;
                        return ValueTask.CompletedTask;
                    })
                    .CompensateWith<RefundPaymentCommand>(
                        "payment-service",
                        ctx => new RefundPaymentCommand(ctx.State.PaymentId!))
                    .Retry(3, attempt => TimeSpan.FromSeconds(attempt * 2))
                    .Timeout(TimeSpan.FromSeconds(30)))

            // Step 3: Direct invocation with conditional execution
            .Step(
                "reserve-inventory",
                async (ctx, ct) =>
                {
                    var svc = ctx.Services.GetRequiredService<IInventoryService>();
                    var result = await svc.ReserveAsync(ctx.State.OrderId, ct);
                    ctx.State.ReservationId = result.ReservationId;
                    ctx.State.InventoryReserved = true;
                },
                compensate: async (ctx, ct) =>
                {
                    if (!ctx.State.InventoryReserved || ctx.State.ReservationId is null)
                        return;
                    var svc = ctx.Services.GetRequiredService<IInventoryService>();
                    await svc.ReleaseAsync(ctx.State.ReservationId, ct);
                },
                configure: step => step
                    .When(state => state.PaymentCaptured)
                    .Timeout(TimeSpan.FromSeconds(20)))

            // Step 4: Wait for external event
            .WaitFor<ShipmentBooked>(
                "wait-for-shipment",
                sagaKey: state => state.OrderId,
                eventKey: evt => evt.OrderId,
                apply: (ctx, evt, ct) =>
                {
                    ctx.State.ShipmentTrackingId = evt.TrackingId;
                    return ValueTask.CompletedTask;
                },
                configure: wait => wait.Timeout(TimeSpan.FromHours(2)))

            // Global timeout
            .Timeout(
                TimeSpan.FromHours(6),
                async (ctx, ct) => await ctx.FailAsync("Order saga timed out", ct))

            // Lifecycle hooks
            .Completed(async (ctx, ct) =>
            {
                await ctx.PublishAsync(new OrderCompleted(ctx.State.OrderId), ct);
            })

            .Failed(async (ctx, ex, ct) =>
            {
                await ctx.PublishAsync(
                    new OrderFailed(ctx.State.OrderId, ex.Message), ct);
            });
    }
}
```

### Registration

```csharp
services.AddHeadlessSagas(options =>
{
    // Discover saga definitions from assembly
    options.AddSagasFromAssembly(typeof(OrderSaga).Assembly);

    // Or register individually
    options.AddSaga<OrderSaga, OrderSagaState>();

    // Global compensation retry
    options.CompensationRetry = new CompensationRetryOptions
    {
        MaxAttempts = 5,
        Delay = attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
    };

    // Stuck saga handling
    options.OnSagaStuck = async (instance, ex, ct) =>
    {
        // alert, publish event, etc.
    };
});

// Storage provider (reuses same connection as messaging)
services.AddHeadlessMessaging(options =>
{
    options.UsePostgreSql(connectionString);
    options.UseSagaStorage(); // adds saga tables to the same schema
});
```

## Package Structure

Following the abstraction + provider pattern:

| Package | Depends On | Contents |
|---------|-----------|----------|
| `Headless.Sagas.Abstractions` | `Headless.Messaging.Abstractions` | ISagaDefinition, ISagaBuilder, ISagaOrchestrator, ISagaContext, ISagaManagement, state types |
| `Headless.Sagas` | `Headless.Sagas.Abstractions`, `Headless.Messaging.Core` | Orchestrator runtime, step engine, compensation engine, timeout polling |
| `Headless.Sagas.Testing` | `Headless.Sagas.Abstractions` | SagaTestHarness, in-memory runtime, assertion helpers |

Storage: saga tables added to existing messaging storage providers via `UseSagaStorage()` extension. No separate `Headless.Sagas.PostgreSql` package — the saga tables live in the same schema as `published`/`received` tables.

### DB Schema (saga tables)

```sql
-- saga instances
CREATE TABLE {schema}.saga_instances (
    id              VARCHAR(36)     PRIMARY KEY,
    saga_name       VARCHAR(500)    NOT NULL,
    state_json      TEXT            NOT NULL,
    status          VARCHAR(20)     NOT NULL,
    step_index      INT             NOT NULL DEFAULT 0,
    created_at_utc  TIMESTAMPTZ     NOT NULL,
    updated_at_utc  TIMESTAMPTZ     NOT NULL,
    expires_at_utc  TIMESTAMPTZ     NULL,
    waiting_event   VARCHAR(500)    NULL,
    waiting_key     VARCHAR(500)    NULL,
    failure_reason  TEXT            NULL,
    exception_info  TEXT            NULL,
    version         INT             NOT NULL DEFAULT 0,
    resolved_reason TEXT            NULL,
    resolved_at_utc TIMESTAMPTZ     NULL,
    resolved_by     VARCHAR(500)    NULL
);

-- per-step completion log (normalized, not JSONB)
CREATE TABLE {schema}.saga_step_log (
    id              BIGINT          GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    saga_id         VARCHAR(36)     NOT NULL REFERENCES {schema}.saga_instances(id),
    step_name       VARCHAR(500)    NOT NULL,
    step_index      INT             NOT NULL,
    status          VARCHAR(20)     NOT NULL,  -- Completed, CompensationFailed, Compensated
    completed_at_utc TIMESTAMPTZ    NOT NULL,
    compensation_data TEXT          NULL,       -- keyed JSON dictionary for compensation context
    duration_ms     INT             NULL,
    exception_info  TEXT            NULL
);

CREATE INDEX ix_step_log_saga ON {schema}.saga_step_log (saga_id);
CREATE INDEX ix_step_log_status ON {schema}.saga_step_log (status)
    WHERE status != 'Completed';

CREATE INDEX ix_saga_status ON {schema}.saga_instances (status);
CREATE INDEX ix_saga_waiting ON {schema}.saga_instances (waiting_event, waiting_key)
    WHERE waiting_event IS NOT NULL;
CREATE INDEX ix_saga_expires ON {schema}.saga_instances (expires_at_utc)
    WHERE expires_at_utc IS NOT NULL;
```

**Why child table over JSONB:** Per-step indexing enables dashboard visibility, direct SQL queries for stuck compensation steps, and step-level analytics (duration, failure rates) without JSON parsing. Write amplification is negligible — sagas typically have 3-7 steps.

## Key Decisions

1. **Builder DSL over handler/interfaces** — one class per saga, explicit step/compensation pairs
2. **Both direct invocation and command/reply** — `Step()` for DI calls, `Command()` for messaging
3. **Single runtime shape** — `Command()` compiles to the same internal step contract as `Step()`, not a separate engine
4. **Explicit compensation only** — framework never auto-generates rollback
5. **Durable timeout processor** — default uses polling over persisted `expires_at_utc`; abstracted for future swap to transport-based delayed messages or external scheduler
6. **Compensation retry with dead-letter** — configurable retry + `Stuck` status + manual intervention
7. **Saga storage alongside messaging** — same schema, same providers, `UseSagaStorage()` extension
8. **Two distinct correlation modes** — runtime correlation (headers) for `Command()` replies, business correlation (type + key) for `WaitFor()` events
9. **Testing harness with two modes** — service mocking for direct steps, command/reply simulation for messaging steps
10. **Generic `IStepOptionsBuilder<TState>`** — typed `When()` predicate, `IdempotencyKey(Func<ISagaContext<TState>, string>)` for runtime context access
11. **Dedicated saga headers** — `Headers.SagaId` + `Headers.SagaStepIndex` for routing (separate from existing `CorrelationId`); reply type determined by message CLR type, not a header
12. **Singleton definitions** — `Build()` called once at startup, immutable step graph cached
13. **Optimistic concurrency** — `version` column on saga_instances, retry on conflict
14. **Dashboard: same UI, new tab** — saga instances surfaced alongside messaging in the existing dashboard
15. **Keyed step data** — `SetStepData<T>(key, data)` / `GetStepData<T>(key)` scoped to current step, persisted after successful completion
16. **Safe management API** — `SkipFailedCompensationAsync` (compensation only, never forward), `MarkResolvedAsync` (distinct `Resolved` terminal state with audit trail)
17. **Cancellation with intermediate state** — `CancelAsync` → `Cancelling` → compensation → `Cancelled` or `Stuck`
18. **No `Critical` step flag** — removed; undefined runtime semantics, better addressed by explicit retry/timeout configuration per step
19. **At-least-once execution** — step, reply, and compensation handlers are at-least-once; users must make handlers idempotent
20. **Immutable definition model** — `Build()` produces metadata; runtime lambdas execute against per-instance `ISagaContext<TState>`; no shared mutable state in definitions
21. **Build-time validation** — unique step names, single global timeout, required reply handlers, no duplicate reply types, no empty destinations, no conflicting compensation
22. **Reply/event safety** — late, duplicate, and stale replies/events are ignored based on `SagaId + StepIndex` + saga status matching; configurable dead-lettering
23. **`SagaName` over `DefinitionType`** — logical name, not CLR type; avoids overloaded "type" semantics
24. **`CompensationDataJson` over `StepDataJson`** — communicates the sole intended use (compensation context)
25. **Split event ingress API** — `RaiseEventAsync(event)` for business-key correlation, `RaiseEventToSagaAsync(sagaId, event)` for direct targeted delivery; separate public intent from internal routing

## Resolved Questions

1. **Generic step options** → `IStepOptionsBuilder<TState>` with typed `When()` predicate
2. **Storage packaging** → Extend messaging providers via `UseSagaStorage()`, no separate packages
3. **Command reply correlation** → Dedicated `Headers.SagaId` / `Headers.SagaStepIndex` headers to avoid collision with existing `CorrelationId`
4. **Definition lifecycle** → Singleton; `Build()` called once, step graph cached and reused
5. **Concurrency** → Optimistic concurrency via `version` column + retry on conflict
6. **Dashboard** → Same messaging dashboard, new saga tab/section
7. **Step data shape** → Keyed access (`SetStepData<T>(key, data)`), scoped to current step during compensation, persisted after successful step completion
8. **`Critical` flag** → Removed; undefined runtime semantics, use explicit retry/timeout per step instead
9. **`IdempotencyKey` signature** → `Func<ISagaContext<TState>, string>` for access to saga ID and step context
10. **`FailAsync` signature** → Added `CancellationToken ct = default` for API consistency
11. **`SkipStepAsync` ambiguity** → Split into `SkipFailedCompensationAsync` (compensation only); no forward step skipping
12. **`ForceCompleteAsync` naming** → `MarkResolvedAsync` with distinct `Resolved` terminal state (not `Completed`); stores audit metadata (reason, timestamp, operator identity)
13. **Timeout abstraction** → Durable timeout processor (abstracted); default polls persisted timestamps, swappable for transport-based or external scheduler
14. **Cancellation flow** → `CancelAsync` → `Cancelling` (intermediate) → compensation → `Cancelled` or `Stuck`
15. **Correlation model** → Two distinct modes: runtime correlation (headers) for Command replies, business correlation (type + key) for WaitFor events
16. **Command() runtime contract** → Compiles to same internal step shape as Step(); syntactic sugar, not a separate engine
17. **Execution guarantees** → At-least-once for steps, replies, and compensation; users must make handlers idempotent
18. **Definition immutability** → `Build()` produces immutable metadata; runtime uses per-instance `ISagaContext<TState>`
19. **Build-time validation** → Unique step names, single global timeout, required reply handlers, no duplicates, no empty destinations
20. **Reply/event safety** → Late/duplicate/stale arrivals ignored based on saga state matching; `options.OnIgnoredMessage` for dead-lettering
21. **`DefinitionType` → `SagaName`** → Logical name avoids overloaded "type" semantics in .NET
22. **`StepDataJson` → `CompensationDataJson`** → Clearer intent: sole use is compensation context
23. **`RaiseEventAsync` split** → `RaiseEventAsync(event)` for business-key matched delivery, `RaiseEventToSagaAsync(sagaId, event)` for direct targeted delivery

## Open Questions

1. **Compensation data serialization**: `SetStepData<T>(key, data)` serializes to `CompletedStepLog.CompensationDataJson` as a keyed dictionary. What serializer? System.Text.Json with the same options as state serialization? Should there be a size limit to prevent bloat?
