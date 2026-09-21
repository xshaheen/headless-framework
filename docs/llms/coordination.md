---
domain: Coordination
packages: Coordination.Abstractions, Coordination.Core, Coordination.Core.Database, Coordination.PostgreSql, Coordination.Redis, Coordination.SqlServer
---

# Coordination

> Store-authoritative node membership and liveness for distributed consumers that need stable `node@incarnation` identity and lifecycle observations.

## Orientation

Use Coordination when a distributed consumer needs to know which process incarnation is alive. It supplies `INodeMembership` for register, heartbeat, leave, live-node reads, full liveness snapshots, and lifecycle events. Consumers stamp `NodeIdentity` (`node@incarnation`) on their own rows; Coordination does not store ownership.

Concrete consumer: the `Headless.Jobs` durable (operational-store) path resolves `INodeMembership` to stamp `node@incarnation` ownership on job rows, drives dead-node recovery from `NodeLeft` events plus a periodic reconcile, and requires a registered provider (see `docs/llms/jobs.md`).

The store is the temporal authority. PostgreSQL uses `clock_timestamp()`, SQL Server uses `SYSUTCDATETIME()`, and Redis uses `TIME` inside Lua. Application clocks do not classify another node as Alive, Suspected, or Dead.

## Agent Rules

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

### API and behavior

- `NodeIdentity`, `NodeId`, and `NodeIncarnation`.
- `INodeMembership` for register, heartbeat, leave, live reads, snapshot reads, and event watch.
- Heartbeats are incarnation-fenced: an incarnation that is dead, gracefully left, or pruned is terminal and cannot be revived; the process must register a higher incarnation.
- `NodeJoined`, `NodeSuspected`, `NodeRecovered`, `NodeLeft`, and `LocalMembershipLost` event contracts.
- `IMembershipEventSource.WatchAsync(...)` for lifecycle events.
- `IDeadOwnerReclaimer` — the per-domain reclaim sink driven by the shared dead-owner recovery bridge (carries `ReconcileInterval` and `ReclaimAsync(owners, ct)`, where `owners` is a single owner from the event path or the whole dead set from a reconcile tick so a consumer can collapse the reclaim into one batched write); implemented by each consumer (Jobs, Messaging).
- `CoordinationOptions` for thresholds, cluster name, node id, role, metadata, and membership-loss behavior.

### Design constraints

The abstraction is a liveness substrate, not an ownership store. Consumers own their domain rows and stamp `NodeIdentity`.

`HeartbeatAsync()` returns `false` when the local incarnation has been superseded or is terminal. Callers must stop ownership-sensitive work and re-register rather than attempting to resurrect that identity.

### Install

```bash
dotnet add package Headless.Coordination.Abstractions
```

### Setup and use

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

### Runtime behavior

None.

---

## Headless.Coordination.Core

### API and behavior

- An internal membership service implements `INodeMembership` (consumers resolve `INodeMembership`).
- Background heartbeat service derives lifecycle events from authoritative snapshots, leaves gracefully on host shutdown under a bounded timeout, and stops beating once local membership is lost.
- Bounded per-subscriber event channels isolate slow consumers from heartbeats.
- Default node-id provider resolves configured id, Kubernetes pod identity, hostname, then generated id.

### Design constraints

`RegisterAsync` durably establishes both the cold descriptor and an initial store-clock liveness entry in one guarded write, so a node is `Alive` (and its role/metadata are visible) immediately after register — without waiting for the first heartbeat. The background loop owns every subsequent beat. Registration is incarnation-guarded: a stale or superseded incarnation establishes no liveness.

Self-heartbeat rejection is a local fencing failure. A heartbeat write is deadline-bounded by the remaining `DeadThreshold` budget, so a continuously failing or hung store call self-fences the node once no write has been confirmed for that threshold; snapshot-read failures do not fence a node whose heartbeats still succeed. The default `MembershipLostBehavior.StopApplication` asks the host to stop; `StopMembershipOnly` is for hosts that explicitly quiesce every worker.

An incarnation is terminal once it leaves, reaches `DeadThreshold`, or its retained liveness entry is pruned. PostgreSQL, SQL Server, and Redis reject later heartbeats for that same incarnation; recovery requires allocating and registering a higher incarnation, so a delayed process cannot resurrect its old ownership identity.

