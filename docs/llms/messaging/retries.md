---
domain: Messaging
packages:
---

# Messaging retries and failure handling

> Part of [Messaging](../messaging.md). How consume and publish failures retry, when a message ends terminally, how the circuit breaker pauses a consumer, and what the retry lock and dead-owner recovery do.

Messaging retries in two directions with separate settings. A failed consume attempt follows the consumer's failure policy. A failed publish follows `MessagingOptions.RetryPolicy.RetryStrategy` and `MaxPersistedRetries`. Changing the publish settings does not change how a consumer retries. Both share the dispatch lease, the storage clock (see [Storage clock and leases](storage.md#storage-clock-and-leases)), and the `RetryPolicy.OnTerminal` callback.

## Consumer failure policy

A competing consumer names a `FailurePolicy` type on its attribute. The policy model, its delay math, and how it compares with Jobs are in [reliability.md](../reliability.md).

```csharp
using Headless.Reliability;

public sealed class PaymentsFailurePolicy : FailurePolicy
{
    protected override void Configure(FailurePolicyBuilder policy) =>
        policy
            .Immediate(retries: 1)
            .Delayed(retries: 6, initialDelay: TimeSpan.FromMinutes(1), maxDelay: TimeSpan.FromMinutes(30))
            .FailOn<PaymentDeclinedException>();
}

public sealed class PaymentDeclinedException(string message) : Exception(message);

[QueueConsumer("payments.charge-order", FailurePolicy = typeof(PaymentsFailurePolicy))]
public sealed class ChargeOrder : IConsume<PlaceOrder>
{
    public ValueTask ConsumeAsync(ConsumeContext<PlaceOrder> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
```

