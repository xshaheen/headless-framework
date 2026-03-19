---
title: Routing Slip Pattern Support
type: brainstorm
date: 2026-03-18
research:
  repo_patterns:
    - src/Headless.Messaging.Abstractions/IMessagePublisher.cs
    - src/Headless.Messaging.Abstractions/IConsume.cs
    - src/Headless.Messaging.Abstractions/ConsumeContext.cs
    - src/Headless.Messaging.Abstractions/Headers.cs
    - src/Headless.Messaging.Core/Internal/OutboxPublisher.cs
    - src/Headless.Messaging.Core/Transport/IDispatcher.cs
    - src/Headless.Messaging.Core/Messages/MediumMessage.cs
    - docs/brainstorms/2026-03-18-saga-pattern-brainstorm.md
  external_research:
    - MassTransit Courier (full routing slip — IActivity<TArgs,TLog>, itinerary builder, ReviseItinerary, variables, event subscriptions)
    - NServiceBus.MessageRouting (Jimmy Bogard — header-based forwarding, no compensation)
    - Rebus (no routing slip support)
    - Wolverine (no routing slip support)
    - Eventuate Tram (static orchestration sagas only, no routing slip)
    - Temporal (code-as-workflow — dynamic sequences via plain code, no explicit pattern needed)
    - Dapr Workflows (task chaining + manual compensation, no routing slip)
    - Enterprise Integration Patterns (Hohpe/Woolf — Routing Slip pattern definition)
  timestamp: 2026-03-18T00:00:00Z
---

# Routing Slip Pattern Support

## What We're Building

Message-carried routing slip support for `headless-framework`. A routing slip is a dynamic sequence of activities where the itinerary travels with the message — no central coordinator. Each activity executes, appends its log, and forwards the slip to the next destination. Activities can revise the remaining itinerary at runtime.

**Complements, does not replace, orchestration sagas.** Sagas are for complex workflows with static step graphs, centralized state, and database-backed compensation. Routing slips are for dynamic processing pipelines where the step sequence varies per transaction and no central coordinator is desired.

**Design principles:**

- Message-carried state — no central routing-slip execution state store; runtime progress is carried in the message rather than reconstructed from a database-owned slip instance
- Typed activity contract — `IActivity<TArgs, TLog>` with explicit execute and compensate
- Dynamic itinerary — steps determined at runtime, revisable mid-execution
- Decentralized execution — each activity self-routes to the next destination
- Event subscriptions for observability — targeted, not broadcast
- Reuses existing messaging infrastructure for transport
- Independent from sagas — separate packages, no cross-dependency

**Non-goals:**

- Central coordinator / persisted slip state (use sagas for that)
- First-class saga integration (`.RoutingSlip()` step type on `ISagaBuilder`)
- Parallel activity execution (fan-out/fan-in)
- Visual slip designer
- Slip versioning / migration

## Subsystem Boundaries

Routing Slips sit *on top of* Messaging — strictly one-way dependency:

```
RoutingSlips ──→ Messaging (transport + serialization)
Messaging    ╳  RoutingSlips
Sagas        ╳  RoutingSlips
RoutingSlips ╳  Sagas
```

| Subsystem | Role | Routing Slip Awareness |
|---|---|---|
| **Messaging** | Transport layer — publish/consume, outbox, broker abstraction | None. Activity host is just another consumer. |
| **Sagas** | Orchestration layer — static step graphs, compensation, state | None. Can compose via Command() + subscription events. |
| **RoutingSlips** | Dynamic pipeline layer — message-carried itinerary, activities, compensation | Consumes Messaging only. |

**Implications:** Adding routing slips touches zero lines in existing messaging or saga packages. Users can adopt routing slips without sagas and vice versa. Composition is manual — a saga `Command()` step can trigger a routing slip, and the saga subscribes to slip completion/fault events.

**Note on messaging infrastructure:** The underlying messaging system (outbox, inbox/received tracking, broker durability) still provides its own persistence guarantees. "No persistence" refers specifically to the routing slip execution model — there is no routing-slip-specific persistence model. The slip's progress is carried in the message, not stored in a database table.

## Why Routing Slips (vs Sagas)

