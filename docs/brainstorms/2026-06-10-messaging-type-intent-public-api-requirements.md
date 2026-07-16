---
date: 2026-06-10
topic: messaging-type-intent-public-api
status: superseded
superseded_by: docs/plans/2026-07-13-002-messaging-reviewed-architecture-plan.md
---

# Messaging Type-Intent Public API — Requirements

> **Superseded 2026-07-13.** The owner selected verb-conveyed Bus/Queue semantics with plain message
> contracts and structurally lane-scoped registration. This file remains as the rejected marker-based
> alternative and decision history. Do not implement it.

## Summary

Redesign the messaging public API so every message type declares its lane once via `IEvent`/`ICommand` marker interfaces, enforced at compile time by generic constraints on `IBus.PublishAsync` / `IQueue.EnqueueAsync`. The outbox interfaces (`IOutboxBus`/`IOutboxQueue`) are deleted: durability becomes a delivery mode on the two remaining surfaces — ambient-transaction-driven by default, overridable per call.

---

## Problem Frame

Headless is the only framework among the seven surveyed (MassTransit, NServiceBus, Wolverine, Rebus, Foundatio, CAP) that lets one message type traverse both the bus and queue lanes. Intent is chosen independently at every registration (`OnBus`/`OnQueue`) and every publish call (which of four interfaces), with nothing reconciling the two axes — the root of the #344 cross-intent leak and of four documented API weaknesses: free-floating intent, a verbose common case, scanning that cannot express intent, and a four-interface publish surface. The dual-lane capability this flexibility preserves is exercised by one unit test and used by nothing else in the repo.

Full analysis, framework comparison, and decision ledger: `docs/brainstorms/2026-06-10-messaging-public-api-design-handoff.md`.

---

## Key Decisions

- **One type → one lane, no escape hatch (Option 3).** Every message is an event or a command. A both-lanes need is modeled as two types. The #344 collision becomes unrepresentable instead of contained.
- **Markers + generic constraints, no conventions (A1).** Classification lives on the type (`IEvent`/`ICommand` over a common `IMessage`); constraints make wrong-verb and unmarked-type publishes compile errors. Predicate conventions, attributes, and builder classification (`.AsEvent()`) do not exist — they cannot satisfy constraints and would forfeit compile-time enforcement. Unowned third-party types are wrapped in owned marker-bearing records. Every public generic surface that takes a message type carries the corresponding marker constraint (`IBus`/`IQueue` per lane; `IConsume<T>`/`ForMessage<T>` on `IMessage`) — unmarked types are unrepresentable across the whole API, not just at publish.
- **Keep `IBus` and `IQueue`; delete only the outbox pair.** The collapse is 4 → 2, not 4 → 1. The lane stays visible as the noun you inject, doubly enforced by the constraint. Familiar names and verbs (`PublishAsync`/`EnqueueAsync`) are preserved.
- **Durability follows the ambient transaction by default, overridable per call.** `DeliveryMode.Auto`: inside a framework-recognized transaction → outbox semantics; outside → direct to transport. `Durable` and `TransportDirect` are explicit per-call overrides. The hot broadcast path (e.g., cache invalidation) stays storage-free because its internal publisher declares `TransportDirect` explicitly (R17) — not as an automatic property of `Auto`, which upgrades to outbox semantics inside transactions.
- **Greenfield rip-and-replace.** `OnBus`/`OnQueue`, `IOutboxBus`/`IOutboxQueue`, and `IntentType`-as-public-signal are removed outright; no compatibility shims.

---

## Requirements

**Markers and classification**

- R1. A dependency-free contracts surface ships three empty marker interfaces: `IMessage`, `IEvent : IMessage`, `ICommand : IMessage`.
- R2. Every concrete message type resolves to exactly one lane marker after walking its full base-type and interface graph: assignable to `IEvent` xor `ICommand`. Derived marker interfaces (`IDomainEvent : IEvent`) are allowed. A type assignable to both, directly or indirectly, fails validation with an error naming the concrete type and the marker paths that caused the conflict.
- R3. No other classification mechanism exists. Guidance for unowned external types: wrap them in owned records.

