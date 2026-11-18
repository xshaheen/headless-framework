---
domain: Messaging
packages:
---

# Publishing

> Part of [Messaging](../messaging.md). How a publish is stored, enlisted, scheduled, expired, addressed, and stamped with a tenant.

## Delivery Modes

Two questions decide how a publish behaves, and two different things answer them:

- **Durability** is `DeliveryMode`, an option. `Durable` (the default) stores the row first and the relay dispatches it. `Direct` sends straight to the transport and stores nothing.
- **Enlistment** is the receiver you call, not an option. `IBus` and `IQueue` never enlist: their rows are standalone and survive the caller's rollback, whatever unit of work is open. `unit.Outbox` always enlists: its row is written inside that unit's transaction and discarded with it.

No per-call value, per-type policy, or host default moves a publish from one receiver to the other. The framework resolves the path before any effect and throws rather than grant a weaker guarantee. Every throw below happens before any storage or transport effect.

| Receiver | A unit of work the storage can join | A unit of work the storage cannot join | No unit of work |
| --- | --- | --- | --- |
| `IBus` / `IQueue`, `Durable` (default) | Standalone durable row; the unit is never consulted | Standalone durable row | Standalone durable row, dispatched by the relay |
| `IBus` / `IQueue`, `Direct` | Straight to the transport, no storage | Same | Same |
| `unit.Outbox` (always durable) | Row inside the unit's transaction, dispatched after commit, discarded on rollback | **Throws** before any effect | Not reachable: the unit is the receiver |

### Choose the delivery mode

- **Precedence.** The per-call `PublishOptions.DeliveryMode` or `QueueOptions.DeliveryMode` wins. Then the per-type, per-lane policy declared on the contract: `m.Message<T>(...).OnBus(b => b.WithDeliveryMode(...))` or `.OnQueue(q => q.WithDeliveryMode(...))`. Then `MessagingOptions.DefaultDeliveryMode` (`Durable`). An unset per-call mode inherits, including omitted, `null`, metadata-only, and fluent options.
- **`Direct` rejects `Delay` and `ScheduledAt`**, because scheduling needs storage.
- **The default costs one storage write.** A `Durable` publish pays a storage write and a relay hop per message. For high-rate events that may be lost, opt out per type and lane with `.OnBus(b => b.WithDeliveryMode(DeliveryMode.Direct))` (or `.OnQueue(...)`), or per host with `DefaultDeliveryMode = DeliveryMode.Direct`. `Direct` gives up durability and atomicity and returns a receipt with no `StorageId`.
- **An enlisted publish consults none of these.** `OutboxOptions` carries no delivery mode, and `unit.Outbox` is `Durable` by construction. `Delay` and `ScheduledAt` still work on it.
- **Storage is mandatory.** A host with no storage fails startup validation.

### Publish atomically with your data

Use `unit.Outbox` when the message must commit or roll back with the application's own writes:

1. Install `Headless.Messaging.Storage.PostgreSql.EntityFramework` or `Headless.Messaging.Storage.SqlServer.EntityFramework`, and select `setup.UsePostgreSqlEntityFramework<TContext>()` or `setup.UseSqlServerEntityFramework<TContext>()`. The raw `UsePostgreSql(...)` and `UseSqlServer(...)` storages join a raw-ADO unit of work the same way.
2. Open the unit of work yourself: `factory.RunAsync(db, ...)` from `Headless.UnitOfWork.EntityFramework`, or a manual `BeginAsync` / `CompleteAsync` pair. Nothing opens it for you.
3. Publish through that unit: `unit.Outbox.PublishAsync(...)` (Bus) or `unit.Outbox.EnqueueAsync(...)` (Queue).

An `IBus.PublishAsync` inside the same block still writes a standalone row that survives a rollback. A `HeadlessDbContext` save pipeline enlists its own unit around each `SaveChangesAsync()` for domain and integration events; a plain `DbContext` gets no such wrapping. The outbox is an atomicity guarantee for the write, not exactly-once delivery: delivery stays at-least-once, so consumers deduplicate by business key or message id.