| Aspect | Orchestration Saga | Routing Slip |
|---|---|---|
| **Step sequence** | Static, defined at compile time | Dynamic, determined at runtime per transaction |
| **State location** | Database (persisted) | Message (carried with the slip) |
| **Coordinator** | Central orchestrator | None — each activity forwards |
| **Runtime modification** | No (steps are compiled) | Yes — `ReviseItinerary()` |
| **Compensation** | Reverse-order, DB-backed | Reverse-order, slip-carried logs |
| **Observability** | DB queries, dashboard | Event subscriptions |
| **Best for** | Complex workflows, known steps, long-running | Assembly-line processing, variable pipelines |

**When to use which:**
- **Saga:** You know the steps at compile time. You need centralized state, timeouts, WaitFor events, dashboard visibility. Example: order processing with payment → inventory → shipping.
- **Routing Slip:** Steps vary per transaction. Different order types need different processing pipelines. No central coordinator desired. Example: document processing where validation → enrichment → transformation → routing varies by document type.

## API Contract

### Activity Contracts

```csharp
/// Full activity: execute + compensate.
/// TArgs: typed input arguments for this activity.
/// TLog: typed compensation log stored in the slip after successful execution.
public interface IActivity<TArgs, TLog>
    where TArgs : class
    where TLog : class
{
    ValueTask<ExecutionResult> ExecuteAsync(
        IActivityContext<TArgs> ctx,
        CancellationToken ct = default);

    ValueTask<CompensationResult> CompensateAsync(
        ICompensationContext<TLog> ctx,
        CancellationToken ct = default);
}

/// Execute-only activity: no compensation.
/// For fire-and-forget steps (notifications, logging, analytics).
public interface IExecuteActivity<TArgs>
    where TArgs : class
{
    ValueTask<ExecutionResult> ExecuteAsync(
        IActivityContext<TArgs> ctx,
        CancellationToken ct = default);
}
```

### Activity Context

```csharp
public interface IActivityContext<TArgs> where TArgs : class
{
    /// Unique tracking ID for this routing slip execution.
    string TrackingId { get; }

    /// Typed arguments for this activity.
    TArgs Arguments { get; }

    /// DI service provider.
    IServiceProvider Services { get; }

    /// Read a slip variable set by the creator or a previous activity.
    /// Deserializes the stored JsonElement to T on demand.
    T? GetVariable<T>(string key);

    /// Set a slip variable for downstream activities.
    /// Value is serialized to JsonElement immediately.
    void SetVariable<T>(string key, T value);
}

public interface ICompensationContext<TLog> where TLog : class
{
    /// Unique tracking ID for this routing slip execution.
    string TrackingId { get; }

    /// Typed compensation log stored during forward execution.
    TLog Log { get; }

    /// DI service provider.
    IServiceProvider Services { get; }

    /// Read a slip variable (read-only during compensation).
    T? GetVariable<T>(string key);
}
```

### Execution & Compensation Results

Result types use static factories with private constructors. Activities produce results via these factories — no leaky internal state.

```csharp
/// Returned by ExecuteAsync to indicate the outcome.
public sealed class ExecutionResult
{
    private ExecutionResult() { }

    /// Complete successfully (execute-only, no compensation log).
    public static ExecutionResult Completed() => new(...);

    /// Complete successfully with a typed compensation log.
    /// The log is stored in the slip and available during CompensateAsync.
    public static ExecutionResult CompletedWithLog<TLog>(TLog log)
        where TLog : class => new(...);

    /// Fault — triggers reverse compensation of completed activities.
    public static ExecutionResult Faulted(Exception exception) => new(...);

    /// Terminate — end the slip immediately without fault or compensation.
    /// Remaining itinerary is discarded. No rollback. No fault.
    /// Subscribers receive RoutingSlipCompleted (not a fault event).
    /// This is "finish early", not cancellation.
    public static ExecutionResult Terminated() => new(...);

    /// Revise the remaining itinerary. Activities can add, remove, or
    /// reorder steps dynamically based on what they discover.
    public static ExecutionResult Revised(
        Action<IItineraryBuilder> configure) => new(...);
}

/// Returned by CompensateAsync to indicate the outcome.
public sealed class CompensationResult
{
    private CompensationResult() { }

    /// Compensation succeeded.
    public static CompensationResult Compensated() => new(...);

    /// Compensation failed — slip enters CompensationFailed terminal state.
    public static CompensationResult Failed(Exception exception) => new(...);
}
```

### Routing Slip Status