The host sets the default for consumers that declare nothing, and replaces one consumer's policy by identity:

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage ...
    setup.DefaultFailurePolicy(policy =>
        policy.Immediate(1).Delayed(3, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10))
    );
    setup.Tune(
        "orders.projection",
        consumer => consumer.FailurePolicy(policy => policy.Delayed(10, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1)))
    );
});
```

**Verify:** the `describe` output shows each consumer's `failurePolicy` (`immediateRetries`, `delayedRetries`, and the delay fields). See [Host manifest](operations.md#host-manifest).

### Resolution

- `Tune(identity, c => c.FailurePolicy(...))` replaces the declared policy.
- A consumer with neither gets the host's `setup.DefaultFailurePolicy<TPolicy>()` or `DefaultFailurePolicy(p => ...)`.
- Without that, the framework default applies: 2 immediate retries, then 5 delayed retries from 30 seconds capped at 15 minutes, with no fail rules.
- `Headless:Messaging:Consumers:{identity}:FailurePolicy` then overrides `ImmediateRetries`, `DelayedRetries`, `DelayedInitialDelay`, or `DelayedMaxDelay` of the winner and keeps its fail rules.
- A publisher cannot choose a consumer's policy. One identity has one policy, applied to every message its class handles. Two modules that declare one identity with different policy types fail startup naming both.
- A competing runtime subscription uses the host default. An every-instance consumer has no policy: declaring one is [HM010](../messaging.md#hm010), and tuning or configuring one fails startup.

### Attempts

A failing message gets at most `1 + ImmediateRetries + DelayedRetries` attempts.

- The first dispatch runs the first attempt and then the immediate retries back to back, with no delay.
- Every attempt, immediate retries included, runs the consumer and its consume middleware in a fresh DI scope. A scoped service a failed attempt left dirty, such as a `DbContext` with tracked changes, never carries into the next attempt.
- Each delayed retry is one later dispatch with one attempt. The row is stored with `NextRetryAt` set to the database clock plus the policy's jittered delay. The retry processor polls every `RetryProcessor.BaseInterval` (adaptive), so a delayed retry can run up to one poll interval late. A delay shorter than the poll interval is not honored precisely.
- Each row is judged against its own consumer's current policy. A row whose retry count already exceeds a policy that shrank since it was scheduled ends as a spent budget without running the handler. A dispatch that crashed after reserving its final attempt ends the same way, so a crash loop cannot exceed the budget.

With the framework default, a message that keeps failing runs 3 attempts at once, then 5 more after about 30 s, 60 s, 120 s, 240 s, and 480 s (each ±20%, each up to one poll interval late), then fails terminally: 8 attempts.

### Before the inbox admits a delivery

The policy counts attempts of a stored row, so it does not apply before admission.

- When the inbox row cannot be written, for example while the messaging database is down, the consumer retries the write in process three times, about 250 ms, 500 ms, and 1 s apart (jittered, under two seconds total), and logs EventId 4134 (`InboxAdmissionRetrying`) for each.
- If the write still fails, the delivery goes back to the broker before any attempt runs. The redelivery delay starts at about 1 s and doubles, up to 30 s, for each further delivery in a row the client could not admit. It starts over once one is admitted.
- NATS (`NAK` delay), SQS (visibility timeout), and Redis (pending-entry idle time, `PendingClaimMinIdleTime`) delay the redelivery in the broker. Azure Service Bus, RabbitMQ, Kafka, and InMemory hold the delivery for the delay before they abandon, requeue, or seek back.
- The failure is reported to the consumer's circuit breaker. When the transient predicate accepts it (the default accepts an unreachable, overloaded, or failing-over database), a lasting outage opens the circuit and pauses the client. A failure the predicate does not accept never closes a half-open circuit.
- The broker still counts each redelivery against its own limit: NATS `MaxDeliver` (no limit by default), the Azure Service Bus max delivery count, or an SQS redrive `maxReceiveCount`. The policy's retry count does not move. Size the broker limit for the storage outage you expect to ride out.

### Classification

- Fail rules see the handler's own exception, unwrapped from the framework wrapper.
- A failure ends at once, skipping remaining retries, when a fail rule matches or the exception is in the built-in permanent set: `ArgumentException` and subtypes, `NotSupportedException`, and `SubscriberNotFoundException`.
- A `FailWhen` predicate that throws counts as a match and is logged (EventId 111, `FailurePolicyRuleThrew`).
- Every other exception is retried.
- A cancellation of the consume token, such as host shutdown, is not a failure. Once that token is cancelled, any `OperationCanceledException` that ends the attempt writes nothing, and the row is picked up again later.
- Shutdown cancels the consume token as soon as it starts, not at the `ShutdownTimeout` deadline. Under the transactional inbox, once the handler has returned, the save, inbox completion, and commit no longer observe the consume token, so a finished attempt commits during shutdown instead of rolling back and running again on the next host. The completion is still fenced on the attempt's lease. A handler that returns early on cancellation therefore commits what it did as a success; throw instead to have the message retried.
- An `OperationCanceledException` raised while the consume token is live is an ordinary failure. That covers an `HttpClient` timeout, a handler-owned `CancelAfter`, and a timeout raised by commit work after the handler, such as a `SaveChanges` interceptor. To end such a cancellation at once, declare `FailOn<OperationCanceledException>()`, which also matches `TaskCanceledException`.

### Failures before the handler runs

Under the `Transactional` inbox guarantee an attempt can fail before the handler runs: opening the connection (a pool timeout when consumer parallelism reaches the pool size), beginning the transaction, or enlisting the unit of work. On the EF storages this counts once the execution strategy gives up. On the raw `UsePostgreSql(...)` and `UseSqlServer(...)` storages it counts on the first fault.

- The attempt hands its reservation back. The retry count stays, no immediate retry runs, and the row is rescheduled after about 1 s, doubling per such failure up to 5 minutes (±20% jitter).
- The attempt logs EventId 133 (`InboxAttemptNotStartedRescheduled`, Warning), records the fault as the row's failure text, and does not count toward a circuit.
- Each message stores how many attempts failed this way (`MediumMessage.PreEntryFailures`), and the count never resets while the row lives.
- Once the count reaches `RetryPolicy.MaxPreEntryFailures` (default 10, range 0 to 100), every later pre-entry failure counts like a handler failure: fail rules see the fault, and the policy decides. A fault that never clears, such as a wrong connection string, therefore ends the message `Failed` after about 14 minutes of reschedules plus the policy's own attempts, and `OnTerminal` fires with `RetriesExhausted`, or `PermanentFailure` when a fail rule matches.
- Set `MaxPreEntryFailures` to 0 to count every pre-entry failure against the budget. A request (request/reply) always counts it.

### A failed success write

When the handler succeeds but writing `Succeeded` throws, for example during a storage outage, the attempt ends without an immediate retry, without `OnTerminal`, and without a circuit-breaker failure, and logs EventId 122 (`ConsumerSucceededButStateWriteFailed`). The retry processor picks the row up once its lease is released or expires, which may run the handler again. Under the `Transactional` guarantee the success write commits with the handler's unit, so its failure rolls back the handler's effects too.

### Terminal failure

A message the policy gives up on is stored as `Failed` with no `NextRetryAt`. There is no separate dead-letter status. It stays until `FailedMessageExpiredAfter` (15 days by default, counted from the terminal write) expires. An operator re-executes it from the Messaging dashboard. A re-executed request runs as a plain Queue message and answers no caller (see [Deadlines](request-reply.md#deadlines-timeouts-and-clock-skew)).

`RetryPolicy.OnTerminal` fires once per terminal consume outcome, only on the node whose terminal write wins:

- the budget is spent (`RetriesExhausted`);
- a fail rule or the built-in permanent set matches (`PermanentFailure`);
- the payload fails to deserialize at execution (`PermanentFailure`);
- the stored message's consumer is no longer registered (`PermanentFailure`);
- the delivery deadline, or a request's response deadline, passed before the attempt (`DeliveryDeadlineExpired`);
- the message is poisoned on arrival (`PermanentFailure`, no storage id). The transport message is dead-lettered with a reason code where the transport supports it (Azure Service Bus, RabbitMQ with a dead-letter exchange, NATS JetStream `AckTerminate`) and committed elsewhere. The poison row is the record the dashboard shows and re-executes.

## Publish retries

`RetryPolicy.RetryStrategy` and `RetryPolicy.MaxPersistedRetries` govern publishing only.

- `RetryStrategy` is a Polly `RetryStrategyOptions`. Its `MaxRetryAttempts` excludes the original execution and controls inline retries. Each attempt is reserved before it runs, so a process crash and recovery do not reset the current inline burst.
- Once the inline budget is spent, Messaging stores `NextRetryAt`, and the retry processor performs up to `MaxPersistedRetries` (default 15) pickups, each with a fresh inline burst. Total attempts are `(RetryStrategy.MaxRetryAttempts + 1) × (MaxPersistedRetries + 1)`. With the defaults (2 and 15) a publish gets 48 attempts.
- A row already past a lowered cap is claimed, written `Failed` without another send, and fires `OnTerminal` once with `RetriesExhausted`.
- A permanent publish failure ends without retry (`PermanentFailure`). A stored lane no registered transport serves ends with `NoTransport`.
- A publish the transport does not confirm within `MessagingOptions.TransportPublishTimeout` is a failed attempt with a `TimeoutException`, retried like any transient failure. The broker may still have taken it, so delivery stays at-least-once. The timeout runs on the back-off clock, not the registered `TimeProvider` (see [Headless.Messaging.Testing](test-harness.md#headlessmessagingtesting)).
- Host shutdown stops a publish only before the sender hands it to the transport. Such a publish is not an attempt: it writes no state, and pickup resumes the row after restart.
- A publish already handed to the transport keeps waiting for the broker's confirm while the host stops, so a publish the broker took is recorded as sent instead of being sent again. The wait ends at `TransportPublishTimeout`. A timeout during shutdown writes no state either, and the row is picked up again once its lease runs out. The lease keeps renewing until the confirm arrives or the timeout fires.
- Keep `TransportPublishTimeout` below `ShutdownTimeout`, and both below the host's shutdown timeout (`HostOptions.ShutdownTimeout`) and the orchestrator's termination grace. A host that stops waiting first abandons the confirm wait, and the row is resent after restart.
- When the broker accepted a publish but the success write matches no row, the sender logs why: EventId 69 `PublishSucceededButStorageTerminal` (Information, row already terminal), 4121 `PublishSucceededButLeaseLost` (Warning), or 4122 `PublishSucceededButStorageRowMissing` (Warning). The consumer still receives the message.
- Startup rejects a `RetryPolicy.DispatchTimeout` (default 5 minutes) that does not exceed the worst-case inline burst: `(RetryStrategy.MaxRetryAttempts + 1) × TransportPublishTimeout` plus each nominal backoff delay capped at `RetryStrategy.MaxDelay`. Jitter and a custom `DelayGenerator` are not counted.

```csharp
using Polly;
using Polly.Retry;

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.Options.RetryPolicy.RetryStrategy = new RetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        MaxDelay = TimeSpan.FromMinutes(5),
        ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Exception is TimeoutException or HttpRequestException
        ),
    };
});
```

When you replace `RetryStrategy`, reuse or compose `RetryPolicyOptions.DefaultShouldHandle` (retry anything that is not a cancellation and not classified permanent), so a custom strategy keeps the built-in classification.

**Verify:** startup throws an options validation error when `DispatchTimeout` is too short for the burst. A row that exhausts its budget logs EventId 4137 `MessageTerminal` with reason `RetriesExhausted`.

## Terminal callback (`OnTerminal`)

`RetryPolicy.OnTerminal` is one callback for every stored message the framework gives up on. It receives a `TerminalMessageInfo` whose `Reason` says why:

| `MessageTerminalReason` | Publish | Consume |
| --- | --- | --- |
| `RetriesExhausted` (0) | The publish budget is spent, or a row is already past a lowered cap | The failure policy budget is spent, or a request's remaining retries cannot finish before its deadline |
| `PermanentFailure` (1) | The retry classification calls the failure non-retryable | A fail rule or the built-in permanent set matches, the payload fails to deserialize, the consumer is no longer registered, a responder returns null, or the message is poisoned on arrival |
| `DeliveryDeadlineExpired` (2) | The row's `DeliverWithin` deadline passed before the relay sent it | The row's delivery deadline, or a request's response deadline, passed before an attempt ran |
| `NoTransport` (3) | No registered transport serves the row's lane | Not applicable |

Handle unrecognized values in a `switch`: more reasons may be added.

- **Once per stored message.** It fires after the terminal write lands, only on the node whose write landed. A racing replica that finds the row already terminal skips it (EventId 66 `SkippingOnTerminalAlreadyTerminal`). Host shutdown is not terminal.
- **Tenant.** `TenantId` is the envelope's `headless-tenant-id` header. Missing, blank, or oversized resolves to `null`. With tenant propagation enabled, `ICurrentTenant` is also set for the callback.
- **Log line.** Every terminal outcome logs EventId 4137 `MessageTerminal` at Error, with storage id, message id, type, reason, tenant, and retry count, whether or not a callback is set.
- **Lifetime.** The callback is bounded by `RetryPolicy.OnTerminalTimeout` (default 30 seconds, at most 1 hour) on the back-off clock. A throw or timeout is logged and absorbed. A timed-out callback's token is cancelled. The token also fires on host shutdown.
- **No stored row, no callback.** A `Direct` send is never stored, and a delivery whose deadline passed before admission is dropped. Neither fires `OnTerminal`. Watch `headless.messaging.send.expired` and the `expired` receive outcome instead.
- **Best effort across crashes.** The terminal write commits first. A process that dies before the callback never fires it, and nothing replays it. Treat it as a notification. The stored `Failed` row is the record.
- **Every-instance consumers** never call it.

`TerminalMessageInfo` has nine required-init properties. Build one in a test or fake like this:

```csharp
var info = new TerminalMessageInfo
{
    ServiceProvider = scope.ServiceProvider, // live per-message dispatch scope: not the root provider, not the handler's scope
    MessageType = MessageType.Subscribe, // or MessageType.Publish
    Message = mediumMessage.Origin, // the message envelope, not the payload
    Lane = MessageLane.Bus, // or MessageLane.Queue
    Reason = MessageTerminalReason.RetriesExhausted,
    TenantId = null, // the envelope's tenant header, or null
    Exception = ex, // the exception that ended delivery; null when none did
    StorageId = mediumMessage.StorageId, // storage row identifier for DLQ correlation; Guid.Empty when poisoned on arrival
    RetryCount = mediumMessage.Retries, // final persisted-retry count
};
```

`ServiceProvider` is the per-message dispatch scope, not the handler's. Each attempt runs the consumer in its own scope, which ends with the attempt. A scoped service resolved through `TerminalMessageInfo.ServiceProvider` is never the instance the handler used and takes no part in its transaction. A message poisoned on arrival gets a fresh scope.

## Retry processor polling

`MessagingOptions.RetryProcessor` controls the persisted-retry processor's cadence:

| Property | Default | Effect |
| --- | --- | --- |
| `BaseInterval` | 60 s | Base polling interval |
| `AdaptivePolling` | `true` | Halves the interval on healthy cycles and doubles it when the circuit-open skip rate exceeds the threshold |
| `MaxPollingInterval` | 15 min | Cap on adaptive doubling |
| `CircuitOpenRateThreshold` | 0.8 | Fraction of circuit-open skips above which the processor backs off |

Published-Bus, Published-Queue, Received-Bus, and Received-Queue retries run as four independent loops, each with its own claim, failure state, cadence, and adaptive interval. A backlog in one does not slow another. `IRetryProcessorMonitor` reports an aggregate: the maximum interval, and backed off when any loop is backed off.

```csharp
var retryMonitor = app.Services.GetRequiredService<IRetryProcessorMonitor>();

