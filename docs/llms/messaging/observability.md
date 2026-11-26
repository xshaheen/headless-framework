---
domain: Messaging
packages:
---

# Messaging observability

> Part of [Messaging](../messaging.md). Traces, metrics, enrichers, and the outbox and inbox backlog gauges that `Headless.Messaging` emits natively.

## OpenTelemetry

`Headless.Messaging` emits spans and metrics itself through `System.Diagnostics` `ActivitySource` and `Meter`. There is no separate OpenTelemetry package. Any listener subscribes: an OpenTelemetry exporter, an `ActivityListener` or `MeterListener`, Application Insights, or `dotnet-counters`.

- The source and meter are both named `Headless.Messaging`, exposed as `MessagingDiagnostics.SourceName`.
- `AddMessagingInstrumentation()` on `TracerProviderBuilder` (namespace `OpenTelemetry.Trace`) and on `MeterProviderBuilder` (namespace `OpenTelemetry.Metrics`) subscribes them. Subscribing by name works too.
- **Subscribing is the switch.** Metrics are always registered, and there is no `EnableMetrics` flag. An unobserved host pays almost nothing.
- Names and attributes follow the [OpenTelemetry messaging semantic conventions](https://opentelemetry.io/docs/specs/semconv/messaging/). Instruments and attributes the conventions do not define are named `headless.messaging.*`. The conventions are at Development stability, so a later release can rename them.
- A failure is recorded on the same instrument as a success, with `error.type` set. There are no separate error counters. `error.type` is the full type name of the transport's or handler's exception, never the framework wrapper, or `ambiguous_delivery` for a send whose broker acceptance is unknown.
- Durations are in seconds.

### Set up tracing and metrics

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// 1. Register enrichers / suppression on the messaging setup builder (optional).
builder.Services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage registration ...
    setup.Instrumentation.SuppressRetryCountTag = true;    // opt out of retry-count tagging
    setup.Instrumentation.AddEnricher(new MyTagEnricher()); // custom tags
});

// 2. Subscribe the messaging scope on your OpenTelemetry providers.
builder
    .Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddMessagingInstrumentation())
    .WithMetrics(metrics => metrics.AddMessagingInstrumentation());