Core also hosts the shared dead-owner recovery bridge — a generic `BackgroundService` parameterized by an `IDeadOwnerReclaimer` that reclaims dead-incarnation resources on `NodeLeft` events plus a periodic `Dead`-only snapshot reconcile (idempotent dedup, `CancellationToken.None` writes). It is internal infrastructure consumed by registering a closed generic from the owning assembly (Jobs, Messaging) via `InternalsVisibleTo`; each closed type yields a distinct hosted service and logger category. Coordination.Core does not register it — the consuming feature does.

### Install

```bash
dotnet add package Headless.Coordination.Core
```

### Setup and use

```csharp
services.AddCoordinationCore<MyMembershipStore>(options =>
{
    options.ClusterName = "orders";
});
```

Applications normally use a provider package and call `AddHeadlessCoordination(setup => setup.Use...)` (which returns the `IServiceCollection`); `AddCoordinationCore<TStore>` is the lower-level hook for provider authors and custom stores.

### Configuration

Set `HeartbeatInterval < SuspicionThreshold < DeadThreshold`; `DeadThreshold` must be at least three heartbeat intervals (a single missed or slow beat must not kill the node), and `DeadRetentionWindow` must be at least two heartbeat intervals.

Storage naming is a separate, feature-owned options type. `CoordinationStorageOptions.Schema` (default `"coordination"`) names the database schema that holds the membership tables, and it is configured through the setup builder rather than through any one provider:

```csharp
services.AddHeadlessCoordination(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "cluster_meta");
    setup.UsePostgreSql(connectionString); // or UseSqlServer(...)
});
```

`setup.ConfigureStorage(configuration)` binds the same options from configuration — pass the `Headless:Coordination:Storage` section to bind `Headless:Coordination:Storage:Schema`.

Coordination owns the setting so the membership tables land in the same schema whichever relational provider backs them; the provider package contributes only the dialect rules the schema is validated against on startup (PostgreSQL's 63-character unquoted-identifier rules, SQL Server's 128-character regular-identifier rules). Redis ignores the option entirely. The trade-off: a schema name valid on one provider can be rejected on the other, and that failure surfaces at startup rather than at first write.

### Runtime behavior

Registers `TimeProvider.System`, framework GUID generator defaults, `IHostIdentityAccessor` (from `Headless.Core`), `INodeIdProvider`, `INodeMembership`, `IMembershipEventSource`, and the heartbeat hosted service. The default `INodeIdProvider` returns `CoordinationOptions.ConfiguredNodeId` when set and otherwise `IHostIdentityAccessor.HostName`, so the node a membership store sees is the same host name that stamps message origins and logs; the discovery order (`POD_NAMESPACE/POD_NAME`, machine name, generated) is documented in [core.md](core.md).

---

## Headless.Coordination.Core.Database

### API and behavior

- Base store algorithm hooks for cluster-scoped relational providers.
- Provider-owned physical identifiers: PostgreSQL uses snake_case; SQL Server uses PascalCase.
- Initializer contract for provider-specific race-safe DDL.

### Design constraints

Provider SQL and physical identifiers remain in the native packages. This package centralizes operation order without forcing PostgreSQL and SQL Server into one naming convention.

### Install

```bash
dotnet add package Headless.Coordination.Core.Database
```

### Setup and use

This package is used by provider packages; applications normally install PostgreSQL or SQL Server providers directly.

### Configuration

None.

### Runtime behavior

None.

---

## Headless.Coordination.PostgreSql

### API and behavior

- Atomic incarnation allocation with `INSERT ... ON CONFLICT ... RETURNING`.
- Heartbeat guard rejects stale, impossible, dead, gracefully left, and pruned incarnations.
- Liveness classification uses `clock_timestamp()`.
- DDL initialization uses PostgreSQL advisory locks.

### Design constraints

Use `clock_timestamp()`, not transaction-start time, for liveness. Operational reads join the generation table so superseded incarnations are not live candidates.

### Install

```bash
dotnet add package Headless.Coordination.PostgreSql
```

### Setup and use

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

The schema is not a provider option: set it with `setup.ConfigureStorage(storage => storage.Schema = "…")` (default `"coordination"`). The initializer creates that schema when absent and every statement names its tables as `"schema"."table"`, so the provider no longer depends on `search_path`. The provider validates the schema against PostgreSQL's unquoted-identifier rules at startup.

### Runtime behavior

