# CONCEPTS

Project-specific vocabulary — words that carry a precise meaning in this codebase that a new
engineer would need defined to follow conversations, tickets, and code. General programming terms
(cache, queue, job, session) are out of scope. This file grows as areas are documented; it is not
yet a complete glossary of the whole framework.

## Coordination (membership / liveness)

### Relationships

A **Node identity** owns one **Descriptor** and one current **Liveness row** per incarnation. The
**Store** is the authority for both the **Incarnation** counter and every liveness timestamp;
application nodes never arbitrate either. **Membership events** are derived from snapshots of the
liveness rows, not from a separate source of truth.

### Node identity
A coordination participant identified as `nodeId@incarnation` — the node id plus the generation that
distinguishes one run of that node from a later restart. Two runs of the same `nodeId` are *distinct*
identities, so a restarted node never inherits its dead predecessor's standing.

### Incarnation
The monotonic generation number that qualifies a Node identity. Allocated by an atomic increment in
the store at registration; a heartbeat or leave carrying a prior incarnation is rejected at the
write so a stale run cannot resurrect or overwrite a newer one. The current incarnation also becomes
terminal once it is dead, gracefully left, or pruned; recovery requires registering a higher
incarnation. *Avoid:* generation (use Incarnation for the per-node value; "generation table/counter"
names the durable authority that issues it).

### Descriptor
The cold, write-once record of a Node identity: host/ports, role, metadata. Established at
registration and not rewritten by heartbeats. Distinct from the Liveness row, which is the hot
per-beat record.

### Liveness row
The hot record carrying a Node identity's last-beat timestamp and derived Liveness state. Refreshed
by every heartbeat. Establishing it is part of the registration contract, not a side effect of the
first heartbeat.

### Liveness state
The store-evaluated condition of a Node identity, transitioning Alive → Suspected → Recovered (back
to Alive) or → Dead/Left. Dead and Left are terminal for that incarnation, as is removal of its retained
liveness entry. Computed from how stale the Liveness row is against the store clock, using
store-evaluated thresholds — never by an application node comparing wall clocks.

### Store as temporal authority
The rule that timestamps governing shared liveness or lease ownership, and the predicates that evaluate
them, must use the durable store's server clock inside the authoritative atomic operation. Application
nodes must not use their wall clocks to decide another node's expiry; in-memory implementations instead
use their single injected clock because no independent shared-store clock exists. Failover decisions use
the authoritative write/primary path rather than a stale replica read.

### Incarnation guard
The write-time check that a heartbeat, leave, or registration write only takes effect when its
incarnation is still the current generation for that node id and has not become terminal, performed
atomically with the write (pessimistic row lock on the relational stores, compare-in-Lua on Redis).
Registration may create the liveness entry; periodic heartbeats may only refresh an existing live
entry. The mechanism that makes the Store-as-temporal-authority invariant enforceable under
concurrency.

### Authoritative provider
A coordination provider that can offer a server clock plus a linearizable liveness-row read/write,
and is therefore eligible to drive failover. A provider that cannot is degraded/unsupported for
failover even if it can serve approximate dashboard views.

## Messaging (lanes / classification / delivery)

### Relationships

A **message contract** is a plain serializable class, record, or interface. The invoked operation and
lane-scoped registration select its **Message lane**: `IBus.PublishAsync` for broadcast or
`IQueue.EnqueueAsync` for point-to-point delivery. **Delivery mode** is orthogonal to lane: it decides
whether the message is captured durably or sent straight to transport.

### Message lane
The semantic channel of a message: bus (broadcast — every subscriber group gets a copy) or queue
(point-to-point — competing consumers, one worker per message). The same CLR contract may use both
lanes intentionally, but each lane has an independent registration, route, runtime key, and storage
discriminator; physical topology is independent only when the provider declares that capability.
Use `MessageLane.Bus` / `MessageLane.Queue` in new APIs and
writing. Persisted columns, headers, wire values, monitoring, and dashboard projections retain the
legacy `IntentType` name and numeric values until the #350 compatibility cutover. `Bus = 0` and
`Queue = 1` are stable compatibility values; an undefined value fails explicitly and never falls
back to Bus.

