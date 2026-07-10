---
title: "Registration must durably establish liveness, not the first heartbeat (incarnation-guarded membership)"
date: 2026-06-07
category: docs/solutions/architecture-patterns
module: Headless.Coordination
problem_type: architecture_pattern
component: background_job
severity: high
applies_when:
  - building a node/worker/lease presence substrate where peers must see a member from the moment it registers
  - presence lives in a persistent store with a staleness/TTL threshold, not just in memory
  - an incarnation or generation counter disambiguates restarts of the same logical identity
  - the register path and the periodic-renewal (heartbeat) path are separate code paths
related_components:
  - database
  - service_class
tags:
  - coordination
  - membership
  - heartbeat
  - liveness
  - incarnation-guard
  - register
  - server-clock
  - conformance-test
---

# Registration must durably establish liveness, not the first heartbeat

## Context

`Headless.Coordination` identifies nodes as `nodeId@incarnation`, where the incarnation is a
monotonic generation bumped on every restart so a stale predecessor is a distinct identity. Each
node has two logical records: a **cold descriptor** (write-once: host/ports, role, metadata,
incarnation) and a **hot liveness row** (heartbeat timestamp, state). Liveness is the sole signal
that drives Alive/Suspected/Dead and therefore failover, and the **store is the single temporal
authority** — every timestamp comes from `clock_timestamp()` (Postgres), `SYSUTCDATETIME()`
(SqlServer), or `redis.call('TIME')` (Redis); no client clock ever enters a liveness decision.

The original design made `RegisterAsync` populate only an in-process descriptor cache and rely on
the **first heartbeat — issued internally by `RegisterAsync`** — to write the durable liveness row
(and, on Redis, the `:known` hash carrying role/metadata). Durable presence was a *side effect of
the heartbeat path*, not of registration.

## Guidance

**Make the registration write — not the first heartbeat — establish durable presence.**
`RegisterAsync` should `AllocateIncarnationAsync` (bump the generation, return the new incarnation)
then, in one atomic incarnation-guarded write, persist **both** the write-once descriptor **and** an
initial `Alive` liveness row. The heartbeat loop then owns only subsequent beats.

Four rules make this correct and portable across providers:

1. **Registration establishes presence; the loop only refreshes it.** If the establishing write and
   the periodic-refresh write are the same call, you cannot remove the redundant first beat without
   losing establishment, and provider-specific "durable only after the first beat" divergences creep
   in.
2. **The establishing write shares the heartbeat's incarnation-guard envelope.** Gate the descriptor
   and liveness writes on the freshly-allocated incarnation still being current in the generation
   table, so a concurrent re-register (e.g. a retry) that bumps the generation cannot let a stale
   incarnation establish phantom presence.
3. **Use the store server clock for the establishing liveness timestamp.** Reuse the exact
   server-clock expression heartbeat uses; a client `DateTimeOffset.UtcNow` at register would make
   expiry math inconsistent across nodes.
4. **Pin the invariant with a conformance test that asserts liveness before any loop tick.** A
   cross-provider test that registers and then reads the live set *without* calling `HeartbeatAsync`
   is the regression guard that fails the instant someone strips the liveness write from the
   register path.

## Why This Matters

Two code-review findings collided precisely on this coupling:

- **Finding A:** "Remove the redundant heartbeat `RegisterAsync` issues internally — the loop owns
  heartbeats."
- **Finding B:** "Redis role/metadata is invisible to other nodes until the first heartbeat —
  relational writes the descriptor at register, Redis does not."

Fixing A naively turned B from a latent gap into a real cross-node visibility hole, and broke the
conformance test `should_register_and_appear_in_live_set` because nothing else wrote the liveness
row at register. The wrong resolution is to keep the redundant beat. The right one is to make the
register write durable enough that the heartbeat loop is never needed to establish presence —
descriptor + initial liveness belong to **registration as a contract**, not to the heartbeat loop.

Without this, there is no clean moment to call a node "live": failover watchers, `NodeJoined`
events, and load-balancing all read the liveness snapshot and would get inconsistent results
depending on whether the loop had ticked yet.

## When to Apply

Revisit any presence/lease/actor-registry where you see "the first heartbeat is special" or
"we call `HeartbeatAsync` once inside `RegisterAsync`." That is the smell that the register path has
delegated liveness establishment to the renewal path. Applies whenever presence is durable,
TTL/staleness-gated, incarnation-disambiguated, and split across a register call and a background
renewal loop.

## Examples

**Before — register caches locally; heartbeat establishes liveness (anti-pattern):**