// Inspect backpressure state
var pollingInterval = retryMonitor.CurrentPollingInterval;
var isBackedOff = retryMonitor.IsBackedOff;

// Manual recovery (operator/agent action)
await retryMonitor.ResetBackpressureAsync(cancellationToken);
```

`ResetBackpressureAsync` resets all four loops.

## Circuit Breaker

A per-consumer circuit breaker pauses transport consumption when a dependency is unhealthy, preventing retry storms. A circuit belongs to one consumer identity on one lane: it opens on that consumer's failures and pauses every client that delivers to it.

- **States.** Closed, then Open (transport paused), then HalfOpen (one probe), then Closed (resume) or Open again. Open duration escalates exponentially on repeated trips and resets after consecutive successful close cycles.
- **Only the probe decides HalfOpen.** A failure or success from a delivery admitted before the circuit opened neither reopens, escalates, nor closes it.
- **Persisted retries share the circuit.** While Open, received retries of that consumer are deferred to the next probe time. In HalfOpen, one row or delivery owns the probe. The other claimed rows of that consumer keep their leases and become available again only when those leases expire. Healthy consumers in the same claimed batch dispatch first, so an open consumer cannot monopolize retry pickup.
- **Force-open wins.** A resume already in flight cannot reopen a circuit that `ForceOpenAsync` or a later trip opened.
- **Per process.** Each instance keeps its own circuit state. One replica can be open while others are closed.
- **Every-instance consumers** have no circuit breaker. An override on one fails startup.

### Configuration

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    // Global circuit breaker (applies to all consumers)
    setup.Options.CircuitBreaker.FailureThreshold = 5; // consecutive transient failures to trip
    setup.Options.CircuitBreaker.OpenDuration = TimeSpan.FromSeconds(30); // initial open duration
    setup.Options.CircuitBreaker.MaxOpenDuration = TimeSpan.FromSeconds(240); // cap after escalation

    // Adaptive retry backpressure
    setup.Options.RetryProcessor.AdaptivePolling = true;
    setup.Options.RetryProcessor.MaxPollingInterval = TimeSpan.FromMinutes(15);
    setup.Options.RetryProcessor.CircuitOpenRateThreshold = 0.8; // back off above 80% circuit-open rate
});
```