```csharp
public enum RoutingSlipStatus
{
    /// Forward execution in progress.
    Running,

    /// Terminal: all activities completed successfully.
    Completed,

    /// Terminal: an activity faulted, all completed activities
    /// were compensated successfully.
    Faulted,

    /// Terminal: compensation itself failed. Manual intervention required.
    CompensationFailed,

    /// Terminal: an activity called Terminated(). Remaining itinerary
    /// discarded, no compensation. Early successful stop.
    Terminated,
}
```

### Routing Slip Builder

```csharp
public interface IRoutingSlipBuilder
{
    /// Optional business name for this routing slip (used in metrics/logging).
    /// If not set, metrics use tracking_id only.
    IRoutingSlipBuilder WithName(string name);

    /// Set the tracking ID. If not set, auto-generated.
    IRoutingSlipBuilder WithTrackingId(string trackingId);

    /// Add an activity to the itinerary.
    IRoutingSlipBuilder AddActivity(
        string name,
        string destination,
        object arguments);

    /// Set a slip-level variable available to all activities.
    /// Value is serialized to JsonElement.
    IRoutingSlipBuilder SetVariable<T>(string key, T value);

    /// Subscribe an endpoint to routing slip events.
    IRoutingSlipBuilder AddSubscription(
        string destination,
        RoutingSlipEvents events);

    /// Build the routing slip message.
    RoutingSlip Build();
}

/// Builder for revising the remaining itinerary from within an activity.
public interface IItineraryBuilder
{
    /// Add an activity to the revised itinerary.
    /// Name must be non-null/non-empty. Destination must be non-null/non-empty.
    IItineraryBuilder AddActivity(
        string name,
        string destination,
        object arguments);

    /// Keep all remaining activities from the original itinerary.
    /// Call this to preserve unexecuted steps after prepending new ones.
    /// May be called at most once per revision.
    IItineraryBuilder AddActivitiesFromSourceItinerary();
}

[Flags]
public enum RoutingSlipEvents
{
    None = 0,
    Completed = 1,
    Faulted = 2,
    CompensationFailed = 4,
    ActivityCompleted = 8,
    ActivityFaulted = 16,
    ActivityCompensated = 32,
    All = Completed | Faulted | CompensationFailed
        | ActivityCompleted | ActivityFaulted | ActivityCompensated,
}
```

### Routing Slip Executor

```csharp
public interface IRoutingSlipExecutor
{
    /// Execute a routing slip by sending it to the first activity's destination.
    Task ExecuteAsync(RoutingSlip routingSlip, CancellationToken ct = default);
}
```

### Subscription Event Types

```csharp
public sealed record RoutingSlipCompleted
{
    public required string TrackingId { get; init; }
    public string? Name { get; init; }
    public required TimeSpan Duration { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Variables { get; init; }
}

public sealed record RoutingSlipFaulted
{
    public required string TrackingId { get; init; }
    public string? Name { get; init; }
    public required string ActivityName { get; init; }
    public required TimeSpan Duration { get; init; }
    public required string ExceptionMessage { get; init; }
    public required string? ExceptionType { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Variables { get; init; }
}

public sealed record RoutingSlipCompensationFailed
{
    public required string TrackingId { get; init; }
    public string? Name { get; init; }
    public required string ActivityName { get; init; }
    public required string ExceptionMessage { get; init; }
    public required string? ExceptionType { get; init; }
}

public sealed record RoutingSlipActivityCompleted
{
    public required string TrackingId { get; init; }
    public required string ActivityName { get; init; }
    public required TimeSpan Duration { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Variables { get; init; }
}

public sealed record RoutingSlipActivityFaulted
{
    public required string TrackingId { get; init; }
    public required string ActivityName { get; init; }
    public required string ExceptionMessage { get; init; }
    public required string? ExceptionType { get; init; }
}

public sealed record RoutingSlipActivityCompensated
{
    public required string TrackingId { get; init; }
    public required string ActivityName { get; init; }
    public required TimeSpan Duration { get; init; }
}
```

## Routing Slip Message (Wire Format)

The routing slip is the message body. The framework serializes/deserializes it transparently. Activities never see the raw slip — they interact via `IActivityContext<TArgs>`.

