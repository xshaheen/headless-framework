---
title: "epic: Headless.Messaging Feature Shape"
type: epic
status: active
date: 2026-04-19
---

# epic: Headless.Messaging Feature Shape

## Overview

Define the target feature shape of `Headless.Messaging` across `Headless.Messaging.Abstractions`, all 11 transport providers, and the satellite packages (`Headless.Messaging.Core`, `Headless.Messaging.OpenTelemetry`, `Headless.Messaging.Testing`, `Headless.Messaging.Dashboard`). The epic is structured as a sequence of **small, independently shippable phases**. Phase 1 lands the foundational envelope and observability primitives (first-class tenancy, uniform retry, OpenTelemetry enricher, capability matrix); the Send/Broadcast split, transactional outbox, and NATS ergonomics ship in subsequent phases on top of that foundation. Greenfield posture: breaking changes are acceptable.

> **Amendment — 2026-04-27 (Phase 1 rescope).** During plan-document review, Phase 1 was narrowed to reduce blast radius and ship the load-bearing envelope/observability primitives first. The new phasing is:
>
> - **Phase 1 (ships now):** U2 (envelope: `TenantId` + `DeliveryKind`) + U4 (`IRetryBackoffStrategy`) + U5 (`IActivityTagEnricher`) + U6 (capability matrix doc).
> - **Phase 2 (deferred from original Phase 1):** U1 (publisher-interface rename `IDirectSendPublisher` / `IOutboxSendPublisher` / `IDirectBroadcastPublisher` / `IOutboxBroadcastPublisher`) + U1b (transport-agnostic `OutboxPublisherDecorator<TTransport>` + `IOutboxStore`) + U3a / U3b / U3c (provider migration to the new publisher shape).
> - **NATS-ergonomics phase (post-Phase 2):** U7 (`StreamAutoCreationMode` with hardened production gate) + U8 (`IDeadLetterObserver` + opt-in `DeadLetterEventScrubOptions`) + U9 (declarative stream routing).
>
> The authoritative phasing source is the **Sequencing Summary** in [`docs/plans/2026-04-26-001-feat-messaging-phase1-plan.md`](../docs/plans/2026-04-26-001-feat-messaging-phase1-plan.md). Unit-level descriptions in this spec retain their original numbering (U1, U1b, U3a, etc.) for cross-reference; their phase assignment is governed by the plan, not by the position they occupy in this document.

## Problem Frame

A consumer of `Headless.Messaging.Nats` surfaced nine friction points. Root-causing them against the actual codebase and against peer libraries (MassTransit, Wolverine, NServiceBus) shows that the underlying issue is not NATS-specific — it is that the current abstractions conflate two orthogonal axes:

1. **Durability axis** — already modeled: `IDirectPublisher` vs `IOutboxPublisher`.
2. **Semantic axis** — missing: "deliver to one competing consumer" vs "fan out to every subscriber".

Today a caller writes `IDirectPublisher.PublishAsync<T>(...)` regardless of whether `T` represents a command (one handler) or an event (N handlers). The transport decides what happens on the wire, which leaks in both directions: callers cannot express intent, and providers cannot refuse a nonsense request. Tenancy, retry policy, and OpenTelemetry enrichment all suffer similar abstraction gaps that currently force NATS-specific workarounds in consumer code.

MassTransit's `ISendEndpoint.Send<T>` vs `IPublishEndpoint.Publish<T>` split is the battle-tested answer: intent is encoded in the API surface; transports are responsible for provisioning whatever topology makes each interface work (SNS topics in front of SQS, ASB topics, RabbitMQ fanout exchanges, SQL routing tables). Transports that cannot support an interface simply do not register it, and DI surfaces the gap at startup rather than at runtime.

## Epic Roadmap

The epic is decomposed into the smallest phases that each deliver a coherent, shippable capability. Each phase has a single focus, a short unit list, and explicit dependencies on earlier phases. Only Phase 1 is fully detailed in this spec today; later phases carry a brief goal/units/deps sketch and get deepened into their own spec when scheduled.

| Phase | Title | Focus | Depends on | Unit count |
|---|---|---|---|---|
| 1 | Envelope + observability foundations | First-class `TenantId` + `DeliveryKind`, uniform retry strategy, OTel tag enricher, capability matrix | — | 4 (2, 4, 5, 6) — see 2026-04-27 amendment above |
| 1.5 | Send/Broadcast split + transactional outbox | Intent-explicit publisher rename + transport-agnostic outbox decorator + per-provider migration | 1 | 5 (1, 1b, 3a, 3b, 3c) — deferred from original Phase 1 |
| 1.6 | NATS ergonomics + DLQ observer | `StreamAutoCreationMode` (production-gated), `IDeadLetterObserver` + opt-in scrubber, declarative stream routing | 1.5 | 3 (7, 8, 9) — deferred from original Phase 1 |
| 2 | Publish/Consume behavior pipeline | `IPublishBehavior<T>` + `IConsumeBehavior<T>` with ordered DI registration; outbox and OTel wrappers re-express as behaviors | 1 | 1 |
| 3 | Auto-`TenantId` propagation behavior | `TenantPropagationPublishBehavior` reads `ITenantContext` and sets `PublishOptions.TenantId` when not explicitly set | 1, 2 | 1 |
| 4 | Polymorphic publish | On `BroadcastAsync<T>`, dispatch to consumers registered for any base type or interface of `T`; ambiguity surfaces at startup | 1 | 1 |
| 5 | Dashboard fan-out rendering | Dashboard UIs surface broadcast fan-out cardinality, per-subscriber delivery status, polymorphic dispatch visibility, tenant filters | 1, 4 | 1–2 |
| 6 | Request/Reply | `IRequestClient<TReq, TRes>` with correlation/reply-to headers; reply-queue lifecycle per transport | 1, 2 | 1–2 |
| 7 | Scheduled broadcast | `IScheduledBroadcastPublisher` for providers with native scheduled fan-out; otherwise bridged via Phase 8 | 1 | 1 |
| 8 | Transactional broadcast bridge | `BroadcastViaSendBridge` dispatcher: SQL outbox row → broadcast-capable transport; enables "reliably broadcast inside a DB transaction" | 1 | 1 |
| 9 | Inbox pattern | Idempotent consume via transactional inbox; composite `(TenantId, MessageId)` dedup persisted per-consumer | 1, 2 | 1 |
| 10 | SNS-fronted SQS broadcast | Auto-provision SNS topics in front of SQS so AWS transport ships `IBroadcastPublisher` | 1 | 1 |
| 11 | Saga / routing-slip integration | Align existing saga and routing-slip plans with the Send/Broadcast split and behavior pipeline | 1, 2 | links to existing plans |

**Rules of the roadmap:**

- Phases are sized for one PR series each wherever possible. If a phase grows past ~3 units during deepening, split it.
- A later phase never modifies Phase 1 abstractions without an explicit amendment note in this document.
- Phase dependencies are hard; a phase cannot start until its dependencies land in `main`.
- Each phase gets its own per-phase plan file under `docs/plans/` when scheduled, generated from the sketch in this spec.
- **Post-2026-04-27 amendment:** The "Depends on 1" entry in the rows below originally referenced the pre-amendment Phase 1 scope (which included U1, U1b, U3a/b/c, U7/8/9). After the rescope, phases that need the publisher-interface rename or outbox depend on Phase 1.5; phases that need NATS ergonomics depend on Phase 1.6. The plan-document Sequencing Summary is authoritative for unit-level dependencies; the table below is left unchanged to minimize diff scope.

## Requirements Trace

- R1. Callers express **intent** (send-one vs broadcast-many) in the API surface, not via options.
- R2. Both `Direct` and `Outbox` durability variants are available for **both** Send and Broadcast — the two axes stay orthogonal.
- R3. Transports declare capability at registration; a transport that cannot broadcast does not ship an `IBroadcastPublisher` implementation, and resolution fails at startup with an actionable message.
- R4. `TenantId` is a first-class envelope field on `PublishOptions`, `ConsumeContext`, and `Headers` — not an ad-hoc header convention. **Trust model: the framework trusts the publisher** and does not verify tenant authenticity on consume. This matches every surveyed messaging library (MassTransit, NServiceBus, Wolverine, Brighter, Rebus). Consumer applications that require stronger guarantees (untrusted cross-boundary publishers, regulated isolation) layer their own authenticity check as a `IConsumeBehavior<T>` in Phase 2.
- R5. `IRetryBackoffStrategy` (which already exposes both delay and predicate) is honored by every provider's dispatch loop, not just one.
- R6. OpenTelemetry spans emitted by `Headless.Messaging.OpenTelemetry` are extensible via a tag-enricher hook so consumer apps can add tenant, route, and domain tags without forking the package.
- R7. Existing consumer surface (`IConsume<T>`, `ConsumeContext<T>`) remains the single consumer entry point — no split consumer interfaces. Exposing `ConsumeContext.DeliveryKind` (Send vs Broadcast) is **intent** metadata, not transport identity — analogous to Rebus's `Headers.Intent`. R7 prohibits branching on *which transport* delivered the message (Kafka vs SQS vs NATS); it does not prohibit branching on *intent*, though branching on intent is still discouraged in favor of modeling distinct message types.
- R8. NATS provider offers per-stream configuration, DLQ observability, and declarative stream routing without leaking NATS types into the abstractions package.
- R9. All public XML docs and package READMEs stay in sync with the new shape.

## Scope Boundaries (Permanent Non-Goals for the Epic)

These are non-goals for the **entire epic**, not just Phase 1. Items previously listed as "deferred" that are now phases (request/reply, polymorphic publish, behavior pipeline, dashboard fan-out, auto-tenant propagation, transactional broadcast bridge, SNS-fronted SQS, scheduled broadcast, saga integration) have been promoted out of this list into the Epic Roadmap above.

- No `MessageKind`/`CommandEvent` enum on the envelope — intent lives in the API surface (R1).
- No `PublishOptions.DeliveryMode` enum — rejected in session discussion; capability cannot be expressed as a flat enum across heterogeneous transports.
- No `CloudEvents` package adoption — separate decision outside this epic.
- No split of `IRetryBackoffStrategy` into delay + predicate interfaces — it already exposes both.
- No `ITenantResolver` abstraction — tenancy is data on the envelope, not a new dependency.
- No consumer-side interface split (`IConsume<Event>` vs `IConsume<Command>`).
- No mandatory multi-tenancy filter; tenancy is observable and routable but not enforced.
- No message marker interfaces (`ICommand` / `IEvent` / `IMessage`). Intent is encoded in the publisher method (`Send` vs `Broadcast`), not on the DTO.
- **No migration shims, adapter types, or compatibility layers for the old `IDirectPublisher`/`IOutboxPublisher` names.** This is a greenfield framework release; there are no external consumers to preserve source compatibility for. The old names are renamed in place and removed. Internal packages and demos are migrated in Unit 3c.

---

## Rejected Alternatives

Surveyed during session discussion. Captured here so future readers see which shapes were considered and why they were rejected.

