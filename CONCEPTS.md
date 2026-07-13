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

A **message type** carries exactly one **Marker** (`IEvent` or `ICommand`), which fixes its **Lane**.
The lane selects the publish surface (`IBus` for events, `IQueue` for commands). **Delivery mode** is
orthogonal to lane: it decides whether a publish goes through storage or straight to transport.

### Lane
The semantic channel of a message: bus (broadcast — every subscriber group gets a copy) or queue
(point-to-point — competing consumers, one worker per message). Under the type-intent model a type
belongs to exactly one lane, fixed by its Marker. *Avoid:* "intent" in new writing — intent was the
late-bound era's name for the per-registration/per-call lane choice.

### Type-intent model
The decided messaging model (2026-06-10): the message type determines the lane via Markers enforced
by generic constraints (`where T : IEvent` / `where T : ICommand`); one type → one lane; cross-lane
use is a compile error. Supersedes Late-bound intent.

### Late-bound intent
The superseded model where the lane was chosen per registration (`OnBus`/`OnQueue`) and per publish
call, independent of the message type — the same type could traverse both lanes. Root cause of the
#344 cross-intent leak.

### Marker
One of the empty contract interfaces `IEvent`/`ICommand` (both extending a common `IMessage`) whose
presence classifies a message type. A type is valid when assignable to exactly one of the two after
walking its full base-type and interface graph — derived aliases (`IDomainEvent : IEvent`) are
allowed; a graph reaching both markers is a boot error. Unowned external types are wrapped in owned
marker-bearing records — never convention-classified (no predicate/attribute/builder classification
exists).

### Delivery mode
The per-call durability choice on publish/enqueue: `Auto`, `Durable`, `TransportDirect`. Auto, the
default, follows the framework transaction accessor (the only source of ambient durability —
`Transaction.Current` alone does not count): recognized transaction present → outbox (row persisted
in that transaction, dispatched post-commit); absent → direct to transport. Durable forces
store-first regardless of transaction state. TransportDirect bypasses storage even inside a
transaction — an explicit, diagnostically-logged escape from atomicity. `Delay` requires storage:
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
