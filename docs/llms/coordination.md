---
domain: Coordination
packages: Coordination.Abstractions, Coordination.Core, Coordination.Core.Database, Coordination.PostgreSql, Coordination.Redis, Coordination.SqlServer
---

# Coordination

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Node Identity](#node-identity)
    - [Liveness States](#liveness-states)
    - [Operational Read Model](#operational-read-model)
    - [Events And Reconcile](#events-and-reconcile)
    - [Safety Ceiling](#safety-ceiling)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Coordination.Abstractions](#headlesscoordinationabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Coordination.Core](#headlesscoordinationcore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Coordination.Core.Database](#headlesscoordinationcoredatabase)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Coordination.PostgreSql](#headlesscoordinationpostgresql)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Design Notes](#design-notes-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Coordination.Redis](#headlesscoordinationredis)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Design Notes](#design-notes-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.Coordination.SqlServer](#headlesscoordinationsqlserver)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Design Notes](#design-notes-5)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)

> Store-authoritative node membership and liveness for distributed consumers that need stable `node@incarnation` identity and lifecycle observations.

## Quick Orientation

Use Coordination when a distributed consumer needs to know which process incarnation is alive. It supplies `INodeMembership` for register, heartbeat, leave, live-node reads, full liveness snapshots, and lifecycle events. Consumers stamp `NodeIdentity` (`node@incarnation`) on their own rows; Coordination does not store ownership.

Concrete consumer: the `Headless.Jobs` durable (operational-store) path resolves `INodeMembership` to stamp `node@incarnation` ownership on job rows, drives dead-node recovery from `NodeLeft` events plus a periodic reconcile, and requires a registered provider (see `docs/llms/jobs.md`).

The store is the temporal authority. PostgreSQL uses `clock_timestamp()`, SQL Server uses `SYSUTCDATETIME()`, and Redis uses `TIME` inside Lua. Application clocks do not classify another node as Alive, Suspected, or Dead.

## Agent Instructions

- Depend on `Headless.Coordination.Abstractions` from application code and add exactly one provider package through `AddHeadlessCoordination(setup => setup.Use...)`.
- Treat `NodeLeft` as an optimization trigger. Consumers must also periodically reconcile rows whose owner identity is not in `GetLiveNodesAsync()`.
- Recovery updates must be idempotent and guarded by owner identity plus non-terminal state.
- Do not use Coordination as Raft, Paxos, RedLock, leader election, or a generic ownership ledger.
- Choose stable node ids deliberately. Kubernetes StatefulSet ordinal names are the strongest default; Deployment pod name plus namespace is stable for the pod lifetime; generated ids are local/dev only.
- Keep `MembershipLostBehavior.StopApplication` unless every ownership-sensitive worker observes `LocalMembershipLostToken`.

## Core Concepts

### Node Identity

`NodeIdentity` is `NodeId + NodeIncarnation` and formats as `node@incarnation`. The store allocates incarnation monotonically per node id. A restarted process with the same node id becomes a different identity, so consumers can reclaim `node@1` work without touching `node@2`.

The per-node generation counter is never purged by any provider — purging it would let a returning node reuse an incarnation and defeat stale-owner detection — so every distinct node id ever registered leaves one permanent generation row (relational) or `:gen:<node-id>` key (Redis). Keep node-id cardinality bounded: prefer stable ids (StatefulSet ordinals, configured ids, or pod name plus namespace) and avoid the generated-`{guid}` fallback in long-lived deployments, where every process start mints a new immortal entry and the generation keyspace grows without bound.

### Liveness States

`Alive` means the store-clock heartbeat age is below `SuspicionThreshold`. `Suspected` is a soft signal between `SuspicionThreshold` and `DeadThreshold`. `Dead` means the hard threshold passed or the node left gracefully. `GetLiveNodesAsync()` returns Alive identities only; `GetLivenessSnapshotAsync()` returns full state. `IsAliveAsync(identity)` checks a single node — it is a targeted O(1) read (one guarded single-row query / small Lua), not a full cluster snapshot, so per-request liveness checks do not scale with cluster size. It returns the same answer the snapshot would for that identity: current-generation-only, store-clock classified, and retention-bounded (a node at or past the retention window reads as absent, i.e. not alive).

Provider SPI note: a custom `IMembershipStore` must implement `ReadNodeLivenessAsync(identity)` returning `NodeLivenessState?` — the targeted, read-only (no prune, no backfill) counterpart to `ReadLivenessAsync`, where `null` means the identity is absent from the current-generation view.

### Operational Read Model

Providers may retain historical incarnations long enough for Dead visibility, event derivation, and diagnostics. Operational reads are current-generation only: `GetLiveNodesAsync()` and the normal recovery snapshot must not treat retained `node@old-incarnation` rows as ownership candidates.

### Events And Reconcile

Events are best-effort local observations from snapshots: `NodeJoined`, `NodeSuspected`, `NodeRecovered`, and `NodeLeft`. A missed event must not block recovery. Consumers reconcile periodically against live identities and recover stale owner rows themselves.

### Safety Ceiling

Coordination is fencing-safe, fail-stop, and fail-closed when backed by an authoritative provider. It is not consensus-safe. It does not provide split-brain-proof leadership or cross-region linearizability.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Coordination.PostgreSql` | Membership should follow a PostgreSQL primary and server clock. | The deployment cannot use primary/write-path reads for failover. | Native SQL, `clock_timestamp()`, Testcontainers conformance. |
| `Headless.Coordination.SqlServer` | Membership should follow SQL Server and `SYSUTCDATETIME()`. | The app cannot grant DDL/init permissions or use primary reads. | Guarded update/insert, no `MERGE`. |
| `Headless.Coordination.Redis` | Redis is the authoritative coordination store. | Redis eviction can delete generation counters or failover reads may hit stale replicas. | Lua scripts use `TIME`; generation counters are not purged by default. |

## Headless.Coordination.Abstractions

### Problem Solved

Defines the public coordination contract: node identity, liveness snapshots, membership operations, events, options, and exceptions.

### Key Features

- `NodeIdentity`, `NodeId`, and `NodeIncarnation`.
- `INodeMembership` for register, heartbeat, leave, live reads, snapshot reads, and event watch.
- `NodeJoined`, `NodeSuspected`, `NodeRecovered`, `NodeLeft`, and `LocalMembershipLost` event contracts.
- `IMembershipEventSource.WatchAsync(...)` for lifecycle events.
- `IDeadOwnerReclaimer` — the per-domain reclaim sink driven by the shared dead-owner recovery bridge (carries `ReconcileInterval` and `ReclaimAsync(owners, ct)`, where `owners` is a single owner from the event path or the whole dead set from a reconcile tick so a consumer can collapse the reclaim into one batched write); implemented by each consumer (Jobs, Messaging).
- `CoordinationOptions` for thresholds, cluster name, node id, role, metadata, and membership-loss behavior.

### Design Notes

The abstraction is a liveness substrate, not an ownership store. Consumers own their domain rows and stamp `NodeIdentity`.

### Installation

```bash
dotnet add package Headless.Coordination.Abstractions
```

### Quick Start

```csharp
public sealed class Worker(INodeMembership membership)
{
    public async Task StartAsync(CancellationToken ct)
    {
        var identity = await membership.RegisterAsync(ct);
        // Stamp identity on work rows owned by this process.
    }
}
```

### Configuration

Configure through provider setup plus `CoordinationOptions`.

### Dependencies

- `Headless.Checks`
- `FluentValidation`

### Side Effects

None.

---

## Headless.Coordination.Core

### Problem Solved

Implements the provider-agnostic membership engine over an `IMembershipStore`.

### Key Features

- An internal membership service implements `INodeMembership` (consumers resolve `INodeMembership`).
- Background heartbeat service derives lifecycle events from authoritative snapshots, leaves gracefully on host shutdown under a bounded timeout, and stops beating once local membership is lost.
- Bounded per-subscriber event channels isolate slow consumers from heartbeats.
- Default node-id provider resolves configured id, Kubernetes pod identity, hostname, then generated id.

### Design Notes

`RegisterAsync` durably establishes both the cold descriptor and an initial store-clock liveness entry in one guarded write, so a node is `Alive` (and its role/metadata are visible) immediately after register — without waiting for the first heartbeat. The background loop owns every subsequent beat. Registration is incarnation-guarded: a stale or superseded incarnation establishes no liveness.

Self-heartbeat rejection is a local fencing failure. The default `MembershipLostBehavior.StopApplication` asks the host to stop; `StopMembershipOnly` is for hosts that explicitly quiesce every worker.

Core also hosts the shared dead-owner recovery bridge — a generic `BackgroundService` parameterized by an `IDeadOwnerReclaimer` that reclaims dead-incarnation resources on `NodeLeft` events plus a periodic `Dead`-only snapshot reconcile (idempotent dedup, `CancellationToken.None` writes). It is internal infrastructure consumed by registering a closed generic from the owning assembly (Jobs, Messaging) via `InternalsVisibleTo`; each closed type yields a distinct hosted service and logger category. Coordination.Core does not register it — the consuming feature does.

### Installation

```bash
dotnet add package Headless.Coordination.Core
```

### Quick Start

```csharp
services.AddCoordinationCore<MyMembershipStore>(options =>
{
    options.ClusterName = "orders";
});
```

Applications normally use a provider package and call `AddHeadlessCoordination(setup => setup.Use...)` (which returns the `IServiceCollection`); `AddCoordinationCore<TStore>` is the lower-level hook for provider authors and custom stores.

### Configuration

Set `HeartbeatInterval < SuspicionThreshold < DeadThreshold`; `DeadRetentionWindow` must be at least two heartbeat intervals.

### Dependencies

- `Headless.Coordination.Abstractions`
- `Headless.Checks`
- `Headless.Core`
- `Headless.Extensions`
- `Headless.Hosting`
- `Microsoft.Extensions.Configuration.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

Registers `TimeProvider.System`, framework GUID generator defaults, `INodeIdProvider`, `INodeMembership`, `IMembershipEventSource`, and the heartbeat hosted service.

---

## Headless.Coordination.Core.Database

### Problem Solved

Provides the shared relational substrate used by native SQL providers.

### Key Features

- Base store algorithm hooks for cluster-scoped relational providers.
- Provider-owned physical identifiers: PostgreSQL uses snake_case; SQL Server uses PascalCase.
- Initializer contract for provider-specific race-safe DDL.

### Design Notes

Provider SQL and physical identifiers remain in the native packages. This package centralizes operation order without forcing PostgreSQL and SQL Server into one naming convention.

### Installation

```bash
dotnet add package Headless.Coordination.Core.Database
```

### Quick Start

This package is used by provider packages; applications normally install PostgreSQL or SQL Server providers directly.

### Configuration

None.

### Dependencies

- `Headless.Coordination.Core`
- `Headless.Hosting`

### Side Effects

None.

---

## Headless.Coordination.PostgreSql

### Problem Solved

Stores membership in PostgreSQL with the primary connection and server statement time.

### Key Features

- Atomic incarnation allocation with `INSERT ... ON CONFLICT ... RETURNING`.
- Heartbeat guard rejects stale or impossible incarnations.
- Liveness classification uses `clock_timestamp()`.
- DDL initialization uses PostgreSQL advisory locks.

### Design Notes

Use `clock_timestamp()`, not transaction-start time, for liveness. Operational reads join the generation table so superseded incarnations are not live candidates.

### Installation

```bash
dotnet add package Headless.Coordination.PostgreSql
```

### Quick Start

```csharp
services.AddHeadlessCoordination(setup =>
{
    setup.Configure(options =>
    {
        options.ClusterName = "orders";
        options.ConfiguredNodeId = "orders-worker-0";
    });

    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = connectionString;
    });
});
```

### Configuration

Configure shared `CoordinationOptions` with `setup.Configure(...)`. Configure `PostgreSqlCoordinationOptions.ConnectionString`, optional `DataSource`, `CommandTimeout`, and `InitializeOnStartup` with `setup.UsePostgreSql(...)`.

### Dependencies

- `Headless.Coordination.Core.Database`
- `Headless.Hosting`
- `Npgsql`

### Side Effects

Registers the core membership services, PostgreSQL membership store, storage initializer, and initializer hosted service. Creates snake_case tables and columns. Requires PostgreSQL DDL permission when initialization runs on startup.

---

## Headless.Coordination.Redis

### Problem Solved

Stores membership in Redis using Lua scripts and Redis server time.

### Key Features

- Incarnation allocation uses persistent `INCR` counters.
- Heartbeat/read/leave/cleanup scripts use Redis `TIME`.
- `:known` retains recently dead members so Dead is observable before cleanup.
- `:known` also mirrors current node generations so snapshot reads do not issue one `GET` per member.
- Generation counters are not purged by default.

### Design Notes

Redis keys use a cluster hash tag around `ClusterName`. The durable `:gen:<node-id>` counters carry no TTL, so an `allkeys-*` `maxmemory-policy` can evict a live node's counter under memory pressure. The next heartbeat then fails the generation guard, the node treats its own membership as lost, and under the default `MembershipLostBehavior.StopApplication` the host is asked to stop — a silent eviction surfaces as a spurious shutdown. Run coordination against a Redis instance or logical database configured with `noeviction` or a `volatile-*` policy; coordination keys carry no TTL, so `volatile-*` never evicts them.

**Generation mirrors in `:known` are read-path projections, not authority.** The durable per-node generation key remains the heartbeat guard. Allocation and heartbeat scripts mirror the current value into a reserved `:known` hash field named `__gen:<node-id>`, so read Lua can classify retained member payloads from one `HGETALL` result instead of calling `GET` for every member. Cleanup sweeps a mirror field once its node has no surviving member payload (orphan prune); the durable generation key is never touched, so a restarting node re-mirrors on its next allocate or heartbeat.

**Dead/Left retention divergence (intentional, plan KTD-16).** Redis retains Dead and Left descriptors in the `:known` hash for `RedisKnownNodeRetention` (default 7 days), so `GetLivenessSnapshotAsync` keeps surfacing them with `State = Dead` until that window elapses — consumers must filter by `NodeLivenessState`. The relational providers instead prune shortly after `DeadThreshold + DeadRetentionWindow` (tens of seconds). This is a documented behavioral difference, not a defaulting bug: lower `RedisKnownNodeRetention` to align Redis with relational pruning.

### Installation

```bash
dotnet add package Headless.Coordination.Redis
```

### Quick Start

```csharp
services.AddSingleton<IConnectionMultiplexer>(multiplexer);

services.AddHeadlessCoordination(setup =>
{
    setup.Configure(options =>
    {
        options.ClusterName = "orders";
        options.ConfiguredNodeId = "orders-worker-0";
    });

    setup.UseRedis(options =>
    {
        options.RedisCleanupInterval = TimeSpan.FromMinutes(5);
    });
});
```

### Configuration

Configure shared `CoordinationOptions` with `setup.Configure(...)`. Configure `RedisCleanupInterval` and `RedisKnownNodeRetention` with `setup.UseRedis(...)`. `RedisKnownNodeRetention` is treated as at least `DeadThreshold + DeadRetentionWindow`.

### Dependencies

- `Headless.Coordination.Core`
- `Headless.Hosting`
- `Headless.Redis`
- `StackExchange.Redis`

### Side Effects

Registers the core membership services, Redis membership store, keyed Lua script loader, script initializer hosted service, and cleanup hosted service. Requires an `IConnectionMultiplexer` registration.

---

## Headless.Coordination.SqlServer

### Problem Solved

Stores membership in SQL Server with guarded update/insert statements and server UTC time.

### Key Features

- Atomic incarnation allocation under `UPDLOCK, HOLDLOCK`.
- Heartbeat guard rejects stale or impossible incarnations.
- Liveness classification uses `SYSUTCDATETIME()`.
- Guarded membership writes retry SQL Server deadlock victim error `1205` with a bounded jittered Polly policy.
- DDL initialization uses `sp_getapplock`.

### Design Notes

The provider intentionally avoids `MERGE`. Explicit locking keeps the generation guard and liveness row update readable and testable.

Membership writes intentionally keep `SERIALIZABLE` transactions plus generation-first `UPDLOCK, HOLDLOCK` access. Under a large concurrent startup, SQL Server can still choose one writer as deadlock victim (`1205`); the provider retries the whole rolled-back transaction. This retry is SQL Server-specific and does not apply to PostgreSQL or Redis providers, whose membership write paths use different concurrency primitives.

### Installation

```bash
dotnet add package Headless.Coordination.SqlServer
```

### Quick Start

```csharp
services.AddHeadlessCoordination(setup =>
{
    setup.Configure(options =>
    {
        options.ClusterName = "orders";
        options.ConfiguredNodeId = "orders-worker-0";
    });

    setup.UseSqlServer(options =>
    {
        options.ConnectionString = connectionString;
    });
});
```

### Configuration

Configure shared `CoordinationOptions` with `setup.Configure(...)`. Configure `ConnectionString`, `Schema` (`dbo` by default), `CommandTimeout`, and `InitializeOnStartup` with `setup.UseSqlServer(...)`.

### Dependencies

- `Headless.Coordination.Core.Database`
- `Headless.Hosting`
- `Microsoft.Data.SqlClient`
- `Polly.Core`

### Side Effects

Registers the core membership services, SQL Server membership store, storage initializer, and initializer hosted service. Creates PascalCase tables and columns. Requires SQL Server DDL permission when initialization runs on startup.
