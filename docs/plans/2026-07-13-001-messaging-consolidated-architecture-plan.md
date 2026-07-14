---
title: "design(messaging): consolidated architecture and delivery roadmap"
status: superseded
date: 2026-07-13
type: design
issues: [217, 220, 221, 222, 223, 224, 225, 226, 233, 263, 271, 273, 276, 332, 333, 336, 337, 344, 346, 347, 348, 349, 350, 351, 359, 402]
supersedes: "GitHub issue #217 roadmap sequencing; does not supersede issue-specific acceptance criteria"
superseded_by: "docs/plans/2026-07-13-002-messaging-reviewed-architecture-plan.md"
---

# Messaging consolidated architecture and delivery roadmap

## BLUF

Headless Messaging should stabilize around two delivery intents on one durable processing model:

- **Bus** means one delivery per subscriber group (fan-out between groups, competing consumers within a group).
- **Queue** means one delivery across the destination's competing consumers.
- The envelope, middleware, persistence, retry, observability, and provider capability model remain shared. Physical broker topology is lane-specific.
- Durable state is the correctness authority. Broker acknowledgements, distributed locks, and Coordination accelerate or fence work; none may be the sole source of truth.
- New capabilities compose over those two intents. Request/reply is a Queue interaction pattern, not a third intent. Scheduled broadcast and transactional broadcast compose over Bus. Transactional inbox is reliability infrastructure shared by both lanes.

The current issue set should not be executed in the order presented by #217. The correct order is: close correctness holes, lock public contracts, complete physical topology and conformance, harden provider/runtime mechanics, then add capabilities.

## Why the current roadmap needs replacement

GitHub issue #217 was last refreshed on 2026-06-10. Its active-interrupt and cluster sections have drifted from current issue state:

- #427, #399, and #272 are closed but remain listed as active work.
- #332, #333, #336, #337, and #402 are open but are absent or underrepresented in the suggested sequence.
- #346 and #347 are carryover bundles, not executable units; their children must be split or absorbed before implementation.
- Reliability defects, API naming, transport topology, test debt, and future product capabilities are mixed into one flat sequence.
- The active #359 plan is a sound issue-specific plan, but its implementation should follow the correctness/API prerequisites below and its conformance boundary should become the foundation for later providers.

This document consolidates all 26 currently open `domain:messaging` issues. Closed plans remain historical evidence and are not reopened.

## Architectural invariants

### 1. Intent is semantic and physical

`IntentType` is not metadata-only. It selects a distinct publisher contract, consumer registration, persisted value, transport capability, and physical broker topology.

| Intent | Delivery guarantee within framework topology | Physical model |
|---|---|---|
| Bus | One copy per subscriber group; replicas in a group compete | Topic/exchange/subject/stream subscriptions isolated from Queue |
| Queue | One copy to one competing consumer in the destination group | Queue/work-queue/consumer-group isolated from Bus |

No message emitted through one lane may be observable by a consumer registered only on the other lane for the same `MessageName`.

### 2. One canonical envelope

Both lanes use the same envelope and header vocabulary: message identity, concrete message type, intent, correlation/causation, tenant, scheduling, retry state, and provider-neutral routing hints. Provider escape hatches remain typed extensions; provider concepts do not leak into the common contract.