Override one consumer by identity. A setting left `null` on `ConsumerCircuitBreakerOptions` falls back to `MessagingOptions.CircuitBreaker`:

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage registration ...
    setup.Tune("payments.handler", consumer => consumer.CircuitBreaker(cb =>
    {
        cb.FailureThreshold = 3; // more sensitive
        cb.OpenDuration = TimeSpan.FromSeconds(60); // longer cooldown
        cb.IsTransientException = ex => ex is PaymentGatewayUnavailableException;
    }));

    // Disable the circuit breaker for a best-effort consumer
    setup.Tune("metrics.handler", consumer => consumer.CircuitBreaker(cb => cb.Enabled = false));
});
```

The same overrides bind from `Headless:Messaging:Consumers:{identity}:CircuitBreaker` (`Enabled`, `FailureThreshold`, `OpenDuration`); see [Host controls](consumers.md#host-controls). `IsTransientException` is code-only. Overrides for one identity do not merge: a later `CircuitBreaker(...)` call or configuration section replaces the earlier override and drops every value it set, including a code-set `IsTransientException`.

### Transient exceptions

```csharp
setup.Options.CircuitBreaker.IsTransientException = ex =>
    CircuitBreakerDefaults.IsTransient(ex) || ex is MyCustomTransientException;
```

`CircuitBreakerDefaults.IsTransient` covers `TimeoutException`, `HttpRequestException` (5xx), `SocketException`, `BrokerConnectionException`, `TaskCanceledException` (timeout only), and a database fault that is transient: an unreachable, overloaded, or failing-over PostgreSQL, SQL Server, or SQLite database, including a dropped or refused SQL Server connection. A delivery whose inbox row cannot be written is reported too, so a lasting messaging-storage outage pauses the client.

### Observe and control

- Metrics: `headless.messaging.circuit_breaker.trips` (counter), `headless.messaging.circuit_breaker.open_duration` (histogram, seconds), and `headless.messaging.circuit_breaker.state` (gauge: 0 Closed, 1 Open, 2 HalfOpen), each tagged `messaging.consumer.group.name` with the lane-qualified identity.
- State transitions log at Warning.
- An `Open` circuit turns the `messaging-consumers` readiness check `Degraded` (see [Health checks](operations.md#health-checks)).

```csharp
var monitor = app.Services.GetRequiredService<ICircuitBreakerMonitor>();