### Verb-conveyed lane model
The decided messaging model (2026-07-13): the operation conveys semantics. Publishing through `IBus`
uses the Bus lane; enqueueing through `IQueue` uses the Queue lane. Message contracts implement no
framework classification marker. Consumer configuration is structurally lane-scoped through
`setup.Bus.ForMessage<T>` / `setup.Queue.ForMessage<T>`, and registry identity is
`(MessageType, MessageName, MessageLane)`. The same contract and logical name may be registered on
both lanes with independent consumers and runtime state when the selected transport can keep their
physical topology separate.

Provider support is declared by immutable transport, storage, and coordination capability
descriptors. Startup and per-call gates reject unsupported lanes, scheduling, or dual-lane topology
before readiness, middleware, storage writes, client creation, or transport I/O. NATS and RabbitMQ
currently cannot isolate the same contract/name on both lanes; that physical topology migration is
owned by #359.

Callback responses always use the Bus delivery lane, even when the request arrived on Queue. Queue
remains the request's origin metadata; the declared callback contract selects typed middleware while
the concrete response type remains payload metadata.

### Delivery mode
The per-call durability choice on publish/enqueue: `Auto`, `Durable`, `TransportDirect`. Auto, the
default, follows the framework transaction accessor (the only source of ambient durability —
`Transaction.Current` alone does not count): recognized compatible transaction present → outbox
(row persisted in that transaction, dispatched post-commit); no coordination → direct to transport;
an active incompatible boundary → reject before side effects. Durable forces
store-first regardless of transaction state. TransportDirect bypasses storage and coordination
compatibility checks even inside a transaction — an explicit, diagnostically-logged escape from
atomicity. `Delay` requires storage:
under Auto it upgrades the call to durable; with explicit TransportDirect it is an error; dispatch
timing is best-effort (not-before semantics).

## Flagged ambiguities

- "Generation" had been used loosely for both a node's Incarnation and the durable counter that
  issues incarnations — these are distinct: Incarnation is the per-node value, the generation
  table/counter is the authority.

## Commit Coordination

### Commit coordinator

The register-only scope object that collects commit and rollback callbacks for one physical unit of
work. It guarantees exactly-once callback invocation per coordinator instance, not exactly-once
business effects.

### Commit signal source

The provider adapter that turns a native commit or rollback edge into a coordinator terminal signal.
Examples include owner-driven in-memory signals and SQL Server provider-key correlation.

### Work buffer

Scope-local state owned by a coordinator. Buffers hold deferred work until the terminal outcome; they
must not be used as arbitrary service-locator bags.

### Capability

A read-only provider escape hatch attached by the scope owner. `IRelationalCommitContext` is the
current capability for BCL `DbConnection` and `DbTransaction` handles.

## Startup validation

### Startup validation gate

A check that runs once during host startup to verify configuration, and on failure either warns or
fails host startup. Gates split into two tiers — Correctness gate and Diagnostic gate — by whether
the check does runtime I/O.

### Correctness gate

A startup validation gate that is cheap and does no network I/O — it inspects options, in-memory EF
model metadata, DI wiring, or tenant posture. Always strict (fails startup) in every environment,
because the misconfigurations it catches are deterministic and cheap to detect, so deferring them
only moves the failure to live traffic.

### Diagnostic gate

A startup validation gate that does runtime I/O — opening a connection or probing a live operation —
and so adds boot latency and can fail on a transient blip. Verifies an environment- or
library-compatibility property rather than a per-request correctness property; defaults to off in
production and active in development.
*Avoid:* diagnostic probe (use Diagnostic gate for the concept; "probe" names the I/O call it makes).

### Validation mode

The per-gate strictness setting: off (skip), warn (log and continue, recording degraded state), or
strict (throw and fail host startup). A gate resolves its mode from an explicit operator value when
set, otherwise from an environment-aware default keyed to its tier.