```csharp
public sealed record RoutingSlip
{
    public required string TrackingId { get; init; }
    public string? Name { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// Number of itinerary revisions applied during execution.
    public int RevisionCount { get; init; }

    /// Remaining activities to execute (head = current).
    public required IReadOnlyList<RoutingSlipActivity> Itinerary { get; init; }

    /// Completed activities with their compensation logs.
    public required IReadOnlyList<RoutingSlipActivityLog> ActivityLogs { get; init; }

    /// Shared variables accessible to all activities.
    /// Values are JsonElement for safe cross-service serialization.
    public required IReadOnlyDictionary<string, JsonElement> Variables { get; init; }

    /// Event subscription registrations.
    public required IReadOnlyList<RoutingSlipSubscription> Subscriptions { get; init; }
}

public sealed record RoutingSlipActivity
{
    public required string Name { get; init; }
    public required string Destination { get; init; }
    public required string ArgumentsJson { get; init; }
}

public sealed record RoutingSlipActivityLog
{
    public required string Name { get; init; }
    public required string Destination { get; init; }
    /// Serialized TLog from CompletedWithLog. Null for execute-only activities.
    public string? CompensationLogJson { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required int DurationMs { get; init; }
}

public sealed record RoutingSlipSubscription
{
    public required string Destination { get; init; }
    public required RoutingSlipEvents Events { get; init; }
}
```

**Why no `CompensationLogType` field:** The activity host already knows `TLog` from its registration (`IActivity<TArgs, TLog>`). When compensation routes back to the same destination that executed the activity, the host deserializes `CompensationLogJson` as `TLog` directly — no wire-format type discriminator needed.

## Execution Model

### Forward Execution

```
[Creator] builds slip → sends to Activity 0's destination
     |
[Activity 0] receives slip
  → framework extracts TArgs from Itinerary[0].ArgumentsJson
  → calls ExecuteAsync(ctx, ct)
  → on Completed: pop Itinerary[0], append to ActivityLogs, forward to Itinerary[1].Destination
  → on Faulted: begin compensation from ActivityLogs (reverse order)
  → on Terminated: publish RoutingSlipCompleted, discard remaining itinerary (no compensation)
  → on Revised: replace remaining Itinerary, increment RevisionCount, forward to new Itinerary[0]
     |
[Activity 1] receives updated slip → same flow
     |
[Activity N] completes → Itinerary empty → publish RoutingSlipCompleted to subscribers
```

### Compensation Flow

```
[Activity N faults]
  → framework reads ActivityLogs in reverse
  → for each log with CompensationLogJson:
       → send slip to log.Destination (same endpoint that executed the activity)
       → activity host deserializes TLog (known from IActivity<TArgs, TLog> registration)
       → calls CompensateAsync(ctx, ct)
       → on Compensated: continue to next log
       → on Failed: publish RoutingSlipCompensationFailed, stop
  → execute-only activities (no CompensationLogJson) are skipped
  → all compensated → publish RoutingSlipFaulted to subscribers
```

### Activity Hosting

Each activity is hosted as a consumer on its destination topic. The framework auto-registers consumers during startup based on `AddActivity<T>()` / `AddActivitiesFromAssembly()`.

```
[Activity Host Consumer]
  receives RoutingSlip message
    → is this forward execution or compensation?
       → forward: deserialize TArgs, call ExecuteAsync
       → compensation: deserialize TLog, call CompensateAsync
    → process result → forward updated slip or compensate next
```

The activity host determines forward vs compensation by checking whether the slip is in compensation mode (an internal flag set when a fault occurs).

## Variable Semantics

Variables are serialized as `JsonElement` values in the slip. This avoids the `object?` round-trip problem — `System.Text.Json` cannot reliably deserialize `object?` back to the original type across service boundaries. `JsonElement` preserves the exact JSON structure.

### Merge Rules

- Keys are **case-sensitive**
- New values **overwrite** existing keys (last-write-wins)
- `null` values are allowed
- Values are serialized using the framework's configured `JsonSerializerOptions` (same as state serialization)
- `GetVariable<T>(key)` deserializes the stored `JsonElement` to `T` on demand — callers must know the expected type
- **Forward execution:** activities can read and write variables via `GetVariable<T>()` / `SetVariable<T>()`
- **Compensation:** read-only access. Activities in compensation cannot mutate slip variables. Compensation sees the variable snapshot as of the last forward activity's completion
- **Subscribers** receive the final variable dictionary in `RoutingSlipCompleted.Variables` / `RoutingSlipFaulted.Variables`

### Flow

- **Creator** sets initial variables via `builder.SetVariable("OrderId", orderId)`
- **Activities** read via `ctx.GetVariable<string>("OrderId")`
- **Activities** write via `ctx.SetVariable("ReservationId", resId)`
- **Downstream activities** see all variables set by upstream activities

