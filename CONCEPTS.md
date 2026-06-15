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
write so a stale run cannot resurrect or overwrite a newer one. *Avoid:* generation (use Incarnation
for the per-node value; "generation table/counter" names the durable authority that issues it).

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
to Alive) or → Dead/Left. Computed from how stale the Liveness row is against the store clock, using
store-evaluated thresholds — never by an application node comparing wall clocks.

### Store as temporal authority
The invariant that all liveness timestamps and all dead/suspected determinations come from the
store's own server clock and predicates. No application node compares another node's wall clock to
its own, and no failover decision is made from a stale (replica) read — only from the authoritative
write/primary path.

### Incarnation guard
The write-time check that a heartbeat, leave, or registration write only takes effect when its
incarnation is still the current generation for that node id, performed atomically with the write
(pessimistic row lock on the relational stores, compare-in-Lua on Redis). The mechanism that makes
the Store-as-temporal-authority invariant enforceable under concurrency.

### Authoritative provider
A coordination provider that can offer a server clock plus a linearizable liveness-row read/write,
and is therefore eligible to drive failover. A provider that cannot is degraded/unsupported for
failover even if it can serve approximate dashboard views.

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
