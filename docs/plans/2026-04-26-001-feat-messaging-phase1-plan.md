---
title: "feat: Messaging Phase 1 — Send/Broadcast split + tenancy + observability foundations"
type: feat
status: active
date: 2026-04-26
origin: specs/2026-04-19-001-messaging-feature-spec.md
issue: https://github.com/xshaheen/headless-framework/issues/217
phase: 1
---

# feat: Messaging Phase 1 — Send/Broadcast split + tenancy + observability foundations

## Overview

Land the **additive foundations** that the rest of the `Headless.Messaging` epic builds on: first-class `TenantId` on the envelope, uniform `IRetryBackoffStrategy` wiring across providers, and an `IActivityTagEnricher` hook for OpenTelemetry. These three changes are non-breaking against today's `IDirectPublisher` / `IOutboxPublisher` shape — consumers can adopt them without renaming any interface.

This plan converts Phase 1 of the canonical spec at `specs/2026-04-19-001-messaging-feature-spec.md` into a durable per-phase plan; later phases (2-11 sketches) live in the spec until scheduled.

### Phase 1 scope (post-review reconciliation, 2026-04-27)

Per document-review findings #7, #9, #19, the originally-drafted Phase 1 was descoped to its additive subset to reduce blast-radius and let consumer apps adopt incremental improvements without absorbing a rename + Outbox-decorator + 11-provider migration in one PR series.

| Unit | Final phase | Rationale |
|------|-------------|-----------|
| **U2** — `TenantId` first-class on envelope | **Phase 1** | Additive — adds nullable property + header constant. Existing call-sites unaffected. |
| **U4** — `IRetryBackoffStrategy` wired across all providers | **Phase 1** | Additive — providers that already wire it stay wired; providers that didn't gain default behavior. |
| **U5** — `IActivityTagEnricher` for OpenTelemetry | **Phase 1** | Additive — new hook + default enricher. Consumers opt in. |
| **U6** — Capability matrix doc + READMEs | **Phase 1** | Documents Phase 1 reality; Phase 2 sections shipped separately when those units land. |
| **U1** — `ISendPublisher` / `IBroadcastPublisher` rename | **Phase 2** | Deferred. Breaking surface change; sequenced after Phase 1 lands so consumers absorb one change at a time. |
| **U1b** — Outbox decorator + store + drainer | **Phase 2** | Deferred. Couples to U1's interface split + the SQL-store storage initializers; not safe to ship before the rename. |
| **U3a / U3b / U3c** — Per-provider migration + downstream-consumer migration | **Phase 2** | Deferred. Depends on U1. |
| **U7 / U8 / U9** — NATS per-stream config, DLQ observer, declarative router | **NATS-ergonomics phase (parallel-track Phase 2 sub-stream)** | Deferred. Provider-specific ergonomics; uncouple from cross-provider rename so NATS work doesn't gate ASB/SQS/Kafka migration and vice versa. |

The unit definitions for the deferred work remain in this document as the **binding specification** for those Phase 2 units — they are not rewritten in a Phase 2 plan, only re-sequenced. This preserves the per-unit dependency graph and lets `dev:plan` reference the canonical Unit IDs from a future phase plan via `phase: 2` frontmatter.

## Problem Frame