| Alternative | Source | Why rejected |
|---|---|---|
| `ISendEndpoint` / `IPublishEndpoint` endpoint-address model | MassTransit | Heavyweight endpoint-resolution model tied to MT's topology primitives; mismatches our capability-declared-at-registration posture and fights C# DI ergonomics. |
| Single `IMessageBus.Send(...)` / `Publish(...)` with durability as a publish-time option | Wolverine | Collapses the durability axis into runtime options; loses the four-way matrix (Direct × Outbox × Send × Broadcast) that our providers already express as distinct interfaces. |
| `Send` / `Publish` on `IMessageSession` with pipeline-behavior-based tenancy | NServiceBus | Commercial licensing is a non-starter for this framework; tenant propagation as a pipeline concern is correct and is adopted for Phase 3. |
| `Post` vs explicit `Send`/`Publish` depending on handler type | Brighter | Different durability model centered on "outbox is the default"; incompatible with our existing Direct/Outbox split that pre-dates this epic. |
| `Headers.Intent = "p2p" \| "pub"` on the envelope | Rebus | Validates that exposing delivery intent on the consume side is a known pattern. We adopt the concept as `ConsumeContext.DeliveryKind` but keep it a typed enum rather than a free-form header string. |

---

## Phase 1 — Detailed Plan

> **Phase 1 goal:** Land the load-bearing abstractions — Send/Broadcast split, first-class `TenantId`, uniform retry, OpenTelemetry enricher, NATS ergonomics, abstracted DLQ observer — and migrate every in-repo consumer. Every later phase builds on this surface.

### Context & Research

#### Relevant Code and Patterns

- `src/Headless.Messaging.Abstractions/IMessagePublisher.cs` — current single publisher seam; becomes an internal base after the split.
- `src/Headless.Messaging.Abstractions/IDirectPublisher.cs`, `IOutboxPublisher.cs`, `IScheduledPublisher.cs` — durability markers to preserve.
- `src/Headless.Messaging.Abstractions/PublishOptions.cs` — envelope config; gains `TenantId`.
- `src/Headless.Messaging.Abstractions/ConsumeContext.cs` — consumer envelope; gains `TenantId`.
- `src/Headless.Messaging.Abstractions/Headers.cs` — wire header constants; gains `TenantId = "headless-tenant-id"`.
- `src/Headless.Messaging.Abstractions/IRetryBackoffStrategy.cs` — already exposes `GetNextDelay` + `ShouldRetry`; wire points exist, usage does not.
- `src/Headless.Messaging.Abstractions/MessagingConventions.cs` — topic/group naming; extend with broadcast-topic naming helper.
- All 11 provider projects under `src/Headless.Messaging.*` — each must declare which publisher interfaces it supports.
- `src/Headless.Messaging.OpenTelemetry/` — host for the new `IActivityTagEnricher` hook.

#### Institutional Learnings

- `docs/solutions/` — review before execution for prior messaging decisions; none referenced explicitly today but the `learnings-researcher` pass should be re-run during `dev:code` kickoff.
- Greenfield breaking-change posture is documented in `CLAUDE.md` and applies here.

#### External References

- MassTransit `ISendEndpoint` / `IPublishEndpoint` split — <https://masstransit.io/documentation/concepts/producers>.
- MassTransit SQS broker topology (SNS fronts SQS for Publish) — <https://masstransit.io/documentation/configuration/transports/amazon-sqs>.
- MassTransit ASB broker topology (topic fronts queues for Publish) — <https://masstransit.io/documentation/configuration/transports/azure-service-bus>.
- MassTransit "riders" model for Kafka/Event Hubs — different API when semantics diverge; informs our "declare capability, don't fake it" rule.

#### Transport Capability Matrix

Established during session; table is authoritative for registration decisions.

| Transport | `ISendPublisher` | `IBroadcastPublisher` | Publisher mechanism | Subscriber requirement for broadcast | Axis & operational cost |
|---|---|---|---|---|---|
| NATS (core + JetStream) | ✅ | ✅ | Distinct subject via `MessagingConventions.GetBroadcastTopicName` | No queue group on the broadcast subject (every subscriber receives every message) | Convention-axis. Native subject fan-out at the broker level once subscribers comply |
| Kafka | ✅ | ✅ | Distinct topic via `MessagingConventions.GetBroadcastTopicName` | Distinct `group.id` per subscriber on the broadcast topic | Convention-axis. Operational cost: N consumer groups linear in subscriber count |
| RabbitMQ | ✅ | ✅ | Fanout (or topic) exchange — different exchange type from send's direct exchange | Independent queues bound to the fanout exchange | **Publisher-axis.** Topology fully enforced at publish time |
| AzureServiceBus | ✅ | ✅ | Topic resource — different sender from send's queue resource | Independent subscriptions on the topic | **Publisher-axis.** Topology fully enforced at publish time |
| Pulsar | ✅ | ✅ | Distinct topic via `MessagingConventions.GetBroadcastTopicName` | One Pulsar subscription per logical subscriber on the broadcast topic (subscription type stays consumer-side) | Convention-axis. Subscription type (Exclusive/Shared/Failover) is consumer-controlled |
| RedisStreams | ✅ | ✅ | Distinct stream via `MessagingConventions.GetBroadcastTopicName` | Distinct consumer group per subscriber on the broadcast stream | Convention-axis. Operational cost: N consumer groups per stream |
| AwsSqs | ✅ | ❌ (Phase 1; arrives in Phase 10) | — (Phase 10: SNS topic resource — publisher-axis) | — (Phase 10: SQS queues subscribed to the SNS topic) | Publisher-axis once Phase 10 lands; throws at registration until then |
| PostgreSql (outbox) | ✅ | ❌ | — | — | Competing queue only |
| SqlServer (outbox) | ✅ | ❌ | — | — | Competing queue only |
| InMemoryQueue | ✅ | ❌ | — | — | Queue-only by design |
| InMemoryStorage | ✅ | ✅ | In-process subscriber enumeration | N/A (in-process) | In-process; fan-out trivially supported |

**Axis terminology and why it matters:**
- **Publisher-axis** (RabbitMQ, AzureServiceBus, AwsSqs+SNS in Phase 10): the publisher's API call materially differs between send and broadcast (different exchange / different sender resource). Broadcast topology is fully enforced from the publish side. `IDirectBroadcastPublisher` and `IDirectSendPublisher` invoke distinct broker primitives.
- **Convention-axis** (NATS, Kafka, Pulsar, RedisStreams): the wire protocol for send and broadcast is the same (write to a topic/subject/stream); the publisher distinguishes the two by writing to a distinct topic name produced by `MessagingConventions.GetBroadcastTopicName`. **Subscriber configuration must match the convention** — if a subscriber wires up a queue group on a NATS broadcast subject, shares a `group.id` on a Kafka broadcast topic, or reuses one Pulsar subscription across logical subscribers, broadcast semantics silently degrade to competing-consumer behavior. The publisher cannot enforce subscriber wiring; capability validation in Unit 3a/3b emits startup warnings when convention-axis broadcast topics are observed with mismatched subscriber configuration.
- **Operational cost:** convention-axis fan-out on Kafka and RedisStreams multiplies consumer-side resource usage linearly with subscriber count (one consumer group per subscriber). Publisher-axis broadcast (RabbitMQ, ASB) scales horizontally with the broker. Operators sizing a cluster need the distinction even when both are checkmarked.

### Key Technical Decisions

