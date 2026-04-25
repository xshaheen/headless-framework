---
title: "epic: Headless.Messaging Feature Shape"
type: epic
status: active
date: 2026-04-19
---

# epic: Headless.Messaging Feature Shape

## Overview

Define the target feature shape of `Headless.Messaging` across `Headless.Messaging.Abstractions`, all 11 transport providers, and the satellite packages (`Headless.Messaging.Core`, `Headless.Messaging.OpenTelemetry`, `Headless.Messaging.Testing`, `Headless.Messaging.Dashboard`). The epic is structured as a sequence of **small, independently shippable phases**. Phase 1 lands the load-bearing changes (Send/Broadcast split, first-class tenancy, uniform retry, OpenTelemetry enricher, NATS ergonomics, abstracted DLQ observer) and anchors every later phase. Subsequent phases each add one focused capability on top of the Phase 1 surface. Greenfield posture: breaking changes are acceptable.

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
| 1 | Send/Broadcast split + tenancy + observability foundations | Intent-explicit publishers, first-class TenantId, uniform retry, OTel enricher, NATS ergonomics, abstracted DLQ observer | — | 9 |
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

| Transport | `ISendPublisher` | `IBroadcastPublisher` | Fan-out mechanism | Notes |
|---|---|---|---|---|
| NATS (core + JetStream) | ✅ | ✅ | Native (subject fan-out) | Queue group vs subject fan-out |
| Kafka | ✅ | ✅ | Emulated (distinct `group.id` per subscriber) | Broadcast requires one consumer group per subscriber; operational cost is N groups on shared topic |
| RabbitMQ | ✅ | ✅ | Native (fanout/topic exchange) | Direct exchange vs fanout/topic exchange |
| AzureServiceBus | ✅ | ✅ | Native (topic + subscription) | Queue vs Topic+Subscription resources |
| Pulsar | ✅ | ✅ | Native (Shared/Failover subscription) | Shared vs Exclusive/Failover subscription |
| RedisStreams | ✅ | ✅ | Emulated (per-subscriber consumer groups) | Operational cost is N consumer groups per stream |
| AwsSqs | ✅ | ❌ (Phase 1; arrives in Phase 10) | — (Phase 10: SNS-fronted SQS, native) | SNS fan-out scheduled for Phase 10; throws at registration until then |
| PostgreSql (outbox) | ✅ | ❌ | — | Competing queue only |
| SqlServer (outbox) | ✅ | ❌ | — | Competing queue only |
| InMemoryQueue | ✅ | ❌ | — | Queue-only by design |
| InMemoryStorage | ✅ | ✅ | In-process | In-process list; fan-out trivially supported |

**Why fan-out mechanism matters:** "✅ Broadcast" describes semantic capability, not operational cost. Native fan-out (RabbitMQ fanout, ASB topics, NATS subjects) scales horizontally with the broker. Emulated fan-out (Kafka distinct group.ids, Redis per-subscriber groups) multiplies consumer-side resource usage linearly with subscriber count. Operators sizing a cluster need the distinction even when both are checkmarked.

### Key Technical Decisions

- **Two publisher interfaces, not a DeliveryMode enum.** Intent is part of the method name, not a parameter. Rationale: matches MT/Wolverine, fail-fast at DI registration, natural C# tooling (you cannot accidentally pass `Broadcast` to a queue-only transport).
- **Durability axis preserved via inheritance.** `IDirectSendPublisher : ISendPublisher`, `IOutboxSendPublisher : ISendPublisher`, same for broadcast. Durability markers stay exactly as they are conceptually; only the semantic axis is added.
- **TenantId as typed property, not a magic header.** `TenantId? : string` on `PublishOptions` and `ConsumeContext`; provider maps it to `Headers.TenantId` on the wire. Consumers read the property, not the header dictionary.
- **Capability declared at registration.** Provider `AddXxxMessaging(...)` extensions register only the interfaces the transport actually implements. No runtime `NotSupportedException` from a method that exists but refuses; instead DI resolution fails fast with a readable message.
- **Consumer surface unchanged.** `IConsume<T>` stays the single entry point. Whether `T` arrived via Send or Broadcast is observable via `ConsumeContext.DeliveryKind` (new enum) for diagnostics and tracing. **Business logic branching on `DeliveryKind` is discouraged**: if send vs broadcast means different behavior, model it as two distinct message types or two distinct consumers, not one consumer that forks. XML docs on `DeliveryKind` state this explicitly.
- **`IMessagePublisher` becomes `internal`.** Remains a shared base type for pipeline behavior (outbox, OpenTelemetry wrap) but is not a public resolution seam. Greenfield posture (per `CLAUDE.md`) makes re-exposing trivial if a concrete wrapper use case ever surfaces; YAGNI until then.
- **NATS stream config uses a callback-shaped options surface.** Matches the existing Headless FluentValidation + hosting options pattern; no direct `StreamConfig` exposure in abstractions.
- **Capability fail-fast via FluentValidation on a capability-descriptor options type.** Per `CLAUDE.md`, validation flows through `services.AddOptions<T, TValidator>().ValidateOnStart()` — not a bespoke `IHostedService`. Each provider's `AddXxxMessaging` extension registers a `MessagingCapabilityOptions` (declared publisher set, forbidden set, per-transport flags) bound via the standard options pipeline, with an internal `MessagingCapabilityOptionsValidator : AbstractValidator<MessagingCapabilityOptions>` that asserts the declared interfaces are actually registered in the container (via a post-build check wired into the validator). `ValidateOnStart()` surfaces any mismatch before the host reaches consumer startup. No `IHostedService`, no custom exception hierarchy — validation failures throw `OptionsValidationException` with the structured message list FluentValidation already produces.
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
- Test: `tests/Headless.Messaging.Tests.Unit/Abstractions/PublisherInterfaceShapeTests.cs`

