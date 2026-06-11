---
title: "feat(messaging): Cluster 0.5 — dual-lane physical topology + Kafka guard"
status: active
date: 2026-06-10
type: feat
issue: 359
depends_on: [358]
closes: [344]
origin: "GitHub issue #359 (the originally-referenced spec/plan docs were superseded and deleted; issue body is the authoritative spec)"
---

# feat(messaging): Cluster 0.5 — Dual-Lane Physical Topology per Provider + Kafka Guard

## Summary

Complete the physical separation between the **Bus** (broadcast / publish-subscribe) and **Queue** (point-to-point / work-queue) intent lanes so that a message published on one lane structurally cannot reach a consumer registered on the other lane for the same `MessageName`. This closes the cross-intent leakage design ticket (#344).

The interface-level split (`IBusTransport` / `IQueueTransport`), sender-side routing by `IntentType` (`MessageSender._ResolveTransportAsync`), and intent-aware consumer clients **already exist** and ship today. AWS (SNS/SQS), Azure Service Bus, Redis (Pub/Sub + Streams), and InMemory already have fully-split transport classes with isolated physical topology. This plan finishes the **three providers that still share one physical topology across both lanes** — RabbitMQ, NATS, Pulsar — adds the **Kafka startup guard** for the bus-incapable case, surfaces the **Redis Pub/Sub durability trade-off** in docs, and builds the **cross-provider lane-isolation conformance test** that proves the invariant.

---

## Problem Frame

**The leak is concrete on RabbitMQ today.** `RabbitMqTransport` is a single class registered as *both* `IBusTransport` and `IQueueTransport` (`src/Headless.Messaging.RabbitMq/Setup.cs:41-43`), publishing to **one topic exchange** (`IConnectionChannelPool.Exchange`, type `"topic"`) with the message name as the routing key. On the consume side, `RabbitMqConsumerClient` binds both a Bus group-queue and a Queue per-message-queue to **that same exchange with the same routing key** (`RabbitMqConsumerClient.cs:78`). Result: a Bus-lane publish (routing key = `messageName`) is delivered to **both** the Bus group queue **and** the Queue-lane queue, and vice versa. The lanes are logically distinguished (queue naming differs by intent) but **physically fused**.

NATS has the same fusion: `NatsTransport` publishes every message — Bus and Queue — to the same JetStream subject (`message.GetName()`), so a Bus durable consumer and a Queue durable consumer on that subject both observe both lanes' traffic.

Pulsar distinguishes lanes by subscription **name** but uses `SubscriptionType.Shared` for the Bus lane (`PulsarConsumerClient.cs:51`), which is competing-consumer semantics — wrong for broadcast where every instance in a Bus group should receive its own copy.

Kafka ships no `IBusTransport` at all. An `OnBus<T>()` registration on a Kafka-only host fails **per-message at send time** (`MessageSender._MissingTransportAsync`) — a late, repeated, confusing runtime failure instead of a single clear startup error.

**Scope boundary:** physical separation is delivered for **framework-managed topology paths**. Consumers who hand-roll their own broker bindings outside the framework are out of scope.

---

## Requirements Traceability

From issue #359 acceptance criteria:

- **R1.** A message published via the Bus lane does NOT reach a Queue-lane consumer registered for the same `MessageName`, and vice versa — verified by conformance test on every provider with integration infrastructure. (U1, U2, U3, U4, U7)
- **R2.** Kafka + `OnBus` registration throws at **startup** with the §8 gap explanation and 3 concrete workarounds. (U5)
- **R3.** `ConsumeContext.IntentType` correctly reflects the inbound lane on every provider. (U1 asserts; U2/U3/U4 preserve)
- **R4.** Pulsar Bus lane fans out — each subscriber group receives its own copy via a distinct per-group subscription name on a lane-isolated topic (see KTD-6 for the subscription-type decision). (U4)
- **R5.** Redis Bus durability trade-off surfaces in package README + `ConsumeContext` XML doc; **no** startup-fail. (U6)
- **R6.** Per-provider Bus/Queue physical mapping holds for RabbitMQ (topic exchange vs direct exchange), NATS (interest-retention vs work-queue JetStream stream — see KTD-2), Pulsar (per-lane topic suffix; Shared subscription on both — see KTD-6). (U2, U3, U4)

---

## Key Technical Decisions

### KTD-1 — The generic guard already exists; only the Kafka hint is new

*(Owner decision, 2026-06-10; refined after plan review verified the existing guard.)* The provider-agnostic startup guard is **already implemented**: `_CheckIntentTransportSupport` → `_RequireTransportFor<IBusTransport>` in `IBootstrapper.Default.cs:385-441` already throws at bootstrap when any Bus consumer or Bus publisher is registered but no `IBusTransport` is available. R2 therefore does **not** add new scanning code (the originally-planned `Core/Setup.cs` scan would have duplicated this). The only gap is the **Kafka-specific §8 hint** — when the registered queue provider is Kafka, append the fan-out gap explanation and its 3 workarounds to the existing exception message. The hint is gated on the `MessageQueueMarkerService("Kafka")` marker so core never references the Kafka package. The guard stays generic — the same misconfiguration is caught for any future bus-incapable provider; only the *hint* is Kafka-shaped.

### KTD-2 — NATS keeps JetStream for **both** lanes; lanes separate by **retention policy**, not Core-vs-JetStream

*(Owner decision via "check what MassTransit does", 2026-06-10.)* Issue §7 specified NATS Bus = Core subject pub/sub (non-durable). Research into MassTransit's design (it has no official NATS transport, but its maintainers and design analysis are explicit) shows a serious .NET messaging transport would build on **JetStream, not Core NATS**, because Core NATS is at-most-once and silently drops messages for offline subscribers — below the durability bar. The principled lane split is **retention-policy-based**: a fan-out stream (interest-retention, per-group durable consumers — every group gets a copy) for Bus, and a work-queue stream (`WorkQueuePolicy`, competing durable consumers) for Queue. Both durable. This **deviates from issue §7** to avoid a Bus durability regression; the deviation is documented in the NATS README and `docs/llms/messaging.md`. The sibling "Redis durable bus lane" follow-up (issue §13) is unaffected — Redis Pub/Sub stays intentionally non-durable (R5).

### KTD-3 — Redis stays a single package; transports are already split internally

*(Owner decision, 2026-06-10.)* Issue "Files touched" referenced separate `Headless.Messaging.RedisPubSub` and `Headless.Messaging.RedisStreams` packages. These do not exist; the code lives in one `Headless.Messaging.Redis` with `RedisPubSubBusTransport` (`IBusTransport`) and `RedisTransport` (Streams, `IQueueTransport`) already separated. Keep the single package — the issue's separate-package paths are stale. No new `.csproj`, no consumer-visible package rename. (The orphan `tests/Headless.Messaging.RedisStreams.Tests.Unit` dir is noted as a naming artifact, not evidence of a real split.)

### KTD-4 — AWS and Azure Service Bus are verify-only; no rename

*(Owner decision, 2026-06-10.)* Both already ship split transport classes with isolated physical topology (SNS vs SQS; ASB topic vs queue). The issue's expected names (`AzureServiceBusBusTransport`) are cosmetic deviations on `internal sealed` types. Wire them into the conformance suite to *prove* isolation; do not rename or refactor working code. Keeps the change focused on the genuinely-incomplete providers.

### KTD-6 — Pulsar isolation is per-lane **topic separation**; subscription type stays Shared

*(Refined after plan review, 2026-06-10.)* The real cross-intent leak on Pulsar is the same shape as RabbitMQ/NATS: today both lanes subscribe to the **same topic** (the message name), and in Pulsar every subscription on a topic receives every message — so a Bus publish reaches the Queue subscription and vice versa. The fix is **per-lane topic separation** (e.g. `OrderPlaced-bus` vs `OrderPlaced-queue`), mirroring the RabbitMQ exchange split. The subscription **type** is a separate concern: the issue's R4 says "Exclusive/Failover," but switching the Bus lane to Exclusive/Failover would make only **one** replica per consumer group ever active, breaking horizontal scaling *within* a group. Correct broadcast semantics are: **fan-out across groups** via distinct subscription *names* (already done), **competing consumption within a group** via `SubscriptionType.Shared` (already done). So the Bus lane keeps `Shared` — this is a **reasoned deviation from R4's literal wording**, documented in the Pulsar README. R4's intent ("each subscriber group receives its own copy") is satisfied by the distinct per-group subscription name on a lane-isolated topic, not by the subscription type.

### KTD-5 — Conformance lives in the existing harness; integration coverage is bounded by available infrastructure

The lane-isolation invariant is an **end-to-end publish→consume** assertion, not a `transport.SendAsync` unit check — it belongs in `Headless.Messaging.Core.Tests.Harness/MessagingIntegrationTestsBase` (full pub-sub cycle base). Integration projects exist **only** for RabbitMq, NATS, AWS, AwsSqs. Pulsar, ASB, Kafka, Redis, InMemory have unit-only suites. Per the Cluster 0.4 precedent ("do not scaffold new Kafka/ASB integration projects"), the plan does **not** create new Testcontainers projects. End-to-end lane isolation is proven on RabbitMq + NATS + AWS; the remaining providers' topology decisions are asserted at unit level (subscription type, exchange type, stream retention). This bound is stated explicitly rather than claiming "all 8 providers" coverage the infrastructure cannot deliver.

---

## High-Level Technical Design

### Current vs target physical topology (the providers this plan changes)

```
                 BUS lane (broadcast)          QUEUE lane (competing)
                 ─────────────────────         ──────────────────────
RabbitMQ  NOW    ┐                             ┌
                 ├─► one "topic" exchange ◄─────┤   ← LEAK: both bound,
                 ┘    (routing key = name)      └      same routing key

RabbitMQ  TARGET   topic exchange (bus)         direct exchange + queue
                   (group queues bound)         (per-message queue bound)
                   ── physically distinct exchanges ──

NATS      NOW    ┐                             ┌
                 ├─► same JetStream subject ◄───┤   ← LEAK
                 ┘                              └

NATS      TARGET   interest-retention stream    work-queue stream
                   per-group durable (fan-out)  competing durable
                   ── distinct subjects/streams per lane ──

Pulsar    NOW    ┐                             ┌
                 ├─► same topic (message name) ─┤   ← LEAK
                 ┘                              └
Pulsar    TARGET   {name}-bus topic              {name}-queue topic
                   per-group Shared sub          shared Shared sub
                   (fan-out across groups)       (competing)
                   ── distinct topics per lane ──
```

### Kafka guard placement (decision flow at host startup)

```
host startup ─► drain ForMessage<T> registrations ─► any OnBus<T> present?
                                                          │
                                            no ──► continue
                                            yes ─► IBusTransport registered?
                                                          │
                                          yes ──► continue
                                          no ──► THROW:
                                                  generic "OnBus needs a bus
                                                  transport; none registered"
                                                  + if queue transport == Kafka:
                                                    append §8 fan-out gap + 3 workarounds
```

---

## Implementation Units

### U1. Cross-provider lane-isolation conformance scenario

- **Goal:** A reusable end-to-end test scenario — publish a message of name `N` on the Bus lane; assert a Queue-lane consumer registered for `N` never receives it, and the symmetric case; assert `ConsumeContext.IntentType` matches the inbound lane. This is the executable form of R1/R3 and the RED baseline that U2–U4 turn green.
- **Requirements:** R1, R3.
- **Dependencies:** none (write first).
- **Files:**
  - `tests/Headless.Messaging.Core.Tests.Harness/LaneIsolationTestsBase.cs` (new — abstract scenario built on the `MessagingIntegrationTestsBase` pub-sub-cycle pattern).
  - Extend `tests/Headless.Messaging.Core.Tests.Harness/Capabilities/TransportCapabilities.cs` only if a provider must opt out (e.g. a provider that legitimately cannot run end-to-end without infra) — prefer per-provider integration wiring (U7) over capability flags.
- **Approach:** Two registrations for the same `MessageName` — one `OnBus`, one `OnQueue`, each with a distinct recording consumer. Publish once per lane. Assert exactly the matching-lane consumer fires within a bounded wait, and the cross-lane consumer's count stays zero after a settle window. Reuse the harness's existing recording-consumer and wait helpers (`Tests.Helpers`).
- **Patterns to follow:** `MessagingIntegrationTestsBase` (full pub-sub cycle, DI setup, `ConfigureTransport`/`ConfigureStorage` abstract seams); existing recording consumers in `Tests.Helpers`.
- **Test suite design:** This unit *is* test infrastructure. It is exercised by U2/U3/U7 leaf integration projects; it has no standalone runner. The base class compiles and is covered transitively.
- **Test scenarios:**
  - Covers R1. Bus publish of `N` → Bus consumer receives exactly once; Queue consumer for `N` receives zero within the settle window.
  - Covers R1. Queue publish of `N` → Queue consumer receives; Bus consumer receives zero.
  - Covers R3. Bus-delivered message has `ConsumeContext.IntentType == Bus`; Queue-delivered has `== Queue`.
  - Edge: a provider with only one lane registered (Bus-only) — the absent lane assertion is skipped, not failed (the scenario must not require both lanes to exist to run the present one).
- **Verification:** Base class compiles; consumed by at least RabbitMq + NATS integration projects (U2, U3) and goes green there once those units land.

### U2. RabbitMQ physical exchange separation (closes #344)

- **Goal:** Split `RabbitMqTransport` into `RabbitMqBusTransport` (dedicated **topic** exchange) and `RabbitMqQueueTransport` (direct exchange + per-message queue). Consumers bind to the lane-correct exchange. A Bus publish can no longer reach a Queue consumer.
- **Requirements:** R1, R3, R6.
- **Dependencies:** U1.
- **Files:**
  - `src/Headless.Messaging.RabbitMq/RabbitMqBusTransport.cs` (new), `src/Headless.Messaging.RabbitMq/RabbitMqQueueTransport.cs` (new); retire `RabbitMqTransport.cs` (or keep a thin shared core).
  - `src/Headless.Messaging.RabbitMq/IConnectionChannelPool.cs` — expose **two** exchange names/types (bus vs queue) instead of one `Exchange`. Declare both on connect.
  - `src/Headless.Messaging.RabbitMq/RabbitMqConsumerClient.cs` — select the exchange to bind by `_intentType` (`ConnectAsync` exchange-declare at `:249`, bind at `:78`).
  - `src/Headless.Messaging.RabbitMq/RabbitMqOptions.cs` — bus/queue exchange-name + exchange-type config (default bus = `topic`, queue = `direct`; see resolved decision below).
  - `src/Headless.Messaging.RabbitMq/Setup.cs` — register `RabbitMqBusTransport` as `IBusTransport`, `RabbitMqQueueTransport` as `IQueueTransport` (replace the shared `:41-43` registration).
  - `tests/Headless.Messaging.RabbitMq.Tests.Integration/...` — derive a concrete `RabbitMqLaneIsolationTests : LaneIsolationTestsBase`.
  - `tests/Headless.Messaging.RabbitMq.Tests.Unit/...` — exchange-type/name selection per intent.
- **Approach:** Bus lane → dedicated topic exchange; group queues bound on the message-name routing key for fan-out (every group gets a copy, with routing-key filtering so a group only receives the names it subscribed to). Queue lane → direct exchange; per-message-name queue bound on the routing key for competing consumers. The two exchanges share a connection-channel pool but are distinct broker objects, so a publish on one is invisible to the other's bindings. Preserve existing message-name validation, persistence, and channel-pool return semantics from `RabbitMqTransport`.
- **Patterns to follow:** existing `RabbitMqTransport.SendAsync` (channel rent/return, `BasicProperties`, error wrapping); the already-split AWS pair (`AmazonSnsBusTransport` / `AmazonSqsQueueTransport`) as the two-class shape.
- **Test suite design:** Unit (exchange selection, options defaults) in `.Tests.Unit`; end-to-end lane isolation in `.Tests.Integration` via U1's base. RabbitMq has Testcontainers infra today.
- **Test scenarios:**
  - Covers R1. Integration: Bus publish does not reach the Queue consumer and vice versa (via `LaneIsolationTestsBase`).
  - Covers R6. Unit: Bus transport targets the dedicated topic exchange; Queue transport targets the direct exchange.
  - Unit: consumer binds the Queue lane to the direct exchange and the Bus lane to the topic exchange.
  - Edge: two Bus groups both receive a Bus publish (fan-out preserved across groups).
  - Error: publish failure on a closed channel still returns `OperateResult.Failed` and returns/aborts the channel (preserved behavior).
- **Verification:** `RabbitMqLaneIsolationTests` green; existing RabbitMq integration + unit suites still pass; #344 acceptance (no cross-intent delivery) demonstrated.

### U3. NATS lane separation via JetStream retention policy

- **Goal:** Split `NatsTransport` into `NatsBusTransport` (interest-retention stream, per-group durable consumers → fan-out) and `NatsQueueTransport` (work-queue stream, competing durable consumers). Distinct subjects/streams per lane. Both durable (KTD-2).
- **Requirements:** R1, R3, R6.
- **Dependencies:** U1.
- **Files:**
  - `src/Headless.Messaging.Nats/NatsBusTransport.cs` (new), `src/Headless.Messaging.Nats/NatsQueueTransport.cs` (new); retire/slim `NatsTransport.cs`.
  - `src/Headless.Messaging.Nats/NatsConsumerClient.cs` — already `_intentType`-aware (`BuildDurableName` at `:123`); route Bus vs Queue to the lane-correct subject/stream and retention.
  - NATS stream/subject setup (stream-creation path referenced from `NatsConsumerClient` `EnableSubscriberClientStreamAndSubjectCreation`; the `NormalizeStreamName` helper) — derive the stream name from the raw message name, then suffix the lane; distinct stream per lane with the right `RetentionPolicy`.
  - `src/Headless.Messaging.Nats/Setup.cs` — register the two transports against `IBusTransport`/`IQueueTransport`.
  - `src/Headless.Messaging.Nats/README.md` — document the JetStream-for-both / retention-split decision and the §7 deviation (KTD-2).
  - `tests/Headless.Messaging.Nats.Tests.Integration/...` — `NatsLaneIsolationTests : LaneIsolationTestsBase`.
  - `tests/Headless.Messaging.Nats.Tests.Unit/...` — subject/stream/retention selection per intent; `BuildDurableName` lane behavior.
- **Approach:** Lanes separate by **per-lane stream + subject**, preserving domain partitioning. **Do not** stamp a flat `bus.`/`queue.` subject prefix — that collapses every message into one giant `bus` or `queue` stream under the existing `NormalizeStreamName` logic, destroying per-message-domain partitioning. Instead, resolve the stream name from the **raw message name first**, then suffix the lane (`orders-bus` / `orders-queue`), and route the subject into the lane-correct stream. Bus stream uses interest/limits retention with per-group durable consumers (fan-out); Queue stream uses `WorkQueuePolicy` with a shared durable consumer (compete). The existing `ResolveSubject` shard logic is preserved within each lane's subject namespace.
- **Patterns to follow:** existing `NatsTransport` JetStream publish (`js.PublishAsync`, `NatsJSPubOpts`, header mapping); `NatsConsumerClient.BuildDurableName` lane discrimination.
- **Test suite design:** Unit for subject/retention selection; end-to-end isolation in `.Tests.Integration` (NATS has Testcontainers infra).
- **Test scenarios:**
  - Covers R1. Integration: cross-lane non-delivery both directions.
  - Covers R6. Unit: Bus → interest-retention stream + per-group durable; Queue → work-queue stream + competing durable.
  - Unit: Bus and Queue resolve to distinct **streams** (`{name}-bus` vs `{name}-queue`) for the same `MessageName` — two different message names do NOT collapse into one shared `bus` stream.
  - Edge: two Bus groups each receive a copy (fan-out via distinct durables on the interest stream).
  - Edge: subject-shard override stays within the lane's subject namespace.
- **Verification:** `NatsLaneIsolationTests` green; NATS README records the §7 deviation; existing NATS suites pass.

### U4. Pulsar per-lane topic separation

- **Goal:** Route Bus and Queue traffic for the same `MessageName` to **distinct topics** (e.g. `OrderPlaced-bus` vs `OrderPlaced-queue`) so a Bus publish structurally cannot reach the Queue subscription, and vice versa. Keep `SubscriptionType.Shared` on both lanes (KTD-6). Fan-out across consumer groups is preserved by the existing distinct per-group subscription names.
- **Requirements:** R1, R3, R4, R6.
- **Dependencies:** U1.
- **Files:**
  - `src/Headless.Messaging.Pulsar/PulsarTransport.cs` — the producer (`connectionFactory.CreateProducerAsync(message.GetName())`, `:23`) targets a lane-suffixed topic. Producers are subscription-agnostic, so **no transport class split is needed** (resolves prior Open Question) — the lane suffix on the topic name is sufficient; keep one `PulsarTransport` serving both `IBusTransport` and `IQueueTransport`, OR a thin pair if registration clarity warrants.
  - `src/Headless.Messaging.Pulsar/PulsarConsumerClient.cs` — subscribe to the lane-suffixed topic; keep `SubscriptionType.Shared` (`:51`) and the per-group subscription name (`GetSubscriptionName`, `:57`) unchanged.
  - `src/Headless.Messaging.Pulsar/README.md` — document the per-lane topic suffix and the deliberate Shared-type choice (KTD-6 / R4 deviation).
  - `tests/Headless.Messaging.Pulsar.Tests.Unit/...` — topic-suffix-per-intent and subscription-name/type assertions.
- **Approach:** Lane isolation = topic separation (mirrors RabbitMQ's exchange split). Within each lane, broadcast vs competing is already correct: distinct subscription names per group give fan-out *across* groups; `Shared` gives competing consumption *within* a group (correct horizontal scaling — Exclusive/Failover would idle all but one replica). No new integration project (KTD-5) — Pulsar is unit-only; isolation is asserted via topic-suffix unit tests.
- **Patterns to follow:** existing `PulsarConsumerClient` subscription builder (`.Topic(...).SubscriptionName(...).SubscriptionType(...)`); the RabbitMQ exchange-suffix shape from U2.
- **Test suite design:** Unit only (Pulsar has no integration infra; not scaffolding one per KTD-5). Topic-suffix and subscription selection per intent carry the correctness assertion.
- **Test scenarios:**
  - Covers R1. Unit: Bus lane produces/subscribes to the `-bus` topic; Queue lane to the `-queue` topic, for the same `MessageName`.
  - Covers R4. Unit: distinct Bus groups produce distinct subscription names on the `-bus` topic (fan-out across groups).
  - Covers R4/KTD-6. Unit: Bus lane subscription type is `Shared` (not Exclusive/Failover) — guards against the intra-group-scaling regression.
  - Unit: Queue lane uses the shared subscription name with `Shared` type on the `-queue` topic.
  - Edge: same group registered twice is idempotent (no duplicate subscription).
- **Verification:** topic-suffix unit tests green; Bus and Queue resolve to distinct topics; no Bus message reaches a Queue subscription by construction; Shared type retained on both lanes.

### U5. Kafka §8 hint on the existing missing-bus startup guard

- **Goal:** When the existing bootstrap guard fires for a missing `IBusTransport` **and** the registered queue provider is Kafka, append the §8 fan-out gap explanation and 3 workarounds to the exception message. The generic startup failure already exists (KTD-1); this unit only enriches the Kafka case. Replaces the per-message runtime failure with the existing single startup failure, now with actionable Kafka wording (R2).
- **Requirements:** R2.
- **Dependencies:** none (independent of U1–U4).
- **Files:**
  - `src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs` — extend `_RequireTransportFor<TTransport>` (`:415`) so the bus-intent failure path appends the Kafka hint when a Kafka queue provider is present. Detect Kafka via the `MessageQueueMarkerService("Kafka")` marker value resolved from the provider — **never** reference the Kafka package type (core stays provider-agnostic).
  - `tests/Headless.Messaging.Core.Tests.Unit/...` — hint present/absent across provider permutations (the generic-throw behavior is already covered by existing bootstrapper tests; add the Kafka-hint assertions).
- **Approach:** The contrapositive guard is already implemented in `_CheckIntentTransportSupport` → `_RequireTransportFor<IBusTransport>` (KTD-1). The only change is message enrichment: in the bus branch, look up whether any registered `MessageQueueMarkerService` carries the Kafka marker and, if so, append the §8 text (use a Bus-capable transport like RabbitMQ/ASB/SNS-SQS / route via the Queue intent if fan-out isn't needed / provide a custom Kafka fan-out emulation per the sibling spec). Keep the generic message unchanged for all other providers.
- **Patterns to follow:** existing `_RequireTransportFor` message construction (`:438`); `MessageQueueMarkerService("RabbitMQ"|"InMemory"|"Kafka"|...)` provider markers.
- **Test suite design:** Unit, in `Core.Tests.Unit` — pure DI/registration permutations, no broker. Extends existing bootstrapper guard tests.
- **Test scenarios:**
  - Covers R2. Kafka (queue marker) + a Bus consumer/publisher + no `IBusTransport` → startup throws; message contains the §8 gap explanation and 3 workarounds.
  - Generic: a non-Kafka queue-only setup + Bus registration + no bus transport → throws the generic message **without** the Kafka hint (existing behavior preserved).
  - Negative: Kafka (queue) + RabbitMQ (bus) both registered + Bus registration → does **not** throw (a bus transport exists).
  - Negative: Kafka-only with **no** Bus registration → does not throw.
- **Verification:** Kafka-hint unit tests green; existing generic-guard tests still pass; a Kafka-only + Bus host fails fast at startup with actionable §8 wording.

### U6. Redis Bus durability warning (docs only)

- **Goal:** Surface the Redis Pub/Sub fire-and-forget trade-off (messages published while a subscriber is offline are lost) in the package README and the `ConsumeContext` XML docs. No startup-fail (R5) — Redis Pub/Sub is intentionally chosen for transient broadcast.
- **Requirements:** R5.
- **Dependencies:** none.
- **Files:**
  - `src/Headless.Messaging.Redis/README.md` — durability-trade-off section for the Pub/Sub Bus lane vs the durable Streams Queue lane.
  - `src/Headless.Messaging.Abstractions/ConsumeContext.cs` — XML-doc note on `IntentType` (or the Bus-lane remarks) that Bus durability is provider-dependent and Redis Pub/Sub is non-durable; point at the sibling "Redis durable bus lane via Streams" follow-up (issue §13).
- **Approach:** Documentation only. Per the `transport-wrapper-drift-and-doc-sync` learning, update human README and the XML-doc surface in the same change.
- **Test suite design:** none.
- **Test scenarios:** `Test expectation: none -- documentation-only change, no behavioral surface.`
- **Verification:** README renders the trade-off; `ConsumeContext` XML doc mentions Redis Pub/Sub non-durability and the §13 follow-up; no code/startup change.

### U7. Verify-only conformance wiring for already-split providers

- **Goal:** Prove (not re-implement) lane isolation for AWS and the InMemory transport by deriving them onto `LaneIsolationTestsBase` where infrastructure allows; do not rename or refactor (KTD-4).
- **Requirements:** R1, R3.
- **Dependencies:** U1.
- **Files:**
  - `tests/Headless.Messaging.Aws.Tests.Integration/...` (or `AwsSqs.Tests.Integration`) — `AwsLaneIsolationTests : LaneIsolationTestsBase` (LocalStack infra exists).
  - `tests/Headless.Messaging.InMemory.Tests.Unit/...` — InMemory lane isolation runs in-process (no container); assert Bus/Queue per-lane channel separation (`MemoryQueue.SendBus`/`SendQueue`).
  - ASB / Redis: unit-level assertion that distinct transport classes back each lane (no integration infra; KTD-5). No new projects.
- **Approach:** AWS already separates SNS (Bus) and SQS (Queue); the test confirms no cross-lane delivery end-to-end on LocalStack. InMemory already routes to per-lane channels; the test confirms a Bus publish never lands on the Queue channel's consumers.
- **Patterns to follow:** existing AWS integration fixtures; `MemoryQueue` per-lane channel structure.
- **Test suite design:** AWS integration (LocalStack), InMemory unit, ASB/Redis unit (class-separation assertion).
- **Test scenarios:**
  - Covers R1. AWS integration: Bus (SNS) publish does not reach the SQS Queue consumer and vice versa.
  - Covers R1. InMemory unit: Bus publish reaches only Bus-channel consumers; Queue publish only Queue-channel consumers.
  - Covers R3. IntentType reflects the inbound lane on AWS and InMemory.
- **Verification:** AWS + InMemory lane-isolation tests green with zero production-code changes to those providers.

### U8. Documentation sync — per-provider topology table + Kafka guard

- **Goal:** Update agent-facing and human docs to describe the finalized per-provider Bus/Queue physical mapping, the Kafka startup guard, the NATS §7 deviation, and the Redis durability trade-off — keeping `docs/llms/messaging.md` and the provider READMEs in lockstep (CLAUDE.md doc-sync trigger: consumer-visible behavior + new startup-fail).
- **Requirements:** R2, R5, R6 (documentation reflection).
- **Dependencies:** U2, U3, U4, U5, U6.
- **Files:**
  - `docs/llms/messaging.md` — per-provider Bus/Queue topology table; Kafka-guard behavior; NATS retention-split + §7 deviation note.
  - `src/Headless.Messaging.RabbitMq/README.md`, `src/Headless.Messaging.Nats/README.md`, `src/Headless.Messaging.Pulsar/README.md`, `src/Headless.Messaging.Kafka/README.md` — per-provider lane topology + (Kafka) the no-bus guard and workarounds.
  - Follow `docs/authoring/AUTHORING.md` for the llms-doc + README lockstep rules.
- **Approach:** Documentation. Land the topology table and guard wording alongside the code units (per the doc-sync learning: rename/reshape docs in the same change as the API behavior).
- **Test suite design:** none (docs).
- **Test scenarios:** `Test expectation: none -- documentation sync.`
- **Verification:** `docs/llms/messaging.md` and the four provider READMEs describe the final topology + guard; no drift between the llms doc and READMEs per AUTHORING.md.

---

## Scope Boundaries

**In scope:** RabbitMQ exchange split; NATS retention-policy lane split; Pulsar Bus subscription-type fix; generic missing-bus startup guard with Kafka hint; Redis durability docs; cross-provider lane-isolation conformance base + RabbitMq/NATS/AWS/InMemory wiring; docs sync.

**Non-goals (true):**
- Renaming the already-split AWS/ASB transport classes (KTD-4 — cosmetic on internal types).
- Splitting Redis into separate packages (KTD-3).
- Building Core-NATS Bus transport (KTD-2 — JetStream-for-both instead).

### Deferred to Follow-Up Work

- **Redis durable bus lane via per-group Streams subscriptions** — issue §13 sibling spec; Redis Pub/Sub stays non-durable for v1 (R5).
- **Pulsar transport class split** — only if U4 implementation reveals producer-side topology must differ per lane; otherwise the consumer-subscription-type change is sufficient. Resolved at implementation time, not now.
- **New integration projects for Pulsar / ASB / Kafka / Redis** — out of scope per KTD-5; would unlock full end-to-end lane-isolation conformance for those providers but requires new Testcontainers infrastructure.

---

## Risks & Dependencies

| Risk | Likelihood | Mitigation |
|---|---|---|
| RabbitMQ exchange split breaks existing single-exchange consumers (greenfield, but in-repo tests bind the old way) | Med | Update `RabbitMqConsumerClient` bind path and integration fixtures in the same unit (U2); the conformance test (U1) catches residual fusion. |
| NATS retention-split misconfigures streams (Bus stream accidentally work-queue → loses fan-out) | Med | Unit-assert retention policy per lane (U3); integration fan-out test (two Bus groups each receive a copy). |
| "All 8 providers" acceptance criterion not fully achievable (no integration infra for Pulsar/ASB/Kafka/Redis) | High (known) | KTD-5: prove end-to-end on RabbitMq/NATS/AWS/InMemory; unit-assert topology decisions for the rest; state the bound explicitly rather than overclaiming. |
| NATS flat subject prefix collapses all messages into one `bus`/`queue` stream, destroying domain partitioning | Med | U3: suffix the lane on the message-derived stream name (`orders-bus`/`orders-queue`), never a flat `bus.` subject prefix. |
| Core references the Kafka package to detect Kafka (layering violation) | Low | Detect via `MessageQueueMarkerService` marker string, never a Kafka type reference (KTD-1, U5). |
| Pulsar Bus switched to Exclusive/Failover would idle all but one replica per group (intra-group scaling regression) | Med | KTD-6: keep `Shared` on both lanes; isolation is via topic separation, not subscription type; unit test guards against a non-Shared Bus subscription. |

**Dependency:** Cluster 0.4 (#358) — topology knobs + builder. Provides the `OnBus`/`OnQueue` registration surface this plan's lanes route on.

---

## Sources & Research

- Issue #359 (authoritative spec; the originally-referenced `2026-05-25…requirements.md` / `2026-05-26-001…plan.md` were superseded and removed).
- Cluster 0.4 plan: `docs/plans/2026-06-05-001-feat-messaging-cluster-0.4-universal-knobs-plan.md` (predecessor; escape-hatch + intent-lane invariant R8).
- Learning: `docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md` (verify wrapped client APIs against the package; update human + LLM docs in the same change; greenfield scope rule).
- MassTransit NATS transport analysis (KTD-2): no official NATS transport; a real transport would require JetStream, not Core NATS, because Core NATS is at-most-once and drops messages for offline subscribers. [Transports | MassTransit Docs](https://masstransit.massient.com/concepts/transports) · [Nats support? · MassTransit Discussion #2548](https://github.com/MassTransit/MassTransit/discussions/2548) · [JetStream | NATS Docs](https://docs.nats.io/nats-concepts/jetstream) · [Queue Groups | NATS Docs](https://docs.nats.io/nats-concepts/core-nats/queue)

---

## Resolved Decisions (formerly open; settled in plan review 2026-06-10)

1. **Kafka guard seam (U5):** ~~Setup.cs scan vs drain~~ → **Resolved.** The generic guard already exists at `IBootstrapper.Default.cs:415` (`_RequireTransportFor<IBusTransport>`); U5 extends *that* method with the Kafka hint. No new scan site. (KTD-1, U5.)
2. **Pulsar transport split (U4):** ~~split needed?~~ → **Resolved: no split.** Pulsar producers are subscription-agnostic (they write to a topic; subscriptions are consumer-side), so per-lane topic suffixing on a single transport is sufficient. (KTD-6, U4.)
3. **RabbitMQ Bus exchange type (U2):** ~~fanout vs topic~~ → **Resolved: `topic`.** A `fanout` Bus exchange ignores routing keys and would deliver every Bus message to every group queue regardless of which message names a group subscribed to. `topic` preserves per-message-name routing-key filtering. (U2.)

## Open Questions (Execution-Time)

*None blocking.* The three prior open questions were resolved during plan review (above). Remaining implementation-time discoveries (exact NATS retention enum values, RabbitMQ exchange-name defaults) are routine and surface naturally during U2/U3 coding.