Variables bridge activities without compile-time coupling. Activities document expected variables in their API contract (XML docs or README).

## Runtime Itinerary Revision

Activities can modify the remaining step sequence mid-execution via `ExecutionResult.Revised()`:

```csharp
public ValueTask<ExecutionResult> ExecuteAsync(
    IActivityContext<ValidateArgs> ctx, CancellationToken ct)
{
    if (ctx.Arguments.RequiresApproval)
    {
        return ValueTask.FromResult(ExecutionResult.Revised(rev =>
        {
            // Prepend approval step before remaining
            rev.AddActivity("approval", "approval-service",
                new { OrderId = ctx.GetVariable<string>("OrderId") });
            // Keep everything that was originally after this step
            rev.AddActivitiesFromSourceItinerary();
        }));
    }

    return ValueTask.FromResult(ExecutionResult.Completed());
}
```

### Semantics

- `Revised()` replaces the remaining itinerary entirely
- `AddActivitiesFromSourceItinerary()` injects the original remaining steps at the current position
- The revision is applied after the current activity completes (log is appended first)
- Revised slip is forwarded to the new `Itinerary[0].Destination`
- Each revision increments `RoutingSlip.RevisionCount` for auditability

### Runtime Validation Rules

- Activity name must be non-null/non-empty
- Destination must be non-null/non-empty
- `AddActivitiesFromSourceItinerary()` may be called **at most once** per revision (throws if called twice)
- Revised itinerary **may** be empty — remaining steps are discarded, slip completes after current activity
- Framework **does not** prevent cycles — user responsibility. An activity can add itself to the itinerary, creating a loop
- Duplicate activity names are **allowed** — same activity type can appear in multiple positions

## Observability

### Event Subscriptions

Subscriptions are registered at slip creation time. The framework publishes events to subscribed destinations as the slip progresses.

```csharp
var builder = new RoutingSlipBuilder();
builder.AddSubscription(
    destination: "order-tracking",
    events: RoutingSlipEvents.Completed | RoutingSlipEvents.Faulted);
builder.AddSubscription(
    destination: "monitoring",
    events: RoutingSlipEvents.All);
```

Events are published via the existing messaging infrastructure (`IMessagePublisher`). Subscribers are standard `IConsume<RoutingSlipCompleted>` consumers.

### OpenTelemetry

Follows the same pattern as `Headless.Sagas.OpenTelemetry`:

| Metric | Type | Tags |
|---|---|---|
| `routing_slip.started` | Counter | `slip_name` (from `WithName()`, omitted if not set) |
| `routing_slip.completed` | Counter | `slip_name` |
| `routing_slip.faulted` | Counter | `slip_name`, `activity_name` |
| `routing_slip.compensation_failed` | Counter | `slip_name`, `activity_name` |
| `routing_slip.activity.duration` | Histogram | `activity_name` |
| `routing_slip.activity.revised` | Counter | `activity_name` |

| Span | Parent | Key attributes |
|---|---|---|
| `routing_slip.execute` | Creator span | `tracking_id`, `slip_name` |
| `routing_slip.activity` | Incoming message span | `tracking_id`, `activity_name` |
| `routing_slip.compensate` | Incoming message span | `tracking_id`, `activity_name` |

`slip_name` is the optional business name set via `WithName()`. If not set, the tag is omitted from metrics. Traces always include `tracking_id`.

## Registration

```csharp
services.AddHeadlessRoutingSlips(options =>
{
    // Discover activities from assembly
    options.AddActivitiesFromAssembly(typeof(ReserveSeat).Assembly);

    // Or register individually with explicit destination
    options.AddActivity<ReserveSeat>(
        destination: "reservations.reserve");
    options.AddExecuteActivity<SendEmail>(
        destination: "notifications.email");
});
```

### Destination Resolution

- Assembly scanning discovers types implementing `IActivity<,>` or `IExecuteActivity<>`
- Destination is resolved in this order:
  1. Explicit `destination` parameter on `AddActivity<T>(destination)` — **wins**
  2. `[ActivityDestination("...")]` attribute on the activity class — **fallback**
  3. No destination found — **startup exception** (fail fast)
- Explicit registration overrides the attribute if both exist
- Same activity type registered twice with different destinations — **startup exception**
- Multiple different activities sharing the same destination — **allowed** (each activity handles its own message type)

## Message Size Considerations