**Publish surface**

- R4. `IBus.PublishAsync<T>` is constrained `where T : IEvent`; `IQueue.EnqueueAsync<T>` is constrained `where T : ICommand`.
- R5. `IOutboxBus` and `IOutboxQueue` are deleted; their capabilities move behind `IBus`/`IQueue` as delivery modes (R8–R10).
- R6. `IConsume<T>` is constrained `where T : IMessage`, so a handler for an unmarked type is a compile error.
- R6a. Consumed message types may be interfaces or abstract classes but must be assignable to exactly one of `IEvent`/`ICommand`. A consumed type assignable to neither (e.g., `IConsume<IMessage>`) is a named boot-validation error.
- R7. `ConsumeContext` drops the typed `IntentType` property; the lane is derivable from the message type. The wire intent header remains on the envelope, accessible through the context's headers collection — this is the metadata R16 preserves.

**Durability and delivery**

```mermaid
flowchart TB
  P[PublishAsync / EnqueueAsync] --> M{DeliveryMode}
  M -->|Auto - default| T{Framework-recognized tx?}
  T -->|yes| O[Outbox: row in same tx, dispatch post-commit]
  T -->|no| D[Direct to transport]
  M -->|Durable| S[Store-first: persist row, dispatch with retry]
  M -->|TransportDirect| D
```