- **Two publisher interfaces, not a DeliveryMode enum.** Intent is part of the method name, not a parameter. Rationale: matches MT/Wolverine, fail-fast at DI registration, natural C# tooling (you cannot accidentally pass `Broadcast` to a queue-only transport).
- **Durability axis preserved via inheritance.** `IDirectSendPublisher : ISendPublisher`, `IOutboxSendPublisher : ISendPublisher`, same for broadcast. Durability markers stay exactly as they are conceptually; only the semantic axis is added.
- **TenantId as typed property, not a magic header.** `TenantId? : string` on `PublishOptions` and `ConsumeContext`; provider maps it to `Headers.TenantId` on the wire. Consumers read the property, not the header dictionary.
- **Capability declared at registration.** Provider `AddXxxMessaging(...)` extensions register only the interfaces the transport actually implements. No runtime `NotSupportedException` from a method that exists but refuses; instead DI resolution fails fast with a readable message.
- **Outbox lives in Core, not per-provider.** `IOutboxSendPublisher` and `IOutboxBroadcastPublisher` are bound to a single transport-agnostic `OutboxPublisherDecorator<TTransport>` in `Headless.Messaging.Core` that persists envelopes via `IOutboxStore` and lets `OutboxDrainer<TTransport>` redispatch through the registered Direct publisher for `TTransport`. Provider packages implement only the Direct interfaces; Outbox composes via `services.AddOutbox<TTransport>()`. Rationale: keeps the transport × durability surface flat (11 transports × 2 Direct interfaces = 22 transport classes, plus 1 decorator + 1 drainer + N store implementations) instead of multiplying to 44; concentrates tenancy stamping, composite `(TenantId, MessageId)` dedup, retry backoff, and dead-letter observation in exactly one place; matches the Brighter / Wolverine split where outbox is a pipeline concern, not a transport concern. The decorator is also the single point where `PublishOptions.TenantId` is captured onto the persisted envelope before the drainer fires — provider code never touches tenancy.
- **`MessagingConventions.GetBroadcastTopicName` is the publisher-side mechanism for convention-axis transports.** For Kafka, NATS, Pulsar, and RedisStreams, broadcast and send share a wire protocol (one publish call to one topic/subject/stream); the publisher distinguishes the two by writing to a distinct topic name produced by this helper. Capability validation in Unit 3a/3b asserts the helper is wired into both the publisher and the consumer subscription path so a `BroadcastAsync<T>` call materially differs from `SendAsync<T>` on the wire. Subscribers MUST configure distinct consumer groups (Kafka, RedisStreams) / queue groups (NATS) / subscriptions (Pulsar) for broadcast semantics — the publisher cannot enforce this; integration tests in Unit 3a/3b emit a warning when broadcast topics see fewer than two distinct subscriber group IDs.
- **Completion contract for `await SendAsync<T>` / `await BroadcastAsync<T>` is broker-acknowledged durable accept.** Phase 1 pins a single mental model across all 11 transports: a publisher call returns successfully only after the broker has durably accepted the message (transport-defined "durable accept" — JetStream `PubAck`, Kafka `acks=all`, RabbitMQ publisher confirms, ASB `SendMessageAsync` ack, SQS `SendMessageAsync` ack, Pulsar producer ack, RedisStreams `XADD` reply). For `IOutboxSendPublisher` / `IOutboxBroadcastPublisher`, the contract is **outbox-row commit** — the call returns when the row has committed in the outbox store (the drainer reaches broker-ack later, asynchronously, with retry). Providers that cannot meet broker-ack by default (NATS core without JetStream, Kafka with `acks < all`, RabbitMQ without publisher confirms) MUST configure their producer for ack-by-default at registration time, or the `MessagingCapabilityOptionsValidator` rejects `IDirectSendPublisher` / `IDirectBroadcastPublisher` registration with a readable `OptionsValidationException` at host startup. Rationale: the durability marker (`IDirect…Publisher` vs `IOutbox…Publisher`) implies a guarantee — Phase 1 is the right time to make the runtime actually deliver it, before more code accumulates against the today's "knowable from reading provider source" semantic. XML docs on `ISendPublisher.SendAsync<T>` and `IBroadcastPublisher.BroadcastAsync<T>` state the contract verbatim. The OTel enricher (Unit 5) records `headless.messaging.completion = "broker_ack" | "outbox_commit"` so traces self-describe which path the publish took. Attribute uses the `headless.messaging.*` prefix (not `messaging.headless.*`) because OTel's naming spec discourages reusing existing semconv namespaces as prefixes for third-party attributes — see Unit 5 Goal for the full rationale. Out of scope for Phase 1: caller-selectable lower-latency `OnEnqueue` mode — deferred to a future phase if a hot-path use case surfaces; collapsing it into Phase 1 would re-introduce per-call configurability and dilute the single-contract value.
- **Consumer surface unchanged.** `IConsume<T>` stays the single entry point. Whether `T` arrived via Send or Broadcast is observable via `ConsumeContext.DeliveryKind` (new enum) for diagnostics and tracing. **Business logic branching on `DeliveryKind` is discouraged**: if send vs broadcast means different behavior, model it as two distinct message types or two distinct consumers, not one consumer that forks. XML docs on `DeliveryKind` state this explicitly.
- **`IMessagePublisher` becomes `internal`.** Remains a shared base type for pipeline behavior (outbox, OpenTelemetry wrap) but is not a public resolution seam. Greenfield posture (per `CLAUDE.md`) makes re-exposing trivial if a concrete wrapper use case ever surfaces; YAGNI until then.
- **NATS stream config uses a callback-shaped options surface.** Matches the existing Headless FluentValidation + hosting options pattern; no direct `StreamConfig` exposure in abstractions.
- **Capability fail-fast via FluentValidation on a capability-descriptor options type.** Per `CLAUDE.md`, validation flows through `services.AddOptions<T, TValidator>().ValidateOnStart()` — not a bespoke `IHostedService`. Each provider's `AddXxxMessaging` extension registers a `MessagingCapabilityOptions` (declared publisher set, forbidden set, per-transport flags) bound via the standard options pipeline, with an internal `MessagingCapabilityOptionsValidator : AbstractValidator<MessagingCapabilityOptions>` that asserts the declared interfaces are actually registered in the container (via a post-build check wired into the validator). `ValidateOnStart()` surfaces any mismatch before the host reaches consumer startup. No `IHostedService`, no custom exception hierarchy — validation failures throw `OptionsValidationException` with the structured message list FluentValidation already produces.
- **P1⇄P2 transition contract for the outbox decorator (binding).** `OutboxPublisherDecorator<TTransport>`, `OutboxDrainer<TTransport>`, `IOutboxStore`, and `services.AddOutbox<TTransport>()` ship in Phase 1 / Unit 1b and survive into Phase 2 unchanged in class signature, DI registration, keying, and runtime semantics. Phase 2's behavior pipeline composes **around** the decorator (`BehaviorChain → OutboxPublisherDecorator<T> → IDirectSendPublisher<T>` on persist; `BehaviorChain → IDirectSendPublisher<T>` on drainer redispatch); it does not absorb, replace, or re-express outbox as an `IPublishBehavior<T>`. Rationale: the decorator is on the **transport axis** (`<TTransport>`) and is stateful (owns `IOutboxStore` lifecycle, the drainer, and per-transport publisher resolution); behaviors are on the **message axis** (`<T>`) and stateless. Collapsing both onto one seam would re-introduce keyed-DI resolution inside a per-message generic, split outbox's identity across multiple registrations, and risk double-pipelining (behaviors running once on persist, again on redispatch through a behavior-shaped outbox). Provider packages that landed in Units 3a/3b continue to register only the Direct interfaces in Phase 2. Any future "behavior-shaped" outbox proposal is out of scope for Phase 2 and would require its own RFC.
- **Deduplication keys include `TenantId` when tenancy is in use.** Outbox tables, Redis idempotency keys, and any provider-level "seen-MessageId" dedupe use a composite `(TenantId, MessageId)` key when `TenantId` is non-null, and fall back to `MessageId` alone when `TenantId` is null. Single-tenant applications (no `AddHeadlessMultiTenancy`, no explicit `PublishOptions.TenantId`) see identical behavior to today — this matches Wolverine and Brighter's single-key dedup. Multi-tenant applications get cross-tenant collision protection automatically as soon as `TenantId` starts flowing. Schema: dedup tables store `TenantId` as a nullable column; the composite unique constraint treats `NULL` as a distinct value (standard SQL semantics), which is the desired behavior — a single-tenant app can never collide with a multi-tenant app sharing the same store because they never share a `TenantId` value.

### Open Questions

#### Resolved During Planning

- *Should `DeliveryMode` be a flat enum on `PublishOptions`?* No — capability varies too much across the 11 transports, and MassTransit's precedent shows intent belongs in the API surface.
- *Should `IRetryBackoffStrategy` be split?* No — it already has both delay and predicate; the work is to **wire it**, not restructure it.
- *Should `TenantId` be resolved automatically from an `ITenantContext` service?* Not in Phase 1 — tenancy is data; auto-propagation via a pipeline behavior is Phase 3 (depends on the Phase 2 behavior pipeline).

#### Deferred to Implementation

- Exact namespace layout for the new interfaces (`Headless.Messaging.Abstractions` root vs a `Publishers/` sub-namespace) — land wherever the existing markers live.
- Whether `IScheduledPublisher` needs a broadcast counterpart on day one, or whether scheduled broadcast stays a Phase 2 concern. Lean: day-one if and only if any provider already has scheduled broadcast support for free; otherwise defer.
- Naming for the `DeliveryKind` enum values (`Send`/`Broadcast` vs `Queued`/`FanOut`). Lean: mirror the interface names.
- Precise diagnostic message when a consumer resolves `IBroadcastPublisher` against a queue-only transport.

### Output Structure

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
  IDeadLetterObserver.cs               # new: transport-agnostic DLQ/terminal-failure observer
  DeadLetterEvent.cs                   # new: envelope carrying TenantId, MessageId, MessageType, attempts, reason

src/Headless.Messaging.Core/
  IOutboxStore.cs                      # new: persist + composite-(TenantId, MessageId) dedup + drain cursor
  OutboxEnvelope.cs                    # new: persisted record (payload, headers, tenancy, attempts, status)
  OutboxPublisherDecorator.cs          # new: implements IOutboxSendPublisher + IOutboxBroadcastPublisher; persists via IOutboxStore
  OutboxDrainer.cs                     # new: BackgroundService<TTransport> that drains store and dispatches via the registered Direct publisher
  OutboxRegistration.cs                # new: services.AddOutbox<TTransport>() extension

src/Headless.Messaging.OpenTelemetry/
  IActivityTagEnricher.cs              # new hook for consumer apps

docs/llms/
  messaging-envelope.md                # new: envelope shape, capability matrix, migration notes
```

### High-Level Technical Design

#### Publisher interface shape (directional — not implementation)

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

#### Capability registration (directional)

```text
services.AddHeadlessMessagingNats(opts => { ... })
  registers: IDirectSendPublisher, IOutboxSendPublisher,
             IDirectBroadcastPublisher, IOutboxBroadcastPublisher

services.AddHeadlessMessagingSqs(opts => { ... })
  registers: IDirectSendPublisher, IOutboxSendPublisher
  does NOT register: IBroadcastPublisher implementations
  -> consumer requesting IBroadcastPublisher gets DI error at startup
```

#### Tenancy flow (directional)

```text
caller sets PublishOptions.TenantId = "acme"
   │
publisher serializes → Headers["headless-tenant-id"] = "acme"
   │
transport wire
   │
consumer pipeline reads Headers["headless-tenant-id"]
   │