```

**Verify:** publish one message and confirm a `publish {message name}` span and a `messaging.client.sent.messages` measurement reach your exporter, or watch the `Headless.Messaging` meter with `dotnet-counters monitor`.

### Trace propagation

- W3C `traceparent` and baggage are injected on publish and extracted on consume whenever any messaging telemetry is enabled. There is no toggle.
- A metrics-only service, or a sampled-out publish, relays the incoming parent context onto outgoing messages instead of dropping it, so trace continuity survives hops that do not trace. A consumed message's context flows to publishes made from its handler even without a span.
- A host with no listeners at all forwards nothing. The framework never creates a root context where no parent exists.
- Your OpenTelemetry setup must set `Propagators.DefaultTextMapPropagator`. The standard `AddOpenTelemetry().WithTracing()` does.

### Log correlation

Every log entry written inside the consume pipeline (consume middleware and the consumer) carries `CausationTraceId`: the trace id of the span that published the message, read from its stored `traceparent`. It is the same on the Bus and Queue lanes and on every transport. Query `TraceId = <request trace id> OR CausationTraceId = <request trace id>` to find the request's lines and its consumers' lines together. The consumer's own `TraceId` can differ from the publisher's: an in-process delivery runs under the consumer loop's activity, and a sampled-out consume has none.

- The field needs Microsoft's log enrichment, which `AddHeadlessLogging()` enables. On a host without it, nothing is added.
- A message published while no messaging telemetry listens stores no `traceparent` (see [Trace propagation](#trace-propagation)), so its consumer's entries get no field. An unreadable `traceparent` adds no field either.
- Entries the framework writes outside the pipeline, such as the retry and terminal-failure logs, do not carry it.
- The tenant is not added by this field. It comes from the ambient-tenant log enrichment; see [multi-tenancy observability](../multi-tenancy/observability.md).

**Verify:** publish from inside a request, then find the consumer's entry by the request's trace id in `CausationTraceId`.

### Enrichers and tags

- Register an `IActivityTagEnricher` with `setup.Instrumentation.AddEnricher(...)`. It runs synchronously at span start (`void Enrich(Activity activity, in MessagingEnrichmentContext context)`), so every tag it adds lands before the span can end. An enricher that throws is isolated from the others.
- An enricher must not write the reserved namespaces `messaging.*`, `server.*`, `headless.messaging.*`, or `exception.*`; the framework or SDK overwrites them.
- Never copy raw `context.Headers` onto tags: they can carry tokens or personal data.
- `setup.Instrumentation.IncludeTenantIdInMetricTags` (default `false`) adds the tenant to metric tags under `TenantTelemetryOptions.AttributeName` (`tenant.id`). Enable it only when your metrics backend and tenant count have a cardinality budget. Traces follow their own tenant setting.

| Tag | Emitted by | Toggle |
| --- | --- | --- |
| `headless.messaging.lane` (`bus`, `queue`) and `headless.messaging.destination.kind` (`topic`, `queue`) | Built-in lane enricher | `setup.Instrumentation.SuppressLaneTags` |
| `tenant.id` (`TenantTelemetryOptions.AttributeName`), written before any enricher runs | Messaging telemetry | `TenantTelemetryOptions.EnrichTraces`, set through `AddHeadlessTenancy(t => t.Telemetry(...))`; see [multi-tenancy observability](../multi-tenancy/observability.md) |
| `headless.messaging.retry_count` | Built-in retry-count enricher, on subscriber-invoke spans | `setup.Instrumentation.SuppressRetryCountTag` |
| Custom tags | Your `IActivityTagEnricher` | `setup.Instrumentation.AddEnricher(...)` |

Delivery-mode tags (`headless.messaging.delivery.requested`, `headless.messaging.delivery.resolved`) emit only `durable` or `direct`. Query and alert on those two values.

## Instrument reference

Every instrument registers on the `Headless.Messaging` meter.

| Instrument | Kind | Attributes |
| --- | --- | --- |
| `messaging.client.sent.messages` | Counter (`{message}`) | `messaging.operation.name` = `publish`, `messaging.operation.type` = `send`, `messaging.system`, `messaging.destination.name`, `server.address`, `server.port`, `headless.messaging.lane`, `headless.messaging.delivery.requested`, `headless.messaging.delivery.resolved`, `error.type` on failure |
| `messaging.client.consumed.messages` | Counter (`{message}`) | `messaging.operation.name` = `receive`, `messaging.operation.type` = `receive`, `messaging.system`, `messaging.destination.name`, `messaging.consumer.group.name`, `server.address`, `server.port`, `error.type` on failure |
| `messaging.client.operation.duration` | Histogram (s) | The attributes of the send or receive it times |
| `messaging.process.duration` | Histogram (s) | `messaging.operation.name` = `process`, `messaging.operation.type` = `process`, `messaging.system`, `messaging.destination.name`, `messaging.consumer.group.name`, `headless.messaging.subscriber`, `error.type` on failure |
| `headless.messaging.persistence.duration` | Histogram (s) | `messaging.destination.name`, `headless.messaging.persistence.type` (`publish`, `consume`), lane and delivery-mode tags on the publish side |
| `headless.messaging.message.body.size` | Histogram (bytes) | `messaging.destination.name`, `messaging.system` |
| `headless.messaging.receive.outcomes` | Counter | `headless.messaging.receive.outcome`: `expired` for a message dropped on arrival past its [`DeliverWithin`](publishing.md#delivery-deadline-deliverwithin) or request deadline, `no_responder` for a request that reached a plain consumer, `skipped` for a receive-middleware skip |
| `headless.messaging.send.expired` | Counter | `headless.messaging.lane`. One per published message dropped without sending because its `DeliverWithin` deadline passed (relay or `Direct` send) |
| `headless.messaging.every_instance.deliveries` | Counter | `messaging.consumer.group.name`, `headless.messaging.every_instance.outcome` (`succeeded`, `failed`, `dropped`, `skipped`), `error.type` |
| `headless.messaging.outbox.terminal` | Counter (`{message}`) | `messaging.destination.name`, `headless.messaging.lane`, `error.type`. One per stored published message the framework stopped sending: retries spent, failure not retried, `DeliverWithin` passed, or no transport serves its lane |
| `headless.messaging.consume.terminal` | Counter (`{message}`) | `messaging.consumer.group.name`, `headless.messaging.lane`, `error.type` (`_OTHER` for a delivery that arrived already marked failed). One per stored received message the framework stopped handling: failed inbox rows, poison rows stored on arrival, and runtime-subscription rows |
| `headless.messaging.backlog.pending` | ObservableGauge (`{message}`) | `headless.messaging.persistence.type` (`publish`, `consume`), `db.namespace` (absent for in-memory storage). Off unless `BacklogSamplingInterval` is set |
| `headless.messaging.backlog.oldest_age` | ObservableGauge (s) | As `backlog.pending`. How long the oldest pending row has waited, `0` when none waits |
| `headless.messaging.sequential.waiting` | ObservableGauge (`{message}`) | `messaging.consumer.group.name`. Messages a [sequential consumer](ordering.md#sequential-consumers) has taken in on this host that have not started |
| `headless.messaging.settlement.failures` | Counter (`{failure}`) | `messaging.system` (`nats`, `rabbitmq`), `messaging.operation.name` = `ack`, `error.type` (`channel_closed` or the exception type). Acks that did not reach the broker; the broker redelivers and the inbox deduplicates |
| `headless.messaging.inbox.duplicates`, `.attempts`, `.recoveries`, `.terminal`, `.replays`, `.retention`, `.capabilities` | Counter | Consumer identity, lane, finite outcome, tier, and provider. Tenant only with `IncludeTenantIdInMetricTags` |
| `headless.messaging.operator.operations` | Counter | `headless.messaging.operator.target_kind`, `headless.messaging.operator.operation`, `headless.messaging.lane`, `headless.messaging.inbox.outcome`, `headless.messaging.inbox.provider` |
| `headless.messaging.request_reply.requests`, `.duration`, `.dropped_replies` | Counter, histogram (s), counter | See [Request/reply telemetry](request-reply.md#telemetry) |
| `headless.messaging.circuit_breaker.trips`, `.open_duration`, `.state` | Counter, histogram (s), gauge | `messaging.consumer.group.name`; see [Circuit Breaker](retries.md#circuit-breaker) |
| `headless.messaging.kafka.consumer.lag` | ObservableGauge | See [Consumer lag metric](kafka.md#consumer-lag-metric) |

Notes:

- **One operation per phase.** The transport send is `send`, delivery to the receive stage is `receive`, and the subscriber invocation is `process`. `messaging.client.sent.messages` counts every send attempt, and `messaging.client.consumed.messages` counts every delivery once. The count of `messaging.process.duration` is the number of subscriber invocations. An every-instance delivery runs its consumer inside the receive span, so it records no `process` measurement.
- **`headless.messaging.inbox.terminal` outcomes.** Each failure is counted once, by the write that made it terminal. `Succeeded`: the consumer completed. `FailedExhausted`: an attempt ran and the row ended `Failed` (budget spent, a fail rule or permanent failure, a payload that did not deserialize, or a responder that returned null). `FailedExpired`: the delivery deadline or request deadline passed before an attempt started. `FailedOnArrival`: a poison row stored before any attempt (no consumer matched, a contract-version mismatch, a receive-stage deserialization failure, or receive middleware that rejected or faulted). With no matching consumer, the consumer label is the subscription name. Runtime subscriptions keep no inbox, so they never appear on the inbox counters. Count every consumed message the framework gave up on with `headless.messaging.consume.terminal`.
- **`messaging.system`** is the transport's `BrokerAddress.Name`: `kafka`, `rabbitmq`, `servicebus`, `aws_sqs`, and `aws.sns` are well-known convention values. `nats`, `redis`, and `in_memory` are framework values. `server.address` and `server.port` come from the first broker in `BrokerAddress.Endpoint`.
- **Histogram buckets.** The operation, process, persistence, and request/reply duration histograms declare the conventions' bucket advice (`0.005` to `10` seconds). The circuit breaker's open duration does not, because a circuit stays open for minutes.
- **Bounded cardinality.** No message id, replay id, request id, correlation id, instance id, payload, or header is ever a metric attribute.

## Spans

| Span (`Activity.OperationName`) | Exported name (`DisplayName`) | Kind | `messaging.operation.name` / `.type` |
| --- | --- | --- | --- |
| `message.publish` | `publish {message name}` | Producer | `publish` / `send` |
| `message.consume` | `receive {message name}` | Consumer | `receive` / `receive` |
| `subscriber.invoke` | `process {message name}` | Consumer | `process` / `process` |
| `message.persist` | `message.persist` | Internal | None: an outbox write is not a messaging operation |

A failed span carries `error.type` and an `Error` status. There is no `settle` span: each transport client acknowledges inside its own code.

Framework span attributes: `headless.messaging.lane`, `headless.messaging.destination.kind`, `tenant.id`, `headless.messaging.retry_count`, `headless.messaging.receive.outcome`, `headless.messaging.delivery.outcome` (`ambiguous`), and per-phase durations on success events (`headless.messaging.persistence.duration_ms`, `send.duration_ms`, `receive.duration_ms`, `invoke.duration_ms`).

## Outbox and inbox backlog

Set `MessagingOptions.BacklogSamplingInterval` (default `null`, off; at least 1 second when set) to report how much stored work is waiting:

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.Options.BacklogSamplingInterval = TimeSpan.FromSeconds(30);
    // transport and storage as usual
});
```