- R8. Default `DeliveryMode.Auto` uses the framework transaction accessor as the only source of truth for ambient durability. With an active framework-recognized transaction, the call writes an outbox row into that transaction and dispatches only after commit; without one, the call sends directly to the transport with no storage row.
- R8a. Presence of `System.Transactions.Transaction.Current` alone does not imply outbox semantics unless it is recognized by the framework transaction accessor.
- R8b. When `Auto` resolves to transport-direct while `System.Transactions.Transaction.Current` is non-null but unrecognized, a structured diagnostic event records that an ambient transaction was present and not honored, throttled to once per unique call site (or sampled) so high-throughput paths stay quiet. This detection covers `System.Transactions` only: ambient provider-native transactions (raw `DbTransaction`, EF `BeginTransaction` without the outbox integration) are structurally undetectable from the publish path and remain silent — a stated contract boundary, documented per R18.
- R9. `DeliveryMode.Durable` forces store-first regardless of transaction state (in a transaction: same-transaction row; outside: standalone row, then dispatched with retry).
- R10. `DeliveryMode.TransportDirect` forces transport-direct even inside a transaction — an explicit escape from atomicity. When used while a framework-recognized transaction is active, a structured diagnostic event records that atomicity was intentionally bypassed.
- R10a. A public registration-level declaration (on the message type's `ForMessage<T>` configuration) marks a type as bypassing atomicity by design; for declared types the R10 and R8b diagnostics downgrade to boot-time or sampled emission instead of per-call. Framework-internal publishers (R17) consume this same public mechanism — no internal special case.
- R11. `Delay` requires storage: under `Auto` with no transaction it upgrades the call to durable; combined with explicit `TransportDirect` it is an argument error. If durable scheduling is unavailable for the configured provider, the call fails synchronously before writing or sending anything. `Delay` means the message is not eligible for dispatch until the delay elapses; exact dispatch time is best-effort (dispatcher polling/backoff). The current silent-ignore of `Delay` on direct publishes is removed.
- R12. `PublishOptions`/`EnqueueOptions` remain separate records and gain the `Delivery` member.

**Registration and discovery**

- R13. Assembly scanning infers the lane from the marker, replacing the hardcoded Bus default. Scanning is the documented default registration style.
- R13a. Scanning discovers consumers, resolves each consumed message type, validates its marker, and registers the consumer on the lane derived from that marker.
- R13b. A consumer for an unmarked type is invalid even when discovered only through scanning.
- R14. `ForMessage<T>` remains the explicit/override surface for name, correlation, and consumer configuration (`Group`, `Concurrency`, `HandlerId`, circuit breaker, provider escape hatches). `OnBus<C>`/`OnQueue<C>` are replaced by a lane-neutral `Consumer<C>`.
- R14a. `ForMessage<T>` is constrained `where T : IMessage`.
- R14b. `Consumer<C>()` validates that `C` consumes `T` — by generic constraints where the API shape allows, by boot validation otherwise.

**Enforcement and diagnostics**

- R15a. Boot validation checks every message type discovered through scanning or explicit registration against R2.
- R15b. Runtime validation checks the resolved message type before writing, dispatching, or consuming an envelope, so reflection, dynamic invocation, open generic consumers, provider escape hatches, and internal dispatch paths cannot bypass classification. The resolved message type is the runtime payload type when the payload is non-null, falling back to the static generic argument for null payloads; the R2 check runs against it, and a runtime payload type whose lane differs from the static argument's lane is a runtime error.
- R15c. Runtime validation fails before any storage write or transport send — a bad envelope is never half-written.
- R16. The wire intent header continues to be stamped for diagnostics; fail-closed behavior on mismatch is owned separately (Q4 follow-up issue). This work preserves enough envelope metadata to later check a resolved message type's marker against the wire intent header.

**Adoption**

- R17. Internal consumers of messaging migrate to the marker model, each with an explicit `Delivery` decision — the verified inventory: Caching.Hybrid invalidation (`IBus`, explicit `TransportDirect` — preserves today's fire-and-forget semantics; accepted trade-off: pre-commit invalidation, identical to current behavior), Permissions.Core's dynamic permission store (`IBus`, `Auto` — intentional: change notifications gain outbox atomicity inside recognized transactions, direct otherwise as today), DistributedLocks.Core and its Redis/InMemory setups (optional `IOutboxBus` → `IBus`, explicit `Durable` — preserves today's store-first wake-up durability across broker outages), and EntityFramework.Messaging's outbox integration-event dispatcher + publish invoker cache (`IOutboxBus` → `IBus`, explicit `Durable` — same-transaction row when the accessor recognizes the transaction, store-first otherwise; fails safe instead of silently direct when the accessor is not flowing). Also in inventory: Headless.Messaging.Testing's `MessagingTestHarness` (public `OutboxBus`/`OutboxQueue` accessors collapse into the existing bus/queue properties) and the Messaging.Storage.{PostgreSql,SqlServer} circular-reference guards (re-key `IsUsingType<IOutboxBus/IOutboxQueue>` detection to `IBus`/`IQueue`).
- R17a. `IIntegrationEvent` (Headless.Domain) extends `IEvent`, classifying integration events as bus-lane so the reflection-built publish invokers satisfy R4's constraint.
- R18. Doc surfaces (`docs/llms/messaging.md`, affected package READMEs) are updated per `docs/authoring/AUTHORING.md`. Docs state the lane meaning explicitly: `IEvent` = broadcast/pub-sub lane, `ICommand` = point-to-point/work-queue lane — not DDD's domain-event/command vocabulary. A "domain command that should also broadcast" is modeled as two message types. Docs also list exactly which transaction integrations the framework accessor recognizes; all others do not trigger outbox semantics under `Auto` (R8a/R8b).

---

## Acceptance Examples

- AE1. **Covers R4.** Given `ProcessPayment : ICommand`, when code calls `bus.PublishAsync(new ProcessPayment(…))`, then compilation fails on the `IEvent` constraint.
- AE2. **Covers R2, R15.** Given `record Confused : IEvent, ICommand` registered via scanning, when the host boots, then startup fails with an error naming `Confused` and both markers.
- AE3. **Covers R8.** Given no ambient transaction and default options, when an event is published, then no storage row is written and the transport receives it directly.
- AE4. **Covers R8.** Given an ambient transaction, when an event is published with default options and the transaction rolls back, then the message is never dispatched.
- AE5. **Covers R9.** Given no ambient transaction and `Delivery = Durable`, when the broker is unavailable at publish time, then the message persists and dispatches once the broker recovers.
- AE6. **Covers R11.** Given `Delivery = TransportDirect` and a non-null `Delay`, when the call is made, then it fails with an argument error rather than ignoring the delay.
- AE7. **Covers R13, R13a.** Given an assembly with handlers for one `IEvent` type and one `ICommand` type, when registered via scanning only, then the event handler consumes from the bus lane and the command handler from the queue lane.
- AE8. **Covers R4.** Given `UserRegistered : IEvent`, when code calls `queue.EnqueueAsync(new UserRegistered(…))`, then compilation fails on the `ICommand` constraint.
- AE9. **Covers R10.** Given an ambient framework transaction, when an event is published with `Delivery = TransportDirect` and the transaction later rolls back, then the transport send is not rolled back and a diagnostic event records that direct delivery bypassed atomicity.
- AE10. **Covers R11.** Given no ambient transaction, default delivery, and a non-null `Delay`, when a command is enqueued, then a storage row is written and the message is not eligible for dispatch until the delay elapses.
- AE11. **Covers R14a.** Given `record ExternalWebhook(…)` with no marker, when code calls `ForMessage<ExternalWebhook>()`, then compilation fails on the `IMessage` constraint.
- AE12. **Covers R2.** Given `interface IDomainEvent : IEvent` and `record UserRegistered(…) : IDomainEvent`, when discovered by scanning, then it classifies as an event.
- AE13. **Covers R2, R15a.** Given `interface IIntegrationMessage : IEvent, ICommand` and `record Bad(…) : IIntegrationMessage`, when the host boots, then validation fails naming `Bad` and both marker paths.
- AE14. **Covers R16.** Given `UserRegistered : IEvent`, when it is published, then the wire intent header is stamped with the bus lane.
- AE15. **Covers R17, R17a.** Given a concrete type implementing `IIntegrationEvent`, when the EF outbox integration-event dispatcher publishes it, then it dispatches without a compile-time or classification error.
- AE16. **Covers R6a.** Given `class AllMessagesConsumer : IConsume<IMessage>`, when the host boots, then startup fails with a named error identifying the consumer and `IMessage` as assignable to neither lane.
- AE17. **Covers R15b.** Given a null payload published as `PublishAsync<OrderPlaced>(null)`, when runtime validation runs, then classification uses the static generic argument and succeeds.
- AE18. **Covers R15b.** Given a base event type whose runtime payload is a derived type classified to the other lane, when published, then the call fails at runtime before any storage write or transport send.
- AE19. **Covers R11.** Given no ambient transaction, default delivery, a non-null `Delay`, and a provider without durable scheduling, when a command is enqueued, then the call fails synchronously and neither a storage row nor a transport send occurs.
- AE20. **Covers R14.** Given explicit registration `ForMessage<OrderPlaced>(m => m.Consumer<OrderPlacedHandler>())` where `OrderPlaced : IEvent`, when the host boots, then the consumer is registered on the bus lane.
- AE21. **Covers R17.** Given an ambient framework transaction, when Caching.Hybrid publishes an invalidation, then no storage row is written and the transport receives it before commit.
- AE22. **Covers R8b.** Given a non-null but unrecognized `System.Transactions.Transaction.Current`, when an event is published under `Auto`, then it goes transport-direct and the not-honored diagnostic is emitted.

---

## Scope Boundaries

- #359 (dual-lane physical topology split) executes from its own plan; it ships regardless and becomes a transition-only correctness net under this model.
- Q4 (fail-closed intent-header validation) is a standalone follow-up issue, not part of this work.
- A Roslyn analyzer for the both-markers hole is a later nicety, not v1.
- Option 4-style pipeline un-sharing (separate storage/dispatch per lane) is rejected; the internal pipeline stays shared.
- Request/response, sagas, and scheduling beyond the existing `Delay` option are untouched.

---

## Dependencies / Assumptions

- The ambient-transaction mechanism (`IOutboxTransactionAccessor` + writer branching) already exists and carries the `Auto`/outbox behavior; this work re-fronts it, not rebuilds it.
- Dual-lane same-type registration has no internal consumers (verified 2026-06-10); removing it breaks only one unit test.
- Message storage remains required by the consume pipeline; `Auto`'s direct path removes the storage write from the publish hot path only.
- Transaction integrations recognized by `IOutboxTransactionAccessor` today: only the EF Core outbox integration (`EntityFramework.Messaging` attaches its transaction to the accessor). This list is the authoritative input for R18's doc obligation; additions require explicitly extending it, not open-ended discovery during implementation.

---

## Outstanding Questions

**Deferred to planning**

- Where the contracts surface lives — markers and `DeliveryMode` placed together as one decision (new contracts package vs `Headless.Messaging.Abstractions`, by dependency weight; `DeliveryMode` may sit one layer up with the options records if the markers go in a leaner package). Member names are settled: `Auto`, `Durable`, `TransportDirect`. Hard constraint: R17a makes `Headless.Domain` (currently zero project references) a consumer of the marker surface, and `Headless.Messaging.Abstractions` today references `Headless.Checks` — satisfying R1 means either a new leaf contracts package or stripping that reference.
- Interface/abstract consumed types (R6a) vs name-keyed routing: routing maps concrete type → message name, so an `IConsume<IOrderEvent>` consumer would subscribe under the interface's name and never receive derived-type envelopes. Decide: define polymorphic delivery, or narrow R6a to concrete consumed types (or explicit name mappings) for v1. *(From 2026-06-10 review, adversarial.)*
- Whether the scanning entry points keep the `ForMessagesFromAssembly*` names or rename to consumer-centric names.
- Exact error codes/wording for R2/R11/R15 errors (`g:snake_case` descriptor space).
- How `Durable`-outside-transaction interacts with the dispatcher's existing retry/backoff configuration.

---

## Implementation Risk Notes

Breadcrumbs for the planner — the areas most likely to bite during implementation.

- Constraints must live on the public methods (`IBus`, `IQueue`, `ForMessage<T>`), not only on internal helpers.
- Lane resolution uses full assignability (`typeof(IEvent).IsAssignableFrom(t)` xor `typeof(ICommand).IsAssignableFrom(t)`), never direct-interface-list checks — derived marker aliases must classify correctly (R2, AE12).
- Scanning currently hardcodes the Bus lane; the migration re-keys discovery on `IConsume<T>` → `T` → marker (R13a).
- Existing tests may rely on `Delay`'s silent-ignore on direct publishes; replace them with explicit-failure tests (R11).
- `Durable` outside a transaction needs a defined boundary — persist row, then dispatch via the dispatcher/relay — not persist-then-best-effort-immediate-send, unless that contract already exists and is documented.
- Marking internal messages (cache invalidation) may surface vague internal message semantics worth tightening while there.

---

## Sources

- `docs/brainstorms/2026-06-10-messaging-public-api-design-handoff.md` — decision ledger (§7, Q1–Q7), framework research (§3.2), modeled API (Appendix F), code map (Appendix A).
- `docs/plans/2026-06-10-001-feat-messaging-dual-lane-topology-kafka-guard-plan.md` — #359 plan, ships independently.
- Industry references: NServiceBus message conventions and enforcement, MassTransit transactional outbox and Riders, Wolverine routing/durability (URLs in the handoff doc §3).