// Enumerate the lane-qualified circuit keys of registered consumers (available before any messages are processed)
IReadOnlySet<string> consumers = monitor.KnownConsumers;

// Check state
var states = monitor.GetAllStates(); // every consumer circuit with its current state
var isOpen = monitor.IsOpen(MessageLane.Bus, "payments.handler");
var state = monitor.GetState(MessageLane.Bus, "payments.handler"); // Closed, Open, HalfOpen, or null if unregistered

// Rich snapshot with escalation and timing details
CircuitBreakerSnapshot? snapshot = monitor.GetSnapshot(MessageLane.Bus, "payments.handler");

// snapshot.State, snapshot.EscalationLevel, snapshot.ConsecutiveFailures,
// snapshot.FailureThreshold, snapshot.OpenedAt, snapshot.EstimatedRemainingOpenDuration,
// snapshot.EffectiveOpenDuration

// Manual recovery (operator/agent action)
var wasReset = await monitor.ResetAsync(MessageLane.Bus, "payments.handler"); // true if reset performed
var wasOpened = await monitor.ForceOpenAsync(MessageLane.Bus, "payments.handler"); // true if force-opened
```

**Verify:** `monitor.GetState(lane, identity)` returns `Closed` for a registered consumer and `null` for an unknown one.

## Distributed Lock Integration

`MessagingOptions.UseStorageLock` (default `false`) makes each of the four retry loops acquire a named lock before its pickup, so only one replica claims that loop per tick.

The retry claim does not need it for correctness. On PostgreSQL and SQL Server one claim statement skips locked rows and leases each row it returns, so replicas claiming at once take disjoint batches and never the same row. The lock makes replicas take turns instead: on a backlog, one batch per loop per tick drains, instead of one per replica.

Wire a provider with `MessagingBuilder.UseDistributedLock(...)`, which also sets `UseStorageLock = true`:

```csharp
// Instance overload — when you already have an IDistributedLock
var lockProvider = new MyDistributedLock(/* ... */);
builder.Services.AddHeadlessMessaging(setup => { /* ... */ })
    .UseDistributedLock(lockProvider);