## Jobs (misfire recovery)

### Schedule watermark

The UTC instant through which one cron definition's schedule has been reconciled. Records what was
*accounted for* rather than what was promised, which is why it stays true when a rule change
invalidates a derived prediction and why a skip advances it without anything firing. Distinct from
`ScheduleRevision`, which is definition-version metadata rather than a schedule position.

### Dispatch projection

The first occurrence after the watermark, persisted and indexed. It is the key the scheduler selects
on, so deciding what is due costs an indexed read rather than evaluating every definition's
expression on every node. Always derivable from the watermark and the definition, so it can be
rebuilt whenever schedule interpretation changes.

### Pending instant

A scheduled instant at or before now that the watermark has not passed — whether or not an
occurrence row exists for it. Row-independence is the point: a process that died mid-sleep left no
row behind, so a row-based definition of "missed" is blind to exactly the case recovery exists for.

### Misfire

A backlog that warrants recovery rather than ordinary dispatch: more than one pending instant, or a
single pending instant older than the definition's grace threshold.

### Grace threshold

Seconds of lateness tolerated before a single pending occurrence counts as a misfire. Resolved once
at creation and persisted per definition, so no node's local configuration can decide whether an
instant misfired.

### Recovery run

An occurrence materialized by recovery rather than normal dispatch, stamped with the earliest missed
instant it stands in for. A coalesced run represents the whole missed window, not one instant.

### Evaluation fingerprint

An opaque hash of the rules a projection was derived under — cron-library semantics and the
effective timezone's DST rules. Only equality is meaningful: a mismatch means an identical
expression and timezone would now resolve to a different instant.

## Jobs (chains)

### Job chain

A conditional sequential tree of one-shot time jobs persisted on the existing parent/child columns —
each edge is a child row carrying a run condition, never a separate workflow schema. A node has at
most one success child and one failure child, so a chain branches on outcome but never fans out in
parallel. Chain depth (nodes along a path from the root, catch branches included) is capped by a
configurable global limit enforced at enqueue, before persistence; building additionally applies a
fixed structural bound.

### Catch step

A chain step attached to run only when its parent fails (`RunCondition.OnFailure`). Pure authoring
sugar: it does not consume, recover, or rewrite the parent's failure — the parent stays failed, no
catch marker is stored, and a catch step that itself fails is an ordinary failed job whose own
continuations follow the same rules. *Avoid:* reading it as try/catch exception handling (the
parent's outcome is immutable history, not something a catch step handles).

### Timed descendant

A chain child carrying an explicit execution time. Unlike untimed children (which run in-process
when their parent completes), it is claimed independently and becomes eligible at the later of its
parent's matching terminal state and its own execution time; a non-matching parent terminal state
skips it with its subtree. It is never started early and never failed merely because its time
arrived while the parent was still running.

## Multi-tenancy

### Tenancy seam

A named integration point (for example "Messaging", "Jobs") where a feature attaches to the
multi-tenancy infrastructure: it records its tenant posture in the manifest via
`HeadlessTenancyBuilder.RecordSeam` and contributes `IHeadlessTenancyValidator` startup checks.

### Tenant posture

The strengthen-only status a seam records in the posture manifest — precedence Configured <
Propagating < Guarded < Enforcing — describing how strongly that seam handles tenant context.
A later registration can raise a seam's posture but never lower it.

## Jobs (tenancy)

### System job

A time job enqueued with `IsSystemJob = true`: persisted with a null tenant and executed without
tenant scope. It can only be created outside tenant context — an ambient tenant rejects the flag
(no tenant-to-system escalation) — and the system-job decision is logged at schedule time.

### Cron fan-out

The pattern for tenant-scoped recurring work: cron definitions and occurrences stay system-scope,
and the cron function enumerates tenants in application code, scheduling one tenant-scoped time job
per tenant. The framework never enumerates tenants.