**Approach:**
- `ISendPublisher.SendAsync<T>` and `IBroadcastPublisher.BroadcastAsync<T>` share a method signature shape with the existing `PublishAsync`; only the name and intent differ.
- Durability markers are empty interfaces — they exist for DI resolution and XML docs, just like today's `IDirectPublisher`.
- `DeliveryKind` is a plain enum `{ Send, Broadcast }` exposed on `ConsumeContext`.

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
- On consume: the pipeline reads `headers["headless-tenant-id"]` and populates `ConsumeContext.TenantId`. Missing header → property is `null`.
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

**Requirements:** R1, R2, R3.

**Dependencies:** Unit 1.

**Files:**
- Modify: `src/Headless.Messaging.Nats/` — register all four markers (Direct/Outbox × Send/Broadcast). Reference implementation.
- Modify: `src/Headless.Messaging.RabbitMq/` — register all four markers (fanout exchange for broadcast, direct exchange for send).
- Modify: `src/Headless.Messaging.AzureServiceBus/` — register all four markers (topic + subscription for broadcast, queue for send).
- Modify: `src/Headless.Messaging.AwsSqs/` — Send markers only; do **not** register broadcast (SNS-fronted topology lands in a follow-up plan).
- Add: per-provider `MessagingCapabilityOptions` + `MessagingCapabilityOptionsValidator : AbstractValidator<MessagingCapabilityOptions>` (same-file pattern, per `CLAUDE.md`), wired via `services.AddOptions<MessagingCapabilityOptions, MessagingCapabilityOptionsValidator>().ValidateOnStart()`. Shared validation helper in `Headless.Messaging.Core` performs the "declared interfaces are registered" cross-check. No `IHostedService`, no custom exception type — failures surface as `OptionsValidationException` at host startup.
- Test: `tests/Headless.Messaging.Nats.Tests.Integration/CapabilityRegistrationTests.cs`
- Test: `tests/Headless.Messaging.RabbitMq.Tests.Integration/CapabilityRegistrationTests.cs`
- Test: `tests/Headless.Messaging.AzureServiceBus.Tests.Integration/CapabilityRegistrationTests.cs`
- Test: `tests/Headless.Messaging.AwsSqs.Tests.Integration/CapabilityRegistrationTests.cs`

- [ ] **Unit 3b: Migrate Tier-2 providers (Kafka, Pulsar, RedisStreams, InMemoryStorage, InMemoryQueue, PostgreSql, SqlServer)**

**Goal:** Apply the Unit 3a reference shape to the remaining seven providers. Split from 3a to keep the blast radius of any single PR bounded.

**Requirements:** R1, R2, R3.

**Dependencies:** Unit 3a landed.

**Files:**
- Modify: `src/Headless.Messaging.Kafka/` — register all four markers (broadcast via distinct consumer groups on the same topic).
- Modify: `src/Headless.Messaging.Pulsar/` — register all four markers (Exclusive for send, Shared/Failover for broadcast).
- Modify: `src/Headless.Messaging.RedisStreams/` — register all four markers (distinct consumer groups for broadcast).
- Modify: `src/Headless.Messaging.InMemoryStorage/` — register all four markers; in-memory fan-out is trivially supported.
- Modify: `src/Headless.Messaging.InMemoryQueue/` — Send markers only; broadcast is out of scope for this provider by design.
- Modify: `src/Headless.Messaging.PostgreSql/` — Send markers only (see Deferred: transactional broadcast bridge).
- Modify: `src/Headless.Messaging.SqlServer/` — Send markers only.
- Test: `tests/Headless.Messaging.*.Tests.Integration/CapabilityRegistrationTests.cs` per provider.

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