The routing slip message grows as it progresses through activities:

- **Itinerary** shrinks (activities are popped as they execute)
- **ActivityLogs** grows (completed activities with `CompensationLogJson` accumulate)
- **Variables** may grow (activities add new variables)

**Guidance:**
- Routing slips are well-suited for moderate pipelines (3-15 activities) with compact compensation logs
- Activity logs should be small — store IDs, keys, and metadata, not large payloads
- Large artifacts (files, images, documents) should live in external storage, referenced by ID in variables
- If the total slip size approaches broker message limits (varies by transport), consider breaking into smaller sub-pipelines or using a saga with database-backed state instead

## Testing

### RoutingSlipTestHarness

```csharp
[Fact]
public async Task Slip_completes_when_all_activities_succeed()
{
    var harness = RoutingSlipTestHarness.Create()
        .WithActivity<ReserveSeat>(mock =>
        {
            mock.ExecuteAsync(Arg.Any<IActivityContext<ReserveArgs>>(), Arg.Any<CancellationToken>())
                .Returns(ExecutionResult.CompletedWithLog(new ReserveLog("res-1")));
        })
        .WithActivity<ChargeCard>(mock =>
        {
            mock.ExecuteAsync(Arg.Any<IActivityContext<ChargeArgs>>(), Arg.Any<CancellationToken>())
                .Returns(ExecutionResult.Completed());
        });

    var slip = new RoutingSlipBuilder()
        .AddActivity("reserve", "reservations", new ReserveArgs("seat-1"))
        .AddActivity("charge", "payments", new ChargeArgs(99.00m))
        .SetVariable("OrderId", "order-1")
        .Build();

    var result = await harness.ExecuteAsync(slip);

    result.Status.Should().Be(RoutingSlipStatus.Completed);
    result.ActivityLogs.Should().HaveCount(2);
    result.GetVariable<string>("OrderId").Should().Be("order-1");
}
```

### Testing Compensation

```csharp
[Fact]
public async Task Slip_compensates_on_activity_fault()
{
    var harness = RoutingSlipTestHarness.Create()
        .WithActivity<ReserveSeat>(mock =>
        {
            mock.ExecuteAsync(Arg.Any<IActivityContext<ReserveArgs>>(), default)
                .Returns(ExecutionResult.CompletedWithLog(new ReserveLog("res-1")));
            mock.CompensateAsync(Arg.Any<ICompensationContext<ReserveLog>>(), default)
                .Returns(CompensationResult.Compensated());
        })
        .WithActivity<ChargeCard>(mock =>
        {
            mock.ExecuteAsync(Arg.Any<IActivityContext<ChargeArgs>>(), default)
                .Returns(ExecutionResult.Faulted(new PaymentDeclinedException()));
        });

    var slip = new RoutingSlipBuilder()
        .AddActivity("reserve", "reservations", new ReserveArgs("seat-1"))
        .AddActivity("charge", "payments", new ChargeArgs(99.00m))
        .Build();

    var result = await harness.ExecuteAsync(slip);

    result.Status.Should().Be(RoutingSlipStatus.Faulted);
    result.CompensatedActivities.Should().ContainSingle("reserve");
    result.SubscriptionEvents.Should().Contain<RoutingSlipFaulted>();
}
```

### Testing Itinerary Revision

```csharp
[Fact]
public async Task Activity_can_revise_itinerary()
{
    var harness = RoutingSlipTestHarness.Create()
        .WithActivity<Validate>(mock =>
        {
            mock.ExecuteAsync(Arg.Any<IActivityContext<ValidateArgs>>(), default)
                .Returns(ExecutionResult.Revised(rev =>
                {
                    rev.AddActivity("approval", "approvals", new { });
                    rev.AddActivitiesFromSourceItinerary();
                }));
        })
        .WithActivity<Approval>(mock =>
        {
            mock.ExecuteAsync(Arg.Any<IActivityContext<ApprovalArgs>>(), default)
                .Returns(ExecutionResult.Completed());
        })
        .WithActivity<Process>(mock =>
        {
            mock.ExecuteAsync(Arg.Any<IActivityContext<ProcessArgs>>(), default)
                .Returns(ExecutionResult.Completed());
        });

    var slip = new RoutingSlipBuilder()
        .AddActivity("validate", "validation", new ValidateArgs(true))
        .AddActivity("process", "processing", new ProcessArgs())
        .Build();

    var result = await harness.ExecuteAsync(slip);

    result.Status.Should().Be(RoutingSlipStatus.Completed);
    result.ExecutedActivities.Select(a => a.Name).Should()
        .BeEquivalentTo(["validate", "approval", "process"],
            o => o.WithStrictOrdering());
}
```