ConsumeContext.TenantId = "acme"  (typed, not header lookup)
```

### Implementation Units (Phase 1)

- [ ] **Unit 1: Introduce `ISendPublisher` / `IBroadcastPublisher` and demote `IMessagePublisher`**

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
- Modify: `src/Headless.Messaging.Abstractions/MessagingConventions.cs` — add `GetBroadcastTopicName(Type messageType)` helper used by every convention-axis provider in Units 3a/3b (NATS subjects, Kafka topics, Pulsar topics, RedisStreams streams). Helper produces a deterministic distinct name so `BroadcastAsync<T>` routes to a different wire destination than `SendAsync<T>` on transports where the publisher API does not encode the semantic difference natively.
- Test: `tests/Headless.Messaging.Tests.Unit/Abstractions/PublisherInterfaceShapeTests.cs`
- Test: `tests/Headless.Messaging.Tests.Unit/Abstractions/MessagingConventionsBroadcastTopicTests.cs` — asserts the helper returns a name distinct from the send-side name for the same type, is deterministic across calls, and survives generic type arguments.

**Approach:**
- `ISendPublisher.SendAsync<T>` and `IBroadcastPublisher.BroadcastAsync<T>` share a method signature shape with the existing `PublishAsync`; only the name and intent differ.
- Durability markers are empty interfaces — they exist for DI resolution and XML docs, just like today's `IDirectPublisher`.
- `DeliveryKind` is a plain enum `{ Send, Broadcast }` exposed on `ConsumeContext`.
- XML docs on `ISendPublisher.SendAsync<T>` and `IBroadcastPublisher.BroadcastAsync<T>` state the completion contract verbatim per the Key Technical Decisions bullet: "the returned task completes only after the broker has durably accepted the message (transport-defined durable accept)." The corresponding doc on the `IOutbox…Publisher` markers states: "the returned task completes when the outbox row has committed; the drainer reaches broker-ack asynchronously."

**Patterns to follow:**
- Existing empty-marker pattern in `IDirectPublisher.cs` / `IOutboxPublisher.cs`.
- File-scoped namespaces, `sealed` where applicable per CLAUDE.md.

**Test scenarios:**
- Happy path: Interfaces compile and resolve via a fake DI container.
- Edge case: Attempting to resolve `IBroadcastPublisher` from a container that registered only `ISendPublisher` surfaces a clear missing-registration error.
- Integration: `ConsumeContext.DeliveryKind` is reachable from a consumer written against `IConsume<T>`.

**Verification:**
- Solution builds with `dotnet build --no-incremental -v:q -nologo /clp:ErrorsOnly` clean.
- No provider project references `IDirectPublisher` or `IOutboxPublisher` by their old names (Unit 3 lands the provider edits).

---

- [ ] **Unit 1b: Land the transport-agnostic Outbox decorator + store + drainer in `Headless.Messaging.Core`**

**Goal:** Implement Outbox once as a Core decorator over the Direct interfaces, so provider packages in Units 3a/3b only need to ship Direct publishers. Bind `IOutboxSendPublisher` / `IOutboxBroadcastPublisher` to the Core decorator via `services.AddOutbox<TTransport>()`. Eliminates the 11-providers × 2-durability × 2-semantic = 44-class explosion; replaces it with 22 Direct classes + 1 decorator + 1 drainer + N stores.

**Requirements:** R1, R2, R3 (capability registration is pure Direct after this unit; Outbox markers register only when `AddOutbox<TTransport>()` is called).

**Dependencies:** Unit 1.

**Files:**
- Create: `src/Headless.Messaging.Core/IOutboxStore.cs` — persist, dedup-by-`(TenantId, MessageId)`, claim-and-drain cursor, mark-completed, mark-failed-with-attempt-count.
- Create: `src/Headless.Messaging.Core/OutboxEnvelope.cs` — payload, headers, `TenantId`, `MessageId`, `CorrelationId`, status (`Pending` / `Dispatched` / `Failed`), `AttemptCount`, `NextAttemptAt`, `DeliveryKind`.
- Create: `src/Headless.Messaging.Core/OutboxPublisherDecorator.cs` — implements `IOutboxSendPublisher` and `IOutboxBroadcastPublisher`. On `SendAsync` / `BroadcastAsync`: snapshots `PublishOptions.TenantId` (or resolves from `ITenantContext` later in Phase 3), writes one `OutboxEnvelope` row to `IOutboxStore` inside the ambient transaction, returns immediately. Never calls the transport directly.
- Create: `src/Headless.Messaging.Core/OutboxDrainer.cs` — `BackgroundService` generic over `TTransport`. Polls `IOutboxStore` for pending rows scoped to `TTransport`, dispatches each via the registered Direct publisher (`IDirectSendPublisher` for `DeliveryKind.Send`, `IDirectBroadcastPublisher` for `DeliveryKind.Broadcast`), applies `IRetryBackoffStrategy` per Unit 4, emits `DeadLetterEvent` via `IDeadLetterObserver` per Unit 8 on terminal failure.
- Create: `src/Headless.Messaging.Core/OutboxRegistration.cs` — `services.AddOutbox<TTransport>()` extension that binds the Outbox markers to the decorator and registers the drainer hosted service. Validates (via FluentValidation + `ValidateOnStart()`, per `CLAUDE.md`) that `IDirectSendPublisher` and/or `IDirectBroadcastPublisher` are registered for `TTransport` — otherwise startup fails with `OptionsValidationException`.
- Test: `tests/Headless.Messaging.Core.Tests.Unit/Outbox/OutboxPublisherDecoratorTests.cs` — asserts `SendAsync` writes to store and does not invoke transport.
- Test: `tests/Headless.Messaging.Core.Tests.Unit/Outbox/OutboxDrainerTests.cs` — asserts drainer dispatches via Direct publisher, retries via backoff, emits `DeadLetterEvent` after max attempts.
- Test: `tests/Headless.Messaging.Core.Tests.Integration/Outbox/CompositeDedupKeyTests.cs` — same `MessageId` across two `TenantId`s yields two distinct envelopes; same `(TenantId, MessageId)` is deduped.

**Approach:**
- The decorator is the *only* place that captures tenancy onto the persisted envelope. Provider transport classes remain tenancy-agnostic — they read tenancy off `OutboxEnvelope.Headers` exactly as they would for any incoming `Headers["headless-tenant-id"]`.
- `IOutboxStore` is an interface in Core; concrete implementations (`PostgreSqlOutboxStore`, `SqlServerOutboxStore`) live in their own packages and ship in their respective provider Units (3a/3b for the SQL providers, no impact on transport-only providers like NATS/Kafka).
- Drainer is generic over `TTransport` so multiple Outbox+Transport pairs can coexist (e.g., `AddOutbox<NatsTransport>()` and `AddOutbox<RabbitMqTransport>()` in the same host run independent drainers against the same store with distinct transport tags).
- Composite dedup key `(TenantId, MessageId)` is enforced at the store level; null `TenantId` is treated as a distinct value per standard SQL semantics (single-tenant apps continue to work unchanged).
- The decorator is registered with `ServiceLifetime.Scoped` so it picks up the ambient `IOutboxTransaction` / `DbContext` from the caller's scope.
- **P1⇄P2 binding contract (carried forward from Key Technical Decisions).** The class signatures of `OutboxPublisherDecorator<TTransport>`, `OutboxDrainer<TTransport>`, `IOutboxStore`, and the public `services.AddOutbox<TTransport>()` extension shipped in this Unit are binding through Phase 2 — Phase 2's behavior pipeline composes around them (`BehaviorChain → OutboxPublisherDecorator<T> → IDirectSendPublisher<T>` on persist; `BehaviorChain → IDirectSendPublisher<T>` on drainer redispatch) and does not re-express outbox as an `IPublishBehavior<T>`. Implementation choices in this Unit that would force a Phase 2 rewrite of the decorator's transport-axis identity (e.g., resolving the underlying Direct publisher through a per-message generic, splitting tenancy stamping out of the decorator, moving `(TenantId, MessageId)` dedup into a separate behavior) are out of scope and rejected at review.

**Patterns to follow:**
- `Headless.DistributedLocks.Core` package layout — abstractions + Core implementation, providers shipping concrete stores.
- `Headless.Caching.Hybrid` decorator pattern over `ICache` — same shape, different concern.
- FluentValidation + `services.AddOptions<T, TValidator>().ValidateOnStart()` per `CLAUDE.md`.

**Test scenarios:**
- Happy path: `SendAsync` persists envelope, returns; drainer dispatches and marks `Dispatched`.
- Happy path: `BroadcastAsync` persists with `DeliveryKind = Broadcast`; drainer routes to `IDirectBroadcastPublisher`.
- Edge case: Transport throws transient → drainer reschedules per `IRetryBackoffStrategy` → eventually succeeds.
- Edge case: Transport throws terminal → drainer emits `DeadLetterEvent` after `MaxAttempts` and marks `Failed`.
- Edge case: Same `MessageId` published twice for same `TenantId` → second insert deduped by composite-key constraint, drainer dispatches once.
- Edge case: Same `MessageId` for two different `TenantId`s → two envelopes, two dispatches.
- Error path: `AddOutbox<TTransport>()` called without a registered `IDirectSendPublisher` for `TTransport` → host startup fails with `OptionsValidationException` listing the missing registration.

**Verification:**
- No provider package under `src/Headless.Messaging.Nats/`, `Kafka/`, `RabbitMq/`, etc. contains an `IOutboxSendPublisher` or `IOutboxBroadcastPublisher` implementation. Verified by `grep -r "class.*: IOutbox" src/Headless.Messaging.* --include='*.cs'` returning only the Core decorator.
- `services.AddHeadlessMessagingNats(...).AddOutbox<NatsTransport>()` resolves all four publisher markers; omitting `AddOutbox` leaves only the two Direct markers resolvable (DI failure on Outbox resolution is the expected behavior, surfaced at host startup, not at runtime).

---

- [ ] **Unit 2: Promote `TenantId` to first-class envelope**

**Goal:** Add typed `TenantId` to `PublishOptions` and `ConsumeContext`, add `Headers.TenantId` constant, and wire header↔property mapping in the shared publisher/consumer base.

**Requirements:** R4.

**Dependencies:** Unit 1.

**Files:**
- Modify: `src/Headless.Messaging.Abstractions/PublishOptions.cs` (add `TenantId? : string`)
- Modify: `src/Headless.Messaging.Abstractions/ConsumeContext.cs` (add `TenantId? : string`, `DeliveryKind` property)
- Modify: `src/Headless.Messaging.Abstractions/Headers.cs` (add `TenantId = "headless-tenant-id"`)
- Modify: `src/Headless.Messaging.Core/` shared publish/consume pipeline (map property ↔ header)
- Test: `tests/Headless.Messaging.Tests.Unit/Abstractions/TenantIdRoundTripTests.cs`

**Approach:**
- On publish: if `PublishOptions.TenantId` is set, the pipeline **overwrites** `headers["headless-tenant-id"]` with the property value (property always wins, raw header is not preserved). If the caller set the raw header but not the property, the header is left untouched. When property and raw header disagree, a debug log emits both values for diagnosis: `TenantId property overrode raw header. property={prop} header={hdr}`.
- On consume: the pipeline reads `headers["headless-tenant-id"]` and populates `ConsumeContext.TenantId`. Missing header → property is `null`. The consume-side header value is bounds-checked to the same 200-char limit and character-set rules applied on publish (via `Headless.Checks`); a header that exceeds the limit or carries disallowed characters is treated as a malformed envelope — the message is rejected (logged at warning, surfaced to `IDeadLetterObserver` per Unit 8) rather than silently truncated. This blocks an upstream sender on a shared broker from forging an oversized `headless-tenant-id` value that would propagate into the outbox `TenantId` column or OTel spans.
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

**Rollout-state callout (Units 2 ↔ 3a/3b):**
Unit 2 introduces the envelope contract (property + header + shared pipeline). Per-provider dedup-key and outbox unique-constraint migrations to composite `(TenantId, MessageId)` land inside Units 3a and 3b. Interim state between Unit 2 and full 3a/3b completion: `TenantId` is envelope-visible on all providers but dedup-correct only on providers whose migration has landed. The Unit 6 capability matrix tracks per-provider dedup-migration status until every provider is green.

---

- [ ] **Unit 3a: Migrate Tier-1 providers (NATS, RabbitMQ, AzureServiceBus, AwsSqs) to the new interfaces**

**Goal:** Land the reference implementations for the highest-traffic providers first. NATS anchors the broadcast-capable shape; SQS anchors the Send-only / capability-not-registered shape. ASB and RabbitMQ validate that the topology-per-semantics approach works outside NATS.

**Note on "register all four markers" wording (applies to Units 3a and 3b):** Per the Core-decorator decision in Key Technical Decisions and Unit 1b, provider packages register only the Direct interfaces (`IDirectSendPublisher` / `IDirectBroadcastPublisher`) keyed by their transport type. The Outbox interfaces resolve through `OutboxPublisherDecorator<TTransport>` once the consumer chains `services.AddOutbox<TTransport>()`. Capability-registration tests in this Unit must cover both the Direct-only and Direct+Outbox configurations to assert end-to-end resolvability.

**Requirements:** R1, R2, R3.

**Dependencies:** Unit 1, Unit 1b.

**Files:**
- Modify: `src/Headless.Messaging.Nats/` — register the Direct send + Direct broadcast publishers keyed by `NatsTransport`. **Completion contract enforcement:** registration validator rejects the Direct interfaces unless JetStream is configured (NATS core has no broker ack); core-only consumers must register `IOutboxSendPublisher` / `IOutboxBroadcastPublisher` instead and pair with `services.AddOutbox<NatsTransport>()`.
- Modify: `src/Headless.Messaging.RabbitMq/` — register Direct send (direct exchange) + Direct broadcast (fanout exchange) publishers keyed by `RabbitMqTransport`. **Completion contract enforcement:** registration validator rejects Direct interfaces unless publisher confirms are enabled on the channel (`ConfirmSelectAsync` + `WaitForConfirmsOrDieAsync` on every publish); the validator inspects the channel-factory options and fails fast if confirms are not opted in.
- Modify: `src/Headless.Messaging.AzureServiceBus/` — register Direct send (queue) + Direct broadcast (topic + subscription) publishers keyed by `AzureServiceBusTransport`. ASB's `SendMessageAsync` is broker-ack by default; no extra producer config required to satisfy the completion contract.
- Modify: `src/Headless.Messaging.AwsSqs/` — register Direct send only keyed by `AwsSqsTransport`; do **not** register broadcast (SNS-fronted topology lands in a follow-up plan). SQS `SendMessageAsync` is broker-ack by default.
- Add: per-provider `MessagingCapabilityOptions` + `MessagingCapabilityOptionsValidator : AbstractValidator<MessagingCapabilityOptions>` (same-file pattern, per `CLAUDE.md`), wired via `services.AddOptions<MessagingCapabilityOptions, MessagingCapabilityOptionsValidator>().ValidateOnStart()`. Shared validation helper in `Headless.Messaging.Core` performs (a) the "declared interfaces are registered" cross-check **and** (b) the completion-contract cross-check (Direct interfaces registered only when the producer is configured to deliver broker-ack durable accept per the Key Technical Decisions bullet). No `IHostedService`, no custom exception type — failures surface as `OptionsValidationException` at host startup with the offending transport, the failing rule, and the remediation in the message.
- Test: `tests/Headless.Messaging.Nats.Tests.Integration/CapabilityRegistrationTests.cs`
- Test: `tests/Headless.Messaging.RabbitMq.Tests.Integration/CapabilityRegistrationTests.cs`
- Test: `tests/Headless.Messaging.AzureServiceBus.Tests.Integration/CapabilityRegistrationTests.cs`
- Test: `tests/Headless.Messaging.AwsSqs.Tests.Integration/CapabilityRegistrationTests.cs`
- Test: `tests/Headless.Messaging.Nats.Tests.Integration/BroadcastConventionAxisWarningTests.cs`

**Convention-axis warning test scenarios (NATS, this Unit):**
- Two consumers subscribe to the broadcast subject produced by `MessagingConventions.GetBroadcastTopicName<T>()` with the **same** queue group → assertion that startup logs a warning at WARN level: `"NATS broadcast subject {subject} has subscribers sharing queue group '{group}'; broadcast semantics will degrade to competing-consumer (one subscriber receives each message). Configure distinct queue groups per subscriber for broadcast, or omit the queue group entirely."` Capability validation does not fail the host — broadcast on convention-axis transports is a subscriber-side contract; the publisher and host can only warn.
- Sanity counterpart: two consumers subscribe with **distinct** queue groups (or no queue group) → no warning logged; both consumers receive every published message.
- Send-side regression guard: a `SendAsync<T>` integration test asserts the wire subject differs from `GetBroadcastTopicName<T>()` so the two semantic axes do not collide on the same subject by accident.

- [ ] **Unit 3b: Migrate Tier-2 providers (Kafka, Pulsar, RedisStreams, InMemoryStorage, InMemoryQueue, PostgreSql, SqlServer)**

**Goal:** Apply the Unit 3a reference shape to the remaining seven providers. Split from 3a to keep the blast radius of any single PR bounded.

**Requirements:** R1, R2, R3.

**Dependencies:** Unit 3a landed.

**Files:**
- Modify: `src/Headless.Messaging.Kafka/` — register Direct send + Direct broadcast publishers keyed by `KafkaTransport` (broadcast via distinct consumer groups on the same topic). **Completion contract enforcement:** registration validator rejects Direct interfaces unless `ProducerConfig.Acks == Acks.All` (broker-quorum acknowledgement); lower ack levels are configurable but require explicit opt-in via `MessagingCapabilityOptions.AllowReducedAcks = true` plus the human reason recorded in options metadata.
- Modify: `src/Headless.Messaging.Pulsar/` — register Direct send (Exclusive) + Direct broadcast (Shared/Failover) publishers keyed by `PulsarTransport`. Pulsar `SendAsync` is broker-ack by default; no extra producer config required.
- Modify: `src/Headless.Messaging.RedisStreams/` — register Direct send + Direct broadcast publishers keyed by `RedisStreamsTransport` (distinct consumer groups for broadcast). Redis `XADD` reply is treated as broker-ack-equivalent (memory persistence); the validator records the AOF-config caveat in startup logs once per host.
- Modify: `src/Headless.Messaging.InMemoryStorage/` — register Direct send + Direct broadcast publishers keyed by `InMemoryStorageTransport`; in-memory fan-out is trivially supported and the call returns when the in-memory store has accepted the envelope (treated as broker-ack-equivalent for this provider per the Key Technical Decisions bullet).
- Modify: `src/Headless.Messaging.InMemoryQueue/` — register Direct send only keyed by `InMemoryQueueTransport`; broadcast is out of scope for this provider by design. Channel-accept is treated as broker-ack-equivalent for this provider.
- Modify: `src/Headless.Messaging.PostgreSql/` — register Direct send only keyed by `PostgreSqlTransport` (see Deferred: transactional broadcast bridge). Direct send returns when the row INSERT commits (the publisher *is* the durable store for this transport); contract is satisfied by definition. Also ships `PostgreSqlOutboxStore : IOutboxStore` so consumers can pair `AddHeadlessMessagingPostgreSql(...)` with `AddOutbox<NatsTransport>()` (or any transport) and persist the outbox table in their PG database.
- Modify: `src/Headless.Messaging.SqlServer/` — register Direct send only keyed by `SqlServerTransport`; same row-commit semantic as PostgreSql. Also ships `SqlServerOutboxStore : IOutboxStore` for consumers using SQL Server as their outbox store.
- Test: `tests/Headless.Messaging.*.Tests.Integration/CapabilityRegistrationTests.cs` per provider.
- Test: `tests/Headless.Messaging.Kafka.Tests.Integration/BroadcastConventionAxisWarningTests.cs`
- Test: `tests/Headless.Messaging.Pulsar.Tests.Integration/BroadcastConventionAxisWarningTests.cs`
- Test: `tests/Headless.Messaging.RedisStreams.Tests.Integration/BroadcastConventionAxisWarningTests.cs`

**Convention-axis warning test scenarios (Kafka, Pulsar, RedisStreams):**
- **Kafka:** two consumers subscribe to the broadcast topic produced by `MessagingConventions.GetBroadcastTopicName<T>()` with the **same** `group.id` → assertion that startup logs WARN: `"Kafka broadcast topic {topic} has subscribers sharing group.id='{group}'; broadcast semantics will degrade to competing-consumer (one subscriber receives each message). Configure distinct group.id per subscriber for broadcast."` Distinct `group.id` counterpart logs no warning.
- **RedisStreams:** two consumers subscribe to the broadcast stream with the **same** consumer group → assertion that startup logs WARN: `"RedisStreams broadcast stream {stream} has subscribers sharing consumer group '{group}'; broadcast semantics will degrade to competing-consumer. Configure distinct consumer groups per subscriber for broadcast."` Distinct consumer-group counterpart logs no warning.
- **Pulsar:** two logical subscribers reuse the **same** subscription name on the broadcast topic → assertion that startup logs WARN: `"Pulsar broadcast topic {topic} has subscribers sharing subscription '{name}'; broadcast semantics will degrade to competing-consumer under Shared/Failover subscription types. Configure distinct subscription names per logical subscriber for broadcast."` Distinct subscription names counterpart logs no warning.
- **Send-side regression guard (all three):** a `SendAsync<T>` integration test asserts the wire topic/stream/subscription differs from `GetBroadcastTopicName<T>()` so the two semantic axes do not collide on the same destination by accident.
- **Out of scope:** InMemoryStorage in-process fan-out is verified by existing per-consumer-delivery tests, no convention-axis warning needed (no group concept). InMemoryQueue and PostgreSql/SqlServer register Send only; broadcast warning tests do not apply.

- [ ] **Unit 3c: Downstream consumer migration — existing tests, demos, samples**

**Goal:** Migrate every in-repo consumer of the old `IDirectPublisher` / `IOutboxPublisher` interfaces so the solution compiles and all pre-existing tests pass against the new shape. This is distinct from the new interface-shape tests introduced alongside Units 1–3b.

**Requirements:** R1, R9 (docs+examples stay truthful).

**Dependencies:** Units 3a + 3b.

**Files:**
- Modify: every `tests/Headless.Messaging.*.Tests.{Unit,Integration}/` test file that references `IDirectPublisher` or `IOutboxPublisher` — rename to `IDirectSendPublisher` / `IOutboxSendPublisher` or split into Send+Broadcast variants where the test covers both semantics.
- Modify: every `tests/Headless.Messaging.Tests.Harness/` helper and builder.
- Modify: every `demo/` and `samples/` application that constructs a publisher — replace with the new interfaces and add a broadcast demo where the transport supports it.
- Modify: `src/Headless.Messaging.Testing/` — the testing harness package exposes the following surface:
  - `FakeSendPublisher` implementing `IDirectSendPublisher` + `IOutboxSendPublisher`; records every `SendAsync` call into an ordered, tenant-aware `IReadOnlyList<RecordedSend>`.
  - `FakeBroadcastPublisher` implementing `IDirectBroadcastPublisher` + `IOutboxBroadcastPublisher`; records every `BroadcastAsync` call into `IReadOnlyList<RecordedBroadcast>`.
  - `MessagingTestHarness` entry-point that wires both fakes into DI with a single `services.AddHeadlessMessagingTestHarness()` call and exposes both recorders.
  - Assertion helpers (designed for AwesomeAssertions extension style): `harness.ShouldHaveSent<T>(predicate)`, `ShouldHaveBroadcast<T>(predicate)`, `ShouldHaveSentExactly(n)`, `ShouldHaveSentForTenant("acme")`, and `ShouldNotHaveBroadcast<T>()`.
  - `FakeDeadLetterObserver` capturing `DeadLetterEvent`s for assertion on failure-path tests.
- Modify: `src/Headless.Messaging.Dashboard/` and `src/Headless.Messaging.Dashboard.K8s/` — update references to renamed publisher interfaces so the packages compile. Rendering `DeliveryKind` in UI views is Phase 5 scope, not Phase 1.

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

### Shared guidance for Units 3a / 3b

**Approach (all providers):**
- Rename the existing publisher class(es) to reflect the new interface set; implementation logic stays identical for Send-side.
- Broadcast-capable providers add a parallel publisher class that uses the transport's native fan-out topology (exchange type, subscription type, distinct consumer group, etc.).
- **Broadcast partial-failure semantics (Phase 1 default):** `BroadcastAsync<T>` is best-effort — the call succeeds if the publish to the broker succeeds, regardless of per-subscriber downstream delivery outcomes. Per-subscriber terminal failures surface via `IDeadLetterObserver` (Unit 8), not via the publish call's return value or exception. Providers with transactional fan-out (outbox broadcast in Phase 8) can promote to at-least-once-to-every-subscriber; Phase 1 does not attempt transactional fan-out across heterogeneous subscribers. Alternatives considered and rejected for Phase 1: (a) "throw if any subscriber fails" — impossible on async transports where subscriber identity is not known at publish time; (b) "all-or-nothing via XA" — no supported transport in the matrix offers this. This matches MassTransit/Wolverine/NServiceBus defaults.
- Queue-only providers do **not** throw `NotSupportedException` from a Broadcast method — they simply do not register the interface. DI + `ValidateOnStart` surface the gap at host start.
- Each provider's DI extension registers only the interfaces actually implemented, documents capability in XML docs, and wires a `MessagingCapabilityOptions` + `MessagingCapabilityOptionsValidator` via `AddOptions<,>().ValidateOnStart()` that cross-checks declared vs registered interfaces at host startup. Failures surface as `OptionsValidationException` — no bespoke hosted service, no custom exception type.
- **Shared capability contract suite.** `tests/Headless.Messaging.Tests.Harness/CapabilityContract/` defines a reusable xUnit fixture that every provider integration test inherits: (1) registered interfaces exactly match the provider's declared capability vector, (2) Send delivers to one of N competing consumers, (3) Broadcast (when declared) delivers to all N subscribers, (4) resolving an undeclared interface fails with the standard DI "no service registered" error, and host startup with a capability mismatch fails with `OptionsValidationException`. Providers implement a thin fixture adapter; the contract assertions are centralized, not re-prosed per provider.
- Dedup tables / Redis idempotency keys / outbox unique constraints are updated to composite `(TenantId, MessageId)` as part of the same PR that adds `TenantId` to the envelope (Unit 2 dependency).

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

- [ ] **Unit 4: Wire `IRetryBackoffStrategy` across every provider's dispatch loop**

**Goal:** Every provider's consume pipeline honors the configured `IRetryBackoffStrategy.ShouldRetry` + `GetNextDelay` before falling through to DLQ. Currently only a subset wires it; NATS does not.

**Requirements:** R5.

**Dependencies:** Units 3a + 3b (so the dispatch classes are already in their final shape).

**Files:**
- Audit + modify: each `src/Headless.Messaging.*/` consumer/dispatcher class that handles message failure.
- Modify: `src/Headless.Messaging.Core/` if a shared retry wrapper makes sense after the audit.
- Test: `tests/Headless.Messaging.*.Tests.Integration/RetryBackoffTests.cs` per provider.

**Approach:**
- **Define the cross-provider retry contract first** (documented in Unit 6 and enforced by tests before provider edits land):
  - *Attempt* = one consumer invocation that returned or threw. Delivery-count header (`headless-attempt`) increments exactly once per attempt, regardless of transport-native redelivery counters.
  - *Delay source* = app-enforced when `GetNextDelay` returns a non-null `TimeSpan`; transport-enforced only when the strategy explicitly returns `null` and the provider has a native redelivery primitive.
  - *Immediate retry* = in-memory retry on the same consumer instance within the current dispatch; does not bump transport redelivery count, does bump `headless-attempt`.
  - *Requeue retry* = transport redelivery; bumps both transport count and `headless-attempt`. Provider picks one per dispatch based on `GetNextDelay` value and its own capability.
  - *Delivery count* surfaced to the strategy = `headless-attempt`, not the transport-native value. This is the single number every strategy sees.
- Audit pass: for each provider, locate the `try/catch` around consumer invocation and document today's behavior (retry count source? fixed delay? immediate DLQ?) against the contract above.
- Replace per-provider retry logic with `IRetryBackoffStrategy` resolution from DI. **Default strategy shape (applied uniformly across all providers in Phase 1):** exponential backoff `100ms → 200ms → 400ms → 800ms → 1600ms` with `±25%` jitter, a hard per-delay cap of `30s`, `MaxAttempts = 5` (on the 6th attempt the message goes to DLQ), and `ShouldRetry = true` for all exceptions except `OperationCanceledException` (cancellation) and a configurable `NonRetryableExceptionTypes` set. Rationale: matches the MassTransit/NServiceBus community default and avoids the pathological "retry forever at a fixed interval" that ad-hoc per-provider logic tends to produce. Providers whose current behavior already aligns (e.g., `ExponentialBackoffStrategy` in `Headless.Messaging.Core`) keep shape; providers with ad-hoc behavior adopt the default explicitly and document the change in the Unit 4 audit.
- Hook `ShouldRetry(exception)` before scheduling the next delay; if it returns false, short-circuit to DLQ.
- Where a provider has native retry primitives (RabbitMQ dead-letter exchange, ASB delivery count, JetStream redelivery), keep those as the **transport** mechanism but drive them from `IRetryBackoffStrategy` outputs.

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

- [ ] **Unit 5: Add `IActivityTagEnricher` to `Headless.Messaging.OpenTelemetry`**

**Goal:** Let consumer apps attach custom OpenTelemetry tags to publish/consume spans without forking the package. Default implementation adds `headless.messaging.tenant_id`, `headless.messaging.delivery_kind`, and `headless.messaging.completion` automatically when the values are present. The `headless.messaging.completion` tag carries `"broker_ack"` or `"outbox_commit"` per the publish-time completion contract in Key Technical Decisions; provider Direct publishers stamp it via `IMessageTagContext.SetCompletion(string mode)` from inside the publish wrapper after the broker ack returns, and the Outbox decorator stamps `"outbox_commit"` after the row commits. **Namespace rationale:** OTel's attribute-naming spec explicitly discourages using existing semconv namespaces (including `messaging.*`) as prefixes for third-party attributes — a future OTel messaging registry update could collide with `messaging.headless.*`. The library uses the reverse-domain-style `headless.messaging.*` prefix for all custom attributes; OTel-standardized attributes (`messaging.operation.type`, `messaging.system`, `messaging.destination.name`) keep their canonical names so vendor dashboards work without configuration.

**Requirements:** R6, R4 (tenancy visibility in traces).

**Dependencies:** Unit 2 (TenantId must exist).

**Files:**
- Create: `src/Headless.Messaging.OpenTelemetry/IActivityTagEnricher.cs`
- Create: `src/Headless.Messaging.OpenTelemetry/DefaultActivityTagEnricher.cs`
- Modify: `src/Headless.Messaging.OpenTelemetry/` existing publish/consume span wrappers to invoke all registered enrichers.
- Modify: `src/Headless.Messaging.OpenTelemetry/` DI extension to register `DefaultActivityTagEnricher` and allow user-supplied enrichers via `Add<T>()`.
- Test: `tests/Headless.Messaging.Tests.Unit/OpenTelemetry/ActivityTagEnricherTests.cs`

**Approach:**
- `IActivityTagEnricher.Enrich(Activity, IMessageTagContext)` — context exposes typed `TenantId`, `DeliveryKind`, `MessageType`, `Topic`, `Completion` (set to `"broker_ack"` or `"outbox_commit"` by provider Direct publishers / the Outbox decorator after their respective acknowledgement event), and the raw `Headers`. The default enricher emits these as `headless.messaging.tenant_id`, `headless.messaging.delivery_kind`, and `headless.messaging.completion` respectively (custom prefix per OTel naming guidance — see Goal above).
- The default enricher also stamps the OTel-standardized attributes from the messaging semantic conventions: `messaging.operation.type` (`"send"` for `IDirectSendPublisher` / `IDirectBroadcastPublisher` / Outbox-redispatch publish spans, `"process"` for consume spans, `"create"` for the Outbox-persist span on the publisher side), and `messaging.system` per provider using the registered enum values (`"kafka"`, `"rabbitmq"`, `"pulsar"`, `"servicebus"`, `"aws_sqs"`; NATS uses the unregistered `"nats"` value pending OTel registration; in-memory transports, the in-process queue, and the SQL transports omit `messaging.system` because OTel registers no enum value for them, and stamping a non-registered string would itself be a violation of the standard). The standardized `messaging.destination.name` carries the topic / subject / stream / queue resolved at publish time. **Why two layers of attributes:** standardized names (`messaging.operation.type`, `messaging.system`, `messaging.destination.name`) make vendor messaging dashboards (Honeycomb, Datadog, Grafana Tempo, Aspire) light up out of the box without per-app configuration; the `headless.messaging.*` attributes layer Headless-axis distinctions (Send vs Broadcast, broker-ack vs outbox-commit, tenant) on top so custom dashboards can pivot on them without colliding with future OTel registry updates. `messaging.operation.type` enum is intentionally narrow (`send` / `receive` / `process` / `settle` / `create` / `deliver`) and does not distinguish unicast Send from multicast Broadcast — that distinction lives in `headless.messaging.delivery_kind`.
- Multiple enrichers compose; resolved `IEnumerable<IActivityTagEnricher>` preserves registration order.
- Default enricher always runs first via **explicit options-driven ordering**, not `services.Insert(0, ...)`. `OpenTelemetryMessagingOptions.EnricherOrder` (a `List<Type>`) defaults to `[typeof(DefaultActivityTagEnricher)]`. The publish/consume wrappers resolve `IEnumerable<IActivityTagEnricher>` from DI and sort by `EnricherOrder` index (unordered types run after ordered ones, in registration order). This matches OTel SDK conventions (ordered processors via options), survives arbitrary DI registration order, and avoids the fragility of descriptor-list position, which breaks when `TryAddEnumerable` or later `services.Insert` calls re-shuffle the list.

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

- [ ] **Unit 6: Capability matrix doc + `docs/llms/messaging-envelope.md`**

**Goal:** Single source of truth for the envelope shape, publisher capability per transport, and the migration from today's shape. Referenced by every provider README.

**Requirements:** R9.

**Dependencies:** Units 1–3c landed (so the matrix reflects reality).

**Files:**
- Create: `docs/llms/messaging-envelope.md`
- Modify: `src/Headless.Messaging.Abstractions/README.md` — add a "see also" link to the new doc.
- Modify: each `src/Headless.Messaging.*/README.md` to state which publisher interfaces the provider supports.
- Modify: top-level solution README where messaging is mentioned.

**Approach:**
- Doc sections: envelope fields (with types), capability matrix table (copy the one from this spec's Phase 1 capability matrix) extended with a **fan-out mechanism** column distinguishing `Native broker fan-out` (RabbitMQ fanout exchange, ASB topic, NATS subject, Pulsar Shared/Failover), `Emulated via per-subscriber group/materialization` (Kafka distinct `group.id`, RedisStreams per-subscriber consumer groups, Pulsar Exclusive), and `In-process` (InMemoryStorage). Callers infer operational cost, not just semantic capability.
- **Retry contract section**: mirror the cross-provider retry definitions from Unit 4 (attempt = one invocation, `headless-attempt` is the canonical count, delay-source rules, immediate vs requeue retry semantics). Single source of truth for strategy authors.
- **Migration appendix for application authors** — concrete rename and call-site recipes:
  - `IDirectPublisher` → `IDirectSendPublisher`
  - `IOutboxPublisher` → `IOutboxSendPublisher`
  - `publisher.PublishAsync(cmd)` → `sendPublisher.SendAsync(cmd)` for commands / point-to-point messages
  - `publisher.PublishAsync(evt)` → `broadcastPublisher.BroadcastAsync(evt)` for events / fan-out
  - When the selected provider does not support broadcast: either switch transports (see capability matrix) or route through the Phase 8 transactional broadcast bridge
  - `grep/sed` recipe per old → new name with caveats (skip XML docs, skip generated files)
- Keep XML docs on each public type in sync — Unit 1–5 must update them inline, not in this unit.

**Test scenarios:**
- Test expectation: none — documentation unit.

**Verification:**
- Manual review: a reader can pick the right publisher interface for their use case from the doc alone.
- `grep -r "IDirectPublisher" docs/` returns zero stale references.

---

- [ ] **Unit 7: NATS — per-stream config callback + `StreamAutoCreationMode`**

**Goal:** Let NATS consumers configure per-stream behavior declaratively without depending on raw `StreamConfig` types in their application code, and make auto-creation of streams an explicit mode choice.

**Requirements:** R8.

**Dependencies:** Unit 3a (NATS already on new interfaces).

**Files:**
- Modify: `src/Headless.Messaging.Nats/NatsMessagingOptions.cs` (or equivalent)
- Modify: `src/Headless.Messaging.Nats/` DI extension
- Test: `tests/Headless.Messaging.Nats.Tests.Integration/StreamProvisioningTests.cs`

**Approach:**
- Add a `StreamAutoCreationMode` enum: `Never | IfMissing | AlwaysReconcile` — explicit replacement for today's implicit "create-if-missing" behavior.
- **Production safety gate for `AlwaysReconcile`.** Default mode in `Development` is `IfMissing`; default in `Production` is `Never`. `AlwaysReconcile` is opt-in only and refuses to activate in `Production` unless either (a) the environment variable `HEADLESS_NATS_RECONCILE_ENABLED=1` is set, or (b) `NatsMessagingOptions.AllowReconcileInProduction = true` is set explicitly in code. Rationale: a config-driven reconcile that crash-loops a running cluster is a worse failure than a missing-stream error at deploy time.
- **Circuit breaker on repeated reconcile failures.** If `AlwaysReconcile` throws `NatsStreamReconcileException` more than 3 times within a 10-minute rolling window for the same stream, the mode automatically downgrades to `IfMissing` for that stream for the remainder of the process lifetime, logs a structured warning with the `ErrorCode`, and raises an `IDeadLetterObserver` event of kind `StreamReconcileDegraded` (so operators observe the downgrade in the same place they observe other terminal failures). Thresholds (`3`, `10min`) are options on `NatsMessagingOptions`.
- Add a per-stream configuration callback shape on the NATS options that lets the caller adjust stream settings without the abstractions package taking a `StreamConfig` dependency.
- Validate via FluentValidation per CLAUDE.md options convention.
- **Safe-reconcile boundary:** `AlwaysReconcile` only applies additive/safe changes that JetStream accepts online — `max_age`, `max_bytes`, `max_msgs` going **up**, subject list additions, `num_replicas` when matching cluster size, and description/metadata. Rejected changes (storage type change, retention policy change, `max_msgs` going **down** below current count, subject removals that would drop messages) fail startup with a structured `NatsStreamReconcileException` carrying a machine-readable `ErrorCode` (stable enum: `StorageTypeChanged`, `RetentionPolicyChanged`, `MaxMsgsDecreaseBelowCurrent`, `SubjectRemoved`, `ReplicasMismatch`, `ReconcileCallbackFailed`), the field name, current value, desired value, and human-readable operator remediation. Operators automate on `ErrorCode`; humans read the message.
- `Never` + missing stream is the only mode that yields a startup failure purely on existence; `IfMissing` is a no-op when present; `AlwaysReconcile` is the only mode that can fail on content drift.

**Patterns to follow:**
- `NatsMessagingOptions` existing shape + FluentValidation validator class in the same file.

**Test scenarios:**
- Happy path: `Never` mode + missing stream → startup fails with a clear error naming the stream.
- Happy path: `IfMissing` mode → stream created exactly once; second startup is a no-op.
- Happy path: `AlwaysReconcile` mode → drifted stream is reconciled to configured shape.
- Edge case: Callback throws during reconcile → startup fails and reports the stream name and underlying exception.

**Verification:**
- NATS integration tests cover all three modes.
- Application code has zero direct references to `StreamConfig` from the NATS.Net client.

---

- [ ] **Unit 8: Abstracted DLQ observability + NATS JetStream advisory adapter**

**Goal:** Introduce a transport-agnostic `IDeadLetterObserver` surface in `Headless.Messaging.Abstractions` so consumer apps can observe terminal failures uniformly across providers. Ship the NATS JetStream advisory-subject adapter as the first implementation; other providers (RabbitMQ DLX, ASB dead-letter queue, SQS DLQ, Kafka poison-message handler, SQL outbox failure column) adopt the same surface in follow-up work.

**Requirements:** R8, R9 (observability parity across providers).

**Dependencies:** Unit 7.

**Files:**
- Create: `src/Headless.Messaging.Abstractions/IDeadLetterObserver.cs` — `ValueTask OnDeadLetteredAsync(DeadLetterEvent evt, CancellationToken ct)`.
- Create: `src/Headless.Messaging.Abstractions/DeadLetterEvent.cs` — record carrying `TenantId`, `MessageId`, `MessageType`, `Attempts`, `TerminalReason`, `Provider`, `SourceSubjectOrTopic`, raw `Headers`. **Operational note (matches every surveyed library — MassTransit, Wolverine, NServiceBus, Brighter, Rebus — none of which scrub DLQ headers):** `Headers` carries whatever the publisher wrote. Consumer apps must not put secrets (API keys, bearer tokens, PII) into `PublishOptions.Headers` because those headers round-trip through the DLQ observer and any downstream sink (log aggregator, alerting, replay tool) that consumes `DeadLetterEvent`. This constraint belongs in the Unit 6 README section on header conventions, not in a runtime scrubber — scrubbing is a consumer-app responsibility when its threat model requires it, wired as an `IDeadLetterObserver` decorator.
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

- [ ] **Unit 9: NATS — declarative stream router**

**Goal:** Let a consumer declare which message types map to which streams/subjects via attributes or a fluent builder, rather than configuring each subscription imperatively.

**Requirements:** R8.

**Dependencies:** Unit 7.

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

### System-Wide Impact (Phase 1)

- **Interaction graph:** The new publisher interfaces are injected wherever today's `IDirectPublisher`/`IOutboxPublisher` are. Internal middleware (OpenTelemetry span wrappers, outbox dispatcher, retry loop) touches the shared `IMessagePublisher` base and must keep working for both Send and Broadcast paths.
- **In-repo migration call-sites:** The rename is not theoretical — the following packages inside this repo inject the old publisher interfaces and must be migrated in the same PR series as Phase 1:
  - `src/Headless.Caching.Hybrid/HybridCache.cs` — publishes `CacheInvalidationMessage` via `IDirectPublisher`; migrate to `IDirectSendPublisher` (cache invalidation is a point-to-point Send, not a Broadcast).
  - `src/Headless.Permissions.Core/Definitions/DynamicPermissionDefinitionStore.cs` + `src/Headless.Permissions.Core/Setup.cs` — publishes permission-change invalidation; migrate to `IDirectSendPublisher` (or `IOutboxSendPublisher` when transactional — pick per call-site).
  - `src/Headless.DistributedLocks.Core/RegularLocks/DistributedLockProvider.cs` + `src/Headless.DistributedLocks.Core/Setup.cs` — publishes lock-release notifications; migrate to `IDirectSendPublisher`.
  - Each of the three owns its DI wiring; migration is a per-file rename plus a constructor-parameter type change. No behavior change.
- **Error propagation:** `IRetryBackoffStrategy.ShouldRetry` becomes the single decision point for "retry vs DLQ" across providers. Exceptions thrown by the strategy itself are caught and logged; the consumer defaults to a safe retry policy rather than crashing the dispatcher.
- **State lifecycle risks:** TenantId header↔property mapping happens in one place (shared pipeline) to prevent tenant-header drift. The outbox tables are unaffected by the interface split since they store the envelope as-is.
- **API surface parity:** The split affects abstractions, OpenTelemetry, and all 11 transports simultaneously. Testing package (`Headless.Messaging.Testing`) and dashboard packages (`Headless.Messaging.Dashboard`, `Headless.Messaging.Dashboard.K8s`) must be audited in Unit 3 and updated in-place; they depend on the existing publisher marker interfaces.
- **Integration coverage:** Every broadcast-capable provider needs at least one integration test that confirms fan-out delivery to N subscribers (not just one). Every queue-only provider needs a test that confirms `IBroadcastPublisher` is **not** resolvable, with a readable error.
- **Unchanged invariants:** `IConsume<T>` stays exactly as it is. `IScheduledPublisher` stays on the Send side only in Phase 1 (scheduled broadcast arrives in Phase 7). `MessagingConventions` topic naming stays backward compatible — the broadcast helper is additive. `Headers.*` constants for `MessageId`, `CorrelationId`, etc. are untouched.

### Risks & Dependencies (Phase 1)

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Broadcast integration tests are flaky because fan-out timing is non-deterministic | Med | Med | Use `ITestOutputHelper` + explicit synchronization primitives; avoid `Task.Delay` in test assertions |
| Provider authors miss the capability-registration contract and silently register `IBroadcastPublisher` for a queue-only transport | Med | High | Add an abstractions-level analyzer or at minimum a per-provider unit test that asserts the exact set of registered publisher interfaces |
| Downstream consumer apps (Atilia SaaS and others) break on the interface rename | High | Low (greenfield posture) | Publish a migration section in Unit 6 doc; provide a single grep/sed recipe per old → new name |
| `IRetryBackoffStrategy` wiring changes retry semantics on providers that previously had ad-hoc behavior | Med | Med | Document per-provider before/after in the Unit 4 audit; keep default strategy shape identical to today's behavior where possible |
| Dashboard packages depend on old publisher marker interfaces and break silently | Med | Med | Unit 3c explicitly migrates dashboard + testing packages; integration smoke test for dashboard after the split |
| Cross-tenant `MessageId` collisions in shared storage (Redis, SQL outbox) silently dedupe across tenants | Med | High | Composite `(TenantId, MessageId)` dedup key enforced in Unit 3a/3b for every provider with dedup storage; integration test asserts same `MessageId` across two tenants is treated as two distinct messages |
| `AlwaysReconcile` NATS mode attempts a server-rejected change and the app crash-loops at startup | Med | Med | Safe-reconcile boundary in Unit 7 classifies changes as safe vs rejected at config-parse time; reject surfaces a clear error before the reconcile call is made |

### Documentation / Operational Notes (Phase 1)

- Unit 6 is the authoritative doc and must land in the same PR (or PR series) as Units 1–3c.
- Every provider README is updated in Unit 6; XML docs on public types are updated inline in Units 1–5, not deferred.
- No runtime rollout plan: this is a framework release, not a service deploy. A release note section for the next minor version calls out the breaking interface rename and points to the migration section.

---

## Phase 2 — Publish/Consume Behavior Pipeline

**Goal:** Introduce `IPublishBehavior<T>` and `IConsumeBehavior<T>` as the single pipeline-extension seam for **message-axis cross-cutting concerns** (`<T>` is the message type). Re-express the OpenTelemetry span wrappers internally as behaviors so the pipeline eats its own dog food on the concerns that genuinely belong on the message axis. **Outbox does not port to a behavior** — it remains the `OutboxPublisherDecorator<TTransport>` shipped in Phase 1 / Unit 1b. Rationale: outbox is on the **transport axis** (`<TTransport>`), is stateful (owns `IOutboxStore` lifecycle and the drainer), and owns publisher resolution per transport — none of which fits the message-typed, stateless, single-call wrapper shape that `IPublishBehavior<T>` defines. Forcing outbox into a behavior would re-introduce keyed-DI resolution inside a per-message generic and split its identity across multiple registrations. The behavior pipeline composes **around** the outbox decorator at runtime: `BehaviorChain → OutboxPublisherDecorator<T> → IDirectSendPublisher<T>` on persist; the drainer's redispatch path runs the behavior chain again around `IDirectSendPublisher<T>` (so the redispatched envelope still gets OTel spans, tenant-propagation defaults, etc.).

**Depends on:** Phase 1.

**Units outline:**
- Define `IPublishBehavior<T>` and `IConsumeBehavior<T>` in `Headless.Messaging.Abstractions` with ordered DI registration semantics (`services.AddPublishBehavior<T>()`, explicit order parameter).
- Refactor the shared publisher/consumer base in `Headless.Messaging.Core` to execute behaviors as an onion around the transport call.
- Port the existing OpenTelemetry span wrapper to the new seam; verify no parity regression via existing tests.
- Verify (with an integration test) that publishes through `IOutboxSendPublisher` flow as `BehaviorChain → OutboxPublisherDecorator<T> → IDirectSendPublisher<T>` on the persist hop, and that the drainer's redispatch flows as `BehaviorChain → IDirectSendPublisher<T>` (so OTel/tenant-propagation behaviors run on both hops, the persist span and the redispatch span are linked via the persisted W3C trace-context headers, and the outbox decorator is **not** registered as a behavior).

**Key decisions (carried forward):**
- Ordering is explicit and deterministic — not reflection-based. Behaviors are per-type (`<T>`) with an `object` fallback; closed-generic resolution precedence mirrors `IConsume<T>`.
- **P1⇄P2 transition contract for the outbox decorator (binding):** `OutboxPublisherDecorator<TTransport>`, `OutboxDrainer<TTransport>`, `IOutboxStore`, and `services.AddOutbox<TTransport>()` ship in Phase 1 / Unit 1b and survive into Phase 2 unchanged in class signature, DI registration, keying, and runtime semantics. Phase 2 does not rewrite outbox as a behavior, does not register the decorator as a behavior, and does not move tenancy stamping or composite-`(TenantId, MessageId)` dedup out of the decorator. Provider packages that landed in Units 3a/3b continue to register only the Direct interfaces. Any future "behavior-shaped" outbox proposal is out of scope for Phase 2 and would require its own RFC.

---

## Phase 3 — Auto-`TenantId` Propagation Behavior

**Goal:** When the caller does not set `PublishOptions.TenantId`, a standard publish behavior reads `ICurrentTenant` (from `Headless.Abstractions`, wired via `Headless.Api.MultiTenancySetup.AddHeadlessMultiTenancy`) and populates it automatically. Explicit caller value always wins.

**Note:** Multi-tenancy lives inside the existing `Headless.Core` package (under the `Headless.Abstractions` namespace) — there is no separate `Headless.MultiTenancy` package. The tenant-propagation behavior references `ICurrentTenant` directly; consumer apps that never call `AddHeadlessMultiTenancy` see a null-returning accessor and the behavior leaves `TenantId` as `null`.

**Depends on:** Phase 1 (envelope field), Phase 2 (behavior pipeline).

**Units outline:**
- Add `TenantPropagationPublishBehavior` in `Headless.Messaging.Core`, referencing `Headless.Abstractions.ICurrentTenant` directly. `Headless.Messaging.Abstractions` currently has zero `ProjectReference` entries, so adding the `Headless.Core` reference is a Phase 3 prerequisite (called out here because Phase 1 / Unit 2 deliberately keeps `Headless.Messaging.Abstractions` reference-free; the dependency is added in Phase 3 alongside the behavior, not retroactively in Phase 1).
- DI extension `services.AddHeadlessMessagingTenantPropagation()` registers the behavior at a well-defined order slot.
- Unit + integration tests: explicit `TenantId` wins; missing tenant context with no caller value → `TenantId` stays `null`; behavior survives scope-captured publishers (e.g., background jobs without an ambient tenant).

---

## Phase 4 — Polymorphic Publish

**Goal:** `BroadcastAsync<T>(msg)` dispatches to consumers registered for any base type or implemented interface of `T`. Startup validation surfaces ambiguous registrations (e.g., two consumers for disjoint base types that both match the runtime type) with a clear error.

**Depends on:** Phase 1.

**Units outline:**
- Extend consumer registry to index by full type hierarchy + implemented interfaces.
- Dispatch loop resolves the set of matching consumers once per message type (cached) and invokes each.
- Startup validator checks for non-deterministic matches and fails fast.
- Dashboard telemetry captures polymorphic dispatch cardinality (consumed in Phase 5).

**Key decisions:** Exact-type handlers always run; base-type handlers run additively. No priority/chain semantics — each matching consumer sees the message independently.

---

## Phase 5 — Dashboard Fan-Out Rendering

**Goal:** `Headless.Messaging.Dashboard` and `Headless.Messaging.Dashboard.K8s` surface broadcast fan-out cardinality, per-subscriber delivery status, polymorphic-dispatch visibility, and tenant filters in the UI.

**Scope clarification:** The dashboard is a **system-admin operator tool**, not a per-tenant end-user surface. It runs behind the existing `Headless.Messaging.Dashboard.Demo` auth layer (same auth story as the caching/locks dashboards) — no new authentication or authorization layer is introduced by this phase. The `TenantId` filter is an admin-side lens over cross-tenant traffic, not a tenant-isolation boundary.

**Depends on:** Phase 1 (DeliveryKind, TenantId on envelope), Phase 4 (polymorphic dispatch metadata).

**Units outline:**
- Backend: extend dashboard query/DTO layer with `DeliveryKind`, subscriber count, per-subscriber outcome, and matched-handler list for polymorphic dispatch.
- Frontend: add a "Fan-Out" column to the message list; add a detail panel showing per-subscriber delivery outcome; add a tenant filter bound to `TenantId`.
- Coordinate with the existing `2026-03-22-001-refactor-unified-dashboard-plan.md` — this phase extends, does not replace, that refactor.

**Design-detail deferral:** Information architecture (column layout, detail-panel fields, filter UX), interaction states (loading, empty, error, per-subscriber partial-failure rendering), and user flows (drill-down from message list → per-subscriber outcome) are deliberately out of scope for this epic. They are owned by the per-phase plan document produced when Phase 5 is scheduled for implementation (`docs/plans/<date>-messaging-dashboard-fanout-plan.md`), not this spec.

---

## Phase 6 — Request/Reply

**Goal:** `IRequestClient<TReq, TRes>` with correlation-id + reply-to headers, plus per-transport reply-queue lifecycle (auto-provisioned per client, torn down on dispose).

**Depends on:** Phase 1 (envelope + capability matrix), Phase 2 (behavior pipeline for correlation).

**Units outline:**
- Define `IRequestClient<TReq, TRes>` + `RequestOptions` (timeout, cancellation) in abstractions.
- Per-transport reply-queue implementation; capability-registered like publishers (not every transport supports ergonomic reply — in-memory + NATS + RabbitMQ + ASB ship first).
- Consumer side: `IConsume<TReq>` can return `TRes` via a context `RespondAsync` method.

---

## Phase 7 — Scheduled Broadcast

**Goal:** `IScheduledBroadcastPublisher` on transports with native scheduled fan-out (ASB, RabbitMQ delayed exchange). Other transports either do not register the interface or bridge via Phase 8 once a scheduled-send path exists.

**Depends on:** Phase 1.

**Units outline:**
- Add `IScheduledBroadcastPublisher : IBroadcastPublisher` marker with `ScheduleAsync<T>(T, DateTimeOffset, ...)`.
- Register on ASB and RabbitMQ only initially; extend the capability validator to cover scheduled variants.

---

## Phase 8 — Transactional Broadcast Bridge

**Goal:** `BroadcastViaSendBridge` dispatcher reads broadcast envelopes written to a SQL outbox (PostgreSql/SqlServer) and republishes via a broadcast-capable transport (NATS/Kafka/RabbitMQ). Enables "reliably broadcast inside a DB transaction" without coupling broadcast semantics to SQL transports.

**Depends on:** Phase 1.

**Units outline:**
- New `Headless.Messaging.OutboxBroadcastBridge` package (or integrate into existing Core) with a hosted service that polls outbox broadcast rows and dispatches via the registered `IDirectBroadcastPublisher`.
- Configuration: outbox source provider + target broadcast provider, per-row retry semantics.
- Integration test: write a broadcast envelope inside a DB transaction, commit, confirm fan-out on the target transport.

---

## Phase 9 — Inbox Pattern

**Goal:** Idempotent consume via a transactional inbox. Composite `(TenantId, MessageId)` dedup key persisted per consumer per message.

**Depends on:** Phase 1 (envelope), Phase 2 (behavior pipeline).

**Units outline:**
- `IInboxStore` abstraction + EF Core implementation in `Headless.Messaging.Core.Inbox` (or similar).
- `InboxConsumeBehavior` runs before the consumer, short-circuits on duplicate `(TenantId, MessageId)`.
- Migration scaffolding for the inbox table; README migration recipe.

---

## Phase 10 — SNS-Fronted SQS Broadcast

**Goal:** `Headless.Messaging.AwsSqs` gains `IBroadcastPublisher` implementations backed by auto-provisioned SNS topics fronting SQS queues. Matches MassTransit's AWS broker topology.

**Depends on:** Phase 1.

**Units outline:**
- Provisioning layer: ensure-topic + subscribe-queue with IAM-aware idempotency.
- `AddHeadlessMessagingSqs` registers the broadcast publisher when SNS is enabled in options.
- Capability matrix row updates from "❌ (Phase 1; arrives in Phase 10)" to "✅ (SNS)".

---

## Phase 11 — Saga / Routing-Slip Integration

**Goal:** Align existing saga and routing-slip plans with the Send/Broadcast split and the Phase 2 behavior pipeline.

**Depends on:** Phase 1, Phase 2.

**Units outline:**
- Review `docs/plans/2026-03-18-001-feat-saga-pattern-support-plan.md` and `docs/plans/2026-03-18-routing-slip-brainstorm.md` against the new surface; amend those plans rather than re-planning here.
- Re-classify any routing-slip step that was previously `PublishAsync` as either `SendAsync` or `BroadcastAsync` based on intent.

---

## Sources & References

- Session discussion (2026-04-19) on MassTransit capability model and transport-native topology provisioning.
- MassTransit docs via Context7: `/websites/masstransit_io` — producers, SQS/SNS, ASB broker topology, Event Hub rider model.
- Codebase: `src/Headless.Messaging.Abstractions/*.cs`, all `src/Headless.Messaging.*` providers, `src/Headless.Messaging.OpenTelemetry`.
- Related in-flight plans: `docs/plans/2026-03-18-001-feat-saga-pattern-support-plan.md`, `docs/plans/2026-03-22-001-refactor-unified-dashboard-plan.md`.