A background sampler starts with messaging, waits one interval, then once per interval runs one `IMonitoringApi.GetBacklogAsync()` query on the primary storage and one on each initialized additional outbox. The `headless.messaging.backlog.pending` and `headless.messaging.backlog.oldest_age` gauges report the last sample. A metrics scrape never queries storage, and the gauges report nothing before the first sample. A failed read logs EventId 4136 (Warning), and that storage's gauges keep their last sample.

**Verify:** after one interval, the `headless.messaging.backlog.pending` gauge reports a value for each storage.

A row is pending when it is not terminal and not scheduled for later:

| Status | Pending | Waits since |
| --- | --- | --- |
| `Scheduled` (stored, or between inline retries) | Yes | `Added` |
| `Delayed` or `Queued` (published for a later time) | Once its publish time has passed | The publish time |
| `Failed` with a next retry | Yes | `Added` |
| `Failed` without a next retry, or `Succeeded` | No: terminal | |

- PostgreSQL and SQL Server measure the age on the database clock, so application clock skew does not move it. The in-memory storage uses the registered `TimeProvider`.
- Every replica samples the same storage-wide numbers. Aggregate the gauges across instances with `max`, never `sum`.
- Rows of other `MessagingOptions.Version` values that share the tables count too.
- To alert on a broker outage, watch `backlog.oldest_age` for `persistence.type = publish`. `headless.messaging.outbox.terminal` counts published messages the framework gave up on, and `headless.messaging.consume.terminal` the consumed ones.