// Factory overload — when the provider depends on other DI services
builder.Services.AddHeadlessMessaging(setup => { /* ... */ })
    .UseDistributedLock(sp => sp.GetRequiredService<IDistributedLock>());
```

- Messaging keeps its provider under its own keyed registration, so it never conflicts with an application-level `IDistributedLock`. An application `IDistributedLock` registered without `UseDistributedLock` is not used.
- `UseDistributedLock(...)` is last-wins.
- The same lock also elects the one host that reads a [sequential consumer's](ordering.md#sequential-consumers) subscription on transports without key routing or a broker single active consumer.

**Verify:** with `UseStorageLock = true` and no real provider, startup logs Warning EventId 77 (`UseStorageLockWithNoOpProvider`). With a real provider registered but not passed through `UseDistributedLock`, it logs EventId 78 (`UseStorageLockWithNoOpProviderButRealUnkeyed`). Alert on either. Without a provider, the fallback is `NullDistributedLock`, whose introspection methods return empty results, so the warnings are the only signal.

### When to enable

- **Enable** only when many replicas run an expensive retry claim (a large table where the due-row scan reads many leased or not-yet-due rows) and you accept slower backlog drain for one claim per loop per tick across the fleet.
- **Skip** on a single replica, when the claim cost is not measured, or when duplicate pickup attempts are tolerable. Duplicate pickup is not duplicate delivery.
- **Cost:** a lock-store round trip per acquire and release on every tick, idle fleet included. A lock-store outage skips that loop's pickup.

### Lock names and isolation

- `messaging.publish-retry-bus-{version}`, `messaging.publish-retry-queue-{version}`, `messaging.receive-retry-bus-{version}`, `messaging.receive-retry-queue-{version}`.
- `messaging.publish-retry-{lane}-{version}-outbox-{key}` for an [additional outbox](storage.md#additional-outboxes), where `{key}` is a 16-hex-digit hash of the outbox's provider, data source, and database.
- `{version}` is `MessagingOptions.Version`. Two services that share one lock store must set distinct `Version` values, or matching loops collide and starve each other.
- Every retry lock is try-once (no wait), leased for that loop's current polling interval, and auto-extended. Contention skips only that loop's cycle. Releasing one sends no lock-release broadcast.
- When a held retry lock's lease is lost (EventId 79), no new pickup starts under it. A dispatch already in flight is not canceled: its own row lease still governs it.

### EventIds

| EventId | Name | Severity | Trigger | Action |
| --- | --- | --- | --- | --- |
| 77 | `UseStorageLockWithNoOpProvider` | Warning | `UseStorageLock = true` but no real provider is registered | Wire one with `UseDistributedLock(...)`, or turn `UseStorageLock` off |
| 78 | `UseStorageLockWithNoOpProviderButRealUnkeyed` | Warning | A real provider is registered un-keyed but not passed through `UseDistributedLock(...)` | Pass it through `UseDistributedLock(...)` |
| 79 | `RetryLockLeaseLost` | Warning | An acquired retry lock's lease was lost before or during pickup | Check lock-store TTLs, clock skew, and auto-extension if frequent |
| 81 | `PublishedRetryLockAcquireFailed` | Warning | Acquiring the published-retry lock threw; pickup skipped | Check lock-store health if persistent |
| 82 | `PublishedRetryLockAcquireFailureEscalated` | Error | Three consecutive published-retry acquire failures; polling backs off | After recovery, call `IRetryProcessorMonitor.ResetBackpressureAsync` |
| 83 | `ReceivedRetryLockAcquireFailed` | Warning | Acquiring the received-retry lock threw; pickup skipped | Check lock-store health if persistent |
| 84 | `ReceivedRetryLockAcquireFailureEscalated` | Error | Three consecutive received-retry acquire failures; polling backs off | After recovery, call `IRetryProcessorMonitor.ResetBackpressureAsync` |

## Dead-owner recovery

Dead-owner recovery runs on every messaging host, independent of `UseStorageLock`. With a Coordination membership provider registered, the host reclaims the retry leases of node incarnations the membership classifies `Dead`, so their rows run again without waiting for the lease to expire. It reacts to a `NodeLeft` event and also reconciles every `MessagingOptions.DeadNodeReconcileInterval`.

- **`DeadNodeReconcileInterval`** defaults to `null`: half the coordination `DeadRetentionWindow` (15 seconds with default coordination options). An explicit value above that ceiling is clamped, with one warning per host (Coordination EventId 14, `DeadNodeReconcileIntervalClamped`). To reconcile less often, raise `DeadRetentionWindow`.
- **Idempotent.** A repeated reclaim, or a peer's concurrent reclaim of the same dead owner, is a no-op.
- **Dead only.** A `Suspected` owner is never reclaimed, so a GC pause or a network blip does not cause duplicate delivery.
- **Lease expiry is the floor.** A row whose lease expired is recovered by normal pickup either way.
- **Set Coordination's dead threshold no lower than the retry `DispatchTimeout`.** A node starved long enough to miss heartbeats is declared `Dead` while it may still be dispatching. With `DeadThreshold >= DispatchTimeout` its lease has already lapsed by then. A lower threshold can revoke a valid lease and re-dispatch a row its owner is still handling, which startup reports as Warning EventId 94 (`MessagingDeadThresholdBelowDispatchTimeout`).
- **Without Coordination** (`NullNodeMembership`), recovery uses lease expiry only, and startup logs Information EventId 88 (`MessagingRecoveryUsingLockedUntilFloorOnly`).
- A reclaim logs Information EventId 91 (`MessagingDeadOwnerRowsReclaimed`) with the row count.
- When `DispatchTimeout - InitialDispatchGrace` exceeds two minutes, startup logs Warning EventId 97 (`MessagingDispatchTimeoutMateriallyExceedsInitialGrace`): a crashed or over-budget handler can delay rolling-restart pickup until lease expiry. Measure the longest valid handler, keep the orchestrator's termination grace above `ShutdownTimeout`, and align the timings. Do not shorten the lease below the valid handler duration.

The recovery service logs under the category `Headless.Coordination.DeadOwnerRecoveryBridge<...>`, with its own EventIds:

| EventId | Name | Severity | Trigger |
| --- | --- | --- | --- |
| 1 | `MembershipWatchFailed` | Error | The membership watch failed; the periodic reconcile still recovers |
| 2 | `DeadNodeReconcileFailed` | Error | A reconcile tick failed; it retries next interval |
| 3 | `DeadNodeReclaimFailed` | Error | One dead owner's reclaim threw; it retries on the next reconcile |
| 14 | `DeadNodeReconcileIntervalClamped` | Warning | `DeadNodeReconcileInterval` exceeded half of `DeadRetentionWindow` and was clamped |

**Verify:** on a host without Coordination, startup logs EventId 88. On a host with Coordination, a stopped node's rows are reclaimed with EventId 91.