The concrete runtime message type must survive every path, including callback responses (#402), so typed middleware and serialization observe the same type.

### 3. One processing state machine

Outbox, inbox/received-message storage, delayed release, retry, exhaustion, and terminal state should share explicit transitions. Required invariants:

- claiming is atomic and fenced;
- a successful state transition is persisted before acknowledgement;
- persistence ambiguity never silently becomes success;
- poison handling is bounded and ends in an inspectable terminal/DLQ state;
- retry budget survives process crashes;
- shutdown does not convert cancellation into terminal failure;
- provider time or a monotonic/fenced mechanism protects cross-node leases from wall-clock rollback (#273).

### 4. Capabilities fail early

Every provider declares Bus, Queue, scheduling, native delay, request/reply, transactional publish, and DLQ capabilities through runtime-owned capability metadata. Unsupported registrations fail during bootstrap with a provider-specific remedy, never on first production send. This is the direction for #351 and #359; it should also govern #222, #223, #226, and #233.

### 5. Provider conformance is the acceptance boundary

Contract behavior belongs in `Headless.Messaging.Core.Tests.Harness`; provider projects supply fixtures and provider-specific cases. A provider is complete only when it passes applicable Bus, Queue, lane-isolation, headers/type, retry, cancellation, poison, and shutdown conformance scenarios. #336 becomes the initial contract suite; #359 extends it to physical lane isolation.

## Target component model

```text
Public API
  IBus / IOutboxBus             IQueue / IOutboxQueue
          |                              |
          +------ canonical send pipeline ------+
                         |
        envelope + typed middleware + capabilities
                         |
             durable processing state machine
        outbox | inbox | schedule | retry | terminal
                         |
              intent-specific transport port
                 Bus               Queue
                  |                   |
          provider physical topology + lifecycle
```

The common send pipeline should be extracted once (#333), but Bus and Queue remain separate public contracts and transport ports. Sharing implementation must not erase intent fencing.

## Issue disposition

Each open messaging issue maps exactly once below.

| Stage | Issues | Disposition |
|---|---|---|
| 0. Correctness floor | #273, #332, #347, #402 | Fix before broad API/topology work. Split #347 into independently reviewable storage-atomicity, dispatcher-backpressure, scheduler-flush, SQL transaction-flush, and poison-policy units. |
| 1. Contract lockdown | #333, #349, #350, #351 | Decide and land the greenfield public shape once. Extract shared internals without merging Bus/Queue contracts; normalize naming; make headers honest; replace runtime `IServiceCollection` inspection with capability metadata. |
| 2. Contract proof and docs | #336, #337 | Establish the reusable conformance matrix and synchronize package/LLM docs with the locked API before provider-wide topology edits. |
| 3. Physical topology | #359, #344 | Execute the existing #359 plan; #344 closes only when lane isolation is proven. Preserve its provider-specific decisions unless implementation evidence invalidates them. |
| 4. Provider/runtime hardening | #348, #271, #233 | Consolidate ASB connection/sender ownership; then address rolling-restart latency and NATS stream/DLQ ergonomics against the new conformance suite. |
| 5. Cross-cutting substrate alignment | #263, #346 | Replace umbrella tracking with explicit child issues. Share vocabulary and primitives with Jobs only where semantics match; do not force one persistence abstraction across domains. Absorb or close every #346 item after revalidation. |
| 6. Reliability feature | #225 | Add transactional inbox/idempotent consume on the stable state machine. This precedes new fan-out/RPC features because it strengthens both lanes. |
| 7. Bus capability line | #220, #223, #224, #226 | Define base broadcast/fan-out semantics first (#220), then scheduling (#223), transactional bridge (#224), and AWS SNS/SQS realization (#226). |
| 8. Interaction capability | #222 | Model request/reply as a Queue wrapper with correlation, temporary/reusable reply destination lifecycle, timeout, cancellation, and capability validation. Promote to a third intent only if provider conformance proves Queue semantics insufficient. |
| 9. Operator experience | #221 | Add fan-out visibility after #220 emits stable delivery/subscriber metadata. Dashboard filtering remains an operator lens, not authorization. |
| Deferred RFC | #276 | Re-evaluate envelope/typed consume phases after the canonical middleware pipeline and inbox design exist. Close as no-op if typed middleware can be expressed without a second public pipeline. |
| Tracker | #217 | Replace its sequencing and issue-state tables with this plan; retain it as the canonical GitHub index linking executable issues. |

## Delivery units

### U0. Revalidate and split umbrella issues

Before code changes, update #217 from current GitHub state and split #347/#346 into atomic issues. Close already-shipped or obsolete items instead of carrying them forward. Every child receives an owner stage, dependency, acceptance criteria, and test boundary.

### U1. Correctness floor

1. #402: preserve concrete response type through callback publishing and prove typed publish middleware runs.
2. #332: make processor shutdown fully async under one host-wide deadline; cancellation must not mark messages failed.
3. #347 non-poison units: close storage commit ambiguity, bounded channel wait, scheduler flush state, and SQL outbox commit gaps.
4. #347 poison unit: define a provider-neutral disposition (`Retry`, `DeadLetter`, `RejectTerminal`) and map Kafka, RabbitMQ, and SQS without unbounded redelivery storms.
5. #273: replace cross-node wall-clock lease safety with a store-authoritative/fenced design. Do not attempt to fix this only by lengthening `LockedUntil`.

Exit: crash, shutdown, persistence ambiguity, poison, and clock-skew scenarios have deterministic tests and no known duplicate-concurrent-dispatch path.

### U2. Public contract lockdown

Resolve the API choices together because they touch every provider:

- plural, clearly mutable `MessageHeaders`; context consumers receive an explicit scoped view rather than a misleading read-only wrapper (#350);
- distinct `PublishOptions` and `EnqueueOptions`, sharing composition internally but exposing intent-correct names;
- one provider naming scheme: `{Provider}MessagingOptions`, `Setup{Provider}`, and role-specific transport names (#349);
- a shared internal direct-publisher core with intent-specific adapters (#333);
- immutable capability metadata created during registration, not runtime container inspection (#351).

Exit: public API review complete, XML docs updated, and no planned breaking rename remains.

### U3. Conformance and documentation baseline

Build a harness matrix for:

- registration symmetry and startup capability errors;
- Bus group fan-out and Queue competing consumption;
- cross-lane non-delivery;
- concrete type/header propagation;
- cancellation and shutdown deadlines;
- retry/exhaustion/poison disposition;
- resource disposal.

Apply scenarios to every provider with real infrastructure where available; unit tests may prove only pure topology mapping. Document explicit gaps instead of claiming full conformance (#336, #337).

### U4. Dual-lane physical topology

Execute `docs/plans/2026-06-10-001-feat-messaging-dual-lane-topology-kafka-guard-plan.md` for #359/#344. Treat that plan as the provider-level source of truth. Feed its lane-isolation tests into U3's permanent harness rather than creating a one-off suite.

Exit: every supported provider either passes both-lane conformance or fails startup for an unsupported lane with actionable guidance.

### U5. Runtime and provider hardening

- #348: one ASB connection/sender pool per configured namespace, race-free lazy creation, sender-before-client disposal.
- #271: reduce recovery latency through event/tick acceleration while durable leases remain the correctness floor.
- #233: expose NATS stream/DLQ behavior through the same poison and monitoring contracts, not NATS-only public abstractions unless unavoidable.

### U6. Shared reliability substrate

Implement #225 as an inbox record keyed by stable message/delivery identity and tenant where applicable. Atomic consume-side effects require an ambient/provider transaction seam; broker acknowledgement follows commit. Duplicate delivery returns the stored terminal outcome or becomes a no-op according to the documented contract.

For #263, share primitives with Jobs only after comparing invariants: retry policy, lease/fence vocabulary, Coordination identity, ambient commit coordination, telemetry tags, and test harness patterns are candidates. Message envelopes, job definitions, and domain-specific stores remain separate.

### U7. Capability expansion

Bus features proceed as one dependency chain: #220 → #223 → #224 → #226 → #221. Request/reply (#222) can proceed after U6 in parallel with the Bus feature line because it composes over Queue and shares only stable envelope/capability infrastructure.

## Design decisions requiring explicit confirmation

These are the only consequential choices not already settled by shipped code or the active #359 plan:

1. **Clock-skew safety (#273):** prefer database/store-authoritative lease comparison plus fencing token/version on state transitions. A process-local monotonic clock cannot establish cross-node ordering.
2. **Headers (#350):** prefer a mutable `MessageHeaders` envelope type with a scoped `IReadOnlyDictionary` view exposed to consumers unless mutation middleware is explicitly enabled.
3. **Request/reply (#222):** prefer a Queue interaction pattern, not a third delivery intent. Revisit only if reply topology cannot satisfy the two-lane conformance model.
4. **Jobs unification (#263):** unify primitives and terminology, not the full persistence/state-machine abstraction.
5. **Poison policy (#347/#233):** framework retry budget owns bounded retry; after exhaustion, providers map to native DLQ/terminal acknowledgement. Never requeue indefinitely by default.

## Non-goals

- Exactly-once delivery. The target is at-least-once transport with atomic state transitions and opt-in idempotent consume.
- A universal broker abstraction that hides provider capabilities.
- One physical topology recipe for every broker.
- A third intent merely to name request/reply.
- Combining Messaging and Jobs domain models or storage schemas.
- Dashboard authorization redesign.

## Verification strategy

For every delivery unit:

1. Run abstraction/unit tests for contract and state-machine behavior.
2. Run provider integration suites through the shared harness using real broker/storage infrastructure.
3. Add failure-injection tests for crash between send/state-write/ack boundaries.
4. Verify cancellation with one shared shutdown deadline and `TimeProvider`-controlled tests.
5. Keep `docs/llms/messaging.md`, package READMEs, XML docs, issue tracker, and capability matrix in lockstep.

The roadmap is complete when every open messaging issue is either delivered, explicitly parked with a dependency, or closed as superseded—and every supported provider has a truthful conformance profile.

## Immediate next actions

1. Refresh #217 to point at this plan and remove closed interrupts.
2. Split #347 and #346 into executable children; close obsolete carryover.
3. Implement U1 in order: #402, #332, #347 correctness children, #273.
4. Hold #359 implementation until U1 and the public-contract decisions in U2 are settled, then execute the existing detailed topology plan.
