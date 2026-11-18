---
domain: Messaging
packages:
---

# Messaging operations

> Part of [Messaging](../messaging.md). Readiness checks, broker reachability, the host manifest, operator APIs, orphaned inbox rows, and history retention.

## Health checks

Messaging registers its readiness checks itself. There is no `AddMessagingHealthChecks()` call. Each check carries the `ready`, `headless`, and `messaging` tags, follows the rules in [Health checks](../utilities.md#health-checks), and is removed with `RemoveHealthChecks`. No check publishes, consumes, or writes. Each one reads state the host already keeps or runs one read-only query.

| Check | Registered by | Failure status | Fails when |
| --- | --- | --- | --- |
| `messaging` | `AddHeadlessMessaging` | `Unhealthy` | The host has not started messaging, or host shutdown has begun. Messaging starts in the background after the host already serves requests, so this check holds readiness until messaging has started |
| `messaging-broker` | `AddHeadlessMessaging` | `Unhealthy` | The broker is unreachable (see [Broker reachability](#broker-reachability)). Applies to consuming and publish-only hosts on every transport. It stays healthy while the host starts or stops |
| `messaging-consumers` | `AddHeadlessMessaging` | `Degraded` | Any consumer circuit is `Open`, tripped or forced with `ICircuitBreakerMonitor.ForceOpenAsync`. `HalfOpen` reports healthy, because it is already resuming |
| `messaging-outboxes` | `AddHeadlessMessaging` with at least one `AddOutbox(name)` | `Degraded` | An [additional outbox](storage.md#additional-outboxes) is not initialized yet. Only units of work on that database are refused, so the host stays ready |
| `messaging-storage-postgresql-default`, `messaging-storage-sqlserver-default` | The primary PostgreSQL or SQL Server storage, raw or EF; also tagged `database` | `Unhealthy` | A connection from the storage's options cannot open, or `SELECT 1` on it fails |
| `messaging-storage-postgresql-outbox-{name}`, `messaging-storage-sqlserver-outbox-{name}` | Each additional PostgreSQL or SQL Server outbox; also tagged `database` | `Unhealthy` | The same probe against that outbox's database. `{name}` is the name passed to `AddOutbox(name)` |
| `messaging-nats` | `UseNats` | `Unhealthy` | A NATS `PING` fails or gets no answer within 5 seconds (see [Headless.Messaging.Nats](nats.md#headlessmessagingnats)) |

`messaging`, `messaging-broker`, `messaging-consumers`, and `messaging-outboxes` read the running host's state, so they also carry `host-state` (`HeadlessHealthCheckTags.HostState`). The [`check` command](../utilities.md#health-check-command) skips them before the host starts. The storage and NATS checks probe their dependency and run there. The in-memory storage and transport contribute no storage or broker probe.

The storage probe runs a query instead of only opening a connection, because a pooled open can return an idle connection without a round trip and report a stopped database as reachable.

### Change a check's status or timeout

`ConfigureHeadlessHealthCheck(name, o => ...)` overrides one check, before or after `AddHeadlessMessaging`. For example, `ConfigureHeadlessHealthCheck("messaging-consumers", o => o.FailureStatus = HealthStatus.Unhealthy)`. The `messaging-nats` timeout also binds from `Headless:HealthChecks:messaging-nats:Timeout`.

A host whose writes go through the outbox keeps serving while the broker is down, because the outbox holds each message until the broker is back. To report a broker outage as `Degraded` while a host that has not started, or is stopping, still fails readiness, grade the broker checks and leave `messaging` alone:

```csharp
using Headless.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

builder.Services.ConfigureHeadlessHealthCheck("messaging-broker", o => o.FailureStatus = HealthStatus.Degraded);
builder.Services.ConfigureHeadlessHealthCheck("messaging-nats", o => o.FailureStatus = HealthStatus.Degraded); // UseNats only
```

On NATS, set both. Otherwise the `messaging-nats` ping fails readiness during the same outage.

**Verify:** run the `check` verb against the host, or request the readiness endpoint while the broker is stopped, and confirm `messaging-broker` reports `Degraded`.

## Broker reachability

The `messaging-broker` check does not ping the broker. Only NATS adds its own `messaging-nats` probe. The check judges reachability from the receive and send outcomes the host already sees:

- **Unreachable** after 3 broker failures in a row with no success between them. A failure is a consumer client that cannot connect or reports a lost connection, a receive that fails because the broker cannot be reached, a send whose transport returns `OperateResult.BrokerUnreachable` (or throws a socket, I/O, HTTP, or timeout failure), or a send that hits `MessagingOptions.TransportPublishTimeout`. One failure is often a blip the client retries through, so readiness does not follow it.
- **A refused message never counts.** A send the broker refuses, such as a missing SNS topic or Service Bus entity, a RabbitMQ exchange that does not exist, a Kafka or NATS message over the size limit, or a NATS publish no stream acknowledged, fails that message's attempt only. One bad outbox row cannot take a host out of readiness.
- **Reachable again** only on an observed success: a successful send, a delivered message, a receive that reaches the broker after a failure, a consumer registration, or a consumer client established with no broker failure while it started. Any success ends the run, whichever path started it. A transport restart alone does not count.
- Every transport feeds the same state, so a publish-only host and a consuming host answer the same way. Under the default `Durable` delivery mode, the outbox dispatcher's sends feed it.
- **Direct delivery does not feed it.** A `Direct` publish failure reaches the caller as an exception instead.

An Azure Service Bus processor reports no success while idle. After a failed receive, its client peeks the entity on a real-time back-off (about 1 second, doubling to 30) until a peek succeeds or a message arrives. A peek locks and settles nothing.

Azure Service Bus, Amazon SQS, and Redis Streams retry a lost broker inside their own receive loop. They log `TransportBrokerUnreachable` (EventId 132, Warning) on a failed receive and `TransportBrokerReachable` (EventId 123, Information) on the first receive that succeeds after one. The warning stops once a failure run makes the broker unreachable, so a client that retries every second does not log every retry.

## Host manifest

`AddHeadlessMessaging` writes the `messaging` section of the host manifest that `RunHeadlessAsync(args)` prints for `describe` and that `MapHeadlessHostManifest` serves. [Host manifest](../utilities.md#host-manifest) owns the command, the document envelope, and the endpoint. The section is read from the host's frozen consumer registry, so it shows the tuning that configuration applied and this host's `ConsumeOnly` filter. A host without `AddHeadlessMessaging` has no `messaging` section.

```json
{
  "consumers": [
    {
      "identity": "orders.fulfillment",
      "lane": "bus",
      "delivery": "competing",
      "runState": "runs-here",
      "consumeOnly": "listed",
      "messages": [
        {
          "name": "orders.placed",
          "contractVersion": "1",
          "consumerType": "MyApp.Orders.OrderPlacedConsumer",
          "handlerMethod": "ConsumeAsync"
        }
      ],
      "concurrency": 8,
      "failurePolicy": { "immediateRetries": 2, "delayedRetries": 0 },
      "inboxRetentionSeconds": 604800,
      "rateLimit": { "algorithm": "fixed-window", "permitLimit": 100, "windowSeconds": 1 },
      "sequential": true,
      "activeHost": "broker",
      "circuitBreaker": { "enabled": true, "failureThreshold": 5 },
      "middleware": ["MyApp.Messaging.AuditMiddleware"]
    }
  ],
  "messages": [
    {
      "name": "orders.placed",
      "contractVersion": "1",
      "lanes": [{ "lane": "bus", "deliveryMode": "direct" }, { "lane": "queue" }]
    }
  ],
  "outboxes": [
    { "name": "primary", "storage": "PostgreSQL" },
    { "name": "billing", "storage": "SQL Server" }
  ],
  "transports": { "bus": "RabbitMQ", "queue": "RabbitMQ" }
}
```

- **One entry per identity and lane**, ordered by identity, then lane. An identity that owns a Queue message and consumes Bus messages has an entry on each lane. `messages` lists every message the entry consumes, with its contract version, the handling class in `consumerType`, and the method in `handlerMethod`: `ConsumeAsync` for `IConsume<T>`, `RespondAsync` for `IRespond<TRequest, TResponse>`.
- **`delivery`** is `competing` or `every-instance`. **`runState`** is `runs-here` when this host starts clients for the entry, and `registered-not-run-here` when its `ConsumeOnly` filter leaves the entry out. An every-instance consumer always runs here.
- **`consumeOnly`** appears only on a competing consumer of a host that calls `ConsumeOnly`: `listed` or `not-listed`.
- **Tuning.** `concurrency` is always written. `sequential: true` appears on a consumer tuned [`Sequential()`](ordering.md#sequential-consumers), with `activeHost` set to `keyRouted`, `broker`, `lock`, or `none`. `inboxRetentionSeconds` appears on competing consumers only. `failurePolicy` appears when the consumer has one: the delay fields only when `delayedRetries` is above zero, `failOn` and `failWhenRules` only when set. `rateLimit` carries `algorithm` plus `tokenLimit`, `tokensPerPeriod`, and `replenishmentPeriodSeconds` for `token-bucket`, or `permitLimit` and `windowSeconds` otherwise. `circuitBreaker` lists only the overrides the consumer sets (`enabled`, `failureThreshold`, `openDurationSeconds`, and `customTransientPredicate: true`). `middleware` lists per-consumer middleware type names.
- **`messages`** is the publish side: one entry per `Message<T>(name, version)` contract, ordered by name, with each lane it declares. A lane shows `deliveryMode` only when the contract pins one, and `routingAffinity` and `rawPayload` only when set.
- **`outboxes`** lists the primary storage as `primary`, then each additional outbox in registration order, under its `AddOutbox(name)` name. `storage` is `PostgreSQL` or `SQL Server` for relational storage, and the storage type name otherwise.
- **`transports`** names each lane's broker from `BrokerAddress.Name`. A lane with no transport is absent. The endpoint is never written.
- **Not described:** runtime subscriptions, provider-specific consumer configuration, broker addresses, connection strings, and credentials.

Use `describe` to verify wiring: it shows which consumers this host runs, their effective tuning, and each lane's transport.

## Operator APIs

These APIs back the Messaging dashboard module. Call them directly to build your own operator tooling. Reads need only an authenticated principal (`OperatorAuthorizationContext.ValidateRead`). Every write needs a named actor and throws `UnauthorizedAccessException` without one.

### Monitoring reads (`IMonitoringApi`)

- **`ListMessagesAsync(MessageListQuery, MessageListPosition?, pageSize)`** pages one table on the keyset (sort key, id), so a row that changes state between pages is never repeated or skipped. Rows carry no content. A page holds at most `MessageListPage.MaxPageSize` (500) rows. `MessageListQuery.SortBy` is `MessageListSortKey.Added` (default) or `Name`. `SortDirection` defaults to `Descending`. Take the next position from a row with `MessageListPosition.Of(view)`. A position is meaningful only to the store that produced it, under the same sort and direction.
- **Filters.** `MessageListQuery` filters by status, `TerminalOnly`, consumer identity, message name, lane, tenant (received rows), message id, failure fingerprint, `IsHeld`, and added time (`AddedFrom` inclusive, `AddedBefore` exclusive). These text filters compare ordinally on every provider, trailing spaces included.
- **Searches.** `Content` matches a literal, case-insensitive substring of the stored envelope (headers and body). `CorrelationId` matches the correlation header exactly, case-insensitive. No index serves either, so each reads every row the other filters leave. Bound a search with the cancellation token, and keep its text out of URLs and logs. A store whose envelopes a replaced `IMessageSerializer` writes in another format finds nothing for `CorrelationId`.
- **`CountMessagesAsync(query, cap)`** reads at most one row past the cap and returns a `MessageCount` with `Capped` set when more rows matched. The default cap is 10,000.
- **`GetFailureGroupsAsync(messageType, consumerIdentity?)`** groups terminal failures by failure fingerprint, and received rows also by a hash of the consumer identity.
- **`GetBacklogAsync()`** returns pending outbox and inbox counts; see [Outbox and inbox backlog](observability.md#outbox-and-inbox-backlog).
- Legacy monitoring pagination uses zero-based `MessageQuery.CurrentPage`, returned as `IndexPage.Index`. A negative value becomes zero.

### Inbox operations (`IInboxOperationsApi`)

- **`QueryAsync(InboxGenerationQuery, authorization)`** lists retained inbox generations, newest first, up to 200 a page, as `InboxGenerationView`: generation number, incarnation, whether it is current, whether it is an orphan, the replay parent, the retention expiry, and the hold. Filters: `IncarnationId`, `MessageId`, `ConsumerIdentity`, `Lane`, `Status`, `IsOrphaned`, and `IsHeld`. An id that differs only by trailing spaces matches nothing.
- **`CountAsync`** gives a capped count of the same query.
- **Single-row actions:** `HoldAsync`, `ReleaseHoldAsync`, `PurgeAsync`, and `ForceReprocessAsync`. Each takes a client-minted operation id, the incarnation, and the expected status. Any outcome other than `Applied` is a refusal, not a failure: another node or an earlier run already changed the row.
- **Operations over a filter:** build a `MessageOperationScope` from the kind (`ReExecute` or `Delete`), a received `MessageListQuery`, and the `HighWaterMark` read from `IDataStorage.GetLatestMessageChangeMarkerAsync` when the operator confirms. `CountOperationScopeAsync` gives the capped count for the confirm step. `ReadOperationScopeAsync(scope, afterId, chunkSize)` returns chunks (at most `MessageOperationChunk.MaxChunkSize`, 500) of terminal generations stamped at or before the mark. Apply each target with `ForceReprocessAsync` or `PurgeAsync`. Re-execution starts a child stamped after the mark, so running the operation again, or serving one chunk on two nodes, re-executes no message twice. `Content` and `CorrelationId` narrow a scope as they narrow a list.
- **`QueryReceiptsAsync(MessagingOperationReceiptQuery, authorization)`** reads the operator ledger newest first, filtered by actor, operation type, target kind, and a time range. Each `MessagingOperationReceiptView` carries `OutboxName`: the additional outbox whose row the operation changed, or `null` for the primary store.

### Published operations (`IPublishedOperationsApi`)

`IDataStorage.GetPublishedOperationsApi()` returns the audited operator surface for published rows.

- **`ResendAsync`** sends a row that failed for good (`Failed` with no `NextRetryAt`) again with a fresh retry budget. **`PurgeAsync`** deletes a terminal row (`Succeeded`, or `Failed` with no retry pending).
- Each `PublishedOperationRequest` carries a client-minted operation id, the storage id, an optional `ExpectedChangedAt`, an optional reason (empty is stored as null), and the operator's authorization. Each call writes a receipt and an audit row in the same transaction that changes the row, refused calls included.
- **Outcomes:** a missing row is `NotFound`. A row still waiting for a send or retry is `Active`. A re-send of a succeeded row, or a row whose change marker no longer equals `ExpectedChangedAt`, is `StateConflict`. A repeated operation id replays its receipt, or is `OperationConflict` when the type, row, actor, or reason differs.
- **Over a filter:** use `MessageOperationScope` with a published `MessageListQuery` and kind `Resend` or `Delete`. Pass each target's `ChangedAt` as `ExpectedChangedAt`, so a row that changed after the read is refused and running the operation again re-sends no row twice.
- **Additional outboxes:** the ledger lives only in the primary store. An outbox serves scope reads, and its writes throw `NotSupportedException`. The dashboard still re-sends and deletes outbox rows: it changes the row on the outbox, then records the receipt in the primary, as two writes that are not atomic.
- `RequeuePublishedMessagesAsync` and `DeletePublishedMessagesAsync` do the same without a receipt.

### Scheduled-delivery operations (`IScheduledDeliveryOperationsApi`)

`IDataStorage.GetScheduledDeliveryOperationsApi()` returns the audited surface for pending scheduled deliveries.

- **`QueryAsync`** lists them, system-scoped with no tenant filter, up to 200 rows a page. **`CountAsync`** gives a capped count.
- **`RevokeAsync`** deletes one row with the same eligibility and fence as `IMessageRevoker`, plus a receipt and audit in the same transaction. Revocation is never a visible status: the ledger is the only trace once the row is gone.
- **`DispatchNowAsync`** moves a pending row's due instant to the store's clock unless a dispatch lease is live. Some node's delayed processor claims it on its next pass, typically within about a minute. A dashboard-only host with no dispatcher cannot promise that latency.
- Every mutation carries a client-minted operation id, the storage id, and the expected due instant. A due instant that no longer matches is `StateConflict`. A repeated operation id with a different request is `OperationConflict`. Other outcomes: `Applied`, `NotFound`, and `Active` (an attempt was reserved or the row is leased).
- Neither action creates, reschedules, or replays a delivery. For a keyed, replaceable, or transactional deadline, use [Jobs](../jobs.md).
- A provider without the capability throws `NotSupportedException` naming the provider.

### Reasons and receipts

A single-row request's reason is optional. An empty or whitespace reason is stored as `NULL`, and a replay matches on that stored value. Require a reason in your own tooling for an operation over a filter. Every outcome writes a receipt, refused ones included, so the ledger shows a `StateConflict` beside the `Applied` it repeated. Use a unique operation id per request, and retry within the receipt retention window: after the receipt is deleted, the same id is evaluated as a new request.

## Orphaned inbox rows

An inbox row whose consumer is not registered on the host that picks it up becomes an orphan. It is deferred without spending the handler's retry budget.

- **Recovery** needs the exact consumer identity, contract name, contract version, and lane to be registered again. Absence on one host does not prove absence on every deployment.
- **`ConsumeOnly` hosts** never classify a row outside their filter, so only a host that runs the consumer, or an unfiltered host, marks that consumer's rows as orphans.
- **Probing.** Known orphans are left out of ordinary retry pickup. Each lane probes them separately, every `setup.Options.OrphanProbeInterval` (default 5 minutes, positive) in batches of `OrphanProbeBatchSize` (default 10, 1 to 100,000). The interval is a delay, not a recovery deadline. A first discovery can take ordinary retry capacity once, so a growing backlog of unclassified rows has no latency guarantee.
- **No automatic expiry.** An orphan without a live claim allows `Hold`, `ReleaseHold`, and, when unheld, `Purge`. A live claim blocks them. `ForceReprocess` stays terminal-only. A hold blocks purge and retention cleanup but does not pause execution: a held orphan can recover and keeps its hold after completion.

## Operation history retention

Receipts and audits of inbox and scheduled-delivery operations have their own retention, separate from inbox generations. Each value is a positive minimum residence time set on `setup.Options`:

| Option | Default |
| --- | --- |
| `InboxCleanupReceiptRetention` | 7 days |
| `InboxCleanupAuditRetention` | 7 days |
| `InboxOperatorReceiptRetention` | 30 days |
| `InboxOperatorAuditRetention` | 90 days |

```csharp
setup.Options.OrphanProbeInterval = TimeSpan.FromMinutes(2);
setup.Options.OrphanProbeBatchSize = 20;
setup.Options.InboxOperatorReceiptRetention = TimeSpan.FromDays(14);
setup.Options.InboxOperatorAuditRetention = TimeSpan.FromDays(180);
```

- Each record ages from its `CreatedAt`. A replay does not refresh it.
- A receipt stays until its residence time passes and every audit that references it is deleted, so audits can extend a receipt's life.
- Deleting audits does not release a surviving generation's hold.
- A retention change applies to existing history. Shortening a value can make old evidence eligible on the next sweep. Lengthening it cannot restore deleted records, so set longer windows before history is collected.
- These options do not change inbox-generation retention, which `Tune(identity, c => c.InboxRetention(...))` sets; see [Host controls](consumers.md#host-controls).
- **Clock.** PostgreSQL and SQL Server age history on the database clock. The in-memory storage uses its registered `TimeProvider`.
- **Collector pacing.** The collector takes one cutoff per run. Each round deletes published messages, received messages, expired audits, and unreferenced expired receipts, at most 1,000 per category, with a one-second pause after each nonzero batch. Rounds repeat until every category returns zero, then wait `CollectorCleaningInterval`. The durations are minimum residence times, not deletion deadlines: storage stays bounded only while the collector keeps up with arrivals.

## Runtime effects

A messaging host:

- Starts background services for dispatch, retry pickup, delayed messages, and collection.
- Verifies relational storage tables at startup and fails the start when a schema step is missing. The deployment creates them with `resources provision`, and a development or test host calls `AddDeployStepsOnStartup()` (see [Schema runner](../sql.md#schema-runner-apply-verify-and-deploy-time-scripts)).
- Opens transport connections.
- Opens one reply channel per process when the host calls `AddRequestReply` (see [Request/reply](request-reply.md#requestreply)).
- Reads each storage's backlog once per `BacklogSamplingInterval` when that option is set (see [Outbox and inbox backlog](observability.md#outbox-and-inbox-backlog)).