## Full Example: Document Processing Pipeline

```csharp
// Activities

public sealed record ValidateDocArgs
{
    public required string DocumentId { get; init; }
    public required string DocumentType { get; init; }
}

public sealed record ValidateDocLog
{
    public required string ValidationId { get; init; }
}

public sealed class ValidateDocument : IActivity<ValidateDocArgs, ValidateDocLog>
{
    public async ValueTask<ExecutionResult> ExecuteAsync(
        IActivityContext<ValidateDocArgs> ctx, CancellationToken ct)
    {
        var validator = ctx.Services.GetRequiredService<IDocValidator>();
        var result = await validator.ValidateAsync(ctx.Arguments.DocumentId, ct);

        ctx.SetVariable("IsValid", result.IsValid);
        ctx.SetVariable("NeedsEnrichment", result.MissingFields.Length > 0);

        // Dynamically add enrichment step if needed
        if (result.MissingFields.Length > 0)
        {
            return ExecutionResult.Revised(rev =>
            {
                rev.AddActivity("enrich", "doc-enrichment",
                    new { ctx.Arguments.DocumentId, Fields = result.MissingFields });
                rev.AddActivitiesFromSourceItinerary();
            });
        }

        return ExecutionResult.CompletedWithLog(
            new ValidateDocLog(result.ValidationId));
    }

    public async ValueTask<CompensationResult> CompensateAsync(
        ICompensationContext<ValidateDocLog> ctx, CancellationToken ct)
    {
        var validator = ctx.Services.GetRequiredService<IDocValidator>();
        await validator.RevokeValidationAsync(ctx.Log.ValidationId, ct);
        return CompensationResult.Compensated();
    }
}

public sealed class NotifyCompletion : IExecuteActivity<NotifyArgs>
{
    public async ValueTask<ExecutionResult> ExecuteAsync(
        IActivityContext<NotifyArgs> ctx, CancellationToken ct)
    {
        var docId = ctx.GetVariable<string>("DocumentId");
        var mailer = ctx.Services.GetRequiredService<INotificationService>();
        await mailer.SendAsync($"Document {docId} processed", ct);
        return ExecutionResult.Completed();
    }
}

// Creator — builds a dynamic pipeline based on document type

public sealed class DocumentProcessor(IRoutingSlipExecutor executor)
{
    public async Task ProcessAsync(Document doc, CancellationToken ct)
    {
        var builder = new RoutingSlipBuilder()
            .WithName("document-processing")
            .SetVariable("DocumentId", doc.Id)
            .SetVariable("DocumentType", doc.Type)
            .AddActivity("validate", "doc-validation",
                new ValidateDocArgs
                {
                    DocumentId = doc.Id,
                    DocumentType = doc.Type,
                });

        // Dynamic pipeline based on document type
        foreach (var step in GetPipelineSteps(doc.Type))
        {
            builder.AddActivity(step.Name, step.Destination, step.Arguments);
        }

        // Always notify at the end
        builder.AddActivity("notify", "notifications",
            new NotifyArgs { Recipient = doc.Owner });

        builder.AddSubscription("doc-tracking",
            RoutingSlipEvents.Completed | RoutingSlipEvents.Faulted);

        await executor.ExecuteAsync(builder.Build(), ct);
    }

    private static IEnumerable<ActivityStep> GetPipelineSteps(string docType) =>
        docType switch
        {
            "invoice" =>
            [
                new("extract-fields", "invoice-ocr", new { }),
                new("match-po", "po-matching", new { }),
                new("approve", "approval-service", new { }),
            ],
            "contract" =>
            [
                new("extract-clauses", "contract-ai", new { }),
                new("legal-review", "legal-queue", new { }),
            ],
            _ =>
            [
                new("generic-process", "doc-processing", new { }),
            ],
        };
}
```

## Package Structure

Following the abstraction + provider pattern:

| Package | Depends On | Contents |
|---------|-----------|----------|
| `Headless.RoutingSlips.Abstractions` | `Headless.Messaging.Abstractions` | IActivity, IExecuteActivity, IActivityContext, ICompensationContext, IRoutingSlipBuilder, IItineraryBuilder, IRoutingSlipExecutor, ExecutionResult, CompensationResult, RoutingSlip, RoutingSlipStatus, event types |
| `Headless.RoutingSlips` | `Headless.RoutingSlips.Abstractions`, `Headless.Messaging.Core` | RoutingSlipBuilder, ActivityHost, compensation engine, slip serialization, consumer registration |
| `Headless.RoutingSlips.OpenTelemetry` | `Headless.RoutingSlips.Abstractions` | Metrics + traces instrumentation |
| `Headless.RoutingSlips.Testing` | `Headless.RoutingSlips.Abstractions` | RoutingSlipTestHarness, in-memory activity host, assertion helpers |

No database tables — routing slips are message-carried. No storage provider integration needed.

## Key Decisions

1. **Message-carried state** — no central routing-slip execution state store; runtime progress is carried in the message. No routing-slip-specific persistence model. The underlying messaging outbox/broker durability still applies.
2. **Typed activity contract** — `IActivity<TArgs, TLog>` for full activities, `IExecuteActivity<TArgs>` for execute-only. Typed `TLog` gives compile-time safety for compensation data.
3. **`JsonElement` variables** — variables serialized as `JsonElement` values, not `object?`. Avoids type-loss across service boundaries. `GetVariable<T>()` deserializes on demand. Case-sensitive keys, last-write-wins, null allowed.
4. **String destination addressing** — consistent with saga `Command()` steps and `IMessagePublisher`. Activities are location-transparent.
5. **Full itinerary revision** — `ExecutionResult.Revised(Action<IItineraryBuilder>)` with `AddActivity()` + `AddActivitiesFromSourceItinerary()`. Activities can prepend, append, remove, or reorder remaining steps.
6. **Event subscriptions** — targeted delivery to registered endpoints, not broadcast. Creator registers subscriptions at build time. Subscribers are standard `IConsume<T>` consumers.
7. **Independent from sagas** — separate packages, no cross-dependency. Composition via manual wiring (saga `Command()` + subscription events). No first-class `.RoutingSlip()` step type.
8. **Separate packages** — `Headless.RoutingSlips.Abstractions` + `Headless.RoutingSlips` + `.OpenTelemetry` + `.Testing`. Depends on Messaging only.
9. **Explicit activity registration** — `AddActivitiesFromAssembly()` or `AddActivity<T>(destination)`. Explicit destination overrides `[ActivityDestination]` attribute. No destination → startup exception.
10. **Full compensation** — reverse-order execution using stored `TLog`. Execute-only activities (no `TLog`) skipped during compensation. On compensation failure, publish `RoutingSlipCompensationFailed` and stop.
11. **Terminate = finish early** — `ExecutionResult.Terminated()` discards remaining itinerary. No compensation. No fault. Subscribers receive `RoutingSlipCompleted`. Semantically distinct from cancellation.
12. **Compensation is read-only for variables** — activities in compensation cannot mutate slip variables. Prevents compensation from affecting other compensations.
13. **Revision counter in wire format** — `RoutingSlip.RevisionCount` incremented on each `ReviseItinerary()` for auditability.
14. **Full v1 scope** — activities, builder, variables, runtime revision, event subscriptions, compensation, testing harness. No features deferred.
15. **Static factory results** — `ExecutionResult` and `CompensationResult` are sealed classes with private constructors and static factories. No leaky internal state. Activities use `ExecutionResult.Completed()`, not `ctx.Completed()`.
16. **No `CompensationLogType` on wire** — the activity host knows `TLog` from its `IActivity<TArgs, TLog>` registration. No CLR type name in the wire format. Avoids type renaming/namespace fragility.
17. **Optional slip `Name`** — business identifier for metrics/logging, set via `WithName()`. Not required. Metrics use `slip_name` tag when set, `tracking_id` otherwise.
18. **Revision validation** — activity/destination names non-empty, `AddActivitiesFromSourceItinerary()` at most once, framework does not prevent cycles (user responsibility), duplicate activity names allowed.
19. **`RoutingSlipStatus` enum** — five terminal states: `Running`, `Completed`, `Faulted`, `CompensationFailed`, `Terminated`. Formally defined in the public contract.
20. **Message size awareness** — routing slips grow as logs accumulate. Suitable for moderate pipelines (3-15 activities) with compact logs. Large payloads should be stored externally and referenced by ID.

## Open Questions

None — all questions resolved.