A consumer of `Headless.Messaging.Nats` surfaced nine friction points (issue #217). Root-causing them shows the underlying issue is not NATS-specific: the current abstractions conflate two orthogonal axes:

1. **Durability axis** — already modeled: `IDirectPublisher` vs `IOutboxPublisher`.
2. **Semantic axis** — missing: "deliver to one competing consumer" vs "fan out to every subscriber".

Today a caller writes `IDirectPublisher.PublishAsync<T>(...)` regardless of whether `T` represents a command (one handler) or an event (N handlers). The transport decides what happens on the wire, which leaks in both directions: callers cannot express intent, and providers cannot refuse a nonsense request. Tenancy, retry policy, and OpenTelemetry enrichment all suffer similar abstraction gaps that currently force NATS-specific workarounds in consumer code.

MassTransit's `ISendEndpoint.Send<T>` vs `IPublishEndpoint.Publish<T>` split is the battle-tested answer: intent is encoded in the API surface; transports provision whatever topology makes each interface work. Transports that cannot support an interface simply do not register it, and DI surfaces the gap at startup rather than at runtime.

## Requirements Trace

Carried forward from origin spec §Requirements Trace:

- **R1.** Callers express **intent** (send-one vs broadcast-many) in the API surface, not via options.
- **R2.** Both `Direct` and `Outbox` durability variants are available for **both** Send and Broadcast — the two axes stay orthogonal.
- **R3.** Transports declare capability at registration; a transport that cannot broadcast does not ship an `IBroadcastPublisher` implementation, and resolution fails at startup with an actionable message.
- **R4.** `TenantId` is a first-class envelope field on `PublishOptions`, `ConsumeContext`, and `Headers` — not an ad-hoc header convention. **Trust model:** the framework trusts the publisher and does not verify tenant authenticity on consume (matches MassTransit, NServiceBus, Wolverine, Brighter, Rebus). The typed `PublishOptions.TenantId` property is the authoritative publish-side seam; the raw `headers["headless-tenant-id"]` is reserved for transport-internal mapping. The publish pipeline rejects publishes where the raw header is set without the typed property, or where the two disagree, to prevent header-injection bypass of the typed surface (see U2 Approach). Consumer apps requiring cross-tenant authenticity layer their own check as an `IConsumeBehavior<T>` in Phase 2 — a typed `TenantId` alone is not an authorization decision.
- **R5.** `IRetryBackoffStrategy` is honored by every provider's dispatch loop, not just one.
- **R6.** OpenTelemetry spans emitted by `Headless.Messaging.OpenTelemetry` are extensible via a tag-enricher hook.
- **R7.** Existing consumer surface (`IConsume<T>`, `ConsumeContext<T>`) remains the single consumer entry point — no split consumer interfaces. `ConsumeContext.DeliveryKind` (Send vs Broadcast) is **intent** metadata, not transport identity.
- **R8.** NATS provider offers per-stream configuration, DLQ observability, and declarative stream routing without leaking NATS types into the abstractions package.
- **R9.** All public XML docs and package READMEs stay in sync with the new shape.

## Scope Boundaries (Permanent Non-Goals for the Epic)

These apply across the entire epic, not just Phase 1:

- No `MessageKind` / `CommandEvent` enum on the envelope — intent lives in the API surface (R1).
- No `PublishOptions.DeliveryMode` enum — capability cannot be expressed as a flat enum across heterogeneous transports.
- No `CloudEvents` package adoption — separate decision.
- No split of `IRetryBackoffStrategy` into delay + predicate interfaces — it already exposes both.
- No `ITenantResolver` abstraction — tenancy is data on the envelope.
- No consumer-side interface split (`IConsume<Event>` vs `IConsume<Command>`).
- No mandatory multi-tenancy filter; tenancy is observable and routable but not enforced.
- No message marker interfaces (`ICommand` / `IEvent` / `IMessage`).
- **No migration shims, adapter types, or compatibility layers** for the old `IDirectPublisher` / `IOutboxPublisher` names. Greenfield posture (per `CLAUDE.md`); old names are renamed in place and removed; internal packages and demos are migrated in Unit 3c.

## Context & Research

### Relevant Code and Patterns

- `src/Headless.Messaging.Abstractions/IMessagePublisher.cs` — current single publisher seam; becomes `internal` after the split.
- `src/Headless.Messaging.Abstractions/IDirectPublisher.cs`, `IOutboxPublisher.cs`, `IScheduledPublisher.cs` — durability markers to preserve conceptually (renamed in place).
- `src/Headless.Messaging.Abstractions/PublishOptions.cs` — envelope config; gains `TenantId`.
- `src/Headless.Messaging.Abstractions/ConsumeContext.cs` — consumer envelope; gains `TenantId` and `DeliveryKind`.
- `src/Headless.Messaging.Abstractions/Headers.cs` — wire header constants; gains `TenantId = "headless-tenant-id"`.
- `src/Headless.Messaging.Abstractions/IRetryBackoffStrategy.cs` — already exposes `GetNextDelay` + `ShouldRetry`; wire points exist, usage does not.
- `src/Headless.Messaging.Abstractions/MessagingConventions.cs` — topic/group naming; extend with `GetBroadcastTopicName(Type)` helper.
- All 11 provider projects under `src/Headless.Messaging.*` — each declares which publisher interfaces it supports.
- `src/Headless.Messaging.OpenTelemetry/` — host for the new `IActivityTagEnricher` hook.

### Institutional Learnings

- `docs/solutions/` — re-run `learnings-researcher` at `dev:code` kickoff for prior messaging decisions.
- Greenfield breaking-change posture is documented in `CLAUDE.md`.
- Pattern reference: `Headless.DistributedLocks.Core` package layout (abstractions + Core + provider stores) for the Outbox split.
- Pattern reference: `Headless.Caching.Hybrid` decorator pattern over `ICache` — same shape, different concern.
- Options/validation pattern: FluentValidation + `services.AddOptions<T, TValidator>().ValidateOnStart()` per `CLAUDE.md` — no bespoke `IHostedService`, no custom exception type.

### External References

- MassTransit `ISendEndpoint` / `IPublishEndpoint` split: <https://masstransit.io/documentation/concepts/producers>.
- MassTransit SQS broker topology (SNS fronts SQS for Publish): <https://masstransit.io/documentation/configuration/transports/amazon-sqs>.
- MassTransit ASB broker topology (topic fronts queues for Publish): <https://masstransit.io/documentation/configuration/transports/azure-service-bus>.
- MassTransit "riders" model for Kafka/Event Hubs — informs our "declare capability, don't fake it" rule.

### Transport Capability Matrix

Authoritative for registration decisions. Carried forward verbatim from origin spec §Transport Capability Matrix.

| Transport | `ISendPublisher` | `IBroadcastPublisher` | Publisher mechanism | Subscriber requirement for broadcast | Axis & operational cost |
|---|---|---|---|---|---|
| NATS (core + JetStream) | yes | yes | Distinct subject via `MessagingConventions.GetBroadcastTopicName` | No queue group on the broadcast subject | Convention-axis. Native subject fan-out at the broker once subscribers comply |
| Kafka | yes | yes | Distinct topic via `MessagingConventions.GetBroadcastTopicName` | Distinct `group.id` per subscriber on the broadcast topic | Convention-axis. Operational cost: N consumer groups linear in subscriber count |
| RabbitMQ | yes | yes | Fanout (or topic) exchange — different exchange type from send | Independent queues bound to the fanout exchange | **Publisher-axis.** Topology fully enforced at publish time |
| AzureServiceBus | yes | yes | Topic resource — different sender from send's queue | Independent subscriptions on the topic | **Publisher-axis.** Topology fully enforced at publish time |
| Pulsar | yes | yes | Distinct topic via `MessagingConventions.GetBroadcastTopicName` | One Pulsar subscription per logical subscriber | Convention-axis. Subscription type is consumer-controlled |
| RedisStreams | yes | yes | Distinct stream via `MessagingConventions.GetBroadcastTopicName` | Distinct consumer group per subscriber | Convention-axis. Operational cost: N consumer groups per stream |
| AwsSqs | yes | no (Phase 1; arrives in Phase 10) | — (Phase 10: SNS topic) | — (Phase 10: SQS queues subscribed to SNS) | Publisher-axis once Phase 10 lands; throws at registration until then |
| PostgreSql (outbox) | yes | no | — | — | Competing queue only |
| SqlServer (outbox) | yes | no | — | — | Competing queue only |
| InMemoryQueue | yes | no | — | — | Queue-only by design |
| InMemoryStorage | yes | yes | In-process subscriber enumeration | N/A | In-process; fan-out trivially supported |

**Axis terminology:**
- **Publisher-axis** (RabbitMQ, AzureServiceBus, AwsSqs+SNS in Phase 10): the publisher API materially differs between send and broadcast. Broadcast topology is fully enforced from the publish side.
- **Convention-axis** (NATS, Kafka, Pulsar, RedisStreams): wire protocol for send and broadcast is the same; the publisher distinguishes them by writing to a distinct topic name from `MessagingConventions.GetBroadcastTopicName`. **Subscriber configuration must match** — capability validation in Units 3a/3b emits startup warnings when convention-axis broadcast topics see mismatched subscriber configuration.
- **Operational cost:** convention-axis fan-out on Kafka and RedisStreams multiplies consumer-side resource usage linearly with subscriber count.

## Key Technical Decisions

Carried forward from origin spec §Key Technical Decisions:

1. **Two publisher interfaces, not a `DeliveryMode` enum.** Intent is part of the method name. Fail-fast at DI registration; natural C# tooling.
2. **Durability axis preserved via inheritance.** `IDirectSendPublisher : ISendPublisher`, `IOutboxSendPublisher : ISendPublisher`, same for broadcast. Only the semantic axis is added.
3. **`TenantId` as typed property, not a magic header.** `string? TenantId` on `PublishOptions` and `ConsumeContext`; provider maps it to `Headers.TenantId` on the wire. Consumers read the property, not the header dictionary.
4. **Capability declared at registration.** Provider extensions register only the interfaces the transport implements. No runtime `NotSupportedException`; DI resolution fails fast with a readable message.
5. **Outbox lives in Core, not per-provider.** `IOutboxSendPublisher` and `IOutboxBroadcastPublisher` are bound to a transport-agnostic `OutboxPublisherDecorator<TTransport>` in `Headless.Messaging.Core`. Provider packages implement only the Direct interfaces; Outbox composes via `services.AddOutbox<TTransport>()`. Eliminates the `11 × 2 × 2 = 44`-class explosion (replaced by 22 Direct classes + 1 decorator + 1 drainer + N stores). Concentrates tenancy stamping, composite `(TenantId, MessageId)` dedup, retry backoff, and dead-letter observation in one place.
6. **`MessagingConventions.GetBroadcastTopicName` is the publisher-side mechanism for convention-axis transports.** Capability validation in Units 3a/3b asserts the helper is wired into both publisher and consumer subscription path. Subscribers MUST configure distinct consumer groups (Kafka, RedisStreams) / queue groups (NATS) / subscriptions (Pulsar) for broadcast semantics — integration tests emit a warning when broadcast topics see fewer than two distinct subscriber group IDs.
7. **Completion contract for `await SendAsync<T>` / `await BroadcastAsync<T>` is broker-acknowledged durable accept.** Phase 1 pins a single mental model across all 11 transports: the call returns successfully only after the broker has durably accepted the message (transport-defined "durable accept" — JetStream `PubAck`, Kafka `acks=all`, RabbitMQ publisher confirms, ASB `SendMessageAsync` ack, SQS `SendMessageAsync` ack, Pulsar producer ack, RedisStreams `XADD` reply). For `IOutboxSendPublisher` / `IOutboxBroadcastPublisher`, the contract is **outbox-row commit**. Providers that cannot meet broker-ack by default (NATS core without JetStream, Kafka with `acks < all`, RabbitMQ without publisher confirms) MUST configure their producer for ack-by-default at registration time, or `MessagingCapabilityOptionsValidator` rejects the Direct interfaces with an `OptionsValidationException`. The OTel enricher (Unit 5) records `headless.messaging.completion = "broker_ack" | "outbox_commit"`. Attribute uses the `headless.messaging.*` prefix (not `messaging.headless.*`) per OTel naming spec. Out of scope: caller-selectable lower-latency `OnEnqueue` mode — deferred until a hot-path use case surfaces.
8. **Consumer surface unchanged.** `IConsume<T>` stays the single entry point. `ConsumeContext.DeliveryKind` is observable for diagnostics/tracing. **Business logic branching on `DeliveryKind` is discouraged** — model distinct intents as distinct message types.
9. **`IMessagePublisher` becomes `internal`.** Remains a shared base for pipeline behavior but is not a public resolution seam. YAGNI until a concrete wrapper use case surfaces.
10. **NATS stream config uses a callback-shaped options surface.** Matches Headless FluentValidation + hosting options pattern; no direct `StreamConfig` exposure in abstractions.
11. **Capability fail-fast via FluentValidation on a capability-descriptor options type.** `services.AddOptions<MessagingCapabilityOptions, MessagingCapabilityOptionsValidator>().ValidateOnStart()` per `CLAUDE.md`. Failures throw `OptionsValidationException` — no bespoke `IHostedService`, no custom exception hierarchy.
12. **P1⇄P2 transition contract for the outbox decorator (binding).** `OutboxPublisherDecorator<TTransport>`, `OutboxDrainer<TTransport>`, `IOutboxStore`, and `services.AddOutbox<TTransport>()` ship in Phase 1 / Unit 1b and survive into Phase 2 unchanged in class signature, DI registration, keying, and runtime semantics. Phase 2's behavior pipeline composes **around** the decorator (`BehaviorChain → OutboxPublisherDecorator<T> → IDirectSendPublisher<T>` on persist; `BehaviorChain → IDirectSendPublisher<T>` on drainer redispatch); it does not absorb, replace, or re-express outbox as an `IPublishBehavior<T>`. Rationale: the decorator is on the **transport axis** and is stateful; behaviors are on the **message axis** and stateless. Any future "behavior-shaped" outbox proposal is out of scope and would require its own RFC.
13. **Deduplication keys include `TenantId` when tenancy is in use.** Outbox tables, Redis idempotency keys, and any provider-level "seen-MessageId" dedup use a composite `(TenantId, MessageId)` key. Schema: dedup tables store `TenantId` as a nullable column; the composite unique constraint treats `NULL` as **equal to** `NULL` (PostgreSQL `NULLS NOT DISTINCT` clause; SQL Server emulates with paired filtered indices `WHERE TenantId IS NOT NULL` + `WHERE TenantId IS NULL` — see U3b for the exact migration). Resulting semantics: (a) within a single-tenant app, two inserts of the same `MessageId` with `TenantId = NULL` collide and the second is deduped (correct); (b) within a multi-tenant app, two inserts of the same `MessageId` for the same `TenantId` collide (correct); (c) two inserts of the same `MessageId` for different non-null `TenantId`s are distinct rows (correct); (d) a single-tenant insert (`NULL`) and a multi-tenant insert (`'acme'`) of the same `MessageId` are distinct rows (cannot collide). **Operational consequence:** a tenant context source that ever writes `NULL` `TenantId` rows in the same store as `non-null` rows is supported; the `NULL` rows form their own logical "no-tenant" partition and dedup within themselves. A startup validator on `IOutboxStore` checks that the registered tenant context source is consistent with the configured store schema (forbids `NULL` writes when `TenantContextRequired = true`).

## Open Questions

### Resolved During Planning

- *Should `DeliveryMode` be a flat enum on `PublishOptions`?* No — capability varies too much across the 11 transports; MassTransit's precedent shows intent belongs in the API surface.
- *Should `IRetryBackoffStrategy` be split?* No — it already has both delay and predicate; the work is to **wire it**, not restructure it.
- *Should `TenantId` be resolved automatically from an `ITenantContext` service?* Not in Phase 1 — auto-propagation via a pipeline behavior is Phase 3 (depends on Phase 2 behavior pipeline).

### Deferred to Implementation

- Exact namespace layout for the new interfaces (`Headless.Messaging.Abstractions` root vs a `Publishers/` sub-namespace) — land wherever the existing markers live.
- Whether `IScheduledPublisher` needs a broadcast counterpart on day one. Lean: day-one **only** if any provider already has scheduled broadcast support for free; otherwise defer to Phase 7.
- Naming for the `DeliveryKind` enum values (`Send` / `Broadcast` vs `Queued` / `FanOut`). Lean: mirror the interface names.
- Precise diagnostic message when a consumer resolves `IBroadcastPublisher` against a queue-only transport.

## Output Structure

```text
src/Headless.Messaging.Abstractions/
  ISendPublisher.cs                    # new
  IBroadcastPublisher.cs               # new
  IDirectSendPublisher.cs              # new (replaces IDirectPublisher intent)
  IOutboxSendPublisher.cs              # new (replaces IOutboxPublisher intent)
  IDirectBroadcastPublisher.cs         # new
  IOutboxBroadcastPublisher.cs         # new
  DeliveryKind.cs                      # new (Send | Broadcast)
  IMessagePublisher.cs                 # demoted to internal shared base
  PublishOptions.cs                    # + TenantId
  ConsumeContext.cs                    # + TenantId, DeliveryKind
  Headers.cs                           # + TenantId constant
  MessagingConventions.cs              # + GetBroadcastTopicName helper
  IDeadLetterObserver.cs               # new: transport-agnostic DLQ observer
  DeadLetterEvent.cs                   # new: envelope (TenantId, MessageId, MessageType, attempts, reason)

src/Headless.Messaging.Core/
  IOutboxStore.cs                      # new: persist + composite-(TenantId, MessageId) dedup + drain cursor
  OutboxEnvelope.cs                    # new: persisted record (payload, headers, tenancy, attempts, status)
  OutboxPublisherDecorator.cs          # new: implements IOutboxSendPublisher + IOutboxBroadcastPublisher; persists via IOutboxStore
  OutboxDrainer.cs                     # new: BackgroundService<TTransport> drains store, dispatches via Direct publisher
  OutboxRegistration.cs                # new: services.AddOutbox<TTransport>() extension

src/Headless.Messaging.OpenTelemetry/
  IActivityTagEnricher.cs              # new hook for consumer apps

docs/llms/
  messaging-envelope.md                # new: envelope shape, capability matrix, migration notes
```

## High-Level Technical Design

### Publisher Interface Shape (directional, not implementation)

```text
ISendPublisher
  SendAsync<T>(T message, PublishOptions? options, CancellationToken ct)

IBroadcastPublisher
  BroadcastAsync<T>(T message, PublishOptions? options, CancellationToken ct)

IDirectSendPublisher      : ISendPublisher
IOutboxSendPublisher      : ISendPublisher
IDirectBroadcastPublisher : IBroadcastPublisher
IOutboxBroadcastPublisher : IBroadcastPublisher
```

### Capability Registration (directional)

```text
services.AddHeadlessMessagingNats(opts => { ... })
  registers: IDirectSendPublisher, IDirectBroadcastPublisher
  + services.AddOutbox<NatsTransport>() binds the Outbox markers via the Core decorator

services.AddHeadlessMessagingSqs(opts => { ... })
  registers: IDirectSendPublisher
  does NOT register: IBroadcastPublisher implementations
  -> consumer requesting IBroadcastPublisher fails at host startup with a readable message
```

### Tenancy Flow (directional)

```text
caller sets PublishOptions.TenantId = "acme"
   |
publisher serializes -> Headers["headless-tenant-id"] = "acme"
   |
transport wire
   |
consumer pipeline reads Headers["headless-tenant-id"]
   |
ConsumeContext.TenantId = "acme"   (typed, not header lookup)
```

## Implementation Units

U-IDs are stable across the lifetime of this plan and never renumbered (gaps are fine). They mirror the spec's labeling: `U1, U1b, U2, U3a, U3b, U3c, U4, U5, U6, U7, U8, U9`. Total: **12 units**.

---

### U1 — Introduce `ISendPublisher` / `IBroadcastPublisher` and demote `IMessagePublisher`

> **DEFERRED — Phase 2.** Per the Phase 1 scope reconciliation in the Overview, this unit is binding spec for Phase 2 sequencing. Not implemented in Phase 1.

**Goal:** Land the two new intent-explicit interfaces and their four durability-marker specializations in the abstractions package. Make `IMessagePublisher` internal.

**Requirements:** R1, R2, R7.

**Dependencies:** None.

**Files:**
- Create: `src/Headless.Messaging.Abstractions/ISendPublisher.cs`
- Create: `src/Headless.Messaging.Abstractions/IBroadcastPublisher.cs`
- Create: `src/Headless.Messaging.Abstractions/IDirectSendPublisher.cs`
- Create: `src/Headless.Messaging.Abstractions/IOutboxSendPublisher.cs`
- Create: `src/Headless.Messaging.Abstractions/IDirectBroadcastPublisher.cs`
- Create: `src/Headless.Messaging.Abstractions/IOutboxBroadcastPublisher.cs`
- Create: `src/Headless.Messaging.Abstractions/DeliveryKind.cs`
- Modify: `src/Headless.Messaging.Abstractions/IMessagePublisher.cs` (make `internal`, keep as shared base)
- Modify: `src/Headless.Messaging.Abstractions/MessagePublisherExtensions.cs` (split extensions per-interface)
- Delete: `src/Headless.Messaging.Abstractions/IDirectPublisher.cs` and `IOutboxPublisher.cs` (replaced by the four new markers)
- Modify: `src/Headless.Messaging.Abstractions/MessagingConventions.cs` — add `GetBroadcastTopicName(Type messageType)` helper used by every convention-axis provider in Units 3a/3b. Helper produces a deterministic distinct name so `BroadcastAsync<T>` routes to a different wire destination than `SendAsync<T>` on transports where the publisher API does not encode the semantic difference natively.
- Test: `tests/Headless.Messaging.Tests.Unit/Abstractions/PublisherInterfaceShapeTests.cs`
- Test: `tests/Headless.Messaging.Tests.Unit/Abstractions/MessagingConventionsBroadcastTopicTests.cs` — asserts the helper returns a name distinct from the send-side name for the same type, is deterministic across calls, and survives generic type arguments.

**Approach:**
- `ISendPublisher.SendAsync<T>` and `IBroadcastPublisher.BroadcastAsync<T>` share a method signature shape with the existing `PublishAsync`; only the name and intent differ.
- Durability markers are empty interfaces — they exist for DI resolution and XML docs.
- `DeliveryKind` is a plain enum `{ Send, Broadcast }` exposed on `ConsumeContext`.
- XML docs on `ISendPublisher.SendAsync<T>` and `IBroadcastPublisher.BroadcastAsync<T>` state the completion contract verbatim per the Key Technical Decisions bullet: "the returned task completes only after the broker has durably accepted the message (transport-defined durable accept)." The corresponding doc on the `IOutbox…Publisher` markers states: "the returned task completes when the outbox row has committed; the drainer reaches broker-ack asynchronously."

**Patterns to follow:**
- Existing empty-marker pattern in `IDirectPublisher.cs` / `IOutboxPublisher.cs`.
- File-scoped namespaces, `sealed` where applicable per `CLAUDE.md`.

**Test scenarios:**
- Happy path: Interfaces compile and resolve via a fake DI container.
- Edge case: Attempting to resolve `IBroadcastPublisher` from a container that registered only `ISendPublisher` surfaces a clear missing-registration error.
- Integration: `ConsumeContext.DeliveryKind` is reachable from a consumer written against `IConsume<T>`.

**Verification:**
- Solution builds with `dotnet build --no-incremental -v:q -nologo /clp:ErrorsOnly` clean.
- No provider project references `IDirectPublisher` or `IOutboxPublisher` by their old names (Unit 3 lands provider edits).

---

### U1b — Land the transport-agnostic Outbox decorator + store + drainer in `Headless.Messaging.Core`

> **DEFERRED — Phase 2.** Per the Phase 1 scope reconciliation in the Overview, this unit is binding spec for Phase 2 sequencing. Not implemented in Phase 1. **Phase 1 scope adjustment:** the Outbox decorator is **single-transport-per-host** and **single-storage-per-host** in Phase 2 — the multi-transport coexistence claim ("multiple Outbox+Transport pairs can coexist", `AddOutbox<NatsTransport>()` and `AddOutbox<RabbitMqTransport>()` in the same host) is removed; multi-transport coexistence is deferred to a later phase pending a concrete consumer use case.

**Goal:** Implement Outbox once as a Core decorator over the Direct interfaces, so provider packages in Units 3a/3b only need to ship Direct publishers. Bind `IOutboxSendPublisher` / `IOutboxBroadcastPublisher` to the Core decorator via `services.AddOutbox<TTransport>()`. Eliminates the 11×2×2=44-class explosion; replaces with 22 Direct classes + 1 decorator + 1 drainer + N stores.

**Requirements:** R1, R2, R3 (capability registration is pure Direct after this unit; Outbox markers register only when `AddOutbox<TTransport>()` is called).

**Dependencies:** U1.

**Files:**
- Create: `src/Headless.Messaging.Core/IOutboxStore.cs` — persist, dedup-by-`(TenantId, MessageId)`, claim-and-drain cursor, mark-completed, mark-failed-with-attempt-count.
- Create: `src/Headless.Messaging.Core/OutboxEnvelope.cs` — payload, headers, `TenantId`, `MessageId`, `CorrelationId`, status (`Pending` / `Dispatched` / `Failed`), `AttemptCount`, `NextAttemptAt`, `DeliveryKind`.
- Create: `src/Headless.Messaging.Core/OutboxPublisherDecorator.cs` — implements `IOutboxSendPublisher` and `IOutboxBroadcastPublisher`. On `SendAsync` / `BroadcastAsync`: snapshots `PublishOptions.TenantId` (or resolves from `ICurrentTenant` later in Phase 3), writes one `OutboxEnvelope` row to `IOutboxStore` inside the ambient transaction, returns immediately. Never calls the transport directly.
- Create: `src/Headless.Messaging.Core/OutboxDrainer.cs` — `BackgroundService` generic over `TTransport`. Polls `IOutboxStore` for pending rows scoped to `TTransport`, dispatches each via the registered Direct publisher (`IDirectSendPublisher` for `DeliveryKind.Send`, `IDirectBroadcastPublisher` for `DeliveryKind.Broadcast`), applies `IRetryBackoffStrategy` per U4, emits `DeadLetterEvent` via `IDeadLetterObserver` per U8 on terminal failure.
- Create: `src/Headless.Messaging.Core/OutboxRegistration.cs` — `services.AddOutbox<TTransport>()` extension that binds the Outbox markers to the decorator and registers the drainer hosted service. Validates (via FluentValidation + `ValidateOnStart()`, per `CLAUDE.md`) that `IDirectSendPublisher` and/or `IDirectBroadcastPublisher` are registered for `TTransport` — otherwise startup fails with `OptionsValidationException`.
- Test: `tests/Headless.Messaging.Core.Tests.Unit/Outbox/OutboxPublisherDecoratorTests.cs` — asserts `SendAsync` writes to store and does not invoke transport.
- Test: `tests/Headless.Messaging.Core.Tests.Unit/Outbox/OutboxDrainerTests.cs` — asserts drainer dispatches via Direct publisher, retries via backoff, emits `DeadLetterEvent` after max attempts.
- Test: `tests/Headless.Messaging.Core.Tests.Integration/Outbox/CompositeDedupKeyTests.cs` — same `MessageId` across two `TenantId`s yields two distinct envelopes; same `(TenantId, MessageId)` is deduped.
- Create: `tests/Headless.Messaging.Core.Tests.Benchmarks/OutboxDecoratorBenchmarks.cs` — BenchmarkDotNet project measuring (a) decorator publish overhead vs a Direct publisher (target: <50µs added per call at p99), (b) drainer single-row dispatch overhead vs the underlying transport (target: <10% over the transport baseline), (c) drainer batch throughput at concurrency=1/8/64 against an in-memory store. Numbers committed in `docs/llms/messaging-envelope.md` so future PRs have a regression baseline. **Note:** This benchmark project is the regression baseline for the abstraction tax; if a Phase 2/3 refactor pushes overhead beyond the recorded p99 by >25%, it must be justified in PR description, not silently absorbed.

**Approach:**
- The decorator is the *only* place that captures tenancy onto the persisted envelope. Provider transport classes remain tenancy-agnostic — they read tenancy off `OutboxEnvelope.Headers` exactly as for any incoming `Headers["headless-tenant-id"]`.
- `IOutboxStore` is an interface in Core; concrete implementations (`PostgreSqlOutboxStore`, `SqlServerOutboxStore`) live in their own packages and ship in their respective provider Units (3a/3b for the SQL providers, no impact on transport-only providers like NATS/Kafka). Each SQL store package ships a raw-SQL `IStorageInitializer` (matching the existing `PostgreSqlStorageInitializer` pattern in this repo — no EF Core `DbContext`, no EF migrations) that idempotently creates `outbox_envelopes` with the composite `(TenantId, MessageId)` unique constraint, `(Status, NextAttemptAt)` covering index for the drainer's claim query, and `(ClaimedUntil)` partial index. Provider-package `Add{Provider}Messaging(...)` registers the initializer as an `IHostedService` and offers a `RunInitializerOnStartup` boolean (default `false` in `Production`, `true` in `Development`) so consumer apps decide between auto-init and a manual SQL deploy. Schema evolution ships as additional idempotent SQL files keyed on a `__headless_messaging_outbox_schema_version` row; destructive changes require a major-version bump of the provider package.
- Drainer is generic over `TTransport` for **single-transport-per-host** registration only in Phase 2. Multi-transport coexistence (`AddOutbox<NatsTransport>()` and `AddOutbox<RabbitMqTransport>()` in the same host) is deferred to a later phase pending a concrete consumer use case; the `<TTransport>` generic stays in the public surface as a forward-compat anchor but the host-startup validator rejects multiple `AddOutbox<...>()` calls in Phase 2.
- Composite dedup key `(TenantId, MessageId)` is enforced at the store level; null `TenantId` is treated as a distinct value per standard SQL semantics.
- **Graceful shutdown contract.** `OutboxDrainer<TTransport>` honors `IHostApplicationLifetime.ApplicationStopping`: (1) stop claiming new rows from `IOutboxStore` immediately, (2) await in-flight dispatches up to `OutboxOptions.ShutdownDrainTimeout` (default 30s, options-driven), (3) when the timeout elapses, in-flight rows are left in `Pending` state with their lease expired (`ClaimedUntil < UtcNow`) so the next process instance picks them up — never marked `Dispatched` until broker-ack confirmed, never marked `Failed` due to shutdown alone. The `IOutboxStore.ClaimAndDrain` API returns rows under a time-boxed lease so an abruptly-killed drainer's in-flight rows become reclaimable after the lease expires (`OutboxOptions.ClaimLeaseDuration`, default 5 minutes). At-least-once is preserved across shutdown; duplicates are deduped by composite key downstream.
- The decorator is registered with `ServiceLifetime.Scoped` so it picks up the ambient `IOutboxTransaction` / `DbContext` from the caller's scope.
- **Ambient-transaction contract.** The decorator requires an ambient transaction or open `DbContext` to honor the outbox guarantee. Behavior when no transaction is present is options-driven, not implicit: `OutboxOptions.NoAmbientTransactionBehavior` is a **two-value enum** (`RequireTransaction` (default) | `OpenSelfManaged`). `RequireTransaction` throws `InvalidOperationException` at `SendAsync` / `BroadcastAsync` time with a remediation hint pointing at the U6 doc. `OpenSelfManaged` opens and commits a single-statement transaction around the row insert (still atomic but loses the "publish atomic with caller's domain write" property — the option name is intentionally explicit). The earlier `BestEffortNoTransaction` value is **removed**: a non-transactional outbox write silently violates the at-least-once guarantee and there is no consumer use case where that tradeoff is correct. The validator from `OutboxRegistration.cs` cross-checks `IOutboxStore` capability — stores that cannot enlist (e.g., a future Redis-backed store) refuse to register with `RequireTransaction`.
- **P1⇄P2 binding contract (carried forward from Key Technical Decisions).** Class signatures of `OutboxPublisherDecorator<TTransport>`, `OutboxDrainer<TTransport>`, `IOutboxStore`, and `services.AddOutbox<TTransport>()` shipped in this Unit are binding through Phase 2. Phase 2's behavior pipeline composes around them and does not re-express outbox as an `IPublishBehavior<T>`. Implementation choices that would force a Phase 2 rewrite of the decorator's transport-axis identity (e.g., resolving the underlying Direct publisher through a per-message generic, splitting tenancy stamping out of the decorator, moving `(TenantId, MessageId)` dedup into a separate behavior) are out of scope and rejected at review.

**Patterns to follow:**
- `Headless.DistributedLocks.Core` package layout — abstractions + Core implementation, providers shipping concrete stores.
- `Headless.Caching.Hybrid` decorator pattern over `ICache`.
- FluentValidation + `services.AddOptions<T, TValidator>().ValidateOnStart()` per `CLAUDE.md`.

**Test scenarios:**
- Happy path: `SendAsync` persists envelope, returns; drainer dispatches and marks `Dispatched`.
- Happy path: `BroadcastAsync` persists with `DeliveryKind = Broadcast`; drainer routes to `IDirectBroadcastPublisher`.
- Edge case: Transport throws transient → drainer reschedules per `IRetryBackoffStrategy` → eventually succeeds.
- Edge case: Transport throws terminal → drainer emits `DeadLetterEvent` after `MaxAttempts` and marks `Failed`.
- Edge case: Same `MessageId` published twice for same `TenantId` → second insert deduped by composite-key constraint, drainer dispatches once.
- Edge case: Same `MessageId` for two different `TenantId`s → two envelopes, two dispatches.
- Error path: `AddOutbox<TTransport>()` called without a registered `IDirectSendPublisher` for `TTransport` → host startup fails with `OptionsValidationException` listing the missing registration.
- Error path: `SendAsync` called outside any ambient transaction with `NoAmbientTransactionBehavior = RequireTransaction` (default) → throws `InvalidOperationException` naming the option, the transport, and the U6 doc anchor.
- Edge case: `SendAsync` called outside any ambient transaction with `NoAmbientTransactionBehavior = OpenSelfManaged` → row insert commits in a self-opened transaction; envelope is observable in the store afterward and dispatched by the drainer.
- Edge case (graceful shutdown): drainer is mid-batch when `IHostApplicationLifetime.StopApplicationAsync` fires → no rows are marked `Failed` due to shutdown, in-flight rows return to claimable state after the lease expires, and a fresh drainer in a follow-up host run picks them up and dispatches them exactly once per the dedup key.
- Edge case (lease expiry): an abruptly-killed drainer leaves rows in `Pending` with `ClaimedUntil > UtcNow` → after `ClaimLeaseDuration` elapses, a peer drainer reclaims and dispatches them.

**Verification:**
- No provider package under `src/Headless.Messaging.Nats/`, `Kafka/`, `RabbitMq/`, etc. contains an `IOutboxSendPublisher` or `IOutboxBroadcastPublisher` implementation. Verified by `grep -r "class.*: IOutbox" src/Headless.Messaging.* --include='*.cs'` returning only the Core decorator.
- `services.AddHeadlessMessagingNats(...).AddOutbox<NatsTransport>()` resolves all four publisher markers; omitting `AddOutbox` leaves only the two Direct markers resolvable (DI failure on Outbox resolution surfaces at host startup, not at runtime).

---

### U2 — Promote `TenantId` to first-class envelope

**Goal:** Add typed `TenantId` to `PublishOptions` and `ConsumeContext`, add `Headers.TenantId` constant, and wire header↔property mapping in the shared publisher/consumer base.

**Requirements:** R4.

**Dependencies:** U1.

**Files:**
- Modify: `src/Headless.Messaging.Abstractions/PublishOptions.cs` (add `string? TenantId`)
- Modify: `src/Headless.Messaging.Abstractions/ConsumeContext.cs` (add `string? TenantId`, `DeliveryKind` property)
- Modify: `src/Headless.Messaging.Abstractions/Headers.cs` (add `TenantId = "headless-tenant-id"`)
- Modify: `src/Headless.Messaging.Core/` shared publish/consume pipeline (map property ↔ header)
- Test: `tests/Headless.Messaging.Tests.Unit/Abstractions/TenantIdRoundTripTests.cs`

**Approach:**
- On publish: the pipeline reconciles `PublishOptions.TenantId` and `headers["headless-tenant-id"]` with **fail-fast on disagreement**:
  - Property set, raw header unset → pipeline writes the property value to the raw header.
  - Property unset, raw header set → publish is **rejected** with `InvalidOperationException("TenantId raw header set without typed PublishOptions.TenantId — typed property is the authoritative seam; do not write the raw header directly.")`. Rationale: the raw header is reserved for transport-internal use; consumer code that bypasses the typed property is a strong indicator of a bug or an exfiltration attempt.
  - Both set, equal → no-op.
  - Both set, **disagree** → publish is **rejected** with `InvalidOperationException("TenantId property and raw header disagree. property={prop} header={hdr}")`. Silent overwrite was the prior behavior; it is replaced with a hard failure to surface header-injection attempts at publish time rather than letting a forged header round-trip through the outbox row, the OTel `headless.messaging.tenant_id` tag, and any downstream sink that consumes either.
- **Trust model (carries forward from R4 with a sharper boundary):** `TenantId` carries no authenticity guarantee end-to-end — a malicious or misconfigured publisher on a shared broker can still write any value into the typed property. The publish-time mismatch check above defends against the *narrow* case where attacker code can set the raw header dictionary but not the typed property (e.g., a low-privilege middleware injecting `headers`). Apps requiring full cross-tenant authenticity layer their own check via an `IConsumeBehavior<T>` in Phase 2 that validates `ConsumeContext.TenantId` against an out-of-band signal (mTLS SAN, signed header, channel binding). Consumer apps with a multi-tenant trust boundary MUST NOT trust `ConsumeContext.TenantId` alone for authorization decisions; this is documented as a callout in U6.
- On consume: the pipeline reads `headers["headless-tenant-id"]` and populates `ConsumeContext.TenantId`. Missing header → property is `null`. The consume-side header value is bounds-checked to the same 200-char limit and character-set rules applied on publish (via `Headless.Checks`); a header that exceeds the limit or carries disallowed characters is treated as a malformed envelope — the message is rejected (logged at warning, surfaced to `IDeadLetterObserver` per U8) rather than silently truncated. This blocks an upstream sender on a shared broker from forging an oversized `headless-tenant-id` value that would propagate into the outbox `TenantId` column or OTel spans.
- Validate `TenantId` length (match `MessageId` 200-char limit for symmetry) via `Headless.Checks` at the publisher boundary.

**Patterns to follow:**
- `MessageId` / `CorrelationId` handling in `PublishOptions` — tenancy mirrors that pattern exactly.
- `Headless.Checks` `Argument.*` validation per `CLAUDE.md`.

**Test scenarios:**
- Happy path: Setting `PublishOptions.TenantId = "acme"` produces `Headers["headless-tenant-id"] = "acme"` on the wire fake.
- Happy path: A consume pipeline wrapping a fake transport with `headers["headless-tenant-id"] = "acme"` yields `ConsumeContext.TenantId == "acme"`.
- Edge case: Missing tenant header → `ConsumeContext.TenantId` is `null`, not empty string.
- Edge case: Caller sets both property and raw header with different values → property wins, debug warning logged.
- Error path: `TenantId` exceeds 200 chars → `Argument.*` throws.

**Verification:**
- Round-trip test passes for both Send and Broadcast pipelines.
- No transport-specific code parses the tenant header directly — all consumers go through `ConsumeContext.TenantId`.

**Rollout-state callout (U2 ⇄ U3a/U3b):** U2 introduces the envelope contract (property + header + shared pipeline). Per-provider dedup-key and outbox unique-constraint migrations to composite `(TenantId, MessageId)` land inside U3a and U3b. Interim state between U2 and full U3a/U3b completion: `TenantId` is envelope-visible on all providers but dedup-correct only on providers whose migration has landed. The U6 capability matrix tracks per-provider dedup-migration status until every provider is green.

---

### U3a — Migrate Tier-1 providers (NATS, RabbitMQ, AzureServiceBus, AwsSqs) to the new interfaces

> **DEFERRED — Phase 2.** Per the Phase 1 scope reconciliation in the Overview, this unit is binding spec for Phase 2 sequencing. Not implemented in Phase 1. **Tier annotation:** Tier-1 providers (NATS, RabbitMQ, ASB, SQS) ship full Direct + Outbox-marker resolvability with the completion-contract enforcement described below. SQS broadcast remains deferred to Phase 10 regardless of tier.

**Goal:** Land reference implementations for the highest-traffic providers first. NATS anchors the broadcast-capable shape; SQS anchors the Send-only / capability-not-registered shape. ASB and RabbitMQ validate that the topology-per-semantics approach works outside NATS.

**Note on "register all four markers" wording (U3a + U3b):** Per the Core-decorator decision in Key Technical Decisions and U1b, provider packages register only the Direct interfaces (`IDirectSendPublisher` / `IDirectBroadcastPublisher`) keyed by their transport type. The Outbox interfaces resolve through `OutboxPublisherDecorator<TTransport>` once the consumer chains `services.AddOutbox<TTransport>()`. Capability-registration tests in this Unit must cover both the Direct-only and Direct+Outbox configurations to assert end-to-end resolvability.

**Requirements:** R1, R2, R3.

**Dependencies:** U1, U1b.

**Files:**
- Modify: `src/Headless.Messaging.Nats/` — register Direct send + Direct broadcast publishers keyed by `NatsTransport`. **Completion contract enforcement:** registration validator rejects the Direct interfaces unless JetStream is configured (NATS core has no broker ack); core-only consumers must register `IOutboxSendPublisher` / `IOutboxBroadcastPublisher` instead and pair with `services.AddOutbox<NatsTransport>()`.
- Modify: `src/Headless.Messaging.RabbitMq/` — register Direct send (direct exchange) + Direct broadcast (fanout exchange) publishers keyed by `RabbitMqTransport`. **Completion contract enforcement:** registration validator asserts the framework's `HeadlessRabbitMqChannelFactory` wrapper is registered (the wrapper always opts into `ConfirmSelectAsync` + per-publish `WaitForConfirmsOrDieAsync`); raw `IConnectionFactory` registrations bypass this guarantee and are rejected at startup. **Broker-ack caveat:** RabbitMQ publisher confirms are per-channel-`DeliveryTag`, not per-publish — the wrapper tracks outstanding tags and the publish call returns only after the matching `BasicAck` arrives or `WaitForConfirmsOrDieAsync` raises. The "broker_ack" completion claim in Decision #7 holds only for publishes that flow through the wrapper; raw-channel publishes are explicitly out of contract.
- Modify: `src/Headless.Messaging.AzureServiceBus/` — register Direct send (queue) + Direct broadcast (topic + subscription) publishers keyed by `AzureServiceBusTransport`. ASB's `SendMessageAsync` is broker-ack by default; no extra producer config required.
- Modify: `src/Headless.Messaging.AwsSqs/` — register Direct send only keyed by `AwsSqsTransport`; do **not** register broadcast (SNS-fronted topology lands in Phase 10). SQS `SendMessageAsync` is broker-ack by default.
- Add: per-provider `MessagingCapabilityOptions` + `MessagingCapabilityOptionsValidator : AbstractValidator<MessagingCapabilityOptions>` (same-file pattern, per `CLAUDE.md`), wired via `services.AddOptions<MessagingCapabilityOptions, MessagingCapabilityOptionsValidator>().ValidateOnStart()`. Shared validation helper in `Headless.Messaging.Core` performs (a) the "declared interfaces are registered" cross-check **and** (b) the completion-contract cross-check (Direct interfaces registered only when the producer is configured to deliver broker-ack durable accept). No `IHostedService`, no custom exception type — failures surface as `OptionsValidationException` at host startup with the offending transport, the failing rule, and remediation in the message.
- Test: `tests/Headless.Messaging.Nats.Tests.Integration/CapabilityRegistrationTests.cs`
- Test: `tests/Headless.Messaging.RabbitMq.Tests.Integration/CapabilityRegistrationTests.cs`
- Test: `tests/Headless.Messaging.AzureServiceBus.Tests.Integration/CapabilityRegistrationTests.cs`
- Test: `tests/Headless.Messaging.AwsSqs.Tests.Integration/CapabilityRegistrationTests.cs`
- Test: `tests/Headless.Messaging.Nats.Tests.Integration/BroadcastConventionAxisWarningTests.cs`

**Convention-axis warning test scenarios (NATS, this Unit):**
- Two consumers subscribe to the broadcast subject produced by `MessagingConventions.GetBroadcastTopicName<T>()` with the **same** queue group → assertion that startup logs WARN: `"NATS broadcast subject {subject} has subscribers sharing queue group '{group}'; broadcast semantics will degrade to competing-consumer (one subscriber receives each message). Configure distinct queue groups per subscriber for broadcast, or omit the queue group entirely."` Capability validation does not fail the host — broadcast on convention-axis transports is a subscriber-side contract; the publisher and host can only warn.
- Sanity counterpart: two consumers subscribe with **distinct** queue groups (or no queue group) → no warning logged; both consumers receive every published message.
- Send-side regression guard: a `SendAsync<T>` integration test asserts the wire subject differs from `GetBroadcastTopicName<T>()` so the two semantic axes do not collide on the same subject by accident.

---

### U3b — Migrate Tier-2 providers (Kafka, Pulsar, RedisStreams, InMemoryStorage, InMemoryQueue, PostgreSql, SqlServer)

> **DEFERRED — Phase 2.** Per the Phase 1 scope reconciliation in the Overview, this unit is binding spec for Phase 2 sequencing. Not implemented in Phase 1. **Tier annotation:** Kafka / Pulsar / RedisStreams / InMemory* are **Tier-2 best-effort** — broadcast is convention-axis with subscriber-side cooperation, and dedup ordering guarantees lag broker-native equivalents on Tier-1. PostgreSql / SqlServer ship `IOutboxStore` implementations only when U1b lands (they do not register messaging-Direct publishers in this phase).

**Goal:** Apply the U3a reference shape to the remaining seven providers. Split from U3a to keep the blast radius of any single PR bounded.

**Requirements:** R1, R2, R3.

**Dependencies:** U3a landed.

**Files:**
- Modify: `src/Headless.Messaging.Kafka/` — register Direct send + Direct broadcast publishers keyed by `KafkaTransport` (broadcast via distinct consumer groups on the same topic). **Completion contract enforcement:** registration validator rejects Direct interfaces unless `ProducerConfig.Acks == Acks.All`; lower ack levels are configurable but require explicit opt-in via `MessagingCapabilityOptions.AllowReducedAcks = true` plus the human reason recorded in options metadata.
- Modify: `src/Headless.Messaging.Pulsar/` — register Direct send (Exclusive) + Direct broadcast (Shared/Failover) publishers keyed by `PulsarTransport`. Pulsar `SendAsync` is broker-ack by default.
- Modify: `src/Headless.Messaging.RedisStreams/` — register Direct send + Direct broadcast publishers keyed by `RedisStreamsTransport` (distinct consumer groups for broadcast). Redis `XADD` reply is treated as broker-ack-equivalent (memory persistence); the validator records the AOF-config caveat in startup logs once per host.
- Modify: `src/Headless.Messaging.InMemoryStorage/` — register Direct send + Direct broadcast publishers keyed by `InMemoryStorageTransport`; in-memory fan-out is trivially supported and the call returns when the in-memory store has accepted the envelope.
- Modify: `src/Headless.Messaging.InMemoryQueue/` — register Direct send only keyed by `InMemoryQueueTransport`; broadcast is out of scope by design. Channel-accept is treated as broker-ack-equivalent.
- Modify: `src/Headless.Messaging.PostgreSql/` — register Direct send only keyed by `PostgreSqlTransport`. Direct send returns when the row INSERT commits (the publisher *is* the durable store). Also ships `PostgreSqlOutboxStore : IOutboxStore` so consumers can pair `AddHeadlessMessagingPostgreSql(...)` with `AddOutbox<NatsTransport>()` (or any transport) and persist the outbox table in their PG database.
- Create: `src/Headless.Messaging.PostgreSql/Storage/PostgreSqlOutboxStorageInitializer.cs` — raw-SQL `IStorageInitializer` (matches the existing `PostgreSqlStorageInitializer` pattern) that idempotently creates the `outbox_envelopes` table (composite unique on `(TenantId, MessageId)` with PG's `NULLS NOT DISTINCT` clause for tenant=null collisions), claim-query covering index, and `ClaimedUntil` partial index. No EF Core `DbContext`, no EF migrations — schema evolution ships as additional idempotent SQL keyed on a `__headless_messaging_outbox_schema_version` row. Postgres-15 baseline.
- Modify: `src/Headless.Messaging.SqlServer/` — register Direct send only keyed by `SqlServerTransport`; same row-commit semantic. Also ships `SqlServerOutboxStore : IOutboxStore`.
- Create: `src/Headless.Messaging.SqlServer/Storage/SqlServerOutboxStorageInitializer.cs` — raw-SQL `IStorageInitializer`; SQL Server emulates `NULLS NOT DISTINCT` via a filtered unique index on `(TenantId, MessageId) WHERE TenantId IS NOT NULL` plus a second filtered unique index on `(MessageId) WHERE TenantId IS NULL` (documented in a SQL comment so future maintainers don't "fix" it). No EF Core `DbContext`, no EF migrations. SQL Server 2022 baseline.
- Test: `tests/Headless.Messaging.*.Tests.Integration/CapabilityRegistrationTests.cs` per provider.
- Test: `tests/Headless.Messaging.Kafka.Tests.Integration/BroadcastConventionAxisWarningTests.cs`
- Test: `tests/Headless.Messaging.Pulsar.Tests.Integration/BroadcastConventionAxisWarningTests.cs`
- Test: `tests/Headless.Messaging.RedisStreams.Tests.Integration/BroadcastConventionAxisWarningTests.cs`

**Convention-axis warning test scenarios (Kafka, Pulsar, RedisStreams):**
- **Kafka:** two consumers subscribe to the broadcast topic with the **same** `group.id` → assertion that startup logs WARN: `"Kafka broadcast topic {topic} has subscribers sharing group.id='{group}'; broadcast semantics will degrade to competing-consumer (one subscriber receives each message). Configure distinct group.id per subscriber for broadcast."` Distinct `group.id` counterpart logs no warning.
- **RedisStreams:** two consumers subscribe to the broadcast stream with the **same** consumer group → assertion that startup logs WARN: `"RedisStreams broadcast stream {stream} has subscribers sharing consumer group '{group}'; broadcast semantics will degrade to competing-consumer. Configure distinct consumer groups per subscriber for broadcast."` Distinct counterpart logs no warning.
- **Pulsar:** two logical subscribers reuse the **same** subscription name on the broadcast topic → assertion that startup logs WARN: `"Pulsar broadcast topic {topic} has subscribers sharing subscription '{name}'; broadcast semantics will degrade to competing-consumer under Shared/Failover subscription types. Configure distinct subscription names per logical subscriber for broadcast."` Distinct counterpart logs no warning.
- **Send-side regression guard (all three):** a `SendAsync<T>` integration test asserts the wire topic/stream/subscription differs from `GetBroadcastTopicName<T>()` so the two semantic axes do not collide on the same destination by accident.
- **Out of scope:** InMemoryStorage in-process fan-out is verified by existing per-consumer-delivery tests, no convention-axis warning needed (no group concept). InMemoryQueue and PostgreSql/SqlServer register Send only; broadcast warning tests do not apply.

---

### U3c — Downstream consumer migration (existing tests, demos, samples)

> **DEFERRED — Phase 2.** Per the Phase 1 scope reconciliation in the Overview, this unit is binding spec for Phase 2 sequencing. Not implemented in Phase 1.

**Goal:** Migrate every in-repo consumer of the old `IDirectPublisher` / `IOutboxPublisher` interfaces so the solution compiles and all pre-existing tests pass against the new shape. Distinct from the new interface-shape tests introduced alongside U1-U3b.

**Requirements:** R1, R9 (docs+examples stay truthful).

**Dependencies:** U3a + U3b.

**Files:**
- Modify: every `tests/Headless.Messaging.*.Tests.{Unit,Integration}/` test file that references `IDirectPublisher` or `IOutboxPublisher` — rename to `IDirectSendPublisher` / `IOutboxSendPublisher` or split into Send+Broadcast variants where the test covers both semantics.
- Modify: every `tests/Headless.Messaging.Tests.Harness/` helper and builder.
- Modify: every `demo/` and `samples/` application that constructs a publisher — replace with the new interfaces and add a broadcast demo where the transport supports it.
- Modify: `src/Headless.Messaging.Testing/` — the testing harness package exposes:
  - `FakeSendPublisher` implementing `IDirectSendPublisher` + `IOutboxSendPublisher`; records every `SendAsync` call into an ordered, tenant-aware `IReadOnlyList<RecordedSend>`.
  - `FakeBroadcastPublisher` implementing `IDirectBroadcastPublisher` + `IOutboxBroadcastPublisher`; records every `BroadcastAsync` call into `IReadOnlyList<RecordedBroadcast>`.
  - `MessagingTestHarness` entry-point that wires both fakes into DI with a single `services.AddHeadlessMessagingTestHarness()` call and exposes both recorders.
  - Assertion helpers (designed for AwesomeAssertions extension style): `harness.ShouldHaveSent<T>(predicate)`, `ShouldHaveBroadcast<T>(predicate)`, `ShouldHaveSentExactly(n)`, `ShouldHaveSentForTenant("acme")`, `ShouldNotHaveBroadcast<T>()`.
  - `FakeDeadLetterObserver` capturing `DeadLetterEvent`s for assertion on failure-path tests.
- Modify: `src/Headless.Messaging.Dashboard/` and `src/Headless.Messaging.Dashboard.K8s/` — update references to renamed publisher interfaces so packages compile. Rendering `DeliveryKind` in UI views is Phase 5 scope, not Phase 1.
- Modify: in-repo runtime call-sites (per "In-repo migration call-sites" under System-Wide Impact below):
  - `src/Headless.Caching.Hybrid/HybridCache.cs` → `IDirectSendPublisher` (cache invalidation is point-to-point Send).
  - `src/Headless.Permissions.Core/Definitions/DynamicPermissionDefinitionStore.cs` + `src/Headless.Permissions.Core/Setup.cs` → `IDirectSendPublisher` (or `IOutboxSendPublisher` when transactional — pick per call-site).
  - `src/Headless.DistributedLocks.Core/RegularLocks/DistributedLockProvider.cs` + `src/Headless.DistributedLocks.Core/Setup.cs` → `IDirectSendPublisher`.

**Approach:**
- Single grep/sed pass renames: `IDirectPublisher` → `IDirectSendPublisher`, `IOutboxPublisher` → `IOutboxSendPublisher`.
- Broadcast-specific tests and demos are net-new and added where the provider supports it.
- No test is deleted; failing tests that cannot be migrated indicate a real semantic gap and must be surfaced, not silenced.

**Test scenarios:**
- The full pre-refactor test suite passes on the new shape (parity baseline).
- New broadcast-capable demos resolve `IBroadcastPublisher` and exercise fan-out end-to-end.
- Attempting to resolve `IBroadcastPublisher` from the SQS demo fails at host start with a readable message.

**Verification:**
- `dotnet test` green across every test project.
- `grep -r "IDirectPublisher\b\|IOutboxPublisher\b" demo/ samples/ tests/ src/` returns zero matches.

---

#### Shared Guidance for U3a / U3b

**Approach (all providers):**
- Rename existing publisher class(es) to reflect the new interface set; implementation logic stays identical for Send-side.
- Broadcast-capable providers add a parallel publisher class that uses the transport's native fan-out topology (exchange type, subscription type, distinct consumer group, etc.).
- **Broadcast partial-failure semantics (Phase 1 default):** `BroadcastAsync<T>` is best-effort — the call succeeds if the publish to the broker succeeds, regardless of per-subscriber downstream delivery outcomes. Per-subscriber terminal failures surface via `IDeadLetterObserver` (U8), not via the publish call's return value or exception. Providers with transactional fan-out (outbox broadcast in Phase 8) can promote to at-least-once-to-every-subscriber; Phase 1 does not attempt transactional fan-out across heterogeneous subscribers. Alternatives considered and rejected: (a) "throw if any subscriber fails" — impossible on async transports where subscriber identity is not known at publish time; (b) "all-or-nothing via XA" — no supported transport in the matrix offers this. Matches MassTransit/Wolverine/NServiceBus defaults.
- Queue-only providers do **not** throw `NotSupportedException` from a Broadcast method — they simply do not register the interface. DI + `ValidateOnStart` surface the gap at host start.
- Each provider's DI extension registers only the interfaces actually implemented, documents capability in XML docs, and wires a `MessagingCapabilityOptions` + `MessagingCapabilityOptionsValidator` via `AddOptions<,>().ValidateOnStart()` that cross-checks declared vs registered interfaces. Failures surface as `OptionsValidationException` — no bespoke hosted service, no custom exception type.
- **Shared capability contract suite.** `tests/Headless.Messaging.Tests.Harness/CapabilityContract/` defines a reusable xUnit fixture every provider integration test inherits: (1) registered interfaces exactly match the provider's declared capability vector, (2) Send delivers to one of N competing consumers, (3) Broadcast (when declared) delivers to all N subscribers, (4) resolving an undeclared interface fails with the standard DI "no service registered" error, and host startup with a capability mismatch fails with `OptionsValidationException`. Providers implement a thin fixture adapter; contract assertions are centralized.
- Dedup tables / Redis idempotency keys / outbox unique constraints are updated to composite `(TenantId, MessageId)` as part of the same PR that adds `TenantId` to the envelope (U2 dependency).

**Patterns to follow:**
- Existing DI registration shape in each provider's `Add{Provider}Messaging(...)` extension.
- `Headless.Hosting` options pattern for capability-conditional registration; `ValidateOnStart` for fail-fast.

**Test scenarios (applicable to both units):**
- Happy path (broadcast-capable): resolving `IDirectBroadcastPublisher` returns a functional publisher; `BroadcastAsync<T>` fans out to N subscribers in an integration test.
- Happy path (send-only): resolving `IDirectSendPublisher` returns a functional publisher; `SendAsync<T>` delivers to one of N competing consumers.
- Error path: resolving `IBroadcastPublisher` from a send-only provider fails at `IHost.StartAsync` with a message naming the provider and pointing at the capability matrix doc.
- Integration: Broadcast to two different consumer groups on the same Kafka topic delivers to both.
- Integration: Send via outbox on PostgreSql persists to the outbox table keyed by `(TenantId, MessageId)`, and the dispatcher delivers exactly once.

**Verification:**
- All provider integration test suites pass under Testcontainers.
- `dotnet build --no-incremental -v:q -nologo /clp:ErrorsOnly` clean.
- `grep -r "NotSupportedException.*Broadcast"` returns zero results — capability is a registration concern, not a runtime throw.

---

### U4 — Wire `IRetryBackoffStrategy` across every provider's dispatch loop

**Goal:** Every provider's consume pipeline honors the configured `IRetryBackoffStrategy.ShouldRetry` + `GetNextDelay` before falling through to DLQ. Currently only a subset wires it; NATS does not.

**Requirements:** R5.

**Dependencies:** U3a + U3b (so the dispatch classes are already in their final shape).

**Files:**
- Audit + modify: each `src/Headless.Messaging.*/` consumer/dispatcher class that handles message failure.
- Modify: `src/Headless.Messaging.Core/` if a shared retry wrapper makes sense after the audit.
- Test: `tests/Headless.Messaging.*.Tests.Integration/RetryBackoffTests.cs` per provider.

**Approach:**
- **Define the cross-provider retry contract first** (documented in U6 and enforced by tests before provider edits land):
  - *Attempt* = one consumer invocation that returned or threw. Delivery-count header (`headless-attempt`) increments exactly once per attempt, regardless of transport-native redelivery counters.
  - *Delay source* = app-enforced when `GetNextDelay` returns a non-null `TimeSpan`; transport-enforced only when the strategy explicitly returns `null` and the provider has a native redelivery primitive.
  - *Immediate retry* = in-memory retry on the same consumer instance within the current dispatch; does not bump transport redelivery count, does bump `headless-attempt`.
  - *Requeue retry* = transport redelivery; bumps both transport count and `headless-attempt`. Provider picks one per dispatch based on `GetNextDelay` value and its own capability.
  - *Delivery count* surfaced to the strategy = `headless-attempt`, not the transport-native value. Single number every strategy sees.
- Audit pass: for each provider, locate the `try/catch` around consumer invocation and document today's behavior (retry count source? fixed delay? immediate DLQ?) against the contract above.
- Replace per-provider retry logic with `IRetryBackoffStrategy` resolution from DI. **Default strategy shape (applied uniformly across all providers in Phase 1):** exponential backoff `100ms → 200ms → 400ms → 800ms → 1600ms` with `±25%` jitter, hard per-delay cap of `30s`, `MaxAttempts = 5` (on the 6th attempt the message goes to DLQ), and `ShouldRetry = true` for all exceptions except `OperationCanceledException` and a configurable `NonRetryableExceptionTypes` set. Rationale: matches MassTransit/NServiceBus community default; avoids the pathological "retry forever at a fixed interval" that ad-hoc per-provider logic tends to produce. Providers whose current behavior already aligns (e.g., `ExponentialBackoffStrategy` in `Headless.Messaging.Core`) keep shape; providers with ad-hoc behavior adopt the default explicitly and document the change in the U4 audit.
- Hook `ShouldRetry(exception)` before scheduling the next delay; if it returns false, short-circuit to DLQ.
- Where a provider has native retry primitives (RabbitMQ dead-letter exchange, ASB delivery count, JetStream redelivery), keep those as the **transport** mechanism but drive them from `IRetryBackoffStrategy` outputs.
- **Consumer-app override surface (three layers, documented in U6).**
  1. **Replace globally:** `services.AddSingleton<IRetryBackoffStrategy, MyStrategy>()` after the messaging registration replaces the default for all providers. Standard DI semantics; no special hook.
  2. **Tune the default without rewriting it:** `services.Configure<RetryBackoffOptions>(o => { o.BaseDelay = TimeSpan.FromMilliseconds(50); o.MaxAttempts = 8; o.JitterRatio = 0.1; o.NonRetryableExceptionTypes.Add(typeof(MyDomainException)); })`. The default `ExponentialBackoffStrategy` reads `RetryBackoffOptions` so most consumers tweak rather than replace.
  3. **Per-message-type override:** register `IRetryBackoffStrategy<TMessage>` keyed by message CLR type; the dispatch loop resolves the typed strategy first and falls back to the unkeyed `IRetryBackoffStrategy`. Lets consumers say "retry payment messages 20 times, everything else 5".
- All three layers are validated at startup via `RetryBackoffOptionsValidator` (FluentValidation, `ValidateOnStart()`); invalid combinations (e.g., `MaxAttempts=0`, `BaseDelay > MaxDelay`) fail fast with `OptionsValidationException`. The override surface is the same across all 11 providers because the dispatch loop resolves through DI — providers do not need to know which layer the consumer used.
- **Two distinct backoff option types — do not share `RetryBackoffOptions` across consumer-dispatch and outbox-redispatch.** The consumer-dispatch loop (this unit) reads `RetryBackoffOptions` (defaults: `MaxAttempts = 5`, `BaseDelay = 100ms`, `MaxDelay = 30s`, `JitterRatio = 0.25`) — calibrated for transient consumer-side failures where 5 attempts over ~3s either succeed or signal a real bug, then route to DLQ. The Outbox drainer's redispatch loop (Phase 2, U1b) reads a separate `OutboxRedispatchBackoffOptions` (defaults: `MaxAttempts = 20`, `BaseDelay = 1s`, `MaxDelay = 5min`, `JitterRatio = 0.25`) — calibrated for *transport-availability* failures where the broker is down for minutes-to-hours and the outbox row must persist until the broker recovers. Sharing a single options type would either (a) DLQ outbox rows after 5 short attempts during a routine broker restart, defeating the durability guarantee, or (b) retry consumer business-logic exceptions for an hour, hiding bugs behind backoff. Both options types validate via FluentValidation with `ValidateOnStart()`; both bind from `Headless:Messaging:RetryBackoff` and `Headless:Messaging:OutboxRedispatchBackoff` sections respectively. The default `ExponentialBackoffStrategy` is parameterized by which options type it reads — the same strategy class instantiates twice (once per options type) under DI keyed registration so consumer-dispatch and outbox-redispatch share the algorithm without sharing the parameters.

**Patterns to follow:**
- Whatever provider already wires the strategy best today — adopt its shape as the reference.

**Test scenarios:**
- Happy path: Strategy returns `ShouldRetry=true` and `GetNextDelay=500ms` → consumer retried after ~500ms (timing tolerant).
- Happy path: Strategy returns `ShouldRetry=false` → consumer **not** retried, message goes straight to DLQ.
- Edge case: Strategy returns `GetNextDelay=null` but `ShouldRetry=true` → treated as immediate retry or transport default; documented and tested.
- Error path: `IRetryBackoffStrategy` itself throws → provider falls back to a safe default and logs.
- Integration: A transient exception on the 1st and 2nd attempt but success on the 3rd results in one consumed message; no DLQ entry.
- Integration: A non-transient exception goes directly to DLQ without retry.

**Verification:**
- Every provider's integration tests exercise both `ShouldRetry` true/false branches.
- Audit doc (internal) lists what each provider did before vs now.

---

### U5 — Add `IActivityTagEnricher` to `Headless.Messaging.OpenTelemetry`

**Goal:** Let consumer apps attach custom OpenTelemetry tags to publish/consume spans without forking the package. Default implementation adds `headless.messaging.tenant_id`, `headless.messaging.delivery_kind`, and `headless.messaging.completion` automatically when values are present. The `headless.messaging.completion` tag carries `"broker_ack"` or `"outbox_commit"` per the publish-time completion contract; provider Direct publishers stamp it via `IMessageTagContext.SetCompletion(string mode)` from inside the publish wrapper after the broker ack returns, and the Outbox decorator stamps `"outbox_commit"` after the row commits. **Namespace rationale:** OTel's attribute-naming spec explicitly discourages using existing semconv namespaces (including `messaging.*`) as prefixes for third-party attributes — a future OTel messaging registry update could collide with `messaging.headless.*`. The library uses the reverse-domain-style `headless.messaging.*` prefix for all custom attributes; OTel-standardized attributes (`messaging.operation.type`, `messaging.system`, `messaging.destination.name`) keep canonical names so vendor dashboards work without configuration.

**Requirements:** R6, R4 (tenancy visibility in traces).

**Dependencies:** U2 (TenantId must exist).

**Files:**
- Create: `src/Headless.Messaging.OpenTelemetry/IActivityTagEnricher.cs`
- Create: `src/Headless.Messaging.OpenTelemetry/DefaultActivityTagEnricher.cs`
- Modify: `src/Headless.Messaging.OpenTelemetry/` existing publish/consume span wrappers to invoke all registered enrichers.
- Modify: `src/Headless.Messaging.OpenTelemetry/` DI extension to register `DefaultActivityTagEnricher` and allow user-supplied enrichers via `Add<T>()`.
- Test: `tests/Headless.Messaging.Tests.Unit/OpenTelemetry/ActivityTagEnricherTests.cs`

**Approach:**
- `IActivityTagEnricher.Enrich(Activity, IMessageTagContext)` — context exposes typed `TenantId`, `DeliveryKind`, `MessageType`, `Topic`, `Completion` (set to `"broker_ack"` or `"outbox_commit"` by provider Direct publishers / the Outbox decorator after their respective acknowledgement event), and the raw `Headers`. The default enricher emits these as `headless.messaging.tenant_id`, `headless.messaging.delivery_kind`, and `headless.messaging.completion` respectively (custom prefix per OTel naming guidance).
- The default enricher also stamps the OTel-standardized attributes from the messaging semantic conventions: `messaging.operation.type` (`"send"` for `IDirectSendPublisher` / `IDirectBroadcastPublisher` / Outbox-redispatch publish spans, `"process"` for consume spans, `"create"` for the Outbox-persist span on the publisher side), and `messaging.system` per provider using the registered enum values (`"kafka"`, `"rabbitmq"`, `"pulsar"`, `"servicebus"`, `"aws_sqs"`; NATS uses the unregistered `"nats"` value pending OTel registration; in-memory transports, the in-process queue, and the SQL transports omit `messaging.system` because OTel registers no enum value for them, and stamping a non-registered string would itself violate the standard). The standardized `messaging.destination.name` carries the topic / subject / stream / queue resolved at publish time. **Why two layers of attributes:** standardized names make vendor messaging dashboards (Honeycomb, Datadog, Grafana Tempo, Aspire) light up out of the box without per-app configuration; the `headless.messaging.*` attributes layer Headless-axis distinctions (Send vs Broadcast, broker-ack vs outbox-commit, tenant) on top so custom dashboards can pivot on them without colliding with future OTel registry updates. `messaging.operation.type` enum is intentionally narrow (`send` / `receive` / `process` / `settle` / `create` / `deliver`) and does not distinguish unicast Send from multicast Broadcast — that distinction lives in `headless.messaging.delivery_kind`.
- Multiple enrichers compose; resolved `IEnumerable<IActivityTagEnricher>` preserves registration order.
- Default enricher always runs first via **explicit options-driven ordering**, not `services.Insert(0, ...)`. `OpenTelemetryMessagingOptions.EnricherOrder` (a `List<Type>`) defaults to `[typeof(DefaultActivityTagEnricher)]`. The publish/consume wrappers resolve `IEnumerable<IActivityTagEnricher>` from DI and sort by `EnricherOrder` index (unordered types run after ordered ones, in registration order). This matches OTel SDK conventions (ordered processors via options), survives arbitrary DI registration order, and avoids the fragility of descriptor-list position which breaks when `TryAddEnumerable` or later `services.Insert` calls re-shuffle the list.
- **Tenant-tag suppression for cross-tenant trace storage.** `OpenTelemetryMessagingOptions.SuppressTenantIdTag` (default `false`) gates emission of `headless.messaging.tenant_id` on publish/consume spans. When `true`, the default enricher omits the tenant tag entirely (it does *not* hash or redact — partial leakage through hashes is worse than absence). Operators set this when their trace backend is shared across tenants and tenant identity itself is considered sensitive (B2B SaaS where a tenant's existence implies a customer relationship; HIPAA/PCI environments where tenant=patient/cardholder). Trace-context columns persisted in the outbox table (`trace_parent`, `trace_state`) inherit a separate retention rule: the outbox row is deleted once the redispatch publish span closes — typical retention is seconds to minutes, never the message-history TTL. Documented explicitly in U6 because operators with strict data-residency or PII-classification rules need to know the trace-context bytes leave the persistence boundary the moment the broker acks.

**Patterns to follow:**
- `IConsumerLifecycle` multi-registration pattern already present in abstractions.

**Test scenarios:**
- Happy path: Default enricher adds `headless.messaging.tenant_id` when `TenantId` is set.
- Happy path: Default enricher skips the tag when `TenantId` is null (no empty-string tag).
- Happy path: User-supplied enricher runs after default and can add additional tags.
- Happy path: A `IDirectSendPublisher.SendAsync<T>` publish span carries `headless.messaging.completion = "broker_ack"` and the OTel-standardized `messaging.operation.type = "send"`.
- Happy path: A `IOutboxSendPublisher.SendAsync<T>` publish span carries `headless.messaging.completion = "outbox_commit"` and `messaging.operation.type = "create"`; the redispatched span emitted by `OutboxDrainer<TTransport>` carries `headless.messaging.completion = "broker_ack"` and `messaging.operation.type = "send"`, and shares the parent trace via the persisted W3C trace-context headers in the envelope.
- Happy path: Provider with a registered `messaging.system` enum value (NATS, Kafka, RabbitMQ, ASB, SQS, Pulsar) emits `messaging.system` on every span; in-memory / in-process / SQL transports omit `messaging.system` (no fabricated value).
- Edge case: Enricher throws → span is still emitted, enricher exception is logged but not propagated.
- Integration: A publish→consume round-trip produces two connected spans both carrying `headless.messaging.tenant_id`, with the publish span carrying `messaging.operation.type = "send"` and the consume span carrying `messaging.operation.type = "process"`.

**Verification:**
- OpenTelemetry test harness confirms tag presence and order.
- No consumer-app code has to wrap activities to get tenant visibility.

---

### U6 — Capability matrix doc + `docs/llms/messaging-envelope.md`

**Goal:** Single source of truth for the envelope shape, publisher capability per transport, and the migration from today's shape. Referenced by every provider README.

**Requirements:** R9.

**Dependencies:** U1-U3c landed (so the matrix reflects reality).

**Files:**
- Create: `docs/llms/messaging-envelope.md`
- Modify: `src/Headless.Messaging.Abstractions/README.md` — add a "see also" link to the new doc.
- Modify: each `src/Headless.Messaging.*/README.md` to state which publisher interfaces the provider supports.
- Modify: top-level solution README where messaging is mentioned.

**Approach:**
- Doc sections: envelope fields (with types), capability matrix table extended with a **fan-out mechanism** column distinguishing `Native broker fan-out` (RabbitMQ fanout exchange, ASB topic, NATS subject, Pulsar Shared/Failover), `Emulated via per-subscriber group/materialization` (Kafka distinct `group.id`, RedisStreams per-subscriber consumer groups, Pulsar Exclusive), and `In-process` (InMemoryStorage). Callers infer operational cost, not just semantic capability.
- **Retry contract section**: mirror the cross-provider retry definitions from U4 (attempt = one invocation, `headless-attempt` is the canonical count, delay-source rules, immediate vs requeue retry semantics). Single source of truth for strategy authors.
- **Convention-axis operational runbook** (new section in `docs/llms/messaging-envelope.md`) — for transports where Send vs Broadcast is a registration-time *configuration* choice rather than a wire-protocol primitive (Kafka, NATS, RedisStreams, Pulsar), document how operators verify at runtime which axis a deployed consumer is actually wired to. Without this, a Phase 1 misconfiguration (e.g., two services mistakenly registered with the same Kafka `group.id` when broadcast was intended) is invisible until message loss is observed in production. The runbook lists, per transport, the exact admin command + expected output:
    - **Kafka:** `kafka-consumer-groups.sh --bootstrap-server <broker> --describe --group <group>` or programmatically `AdminClient.DescribeConsumerGroupsAsync(new[] { groupId })`. *Send semantics* = one group, N members; *Broadcast semantics* = N distinct groups, one member each. The runbook shows the diff between the two patterns side-by-side.
    - **NATS:** `nats stream info <stream>` for the durable subscription roster + `nats consumer info <stream> <consumer>` for the delivery semantics (`deliver_policy`, `ack_policy`, queue group name). *Send* = shared queue group on the consumer; *Broadcast* = per-subscriber durable consumer with no queue group.
    - **RedisStreams:** `XINFO GROUPS <stream>` lists consumer groups. *Send* = one group, multiple consumers via `XREADGROUP`; *Broadcast* = N groups, each with one consumer; the runbook flags the `pending` counters as the canary for "intended Send but actually Broadcast" (or vice versa).
    - **Pulsar:** `pulsar-admin topics subscriptions <topic>` + `pulsar-admin topics stats <topic>` shows subscription type (`Exclusive`, `Shared`, `Failover`, `Key_Shared`). *Send* = `Shared`/`Key_Shared`; *Broadcast* = N `Exclusive`/`Failover` subscriptions.
  Each transport entry also includes the **expected `headless.messaging.delivery_kind` tag value on consume spans** so operators can cross-check the OTel emission against broker admin state — if the tag says `broadcast` but the broker admin shows one shared group, the operator has caught the misconfiguration without waiting for a missed-message report.
- **Migration appendix for application authors** — concrete rename and call-site recipes:
  - `IDirectPublisher` → `IDirectSendPublisher`
  - `IOutboxPublisher` → `IOutboxSendPublisher`
  - `publisher.PublishAsync(cmd)` → `sendPublisher.SendAsync(cmd)` for commands / point-to-point messages
  - `publisher.PublishAsync(evt)` → `broadcastPublisher.BroadcastAsync(evt)` for events / fan-out
  - When the selected provider does not support broadcast: either switch transports (see capability matrix) or route through the Phase 8 transactional broadcast bridge.
  - `grep/sed` recipe per old → new name with caveats (skip XML docs, skip generated files).
- Keep XML docs on each public type in sync — U1-U5 must update them inline, not in this unit.

**Test scenarios:**
- Test expectation: none — documentation unit.

**Verification:**
- Manual review: a reader can pick the right publisher interface for their use case from the doc alone.
- `grep -r "IDirectPublisher" docs/` returns zero stale references.

---

### U7 — NATS: per-stream config callback + `StreamAutoCreationMode`

> **DEFERRED — NATS-ergonomics phase (post-Phase 2).** This unit is NATS-specific operator ergonomics and depends on the U3a NATS provider being on the new publisher interfaces (Phase 2). It does not block Phase 1 (U2/U4/U5/U6) and is sequenced after U1/U1b/U3a-c land.

**Goal:** Let NATS consumers configure per-stream behavior declaratively without depending on raw `StreamConfig` types in their application code, and make auto-creation of streams an explicit mode choice.

**Requirements:** R8.

**Dependencies:** U3a (NATS already on new interfaces).

**Files:**
- Modify: `src/Headless.Messaging.Nats/NatsMessagingOptions.cs` (or equivalent)
- Modify: `src/Headless.Messaging.Nats/` DI extension
- Test: `tests/Headless.Messaging.Nats.Tests.Integration/StreamProvisioningTests.cs`

**Approach:**
- Add a `StreamAutoCreationMode` enum: `Never | IfMissing | AlwaysReconcile` — explicit replacement for today's implicit "create-if-missing" behavior.
- **Production safety gate for `AlwaysReconcile`.** Default mode in `Development` is `IfMissing`; default in `Production` is `Never`. `AlwaysReconcile` is opt-in only and refuses to activate in `Production` unless **all** of the following are set explicitly in code: (a) `NatsMessagingOptions.AllowReconcileInProduction = true`, and (b) `NatsMessagingOptions.ProductionReconcileJustification` is a non-empty string ≥ 20 characters describing the operator-approved reason (logged verbatim at startup at `LogLevel.Warning` regardless of any other log filters, and emitted as a `headless.messaging.nats.reconcile.production_override` OTel attribute on the startup span). The justification is a tripwire — humans pause before typing a paragraph, environment variables do not have that property. **No environment-variable bypass exists** (no `HEADLESS_NATS_RECONCILE_ENABLED` or equivalent); env-var gates are commonly enabled by container-orchestration defaults or shell-history copy-paste and weaken the audit trail. On every Production startup with `AlwaysReconcile` active, the runtime emits an unconditional `LogLevel.Warning` line `"Headless.Messaging.Nats: AlwaysReconcile is active in Production. Justification: <text>. Reconcile attempts are auditable via the IDeadLetterObserver StreamReconcileDegraded event."` regardless of options, log filtering, or success — operators cannot silence this with a log-level config without disabling Warning entirely. Rationale: a config-driven reconcile that crash-loops a running cluster is a worse failure than a missing-stream error at deploy time, and the gate must defeat both accidental enablement and tribal-knowledge "we always set this env var" patterns.
- **Circuit breaker on repeated reconcile failures.** If `AlwaysReconcile` throws `NatsStreamReconcileException` more than 3 times within a 10-minute rolling window for the same stream, the mode automatically downgrades to `IfMissing` for that stream for the remainder of the process lifetime, logs a structured warning with the `ErrorCode`, and raises an `IDeadLetterObserver` event of kind `StreamReconcileDegraded` (so operators observe the downgrade in the same place they observe other terminal failures). Thresholds (`3`, `10min`) are options on `NatsMessagingOptions`.
- Add a per-stream configuration callback shape on the NATS options that lets the caller adjust stream settings without the abstractions package taking a `StreamConfig` dependency.
- Validate via FluentValidation per `CLAUDE.md` options convention.
- **Safe-reconcile boundary:** `AlwaysReconcile` only applies additive/safe changes that JetStream accepts online — `max_age`, `max_bytes`, `max_msgs` going **up**, subject list additions, `num_replicas` when matching cluster size, and description/metadata. Rejected changes (storage type change, retention policy change, `max_msgs` going **down** below current count, subject removals that would drop messages) fail startup with a structured `NatsStreamReconcileException` carrying a machine-readable `ErrorCode` (stable enum: `StorageTypeChanged`, `RetentionPolicyChanged`, `MaxMsgsDecreaseBelowCurrent`, `SubjectRemoved`, `ReplicasMismatch`, `ReconcileCallbackFailed`), the field name, current value, desired value, and human-readable operator remediation. Operators automate on `ErrorCode`; humans read the message.
- `Never` + missing stream is the only mode that yields a startup failure purely on existence; `IfMissing` is a no-op when present; `AlwaysReconcile` is the only mode that can fail on content drift.
- **Concurrent-startup race contract.** Multiple application instances starting simultaneously may all enter the reconcile path against the same stream. JetStream's `AddStream` / `UpdateStream` APIs are idempotent for additive changes — last-writer-wins on identical desired state is safe. The race is harmful only when (a) two instances disagree on desired config (different deploy versions racing), or (b) both attempt destructive reconciles. Mitigation: (1) the reconcile call resolves desired config from `NatsMessagingOptions` and only proceeds if a hash of `(stream-name, subjects, retention, max_age, max_bytes, max_msgs, num_replicas)` matches across instances — instances log the hash at startup so divergent versions are observable in operator logs; (2) reconcile attempts are serialized per-stream within a single process via `SemaphoreSlim` so the circuit-breaker counter is meaningful; (3) cross-process serialization is **not** attempted in Phase 1 — operators running blue/green deploys with divergent stream configs are responsible for sequencing them, with the failure mode (one instance's `UpdateStream` overwriting another's) documented in U6. The integration test for U7 explicitly spins up three concurrent `IfMissing` and three concurrent `AlwaysReconcile` startups against a fresh JetStream container and asserts: (a) zero `NatsStreamReconcileException` at the API layer, (b) the final stream config matches the desired hash, (c) `JsApi.StreamCreate` count from JetStream metrics ≤ 1 across the run.

**Patterns to follow:**
- `NatsMessagingOptions` existing shape + FluentValidation validator class in the same file.

**Test scenarios:**
- Happy path: `Never` mode + missing stream → startup fails with a clear error naming the stream.
- Happy path: `IfMissing` mode → stream created exactly once; second startup is a no-op.
- Happy path: `AlwaysReconcile` mode → drifted stream is reconciled to configured shape.
- Edge case: Callback throws during reconcile → startup fails and reports the stream name and underlying exception.
- Edge case: Repeated reconcile failures (>3 in 10 min) → mode downgrades to `IfMissing` for the offending stream and emits `StreamReconcileDegraded` event.
- Edge case (concurrent startup): three application instances start simultaneously against a fresh JetStream cluster — `IfMissing` and `AlwaysReconcile` modes each tested separately — and only one stream is created (asserted via JetStream `JsApi.StreamCreate` metric ≤ 1), no `NatsStreamReconcileException` is observed, and the final stream config matches the desired-config hash logged at startup.

**Verification:**
- NATS integration tests cover all three modes.
- Application code has zero direct references to `StreamConfig` from the NATS.Net client.

---

### U8 — Abstracted DLQ observability + NATS JetStream advisory adapter

> **DEFERRED — NATS-ergonomics phase (post-Phase 2).** Depends on U7. The transport-agnostic `IDeadLetterObserver` surface in `Headless.Messaging.Abstractions` is small and could in principle land standalone, but the first concrete adapter is NATS-specific and the surface should not ship without at least one in-tree implementation exercising it.

**Goal:** Introduce a transport-agnostic `IDeadLetterObserver` surface in `Headless.Messaging.Abstractions` so consumer apps can observe terminal failures uniformly across providers. Ship the NATS JetStream advisory-subject adapter as the first implementation; other providers (RabbitMQ DLX, ASB dead-letter queue, SQS DLQ, Kafka poison-message handler, SQL outbox failure column) adopt the same surface in follow-up work.

**Requirements:** R8, R9 (observability parity across providers).

**Dependencies:** U7.

**Files:**
- Create: `src/Headless.Messaging.Abstractions/IDeadLetterObserver.cs` — `ValueTask OnDeadLetteredAsync(DeadLetterEvent evt, CancellationToken ct)`.
- Create: `src/Headless.Messaging.Abstractions/DeadLetterEvent.cs` — record carrying `TenantId`, `MessageId`, `MessageType`, `Attempts`, `TerminalReason`, `Provider`, `SourceSubjectOrTopic`, raw `Headers`. **Operational note (matches every surveyed library — MassTransit, Wolverine, NServiceBus, Brighter, Rebus — none of which scrub DLQ headers):** `Headers` carries whatever the publisher wrote. Consumer apps must not put secrets (API keys, bearer tokens, PII) into `PublishOptions.Headers` because those headers round-trip through the DLQ observer and any downstream sink (log aggregator, alerting, replay tool) that consumes `DeadLetterEvent`. This constraint is documented in the U6 README section on header conventions; **defense-in-depth via opt-in built-in scrubber** is provided through `DeadLetterEventScrubOptions` (see below) so operators get a known-good default denylist without implementing their own observer decorator.
- Create: `src/Headless.Messaging.Abstractions/DeadLetterEventScrubOptions.cs` — opt-in scrubber configuration with `IReadOnlyList<string> HeaderDenylist` (default: `["Authorization", "X-Api-Key", "Cookie", "Set-Cookie", "Proxy-Authorization", "X-Auth-Token", "X-Csrf-Token", "X-Forwarded-Authorization"]`, case-insensitive match per RFC 7230 §3.2 header-name semantics) and `bool Enabled` (default: `false` — opt-in, not opt-out, so the documented "headers carry what the publisher wrote" contract is not silently changed). When `Enabled = true`, a built-in `ScrubbingDeadLetterObserver` decorator wraps every registered `IDeadLetterObserver`, replaces denylisted header values with the literal string `"[REDACTED]"` (preserves the key so observers can still detect that the header was *present*), and emits a single `headless.messaging.dlq.scrubbed_count` OTel counter per dispatched event. The denylist is *additive* — operator-supplied `HeaderDenylist` entries extend rather than replace the defaults, and operators can reset to a clean list via an explicit `HeaderDenylist = []` followed by `HeaderDenylist.AddRange(...)`. **Why opt-in defaults rather than always-on:** the default configuration produces predictable behavior (publishers see exactly what they wrote in DLQ replay tools), and operators in regulated environments who flip the flag get a denylist that already covers HTTP-derived headers — the most common accidental-secret pathway in webhook-driven publishers. **Why a denylist rather than an allowlist:** consumer-app header schemas are extensible by design (correlation IDs, business-domain tags, A/B test bucket names) and an allowlist would silently drop them; a denylist trades the smaller risk of missed-secret-prefix matches for not breaking observability of consumer-defined headers.
- Create: `src/Headless.Messaging.Nats/JetStreamAdvisoryHostedService.cs` — JetStream-advisory-subject adapter that constructs `DeadLetterEvent` instances and dispatches to every registered `IDeadLetterObserver`.
- Create: `src/Headless.Messaging.Nats/AdvisorySubscriptionOptions.cs`.
- Modify: `src/Headless.Messaging.Nats/` DI extension to register the hosted service plus a default logging `IDeadLetterObserver`.
- Test: `tests/Headless.Messaging.Nats.Tests.Integration/JetStreamAdvisoryTests.cs`
- Test: `tests/Headless.Messaging.Tests.Unit/Abstractions/DeadLetterObserverTests.cs` — contract test (multi-observer dispatch, throwing observer does not block others).

**Approach:**
- `IDeadLetterObserver` is the public, transport-agnostic seam. NATS-specific advisory parsing stays inside `Headless.Messaging.Nats` and surfaces via the shared observer interface; apps never subscribe to `$JS.EVENT.ADVISORY.*` directly.
- NATS hosted service subscribes to a **configurable** advisory subject filter (default: only terminal-failure advisories — `$JS.EVENT.ADVISORY.CONSUMER.MAX_DELIVERIES.>` and `$JS.EVENT.ADVISORY.CONSUMER.MSG_TERMINATED.>`) rather than the full `$JS.EVENT.ADVISORY.>` firehose. `AdvisorySubscriptionOptions.Subjects` is an `IReadOnlyList<string>` with a documented default; operators can opt in to the firehose explicitly.
- Parsed advisories become `DeadLetterEvent`s dispatched to all registered `IDeadLetterObserver` implementations; default observer logs at warning; consumer apps add their own for alerting, metrics, compensating workflows.
- Other providers adopting this surface in follow-up plans map their native DLQ/terminal-failure signal (RabbitMQ DLX message, ASB `DeadLetterMessageAsync`, SQS DLQ redrive, Kafka retry-topic exhaustion, SQL outbox `failed_at`) to the same `DeadLetterEvent` shape.

**Patterns to follow:**
- `IConsumerLifecycle` multi-registration pattern.
- Hosted service lifecycle in the existing NATS package.

**Test scenarios:**
- Happy path: A terminal failure in a JetStream consumer triggers the advisory subject; the hosted service dispatches to the registered handler.
- Edge case: Multiple handlers registered → all receive the advisory; one throwing does not prevent the others from running.
- Integration: DLQ count reported by the advisory matches the actual dead-lettered messages.

**Verification:**
- Integration test confirms advisory→handler flow under a real NATS JetStream Testcontainer.

---

### U9 — NATS: declarative stream router

> **DEFERRED — NATS-ergonomics phase (post-Phase 2).** Pure NATS-developer ergonomics with no cross-transport implications. Sequenced after U7/U8 land.

**Goal:** Let a consumer declare which message types map to which streams/subjects via attributes or a fluent builder, rather than configuring each subscription imperatively.

**Requirements:** R8.

**Dependencies:** U7.

**Files:**
- Create: `src/Headless.Messaging.Nats/StreamRouteAttribute.cs`
- Create: `src/Headless.Messaging.Nats/IStreamRouter.cs` + default implementation
- Modify: `src/Headless.Messaging.Nats/` subscription wiring to consult the router
- Test: `tests/Headless.Messaging.Nats.Tests.Unit/StreamRouterTests.cs`, plus integration coverage

**Approach:**
- `[StreamRoute("orders", "orders.created")]` on a message class sets the default stream + subject.
- A fluent `UseStreamRouter(b => b.Map<OrderCreated>().ToStream("orders").Subject("orders.created"))` option provides non-attribute configuration.
- Imperative subscription wiring stays supported; the router is additive.

**Patterns to follow:**
- Existing `MessagingConventions` topic-name helpers — router defers to conventions when no explicit mapping exists.

**Test scenarios:**
- Happy path: A message with `[StreamRoute]` is published to the configured stream/subject.
- Happy path: A message without attributes falls back to `MessagingConventions` defaults.
- Edge case: Both attribute and fluent mapping exist → fluent wins and is documented.
- Error path: Ambiguous mapping (same type mapped twice via fluent) → startup fails with a clear message.

**Verification:**
- Unit tests for route resolution logic; integration test for end-to-end stream selection.

---

## System-Wide Impact

- **Interaction graph:** The new publisher interfaces are injected wherever today's `IDirectPublisher` / `IOutboxPublisher` are. Internal middleware (OpenTelemetry span wrappers, outbox dispatcher, retry loop) touches the shared `IMessagePublisher` base and must keep working for both Send and Broadcast paths.
- **In-repo migration call-sites:** The rename is not theoretical — these packages inject the old publisher interfaces and must be migrated in the same PR series as Phase 1 (handled in U3c):
  - `src/Headless.Caching.Hybrid/HybridCache.cs` — publishes `CacheInvalidationMessage` via `IDirectPublisher`; migrate to `IDirectSendPublisher` (cache invalidation is a point-to-point Send, not a Broadcast).
  - `src/Headless.Permissions.Core/Definitions/DynamicPermissionDefinitionStore.cs` + `src/Headless.Permissions.Core/Setup.cs` — publishes permission-change invalidation; migrate to `IDirectSendPublisher` (or `IOutboxSendPublisher` when transactional — pick per call-site).
  - `src/Headless.DistributedLocks.Core/RegularLocks/DistributedLockProvider.cs` + `src/Headless.DistributedLocks.Core/Setup.cs` — publishes lock-release notifications; migrate to `IDirectSendPublisher`.
  - Each of the three owns its DI wiring; migration is a per-file rename plus a constructor-parameter type change. No behavior change.
- **Error propagation:** `IRetryBackoffStrategy.ShouldRetry` becomes the single decision point for "retry vs DLQ" across providers. Exceptions thrown by the strategy itself are caught and logged; the consumer defaults to a safe retry policy rather than crashing the dispatcher.
- **State lifecycle risks:** TenantId header↔property mapping happens in one place (shared pipeline) to prevent tenant-header drift. The outbox tables are unaffected by the interface split since they store the envelope as-is.
- **API surface parity:** The split affects abstractions, OpenTelemetry, and all 11 transports simultaneously. Testing package (`Headless.Messaging.Testing`) and dashboard packages (`Headless.Messaging.Dashboard`, `Headless.Messaging.Dashboard.K8s`) must be audited in U3c and updated in-place; they depend on the existing publisher marker interfaces.
- **Integration coverage:** Every broadcast-capable provider needs at least one integration test that confirms fan-out delivery to N subscribers (not just one). Every queue-only provider needs a test that confirms `IBroadcastPublisher` is **not** resolvable, with a readable error.
- **Unchanged invariants:** `IConsume<T>` stays exactly as it is. `IScheduledPublisher` stays on the Send side only in Phase 1 (scheduled broadcast arrives in Phase 7). `MessagingConventions` topic naming stays backward compatible — the broadcast helper is additive. `Headers.*` constants for `MessageId`, `CorrelationId`, etc. are untouched.

## Risks & Dependencies

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Broadcast integration tests are flaky because fan-out timing is non-deterministic | Med | Med | Use `ITestOutputHelper` + explicit synchronization primitives; avoid `Task.Delay` in test assertions |
| Provider authors miss the capability-registration contract and silently register `IBroadcastPublisher` for a queue-only transport | Med | High | Add an abstractions-level analyzer or at minimum a per-provider unit test that asserts the exact set of registered publisher interfaces |
| Phase 2 publisher-interface rename breaks downstream consumer apps (Atilia SaaS, Zad NGO, and external NuGet consumers) | High | Med — internal consumers absorb the rename in one PR sweep; external NuGet consumers face a hard breaking change | (1) Publish migration section in U6 (Phase 1) ahead of the rename with grep/sed recipe per old → new name; updated when Phase 2 lands. (2) Headless major-version bump explicitly acknowledged when Phase 2 ships — greenfield posture means no obsolete aliases or deprecation window; the rename is atomic with the major bump. (3) Release-note section calls out the breaking rename and links to the migration doc |
| `IRetryBackoffStrategy` wiring changes retry semantics on providers that previously had ad-hoc behavior | Med | Med | Document per-provider before/after in the U4 audit; keep default strategy shape identical to today's behavior where possible |
| Dashboard packages depend on old publisher marker interfaces and break silently when Phase 2 lands | Med | Med | U3c (Phase 2) explicitly migrates dashboard + testing packages; integration smoke test for dashboard after the split |
| Cross-tenant `MessageId` collisions in shared storage (Redis, SQL outbox) silently dedupe across tenants | Med | High | Composite `(TenantId, MessageId)` dedup key enforced in U3a/U3b (Phase 2) for every provider with dedup storage; integration test asserts same `MessageId` across two tenants is treated as two distinct messages. Phase 1 ships the envelope `TenantId` property so existing dedup paths can opt in opportunistically before the Phase 2 rollout |
| `AlwaysReconcile` NATS mode attempts a server-rejected change and the app crash-loops at startup (NATS-ergonomics phase) | Med | Med | Safe-reconcile boundary in U7 classifies changes as safe vs rejected at config-parse time; production gate refuses activation without explicit `AllowReconcileInProduction = true` + ≥20-char justification (no env-var bypass); circuit breaker downgrades to `IfMissing` after 3 failures in 10 min |

## Security Considerations

This section consolidates the cross-cutting security postures that the unit-level descriptions touch individually. Reviewers and operators should treat the four threats below as the deliberate Phase 1 (and Phase 2) security baseline; deviations need an explicit ADR.

### 1. Header injection / publisher trust boundary (R4, U2)
- **Threat:** A producer constructs an envelope where the `headless-tenant-id` raw header carries one tenant value while the typed `TenantId` property carries another (or null). Without a defined precedence rule, downstream consumers may authorize against the header while persistence keys against the property, opening a cross-tenant authorization gap.
- **Posture:** The typed `Envelope.TenantId` property is **the** authoritative tenant identity for the entire pipeline (DI tenant context, outbox dedup key, OTel tag, observers). The raw `headless-tenant-id` header is a transport-layer hint only and is **never** consumed authoritatively. The Phase 1 publish wrapper applies a four-case fail-fast policy: (a) both set & equal → emit, (b) both set & unequal → throw `EnvelopeTenantConflictException` at publish time, (c) only property set → emit and stamp the header from the property, (d) only header set → emit only when the publisher is registered with `AllowHeaderTenantHydration = true` (off by default — explicit per-call-site opt-in for legacy producers). The `TenantContextRequired` startup validator additionally forbids null `TenantId` writes when the application opts into strict tenancy, and Phase 2 introduces an `IConsumeBehavior<T>` seam for cross-tenant authenticity checks (envelope signing / shared-secret HMAC).
- **What stays out of Phase 1:** Inbound envelope signing. Consumer apps that need cryptographic authenticity wire their own `IConsumeBehavior<T>` decorator in Phase 2.

### 2. DLQ secret leakage via headers (U8)
- **Threat:** Publishers commonly forward HTTP-derived headers (`Authorization`, `X-Api-Key`, `Cookie`) into `PublishOptions.Headers` for traceability. Those headers round-trip through `IDeadLetterObserver` into log aggregators, alerting tools, and replay UIs — exposing live secrets at every sink that consumes `DeadLetterEvent`.
- **Posture:** The default `DeadLetterEvent.Headers` is unscrubbed (matches every surveyed library — MassTransit, Wolverine, NServiceBus, Brighter, Rebus). Defense-in-depth opt-in via `DeadLetterEventScrubOptions.Enabled = true` activates a built-in `ScrubbingDeadLetterObserver` decorator with an opinionated denylist (`Authorization`, `X-Api-Key`, `Cookie`, `Set-Cookie`, `Proxy-Authorization`, `X-Auth-Token`, `X-Csrf-Token`, `X-Forwarded-Authorization`). Documentation in U6 and U8 explicitly tells consumer apps not to put secrets in headers and explains the denylist contract.
- **What stays out of Phase 1:** Mandatory always-on scrubbing (would silently change the documented "headers are passthrough" contract; would break consumer-app schemas that legitimately use header fields colliding with the denylist).

### 3. Tenant impersonation via dedup collision (R4, U3a/U3b)
- **Threat:** Pre-Phase-1 outbox dedup is keyed by `MessageId` alone. Tenant A and tenant B publishing the same `MessageId` would silently dedupe across tenants, allowing tenant A's outbox writer to "claim" tenant B's `MessageId` and prevent tenant B's message from publishing.
- **Posture:** Composite `(TenantId, MessageId)` uniqueness with `NULLS NOT DISTINCT` semantics on PG and an equivalent computed-column unique index on SQL Server. Integration tests assert that the same `MessageId` published under two distinct `TenantId`s results in two distinct outbox rows and two delivered messages. Decision #13 documents the four NULL-vs-set cases (set/set, set/null, null/set, null/null) and the startup validator that forbids null writes when `TenantContextRequired = true`.

### 4. NULL-tenant abuse / strict-tenancy bypass (R4, Decision #13, U2)
- **Threat:** A producer in a strict-tenancy deployment writes a row with `TenantId = NULL` (deliberately or via a bug). With `NULLS NOT DISTINCT`, that single row prevents all subsequent NULL-tenant publishes and also collides with the documented "no-tenant" sentinel — but more critically, the application is silently in a state where *some* messages have a tenant and others do not, a posture that should not exist when `TenantContextRequired = true`.
- **Posture:** The startup validator evaluates `TenantContextRequired` against the tenancy registration and refuses to start the host if any provider is wired to allow null-tenant writes when strict tenancy is required. The publish wrapper double-checks at runtime and throws `MissingTenantException` rather than silently writing a NULL. Operators with mixed-tenancy deployments (multi-tenant + system-level messages) explicitly set `TenantContextRequired = false` and document the "no-tenant" sentinel use cases in their own ADR; the framework does not assume the right answer.

### Cross-cutting: security audit checklist (referenced by U6)
- All four postures above are summarized as a single bullet list in `docs/llms/messaging-envelope.md` so consumer apps can answer "what does Headless.Messaging assume about my threat model?" without reading this plan. Each bullet links back to the unit and decision that originated it.

## Strategic Positioning vs MassTransit / Other .NET Messaging Libraries

This plan converges Headless.Messaging toward the proven shapes that MassTransit, Wolverine, NServiceBus, Brighter, and Rebus have validated over a decade — but deliberately retains four distinguishing axes. The intent is *parity on the well-understood ergonomics* (intent-explicit publishers, transactional outbox, OTel-first telemetry, retry+DLQ contracts) so consumer apps moving between Headless and MassTransit don't relearn fundamentals, while *keeping the differentiators* that justify a separate framework.

**Convergence (Phase 1 + Phase 2):**
- **Intent-explicit publisher split** mirrors MassTransit's `ISendEndpoint` / `IPublishEndpoint` distinction — the most-cited ergonomic lesson from the .NET messaging ecosystem. `IDirectSendPublisher` / `IDirectBroadcastPublisher` (and their outbox counterparts) make Send-vs-Broadcast a compile-time call-site choice, not a registration-time guess.
- **Transactional outbox with `(TenantId, MessageId)` dedup** matches MassTransit's `EntityFrameworkOutboxBusOutbox` posture for at-least-once delivery semantics and exactly-once dedup downstream. Composite key with `NULLS NOT DISTINCT` is a tenancy-first refinement (MassTransit's outbox is `MessageId`-only because it does not assume multi-tenancy).
- **`IRetryBackoffStrategy` three-layer override** (global / options / per-message-type) matches MassTransit's `UseMessageRetry` + `UseRetry` layering. Default exponential `100ms→1.6s` with `±25%` jitter and `MaxAttempts = 5` is the community-standard shape — copied because deviating without reason would surprise users coming from MassTransit/NServiceBus.
- **OpenTelemetry-first observability** with `messaging.operation.type`, `messaging.system`, `messaging.destination.name` standardized attributes — every modern .NET messaging library converged on this in 2024-2025 because vendor dashboards (Honeycomb, Datadog, Aspire, Grafana Tempo) light up automatically.

**Differentiation (deliberate):**
- **First-class multi-tenancy.** MassTransit, Wolverine, NServiceBus, Brighter, and Rebus all treat tenant identity as a userland concern — apps thread it through headers or message-body fields. Headless.Messaging puts `TenantId` on the envelope as a typed property with composite-key dedup, startup validation (`TenantContextRequired`), and OTel emission as `headless.messaging.tenant_id`. This is the single biggest shape difference from the surveyed libraries and is non-negotiable in a multi-tenant SaaS framework.
- **No "rider" model / no implicit message-routing topology.** MassTransit's rider abstraction (Kafka rider, Event Hub rider) decouples the conceptual transport from the configured one but requires consumer apps to learn a Headless-Messaging-specific architectural pattern. Headless deliberately stays "thin abstractions over the native client" — the per-transport package depends directly on the broker SDK and exposes capability via DI registration, not via an indirection layer. Rationale: the rider model adds value when an app routes the same message across heterogeneous transports (Kafka + Service Bus in one app); for the dominant single-transport-per-bounded-context pattern, it's overhead. Phase 2's transport-agnostic `OutboxPublisherDecorator<TTransport>` is the *only* abstraction layer; transports below it are concrete and direct.
- **Transport-agnostic outbox decorator (one decorator class, N transports).** MassTransit's outbox is EF-Core-coupled; Wolverine's is Marten-coupled. Headless ships `OutboxPublisherDecorator<TTransport>` with raw-SQL `IStorageInitializer` (PG and SQL Server in Phase 2) so consumer apps that don't use EF Core or Marten — including those using Dapper, ADO, or non-relational stores via custom `IOutboxStore` implementations — get the same durability guarantee without an ORM dependency.
- **Capability declaration as registration-time DI shape, not runtime exception.** Resolving `IBroadcastPublisher<T>` from a queue-only provider fails at `IHost.StartAsync()` with a structured error pointing at the capability matrix doc. MassTransit-style `NotSupportedException` at the first publish call is explicitly avoided — capability errors belong in CI/CD, not at 2 AM. This is enforced by an analyzer-tested rule (`grep -r "NotSupportedException.*Broadcast" → 0 results`).

**What this means for migration audiences:**
- **From MassTransit:** ergonomic shapes (Send/Publish, outbox, retry) translate near-1:1; rider users will need to flatten their topology to single-transport-per-bounded-context; gain first-class tenant safety.
- **From NServiceBus / Rebus / Brighter:** outbox posture is similar; OTel emission is more standardized; tenancy is a first-class envelope property rather than a header convention.
- **From Wolverine:** publisher-axis split and outbox decorator pattern are familiar; rider-equivalent (Wolverine's "transports") is replaced by direct package references; Marten dependency is replaced by raw-SQL initializer or any `IOutboxStore` implementation.

## Documentation / Operational Notes

- U6 is the authoritative doc and must land in the same PR (or PR series) as U1-U3c.
- Every provider README is updated in U6; XML docs on public types are updated inline in U1-U5, not deferred.
- No runtime rollout plan: this is a framework release, not a service deploy. A release-note section for the next minor version calls out the breaking interface rename and points to the migration section.

## Sequencing Summary

The original plan combined three distinct concerns into a single sequence. Triage moved the publisher-interface split (U1) and the transport-agnostic outbox (U1b/U3a/U3b/U3c) into Phase 2, leaving Phase 1 focused on envelope + cross-cutting axes that ship cleanly without the interface rename. NATS-specific operator ergonomics (U7/U8/U9) form a third phase gated on Phase 2 completion.

### Phase 1 (this plan, ships now)

```text
U2                              # envelope: TenantId + DeliveryKind + 4-case header policy
 |
 +-- U4                         # IRetryBackoffStrategy across all providers
 |                              # (RetryBackoffOptions for consumer-dispatch)
 |
 +-- U5                         # IActivityTagEnricher + OTel standardized attributes
 |                              # (depends on U2 for TenantId emission)
 |
 +-- U6                         # capability + envelope doc + convention-axis runbook
                                 # (depends on U2/U4/U5 to document shipped reality)
```

**Phase 1 invariants:** single transport per host, single storage per host. No outbox decorator, no publisher-interface rename. Existing `IDirectPublisher` / `IOutboxPublisher` symbols continue to compile; consumer-app call sites are unchanged. Tenant safety, retry contract, and OTel emission land independently and are valuable on their own.

**Parallelism:** U4 and U5 run in parallel after U2 completes. U6 waits on both because the capability matrix references retry contract (U4) and OTel attributes (U5).

### Phase 2 (deferred — sequenced after Phase 1)

```text
U1                              # abstractions: new publisher interfaces
 |
 +-- U1b                        # Core: outbox decorator + raw-SQL IOutboxStore
 |                              # (single-storage-per-host scope; OutboxRedispatchBackoffOptions
 |                              # distinct from Phase 1's RetryBackoffOptions)
 |
 +-- U3a                        # Tier-1 providers (NATS, RabbitMQ, ASB, SQS)
      |
      +-- U3b                   # Tier-2 providers (Kafka, Pulsar, RedisStreams, InMem*, PG, SQL)
           |
           +-- U3c              # downstream migration (Caching.Hybrid, Permissions, DistributedLocks,
                                # tests, demos, dashboard, testing harness)
```

Phase 2 introduces the publisher-interface rename and outbox decorator. The migration appendix in U6 (Phase 1) is updated when Phase 2 lands.

### NATS-ergonomics phase (deferred — gated on Phase 2 U3a)

```text
U3a -- U7                       # NATS: per-stream config + StreamAutoCreationMode
        |                       # (production gate with mandatory justification, no env-var bypass)
        +-- U8                  # IDeadLetterObserver + JetStream advisory adapter
        |                       # (DeadLetterEventScrubOptions opt-in header denylist)
        +-- U9                  # NATS declarative stream router
```

NATS-ergonomics is a transport-specific operator-experience pass. Other transports adopt the `IDeadLetterObserver` surface (introduced in U8) in their own follow-up plans.

### Why this phasing
- **U2 + U4 + U5 + U6 are independent of the interface rename.** Tenant safety and retry semantics should not wait on a multi-package publisher rename.
- **U1/U1b/U3a-c form one atomic surface change.** Splitting them across phases would leave consumer apps mid-migration and would force two breaking-change windows. They ship together as Phase 2.
- **U7/U8/U9 are NATS-only and have no Phase 1 dependency from non-NATS transports.** Sequencing them after Phase 2 keeps the NATS package on a single set of publisher interfaces (Phase 2's renamed shape) rather than refactoring twice.

## Sources & References

- Origin spec: [`specs/2026-04-19-001-messaging-feature-spec.md`](../../specs/2026-04-19-001-messaging-feature-spec.md) — full epic shape, Phase 1 detailed plan, Phase 2-11 sketches, rejected alternatives.
- GitHub issue: <https://github.com/xshaheen/headless-framework/issues/217>.
- Session discussion (2026-04-19) on MassTransit capability model and transport-native topology provisioning.
- MassTransit docs via Context7: `/websites/masstransit_io` — producers, SQS/SNS, ASB broker topology, Event Hub rider model.
- Codebase: `src/Headless.Messaging.Abstractions/*.cs`, all `src/Headless.Messaging.*` providers, `src/Headless.Messaging.OpenTelemetry`.
- Related in-flight plans: [`docs/plans/2026-03-18-001-feat-saga-pattern-support-plan.md`](2026-03-18-001-feat-saga-pattern-support-plan.md), [`docs/plans/2026-03-22-001-refactor-unified-dashboard-plan.md`](2026-03-22-001-refactor-unified-dashboard-plan.md) — Phase 11 amends rather than re-plans these.
- `CLAUDE.md` (greenfield posture, FluentValidation+`ValidateOnStart` options pattern, `Headless.Checks` argument validation).
