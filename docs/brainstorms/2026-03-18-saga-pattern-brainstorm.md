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
        Action<IStepOptionsBuilder>? configure = null);

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

    /// Store step-scoped data for compensation context.
    /// Persisted in CompletedStepLog.StepDataJson.
    void SetStepData<T>(T data);
    T? GetStepData<T>();

    /// Force-fail the saga (triggers compensation).
    ValueTask FailAsync(string reason);
}
```

### Step Options

```csharp
public interface IStepOptionsBuilder
{
    IStepOptionsBuilder Retry(int maxAttempts, Func<int, TimeSpan>? delay = null);
    IStepOptionsBuilder Timeout(TimeSpan timeout);
    IStepOptionsBuilder Critical(bool value = true);
    IStepOptionsBuilder IdempotencyKey(Func<object, string> factory);
    IStepOptionsBuilder When(Func<object, bool> predicate);
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

    /// Handle a failure reply type. Triggers compensation.
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

    Task RaiseEventAsync<TEvent>(
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
    Task RetryCompensationAsync(string sagaId, CancellationToken ct = default);

    /// Force-complete a saga, skipping remaining steps.
    Task ForceCompleteAsync(string sagaId, string reason, CancellationToken ct = default);

    /// Skip the current failed step and continue.
    Task SkipStepAsync(string sagaId, CancellationToken ct = default);

    /// Find sagas stuck in a failed/compensating state.
    Task<IReadOnlyList<SagaInstance>> GetStuckSagasAsync(
        TimeSpan? olderThan = null,
        CancellationToken ct = default);
}
```

## Step Types

### 1. Local/Direct Step (`Step`)

Calls a service directly via DI or runs in-process logic. The saga orchestrator executes the lambda synchronously (in saga context) and advances immediately on success.

```
Orchestrator --execute--> Lambda (calls IPaymentService via DI)
                          |
                          +-- success: advance to next step
                          +-- exception: begin compensation
```

### 2. Command/Reply Step (`Command`)

Sends a command message to a participant service via the existing messaging infrastructure. The saga enters a waiting state until the reply arrives. Reply correlation uses `Headers.CorrelationId` = saga ID + step index.

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

Waits for an external event not triggered by a command. The runtime matches incoming events by type + correlation key.

```
Orchestrator --enters wait state (persisted)
     |
External System --publishes event--> Message Broker
     |
     +-- Runtime matches: event type + eventKey(event) == sagaKey(state)
     +-- apply handler: update state, advance
     +-- Timeout: invoke onTimeout or fail
```

**Event correlation**: `sagaKey(state)` produces the key stored in the saga instance. When an event arrives, `eventKey(event)` extracts the key from the event. The runtime matches by `(event type, key)`.

## Execution Model

### Saga Instance (Persistence)

```csharp
public sealed record SagaInstance
{
    public required string Id { get; init; }
    public required string DefinitionType { get; init; }
    public required string StateJson { get; set; }
    public required SagaRuntimeStatus Status { get; set; }
    public int CurrentStepIndex { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string? WaitingForEventType { get; set; }
    public string? WaitingForEventKey { get; set; }
    public List<CompletedStepLog> CompletedSteps { get; init; } = [];
    public string? FailureReason { get; set; }
    public string? ExceptionInfo { get; set; }
}

public enum SagaRuntimeStatus
{
    Running,
    WaitingForEvent,
    WaitingForReply,
    Compensating,
    Completed,
    Failed,        // compensation succeeded but saga didn't complete
    Stuck,         // compensation itself failed
    Cancelled,
}

public sealed record CompletedStepLog
{
    public required string StepName { get; init; }
    public required int StepIndex { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
    public string? StepDataJson { get; init; }  // snapshot for compensation
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

## Timeout Design

Three levels, aligned with NServiceBus timeout model but using persisted timers (not in-memory):

| Level | Scope | Trigger | Default |
|-------|-------|---------|---------|
| **Step timeout** | Single step execution | Step exceeds duration | None (no timeout) |
| **Wait timeout** | WaitFor / Command reply | Event/reply not received | None |
| **Saga timeout** | Entire saga lifetime | Saga not completed | None |

All timeouts are persisted (stored as `ExpiresAtUtc` on the saga instance or step). A background job polls for expired sagas/steps. No `Task.Delay`.

## Conditional Steps

Steps can be skipped based on current state via `When(predicate)`:

```csharp
.Step("charge-payment",
    execute: async (ctx, ct) => { ... },
    configure: step => step.When(state => state.RequiresPayment))
```

When the predicate returns `false`, the step is skipped entirely (no execution, no compensation entry). The step index advances. This matches Eventuate Tram's `Predicate<Data>` behavior.

## Compensation Design

### Principles

1. **Compensation is explicit** — never auto-generated
2. **Reverse-order execution** — compensate completed steps in LIFO order
3. **Step data available** — `GetStepData<T>()` provides snapshot from when the step executed
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
- `ForceCompleteAsync` — mark as complete despite partial compensation
- `SkipStepAsync` — skip the stuck compensation step, continue with next
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
                async (ctx, ct) => await ctx.FailAsync("Order saga timed out"))

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
    definition_type VARCHAR(500)    NOT NULL,
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
    completed_steps JSONB           NOT NULL DEFAULT '[]'
);

CREATE INDEX ix_saga_status ON {schema}.saga_instances (status);
CREATE INDEX ix_saga_waiting ON {schema}.saga_instances (waiting_event, waiting_key)
    WHERE waiting_event IS NOT NULL;
CREATE INDEX ix_saga_expires ON {schema}.saga_instances (expires_at_utc)
    WHERE expires_at_utc IS NOT NULL;
```

## Key Decisions

1. **Builder DSL over handler/interfaces** — one class per saga, explicit step/compensation pairs
2. **Both direct invocation and command/reply** — `Step()` for DI calls, `Command()` for messaging
3. **Explicit compensation only** — framework never auto-generates rollback
4. **Persisted timers** — background job polls `expires_at_utc`, no in-memory `Task.Delay`
5. **Compensation retry with dead-letter** — configurable retry + `Stuck` status + manual intervention
6. **Saga storage alongside messaging** — same schema, same providers, `UseSagaStorage()` extension
7. **Event correlation by type + key** — `WaitFor` uses dual key extractors (saga-side + event-side)
8. **Testing harness with two modes** — service mocking for direct steps, command/reply simulation for messaging steps

## Open Questions

1. **`IStepOptionsBuilder.When` generic constraint**: The `When(Func<object, bool>)` predicate uses `object` because `IStepOptionsBuilder` is not generic. Should it be `IStepOptionsBuilder<TState>` to get `When(Func<TState, bool>)`? This would make `Step()`'s `configure:` parameter generic too.

2. **Saga storage as extension vs separate package**: Current proposal uses `UseSagaStorage()` on messaging options. Alternative: separate `Headless.Sagas.PostgreSql` package that shares the schema but has independent lifecycle. Trade-off: convenience vs coupling.

3. **Command reply correlation**: The proposal uses `Headers.CorrelationId` = saga ID. But the existing messaging system already uses `CorrelationId` for its own correlation. Should sagas use a separate `SagaId` header to avoid collision?

4. **Saga definition lifecycle**: Should `ISagaDefinition<TState>` instances be singletons (registered once, `Build()` called once to create a step graph) or transient (new instance per saga execution)? Singleton is more efficient but means the definition can't hold instance state.

5. **Step data serialization**: `SetStepData<T>()` serializes to `CompletedStepLog.StepDataJson`. What serializer? System.Text.Json with the same options as state serialization? Should there be a size limit to prevent bloat?

6. **Dashboard integration**: The existing messaging dashboard shows published/received messages. Should saga instances be surfaced in the same dashboard, or a separate saga-specific view?

7. **Concurrency**: What happens if two events arrive simultaneously for the same saga instance (e.g., two `RaiseEventAsync` calls)? Options: optimistic concurrency (retry on conflict), pessimistic locking (DB row lock), or queue-per-saga.