**Goal:** Let consumer apps attach custom OpenTelemetry tags to publish/consume spans without forking the package. Default implementation adds `messaging.headless.tenant_id` and `messaging.headless.delivery_kind` automatically when the values are present.

**Requirements:** R6, R4 (tenancy visibility in traces).

**Dependencies:** Unit 2 (TenantId must exist).

**Files:**
- Create: `src/Headless.Messaging.OpenTelemetry/IActivityTagEnricher.cs`
- Create: `src/Headless.Messaging.OpenTelemetry/DefaultActivityTagEnricher.cs`
- Modify: `src/Headless.Messaging.OpenTelemetry/` existing publish/consume span wrappers to invoke all registered enrichers.
- Modify: `src/Headless.Messaging.OpenTelemetry/` DI extension to register `DefaultActivityTagEnricher` and allow user-supplied enrichers via `Add<T>()`.
- Test: `tests/Headless.Messaging.Tests.Unit/OpenTelemetry/ActivityTagEnricherTests.cs`

**Approach:**
- `IActivityTagEnricher.Enrich(Activity, IMessageTagContext)` — context exposes typed `TenantId`, `DeliveryKind`, `MessageType`, `Topic`, and the raw `Headers`.
- Multiple enrichers compose; resolved `IEnumerable<IActivityTagEnricher>` preserves registration order.
- Default enricher always runs first via **explicit options-driven ordering**, not `services.Insert(0, ...)`. `OpenTelemetryMessagingOptions.EnricherOrder` (a `List<Type>`) defaults to `[typeof(DefaultActivityTagEnricher)]`. The publish/consume wrappers resolve `IEnumerable<IActivityTagEnricher>` from DI and sort by `EnricherOrder` index (unordered types run after ordered ones, in registration order). This matches OTel SDK conventions (ordered processors via options), survives arbitrary DI registration order, and avoids the fragility of descriptor-list position, which breaks when `TryAddEnumerable` or later `services.Insert` calls re-shuffle the list.

**Patterns to follow:**
- `IConsumerLifecycle` multi-registration pattern already present in abstractions.

**Test scenarios:**
- Happy path: Default enricher adds `messaging.headless.tenant_id` when `TenantId` is set.
- Happy path: Default enricher skips the tag when `TenantId` is null (no empty-string tag).
- Happy path: User-supplied enricher runs after default and can add additional tags.
- Edge case: Enricher throws → span is still emitted, enricher exception is logged but not propagated.
- Integration: A publish→consume round-trip produces two connected spans both carrying `messaging.headless.tenant_id`.

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

**Goal:** Introduce `IPublishBehavior<T>` and `IConsumeBehavior<T>` as the single pipeline-extension seam across Headless.Messaging. Re-express the OpenTelemetry span wrappers and the outbox dispatcher internally as behaviors so the pipeline eats its own dog food.

**Depends on:** Phase 1.

**Units outline:**
- Define `IPublishBehavior<T>` and `IConsumeBehavior<T>` in `Headless.Messaging.Abstractions` with ordered DI registration semantics (`services.AddPublishBehavior<T>()`, explicit order parameter).
- Refactor the shared publisher/consumer base in `Headless.Messaging.Core` to execute behaviors as an onion around the transport call.
- Port existing OpenTelemetry span + outbox wrappers to the new seam; verify no parity regression via existing tests.

**Key decisions (carried forward):** Ordering is explicit and deterministic — not reflection-based. Behaviors are per-type (`<T>`) with an `object` fallback; closed-generic resolution precedence mirrors `IConsume<T>`.

---

## Phase 3 — Auto-`TenantId` Propagation Behavior

**Goal:** When the caller does not set `PublishOptions.TenantId`, a standard publish behavior reads `ICurrentTenant` (from `Headless.Abstractions`, wired via `Headless.Api.MultiTenancySetup.AddHeadlessMultiTenancy`) and populates it automatically. Explicit caller value always wins.

**Note:** Multi-tenancy lives in the existing `Headless.Abstractions` + `Headless.Api` packages — there is no separate `Headless.MultiTenancy` package. The tenant-propagation behavior references `ICurrentTenant` directly; consumer apps that never call `AddHeadlessMultiTenancy` see a null-returning accessor and the behavior leaves `TenantId` as `null`.

**Depends on:** Phase 1 (envelope field), Phase 2 (behavior pipeline).

**Units outline:**
- Add `TenantPropagationPublishBehavior` in `Headless.Messaging.Core`, referencing `Headless.Abstractions.ICurrentTenant` directly (no separate multi-tenancy package — `Headless.Abstractions` is already a transitive dependency of `Headless.Messaging.Abstractions`).
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