`unit.Outbox` is an extension property on `IUnitOfWork` in the `Headless.UnitOfWork` namespace. It throws `InvalidOperationException` naming `AddHeadlessMessaging` when the host registered no messaging, and a completed or rolled-back unit refuses it.

**Verify:** in a test, `MessagingTestHarness.RunInUnitOfWorkAsync` publishes through `unit.Outbox`, and the recorded message has `RecordedMessage.IsCoordinated == true`; see [test-harness.md](test-harness.md#headlessmessagingtesting).

### Which units a storage can join

- **Relational storages** join only a unit whose live transaction is on the same database, judged by the same database-identity check the Jobs store uses (see [unit-of-work.md § Database identity](../unit-of-work.md#database-identity-and-several-databases)).
- **In-memory storage** joins any active unit, including a resource-less one from `IUnitOfWorkFactory.BeginAsync()` (test hosts). The row waits on the unit and is stored only after the unit commits; a rollback publishes nothing.
- **Additional outboxes.** `unit.Outbox` writes to the outbox whose database matches the unit; see [storage.md § Additional outboxes](storage.md#additional-outboxes).
- **A refused enlistment throws** `Publishing 'OrderPlaced' cannot join the active unit of work ({Mismatch}): {detail}. {advice}`, for example when the unit exposes no relational resource for the messaging database, is on another database, has a completed transaction, or belongs to another provider. Only `unit.Outbox` can throw it: `IBus` and `IQueue` hand the storage no unit.
- **There is no startup gate for enlistment.** `IUnitOfWorkFactory` is always registered: `AddHeadlessMessaging`, `AddHeadlessJobs`, `AddHeadlessDbContextServices`, and the `Headless.UnitOfWork.*` setups all call the idempotent `AddUnitOfWork()`.

### Guard against autonomous publishes

No host-wide setting forbids an un-enlisted publish. Reading `unit.Outbox.PublishAsync(...)` at the call site proves enlistment. For a build-time guard, reference `Headless.UnitOfWork.Analyzers`: [HF2001](../unit-of-work.md#hf2001) reports an `IBus.PublishAsync` or `IQueue.EnqueueAsync` made while a unit of work is in scope, and its code fix rewrites the call to `unit.Outbox`. It is a suggestion by default; set `dotnet_diagnostic.HF2001.severity = error` in `.editorconfig` to fail the build (see [Severity and suppression](../unit-of-work.md#severity-and-suppression)).

HF2001 reports nothing where no unit is in scope. To forbid autonomous publishes in a whole assembly, ban the `IBus` and `IQueue` types with `Microsoft.CodeAnalysis.BannedApiAnalyzers` and a `BannedSymbols.txt` `AdditionalFiles` item that holds exactly:

```text
T:Headless.Messaging.IBus;Publish through unit.Outbox in this assembly — IBus never joins the transaction.
T:Headless.Messaging.IQueue;Enqueue through unit.Outbox in this assembly — IQueue never joins the transaction.
```

### Execution-strategy replay

- **Inside `factory.RunAsync(db, ...)`** a transient failure replays your whole block with a fresh transaction and unit. The first attempt's row rolls back, and the replay publishes again. A `RunAsync` block that publishes through `unit.Outbox` stays retriable under `EnableRetryOnFailure`.
- **A `HeadlessDbContext` save that owns its unit** (no caller transaction) ends replay once any event handler returns. A `unit.Outbox` publish from a domain-event handler there calls `IUnitOfWork.PreventRetry()` before it writes, so that save surfaces its fault without replay. Retry it with a fresh context and aggregate graph.
- **Integration events an entity emitted** stay retriable: the save pipeline keeps and re-publishes them on a replayed attempt. A save that mixes emitted integration events and a `unit.Outbox` publish from a handler is not retriable. See [Headless.EntityFramework.Messaging](../orm.md#headlessentityframeworkmessaging).
- **A `SaveChangesAsync` inside your own block that dispatched domain or integration events** also ends replay, because it clears the aggregate's events.
- **`IUnitOfWork.OnCompleted` is savepoint-blind.** A registration made inside a savepoint that is later rolled back still runs when the outer transaction commits. Publish after the last partial rollback. See [Unit of Work](../unit-of-work.md).

### Hand-off after commit

Once a durable row is stored (the standalone write, or the unit's commit for `unit.Outbox`), the publish hands the message to this host's in-process sender so it goes out without waiting for the relay. The hand-off channel holds `Environment.ProcessorCount * 500` messages.

- When a burst fills the channel, the publish waits up to about 100 ms of real time for a slot. If none frees, it drops the hand-off and returns normally. The row is committed, so the relay sends it after `RetryPolicy.InitialDispatchGrace` (default 30 s) plus up to one retry poll (`RetryProcessor.BaseInterval`, default 60 s).
- The bound is per operation: a unit that commits N messages against a full channel waits about 100 ms in total, and `IUnitOfWork.CompleteAsync` returns after that wait.
- The publish's cancellation token does not cover this wait, because the publish already succeeded. A stopping host drops the hand-off at once.
- Delayed and scheduled publishes never wait. One due within two minutes goes to the scheduler queue when it has room; a later one stays in storage until the delayed processor claims it.

### Delivery telemetry

`headless.messaging.delivery.requested` and `headless.messaging.delivery.resolved` carry `durable` or `direct` only. Whether a row was written inside a transaction travels in the reserved `headless-delivery-coordinated` header as `"true"` or `"false"`. An absent header means unrecorded, never "not coordinated". The dashboard shows it as "Transaction coordinated" (`Yes` / `No` / `Not recorded`), `MessageView.IsCoordinated` is the monitoring projection, and `RecordedMessage.IsCoordinated` exposes it to tests; all three are `bool?`.

Jobs follows the same rule: `unit.Jobs` enlists and the injected `IJobScheduler` never does. See [Enlisted Enqueue](../jobs/atomic-enqueue.md#enlisted-enqueue-atomic-enqueue).

## Delivery deadline (`DeliverWithin`)

Set `MessageOptions.DeliverWithin` when a message is worthless after a period, such as a price quote, a one-time-code notification, or a cache invalidation for a short-lived key. Past the deadline the message is dropped instead of delivered, visibly. `PublishOptions`, `QueueOptions`, and `OutboxOptions` all carry it, and the builders have `WithDeliverWithin(TimeSpan?)`.

```csharp
await publisher.PublishAsync(message, new PublishOptions { DeliverWithin = TimeSpan.FromMinutes(2) }, cancellationToken);
await queue.EnqueueAsync(message, o => o.WithDeliverWithin(TimeSpan.FromSeconds(30)), cancellationToken);
```

- **The deadline is absolute and stamped once.** The publisher's registered `TimeProvider` adds `DeliverWithin` to the time the publish builds its envelope (after publish middleware) and writes it as the reserved `headless-deliver-by` header (`Headers.DeliverBy`, UTC ISO 8601). Retries and redeliveries read that one instant; nothing extends it.
- **Validated at publish, before any side effect.** A non-positive `DeliverWithin` throws `ArgumentOutOfRangeException`. A `Delay` or `ScheduledAt` due at or after the deadline throws `ArgumentException`. A shorter delay is fine: the deadline counts from publish time.
- **Each side checks it on its own clock.** Clock skew shifts the window, so keep host clocks synchronized and leave headroom in short deadlines. An unreadable `headless-deliver-by` value (a foreign producer's) is ignored.

| Situation | Past the deadline |
| --- | --- |
| The relay reaches a stored published row: first dispatch, a delayed or scheduled row coming due, a persisted retry, or a crash-recovered attempt | Not sent. The row becomes terminal `Failed` with `MessageDeliverByExceededException` (`deliver_by_exceeded: ...`) in the stored envelope's `headless-exception` header. No retry. `OnTerminal` fires with `DeliveryDeadlineExpired`. Counted on `headless.messaging.send.expired`; logged at Information (EventId 4125) |
| `Direct` send | Not sent; the call still returns a receipt with the message id. No row, so no `OnTerminal`. Counted on `headless.messaging.send.expired`; logged (EventId 4128) |
| A competing consumer receives the delivery | Committed and dropped before receive middleware and inbox admission: no row, no handler, no `OnTerminal`, no circuit-breaker report. `headless.messaging.receive.outcomes` with `expired`; logged (EventId 4126) |
| An every-instance consumer receives the delivery | Committed and dropped. `receive.outcomes` `expired`, plus `headless.messaging.every_instance.deliveries` `dropped`; logged (EventId 4126). A rate-limited consumer checks again after its permit wait and drops the same way (EventId 4132) |
| A stored inbox row starts an attempt | The consumer does not run. The row becomes terminal `Failed`; `OnTerminal` fires with `DeliveryDeadlineExpired`; no circuit-breaker report. Counted on `headless.messaging.inbox.terminal` with outcome `FailedExpired`; logged (EventId 4127) |
| An operator force-reprocesses a received message | The replayed generation drops `headless-deliver-by` and runs: the operator works past the deadline on purpose |

- **Dropped rows are `Failed` with a reason**, not a separate status. Their `FailureFingerprint` is `v1:expired`. Requeuing a dropped published row does not deliver it, because the stored deadline still applies. To run a dropped received message anyway, force-reprocess it.
- **Native broker TTL.** RabbitMQ and Azure Service Bus receive the remaining time at send, so an unconsumed message expires inside the broker. The framework never sees a broker expiry, so watch the broker's own metrics. With RabbitMQ dead-lettering on, or a Service Bus entity that dead-letters on expiry, an expired message goes to the dead-letter queue. RabbitMQ expires a message only at the head of its queue, so behind a backlog the consumer check drops it. Kafka, Redis Streams, NATS JetStream, Amazon SQS, and the in-memory transport get no broker TTL; the relay and consumer checks still apply.
- **Requests use their own deadline.** `RequestOptions` has no `DeliverWithin`; see [request-reply.md § Deadlines](request-reply.md#deadlines-timeouts-and-clock-skew). A `DeliverWithin` that publish middleware sets on a request is ignored.

## Scheduling and revocation

- **`Delay` and `ScheduledAt` are mutually exclusive one-shot schedules.** Both need storage, so they work on every durable publish, enlisted or not, and `Direct` rejects them before any side effect. A delay must be positive.
- **`ScheduledAt` accepts past instants** and is stored in UTC at microsecond precision. Dispatch is best effort after that not-before instant, with no upper latency bound.
- **Revoke with `IMessageRevoker.RevokeAsync`.** Keep `PublishReceipt.StorageId` and revoke after the publishing transaction commits. Outcomes:
  - `Revoked`: deletion won before the first dispatch attempt was reserved.
  - `NotFound`: no matching row in the configured storage.
  - `AttemptReserved`: dispatch, terminal, or retry state prevented deletion. It is not proof of delivery.
- Canceling the publish token cancels the request, not an accepted message. A storage without revocation support throws `NotSupportedException` naming the provider.
- **Revocation has no audit record and no tenant filter.** Authorize access to storage ids in your application.
- **Use Jobs for anything richer.** Keyed identity, replace, reschedule, tenant scoping, and transactional business deadlines belong to Jobs. See [Enlisted Enqueue (Atomic Enqueue)](../jobs/atomic-enqueue.md#enlisted-enqueue-atomic-enqueue).

Operators revoke or dispatch a scheduled row now, with an audit receipt, through `IScheduledDeliveryOperationsApi`; see [operations.md § Operator APIs](operations.md#operator-apis).

## Publish options and receipts

`IBus.PublishAsync` and `IQueue.EnqueueAsync` return `Task<PublishReceipt>`. Await the task to read it:

```csharp
var receipt = await publisher.PublishAsync(message, cancellationToken);
```

- `PublishReceipt.MessageId` is the resolved wire message id. `StorageId` is the durable row id, or `null` for `Direct` delivery.
- Publish middleware that suppresses the message before it is published returns both values `null`.
- A receipt from `unit.Outbox` is still subject to that unit's completion or rollback. No receipt implies that a consumer ran.
- Durable acceptance waits for storage, not for a consumer. Restart survival needs persistent storage.

### Options records and builders

Pass a `PublishOptions` or `QueueOptions` record before the cancellation token, or a builder callback:

```csharp
public sealed class OrderEvents(IBus bus)
{
    public Task PublishWithMetadataAsync(OrderPlaced message, string correlationId, CancellationToken cancellationToken)
    {
        return bus.PublishAsync(message, options => options
            .WithHeader("source", "checkout")
            .WithCorrelationId(correlationId), cancellationToken);
    }
}
```

- `PublishOptionsBuilder` and `QueueOptionsBuilder` (namespace `Headless.Messaging`) offer `WithHeader`, `WithHeaders`, `WithCorrelationId`, `WithCausationId`, `WithMessageId`, `WithTenantId`, `WithDelay`, `WithScheduledAt`, `WithDeliverWithin`, and `Build`. Set `DeliveryMode` and other advanced values on the record, or with `builder.Build() with { ... }`.
- A callback runs synchronously, exactly once, on a fresh builder. Async-void callbacks are unsupported. A throwing callback submits nothing.
- A builder supports sequential reuse, not concurrent mutation. Header input is copied on each call and on each `Build()`. Keys are ordinal and the last write wins.
- `Build()` leaves `DeliveryMode` `null`, so the publish inherits the policy. It does not validate; the publisher validates headers, tenancy, and positive delays (zero is invalid).
- Use the named arguments `options:` and `configure:` to make intent explicit when you pass `null` or `default`.

`IBus` and `IQueue` are singletons that never look for a unit of work, so any singleton or hosted service can take them directly. A custom `IBus` or `IQueue` implementation or test double returns a `PublishReceipt`.

## Envelope, headers, and correlation

Every message carries framework headers: message id, message name, contract version, type, sent time, lane, root correlation id, optional causation id, and optional tenant id.

### Reserved and custom headers

- **Reserved names are rejected** in custom publish headers, `RequestOptions.Headers`, and provider contributions: `MessageId`, `ContractVersion`, `CorrelationId`, `CausationId`, `CorrelationSequence`, `CallbackName`, `MessageName`, `Type`, `SentTime`, `DelayTime`, `DeliverBy` (`headless-deliver-by`), `Intent`, `RequestId` (`headless-request-id`), `ReplyTo` (`headless-reply-to`), `RequestDeadline` (`headless-request-deadline`), `InReplyTo` (`headless-in-reply-to`), `ReplyStatus` (`headless-reply-status`), and `TransportAddress` (`headless-transport-address`), as named on the `Headers` class.
- **Never write `Headers.TenantId` directly.** Set `MessageOptions.TenantId` or use ambient tenancy; see [Strict Publish Tenancy](#strict-publish-tenancy).
- **A header name** must be 1 to 255 printable ASCII characters (`!` through `~`) other than `:`, which every transport can carry. Any other name throws `InvalidOperationException` at the publish call, so an outbox row never fails on every retry later.
- **Header values** reject control characters before publish, including explicit `MessageId`, `CorrelationId`, `CallbackName`, and `TenantId`.
- **`PublishOptions.MessageName`** follows the same validator as registered message names.
- **Contract version.** `Message<T>(name, version)` sets it (default `"1"`). `PublishOptions.ContractVersion` overrides it for one send. Consumers check it before deserialization and expose it as `ConsumeContext.ContractVersion`; a missing header reads as `"1"`, except on a `RawPayload()` lane, which fills in the contract's own version.

### Correlation and causation

1. `PublishOptions.CorrelationId` wins.
2. Otherwise the contract's `CorrelateBy(...)` selector runs on the payload.
3. Otherwise a publish inside a consumer keeps the ambient `ConsumeContext.CorrelationId`.
4. Otherwise the message id becomes the root correlation id.

`PublishOptions.CausationId` sets the immediate parent; otherwise a publish inside a consumer uses that consumer's message id. Consumers read it from `ConsumeContext.CausationId`.

`MessageOptions.SuppressAmbientBusinessContext = true` turns off the ambient correlation, causation, and tenant defaults, so a forwarder can replay a captured snapshot. Explicit options and the contract's selector still apply, and trace propagation is unchanged.

### Transport address

`ConsumeContext.TransportAddress` is the native address a delivery arrived on: the NATS subject (shard token included), the Kafka topic, the RabbitMQ routing key, the Azure Service Bus entity path, the Amazon SQS queue URL, or the Redis stream key. It is `null` on the in-memory transport. Read routing tokens such as a tenant or run id from it, not from the payload. The consumer client stamps it on receipt and overwrites any wire value, so a producer cannot choose it, and a retry from storage sees the original address.

### Callbacks

Set `CallbackName` on `PublishOptions` or `QueueOptions` to have the consumer publish a response with `SetResponse<TResponse>`. The response always goes out as a durable Bus publish, including for a Queue-originated message. A callback is fire-and-forget and at-least-once, so response consumers must be idempotent, and a Bus request that fans out gets one response per subscriber. When the caller must wait for the answer, use [request/reply](request-reply.md#requestreply).

## Strict Publish Tenancy

Strict publish tenancy makes every publish resolve a tenant. It is off by default. Turn it on, together with tenant propagation, through the root tenancy setup:

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http => http.ResolveFromClaims()) // the ambient tenant that propagation captures
        .Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
);
```

Configure transport and storage with `AddHeadlessMessaging(...)`, and tenancy only through `AddHeadlessTenancy(...)`. `MessagingOptions.TenantContextRequired` reports the setting and has no public setter.

A guarded publish resolves its tenant in this order:

1. `PublishOptions.TenantId` when set.
2. Otherwise the ambient `ICurrentTenant.Id`, unless `SuppressAmbientBusinessContext` is set.
3. Otherwise the publish throws `Headless.MultiTenancy.MissingTenantContextException`, unless it is a system message.

`MissingTenantContextException` derives from `Exception`, not `InvalidOperationException`. Catch it directly to map it to an HTTP 4xx or to stop retries. A raw `headless-tenant-id` header written without `TenantId` throws `InvalidOperationException` whether or not strict tenancy is on, so turning the setting on cannot bypass that check.

**System messages.** `MessageOptions.IsSystemMessage = true` lets a publish with no explicit and no ambient tenant go out tenantless instead of throwing. It never removes a resolved tenant, and a system message that sets `SuppressAmbientBusinessContext` while an ambient tenant exists is still rejected. The framework sets it on its own messages (cache invalidations, lock release signals, and settings, features, and permissions change announcements). Leave it unset on application messages. It mirrors `JobOptions.IsSystemJob`.

**Background workers** have no ambient HTTP tenant. Pass the tenant explicitly, or scope it:

```csharp
// Option A: pass the tenant explicitly
await publisher.PublishAsync(message, new PublishOptions { TenantId = tenantId }, cancellationToken);

// Option B: scope the AsyncLocal accessor before publishing
using (currentTenant.Change(tenantId))
{
    await publisher.PublishAsync(message, cancellationToken);
}
```

**Verify:** a test that publishes with no tenant asserts `MissingTenantContextException`. See [multi-tenancy.md § Message Consumers](../multi-tenancy/background-work.md) for the trust boundary, and [middleware.md § Tenant propagation](middleware.md#tenant-propagation) for the consume side.