```csharp
await store.WriteDescriptorAsync(descriptor, ct);   // cold descriptor only; no liveness row
// ...background service then "activates" the node:
await membership.RegisterAsync(stoppingToken);
await membership.HeartbeatAsync(loopToken);          // node invisible to peers until HERE
```

**After — `UpsertDescriptorAsync` establishes both rows atomically; loop owns later beats:**

```csharp
// MembershipService.RegisterAsync
await store.UpsertDescriptorAsync(descriptor, ct);   // descriptor + guarded Alive liveness, one write
Identity = identity;                                  // visible in ReadLivenessAsync immediately
// MembershipHeartbeatBackgroundService loops on HeartbeatAsync only — no special first beat.
```

**Postgres — single CTE, `FOR UPDATE` on the generation row, gated on the allocated incarnation:**

```sql
WITH generation AS (
    SELECT current_incarnation FROM coordination.generation
    WHERE cluster_name = @ClusterName AND node_id = @NodeId
    FOR UPDATE
),
descriptor AS (
    INSERT INTO coordination.descriptor (..., date_created)
    SELECT ..., clock_timestamp() FROM generation
    WHERE current_incarnation = @Incarnation
    ON CONFLICT DO NOTHING            -- write-once: re-register is idempotent
)
INSERT INTO coordination.liveness (..., last_beat, left_at)
SELECT ..., clock_timestamp(), NULL FROM generation
WHERE current_incarnation = @Incarnation
ON CONFLICT (...) DO UPDATE SET last_beat = clock_timestamp(), left_at = NULL;
```

**SqlServer — `SERIALIZABLE` tx, `UPDLOCK, HOLDLOCK` read, early-return on stale incarnation:**

```sql
SELECT @currentIncarnation = [current_incarnation]
FROM coordination.generation WITH (UPDLOCK, HOLDLOCK)
WHERE cluster_name = @ClusterName AND node_id = @NodeId;

IF @currentIncarnation IS NULL OR @currentIncarnation <> @Incarnation RETURN;  -- no phantom rows

INSERT INTO coordination.descriptor (...) SELECT ..., SYSUTCDATETIME() WHERE NOT EXISTS (...);
UPDATE coordination.liveness WITH (UPDLOCK, HOLDLOCK)
   SET last_beat = SYSUTCDATETIME(), left_at = NULL
 WHERE cluster_name = @ClusterName AND node_id = @NodeId AND incarnation = @Incarnation;
IF @@ROWCOUNT = 0 INSERT INTO coordination.liveness (...) SELECT ..., SYSUTCDATETIME(), NULL WHERE NOT EXISTS (...);
```

**Redis — register runs the gen-guarded heartbeat Lua in creation mode (server `TIME`):**

```csharp
_descriptors[descriptor.Identity] = descriptor;                 // write-through cache for the hot path
_metadataJson[descriptor.Identity] = _SerializeDictionary(descriptor.Metadata);
var parameters = _CreateHeartbeatParams(descriptor.Identity, allowCreate: true);
await scriptsLoader.EvaluateAsync(Db, RedisMembershipHeartbeatScriptDefinition.Instance, parameters, ct);
```

Periodic heartbeats call the same script with `allowCreate: false`; a dead, gracefully left, or
pruned incarnation cannot recreate its liveness entry and must register a higher incarnation.

**Conformance test — the invariant pin (runs on every provider):**

```csharp
var identity = await Store.AllocateIncarnationAsync(nodeId, AbortToken);
await Store.UpsertDescriptorAsync(descriptor, AbortToken);       // register only; NO HeartbeatAsync
var snapshots = await Store.ReadLivenessAsync(AbortToken);
snapshots.Should().ContainSingle(s => s.Identity == identity && s.State == NodeLivenessState.Alive);
```

## Related

- [storage-initializer-lifecycle-correctness](../best-practices/storage-initializer-lifecycle-correctness.md) — the SqlServer `TRY/CATCH` envelope that *every* DDL block (including `CREATE INDEX`, error 1913) must carry; a sibling bug in this same PR violated it and was caught by the concurrent-startup conformance test.
- [terminal-state-overwrite-on-redelivery](../logic-errors/terminal-state-overwrite-on-redelivery-2026-05-16.md) — same class of "storage's rejection signal is a contract, not a hint" that the incarnation guard relies on; also the source of the `CancellationToken.None` for must-complete writes pattern.
- [unified-provider-setup-builder-pattern](./unified-provider-setup-builder-pattern.md) — the DI/builder grammar the coordination providers reuse.
- GitHub: PR #416 (where this was caught/fixed); follow-ups #417 (Redis O(N) snapshot GETs), #418 (single-node `IsAliveAsync` SPI), #419 (SqlServer `SERIALIZABLE` 1205 deadlock-retry).