Registers the core membership services, PostgreSQL membership store, storage initializer, and initializer hosted service. Creates snake_case tables and columns. Requires PostgreSQL DDL permission when initialization runs on startup.

---

## Headless.Coordination.Redis

### API and behavior

- Incarnation allocation uses persistent `INCR` counters.
- Heartbeat/read/leave/cleanup scripts use Redis `TIME`.
- Heartbeats reject dead, gracefully left, and missing/pruned member payloads for the same incarnation.
- Leave is a guarded no-op for an absent, pruned, or superseded-and-swept identity — it never materializes a member payload.
- `:known` retains recently dead members so Dead is observable before cleanup.
- `:known` also mirrors current node generations so snapshot reads do not issue one `GET` per member.
- Generation counters are not purged by default.

### Design constraints

Redis keys use a cluster hash tag around `ClusterName`. The durable `:gen:<node-id>` counters carry no TTL, so an `allkeys-*` `maxmemory-policy` can evict a live node's counter under memory pressure. The next heartbeat then fails the generation guard, the node treats its own membership as lost, and under the default `MembershipLostBehavior.StopApplication` the host is asked to stop — a silent eviction surfaces as a spurious shutdown. Run coordination against a Redis instance or logical database configured with `noeviction` or a `volatile-*` policy; coordination keys carry no TTL, so `volatile-*` never evicts them.

**Generation mirrors in `:known` are read-path projections, not authority.** The durable per-node generation key remains the heartbeat guard. Allocation and heartbeat scripts mirror the current value into a reserved `:known` hash field named `__gen:<node-id>`, so read Lua can classify retained member payloads from one `HGETALL` result instead of calling `GET` for every member. Cleanup sweeps a mirror field once its node has no surviving member payload (orphan prune); the durable generation key is never touched, so a restarting node re-mirrors on its next allocate or heartbeat.

**Dead/Left retention divergence (intentional).** Redis retains Dead and Left descriptors in the `:known` hash for `RedisKnownNodeRetention` (default 7 days), so `GetLivenessSnapshotAsync` keeps surfacing them with `State = Dead` until that window elapses — consumers must filter by `NodeLivenessState`. The relational providers instead prune shortly after `DeadThreshold + DeadRetentionWindow` (tens of seconds). This is a documented behavioral difference, not a defaulting bug: lower `RedisKnownNodeRetention` to align Redis with relational pruning.

### Install

```bash
dotnet add package Headless.Coordination.Redis
```

### Setup and use

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

### Runtime behavior

Registers the core membership services, Redis membership store, keyed Lua script loader, script initializer hosted service, and cleanup hosted service. Requires an `IConnectionMultiplexer` registration.

---

## Headless.Coordination.SqlServer

### API and behavior

- Atomic incarnation allocation under `UPDLOCK, HOLDLOCK`.
- Heartbeat guard rejects stale, impossible, dead, gracefully left, and pruned incarnations.
- Liveness classification uses `SYSUTCDATETIME()`.
- Guarded membership writes retry SQL Server deadlock victim error `1205` with a bounded jittered Polly policy.
- DDL initialization uses `sp_getapplock`.

### Design constraints

The provider intentionally avoids `MERGE`. Explicit locking keeps the generation guard and liveness row update readable and testable.

Membership writes intentionally keep `SERIALIZABLE` transactions plus generation-first `UPDLOCK, HOLDLOCK` access. Under a large concurrent startup, SQL Server can still choose one writer as deadlock victim (`1205`); the provider retries the whole rolled-back transaction. This retry is SQL Server-specific and does not apply to PostgreSQL or Redis providers, whose membership write paths use different concurrency primitives.

### Install

```bash
dotnet add package Headless.Coordination.SqlServer
```

### Setup and use

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

Configure shared `CoordinationOptions` with `setup.Configure(...)`. Configure `ConnectionString`, `CommandTimeout`, and `InitializeOnStartup` with `setup.UseSqlServer(...)`.

The schema is not a provider option: set it with `setup.ConfigureStorage(storage => storage.Schema = "…")`. The default is the feature name `"coordination"`, not `dbo` — the initializer creates the schema when absent. The provider validates the schema against SQL Server's regular-identifier rules at startup.

### Runtime behavior

Registers the core membership services, SQL Server membership store, storage initializer, and initializer hosted service. Creates PascalCase tables and columns. Requires SQL Server DDL permission when initialization runs on startup.
