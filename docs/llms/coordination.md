---
domain: Coordination
packages: Coordination.Abstractions, Coordination, Coordination.Sql, Coordination.PostgreSql, Coordination.Redis, Coordination.SqlServer, Coordination.Sqlite, Cluster.Dashboard
---

# Coordination

> Store-authoritative node membership and liveness for distributed consumers that need stable `node@incarnation` identity, cluster-qualified owner tags, and lifecycle observations.

## Orientation

Use Coordination when a distributed consumer needs to know which process incarnation is alive. It supplies `INodeMembership` for register, heartbeat, leave, live-node reads, full liveness snapshots, and lifecycle events. Consumers stamp the cluster-qualified owner tag (`NodeOwnerTag`, `cluster/node@incarnation`) on their own rows. Coordination does not store ownership.

`Headless.Jobs` on its durable store is the main consumer: it stamps owner tags on job rows, recovers a dead node's jobs from `NodeLeft` events plus a periodic reconcile, and requires a registered provider (see [jobs.md](jobs.md)).

The store is the clock. PostgreSQL uses `clock_timestamp()`, SQL Server uses `SYSUTCDATETIME()`, SQLite uses the clock of the host that holds the file, and Redis uses `TIME` inside Lua. Application clocks never classify another node as Alive, Suspected, or Dead.

## Agent Rules

- Depend on `Headless.Coordination.Abstractions` from application code. Register exactly one provider through `AddHeadlessCoordination(setup => setup.Use...)`.
- Treat `NodeLeft` as an optimization trigger. Also reconcile periodically: recover rows whose owner identity is not in `GetLiveNodesAsync()`.
- Read `IMembershipEventLog.GetEntries()` for display and diagnostics only. It is not a recovery path: entries are evicted and lost on restart, so recovery reconciles from `GetLivenessSnapshotAsync()`.
- Make recovery updates idempotent, and guard them by owner identity plus a non-terminal state.
- Do not use Coordination as Raft, Paxos, RedLock, leader election, or a generic ownership ledger.
- Choose stable node ids. Kubernetes StatefulSet ordinal names are the strongest default. A Deployment pod name plus namespace is stable for the pod lifetime. Generated ids are for local development only: every process start leaves one permanent generation entry, so generated ids grow the store without bound.
- Keep `MembershipLostBehavior.StopApplication` unless every ownership-sensitive worker observes `LocalMembershipLostToken`.
- Give each Jobs store its own `ClusterName`. Jobs publishes node metadata that gates cron rebases across every node in a cluster; see [Node Metadata](#node-metadata).
- When environments share one Redis, give each host a `Headless:Prefix`. The Redis provider puts it in front of `CoordinationOptions.KeyPrefix` (`wt1:coordination:{cluster}:known`). Relational providers ignore the key prefix and separate clusters by `ClusterName`. Hosts that share a Jobs database must share the prefix, and changing it on such a fleet needs a full stop, not a rolling deploy. See [Isolation prefix](utilities.md#isolation-prefix).

## How this differs from locks and leases

Membership answers "which node incarnations are alive?". It reports liveness only and never grants ownership. To decide who may run or who owns work, use a [distributed lock](distributed-locks.md), a [fenced lease](fencing.md), or a lease on your own work row stamped with the node's owner tag, as Jobs does. See [Choosing a coordination primitive](fencing.md#choosing-a-coordination-primitive).

## Core Concepts

### Node Identity

`NodeIdentity` is `NodeId + NodeIncarnation` and formats as `node@incarnation`. The store allocates incarnations strictly increasing per node id, but not as a dense sequence (Redis seeds them from its server clock). A restarted process with the same node id gets a different identity, so a consumer can reclaim `node@1` work without touching `node@2`.

A `NodeId` must be text every store keeps unchanged: surrounding white space, a NUL character, or an unpaired UTF-16 surrogate throws `ArgumentException`. A `NodeId` is at most `NodeId.MaxLength` (256) characters, and `ClusterName` at most `CoordinationOptions.ClusterNameMaxLength` (128).

An identity is unique only inside its cluster. Membership, liveness snapshots, and `NodeLeft` events are all per `ClusterName`, so two services under different clusters can hold the same `node@incarnation` at once. Stamp the owner tag `NodeOwnerTag.Format(clusterName, identity)` (`cluster/node@incarnation`; `membership.GetOwnerTag()` for the local node) on rows in stores that several clusters can share. Limit any decision taken from a snapshot to tags of the snapshot's cluster with `NodeOwnerTag.IsInCluster`. Size a column that stores owner tags to at least `NodeOwnerTag.MaxLength` (405) characters.

No provider purges the per-node generation counter, because a purged counter would let a returning node reuse an incarnation. Every distinct node id ever registered leaves one permanent generation row (relational) or `:gen:<node-id>` key (Redis). Keep node-id cardinality bounded.

### Liveness States

- `Alive`: the store-clock heartbeat age is below `SuspicionThreshold`.
- `Suspected`: a soft signal between `SuspicionThreshold` and `DeadThreshold`.
- `Dead`: the hard threshold passed, or the node left gracefully.

`GetLiveNodesAsync()` returns `Alive` identities only. `GetLivenessSnapshotAsync()` returns the full state. `IsAliveAsync(identity)` checks one node with a single-row read, so a per-request check does not scale with cluster size, and it answers as the snapshot would for that identity.

Each `NodeLivenessSnapshot` carries `LastHeartbeatAt` (`DateTimeOffset`, UTC), the store-clock instant of the incarnation's latest accepted heartbeat or registration. Compare it with another store-clock instant, never with the application clock. After a graceful leave, the relational providers keep the last real beat, while Redis backdates it by one `DeadThreshold`.

Reads see only the current incarnation of each node. `GetLiveNodesAsync()` never offers a retained `node@old-incarnation` as an ownership candidate.

### Node Metadata

`CoordinationOptions.Metadata` is copied into the node descriptor once, at `RegisterAsync`, and every peer reads it back through `NodeLivenessSnapshot.Metadata`. It is write-once per incarnation: a value changed after registration reaches peers only when the process restarts and registers a new incarnation. A node with no descriptor row reports empty metadata.

- Name keys with a dotted prefix that owns them (`headless.<feature>.<name>`; application keys use their own prefix). Keys are stored exactly as written, case included.
- Never put secrets in metadata: every node in the cluster can read it.
- Metadata is serialized by the keyed `IJsonSerializer` under `CoordinationOptions.JsonSerializerServiceKey`, so registering other JSON options for the host never changes the stored format.

Jobs publishes `headless.jobs.cron-rules-digest` and `headless.jobs.fingerprint-sweep` from every node on its durable store. Those keys gate cron rebases across every node in a cluster, so give each Jobs store its own `ClusterName`.

### Events And Reconcile

Events are best-effort local observations derived from snapshots: `NodeJoined`, `NodeSuspected`, `NodeRecovered`, and `NodeLeft`, read through `IMembershipEventSource.WatchAsync(...)`. The same events, with the snapshot state behind each, stay readable for a while in the [membership event log](#membership-event-log). A missed event must not block recovery. Reconcile periodically against live identities and recover stale owner rows yourself, or implement `IDeadOwnerReclaimer` and let [Headless.Coordination](#headlesscoordination) drive it.

### Safety Ceiling

Coordination is fencing-safe, fail-stop, and fail-closed when backed by an authoritative provider. It is not consensus-safe. It does not provide split-brain-proof leadership or cross-region linearizability.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Coordination.PostgreSql` | Membership should follow a PostgreSQL primary and server clock. | Reads after a failover can reach a lagging replica. | Relational store; `clock_timestamp()`. |
| `Headless.Coordination.SqlServer` | Membership should follow SQL Server and `SYSUTCDATETIME()`. | The deploy identity cannot get DDL permission, or reads can reach a lagging replica. | Relational store; guarded update and insert, no `MERGE`. |
| `Headless.Coordination.Sqlite` | Every node runs on one host and shares a SQLite file, such as an embedded or edge deployment. | Nodes run on several hosts: SQLite locking does not work over a network file system. | Every write serializes on the database write lock, and the clock is the host's. |
| `Headless.Coordination.Redis` | Redis is the authoritative coordination store. | Redis evicts keys, runs without persistence, or fails over to replicas that miss writes. | Lua scripts use `TIME`; generation counters are never purged. |

---

## Headless.Coordination.Abstractions

### API and behavior

- `NodeIdentity`, `NodeId`, and `NodeIncarnation`.
- `NodeOwnerTag`: `Format(clusterName, identity)` builds the `cluster/node@incarnation` owner tag, `IsInCluster(tag, clusterName)` tests a stored tag against a cluster, `GetOwnerTag(this INodeMembership)` returns the local node's tag or `null` when it has no identity, and `MaxLength` sizes an owner column.
- `INodeMembership`: `RegisterAsync`, `HeartbeatAsync`, `LeaveAsync`, `IsAliveAsync`, `GetLiveNodesAsync`, `GetLivenessSnapshotAsync`, and event watch. `Identity` is the local identity or `null`. `ClusterName` names the cluster of every identity it reports. `LocalMembershipLostToken` fires when the local membership is lost.
- Heartbeats are incarnation-fenced. An incarnation that is dead, gracefully left, or pruned is terminal and cannot be revived; the process must register a higher incarnation.
- `NodeJoined`, `NodeSuspected`, `NodeRecovered`, `NodeLeft`, and `LocalMembershipLost` event contracts, and `IMembershipEventSource.WatchAsync(...)` to read them.
- `INodeDrainParticipant` (`Task WaitUntilDrainedAsync(CancellationToken)`): register it as a singleton to keep the node in membership during host stop until the work it owns has drained and written its outcome. The token is the host stop token, so return or throw `OperationCanceledException` promptly when it fires. Jobs registers one on hosts that claim work.
- `IDeadOwnerReclaimer`: the reclaim sink the dead-owner recovery bridge in `Headless.Coordination` drives. `TimeSpan? ReconcileInterval` is the reconcile cadence (`null` means half of `CoordinationOptions.DeadRetentionWindow`). `ReclaimAsync(owners, ct)` receives a single owner from the event path or the whole dead set from a reconcile tick, so you can reclaim in one batched write. `ct` is always `CancellationToken.None`. Jobs and Messaging implement it; register your own with `AddDeadOwnerReclaimer<TReclaimer>()`.
- `CoordinationOptions` for thresholds, cluster name, role, metadata, and membership-loss behavior. The node id is not an option here: it is `IHostIdentityAccessor.HostName` from `Headless.Context`.
- `NullNodeMembership`: a membership that reports no node leaving and no dead node.

### Design constraints

The abstraction is a liveness substrate, not an ownership store. Consumers own their domain rows and stamp the cluster-qualified owner tag.

`HeartbeatAsync()` returns `false` when the local incarnation has been superseded or is terminal, or when the node is past its self-fence deadline. Stop ownership-sensitive work when it does. A process whose membership was lost must restart: `RegisterAsync` throws `InvalidOperationException` once local membership is lost.

`RegisterAsync` throws `MembershipRegistrationRejectedException` (a `CoordinationException`) when the store refuses the registration because a newer incarnation of the same node id was allocated first: another process is registering with the same `NodeId`. `Identity` stays unset, and startup fails without a retry. Concurrent `RegisterAsync` calls in one process share one registration and get the same identity.

### Install

```bash
dotnet add package Headless.Coordination.Abstractions
```

### Setup and use

```csharp
public sealed class Worker(INodeMembership membership)
{
    public void Stamp(WorkItem item)
    {
        // The node registers at startup; the tag is null only before that, or after membership is lost.
        item.Owner = membership.GetOwnerTag() ?? throw new InvalidOperationException("Node is not a live member.");
    }
}
```

**Verify:** after the host starts, `membership.Identity` is not `null`, and `GetLiveNodesAsync()` contains it.

### Configuration

Configure through provider setup and `CoordinationOptions`.

### Runtime behavior

None.

---

## Headless.Coordination

### API and behavior

- Registers `INodeMembership`, `IMembershipEventSource`, `IMembershipEventLog`, `CoordinationClusterSummary`, `INodeIdProvider`, `IHostIdentityAccessor` (from `Headless.Context`), `TimeProvider.System`, and a heartbeat hosted service.
- Inspection types for operators and dashboards ship here, not in `Headless.Coordination.Abstractions`, because only a host that registers coordination has them:
  - `IMembershipEventLog.GetEntries()` and `MembershipEventLogEntry`: the last events this process published. See [Membership event log](#membership-event-log).
  - `NodeMetadataAgreement.Compare(snapshots, key)`, returning `NodeMetadataAgreementResult` and `NodeMetadataValueGroup`. See [Metadata agreement](#metadata-agreement).
  - `CoordinationClusterSummary`: the settings a node runs under. See [Cluster summary](#cluster-summary).
- **Start.** The heartbeat starts before any hosted service's `StartAsync`. The node registers once: Jobs registers it at its startup gate, and on hosts without Jobs the heartbeat service registers it. `RegisterAsync` writes the descriptor and an initial liveness entry together, so the node reads `Alive`, with its role and metadata visible, before the first heartbeat. A transient store failure during registration is retried, up to 5 attempts with a back-off from 200 ms doubling to at most 5 seconds; the last failure then fails startup, or under `StopMembershipOnly` is logged and the node stays unregistered. A registration the store refuses (`MembershipRegistrationRejectedException`) fails startup without a retry.
- **Stop order.** The heartbeat keeps beating through every hosted service's `StopAsync`. Then it awaits every `INodeDrainParticipant`, bounded by the host stop token, and only then stops beating and leaves. Registration order and `HostOptions.ServicesStopConcurrently` do not change this. If the stop token cuts the wait short, it logs once and leaves anyway. A participant that throws is logged and does not block the leave or the other participants. The leave has its own 5-second budget, so it still reaches the store after a cut-short wait. A node that left reads as dead to peers, which reclaim its rows.
- **Self-fence.** On a host that runs the heartbeat, the node fences itself at its last accepted beat (or its registration) plus `DeadThreshold` minus `SelfFenceMargin`, even when no beat runs. It stops before any peer can classify it `Dead` and reclaim its work. From that instant `Identity` reads `null`, so stamp sites stop handing out an owner, and a beat the store accepts after the deadline does not bring the identity back. A store call that keeps failing or hangs fences the node at the deadline. A snapshot-read failure does not fence a node whose heartbeats still succeed. `LeaveAsync` still writes the departure for a node past its deadline, so peers see `NodeLeft` instead of waiting out `DeadThreshold`.
- **Membership lost.** The default `MembershipLostBehavior.StopApplication` asks the host to stop. `StopMembershipOnly` is for hosts where every worker observes `LocalMembershipLostToken`.
- `setup.DisableMembershipHeartbeat()` skips the heartbeat service. Use it on test hosts and tools that must not join the cluster on their own. `INodeMembership` stays registered, so a test can call `RegisterAsync` and `HeartbeatAsync` itself. Such a host never self-fences, and peers read it as `Dead` once `DeadThreshold` passes, so production nodes keep the heartbeat. `AddHeadlessJobs` on the durable store refuses the switch unless Jobs background services are off. A feature package that needs the same guard calls `services.IsMembershipHeartbeatDisabled()` after `AddHeadlessCoordination`: it returns `true` only when the coordination membership is registered without its heartbeat service, and `false` when coordination is not registered, such as on a host that registers its own `INodeMembership` instead.
- **Clocks.** The registered `TimeProvider` paces beats, the dead-owner reconcile, and the self-fence, so a test advances a `FakeTimeProvider` to drive liveness. The registration retry back-off, the leave on shutdown, the event re-subscribe back-off, and the reconcile's read timeout (the reconcile interval, at least 5 seconds) wait real time, so a host with a `FakeTimeProvider` it never advances still registers after a transient store failure and still stops.
- A slow event subscriber never delays heartbeats.
- The default `INodeIdProvider` returns `IHostIdentityAccessor.HostName`, the same host name that stamps message origins and logs. To pin it, call `services.AddHeadlessHostIdentity(o => o.HostName = "orders-worker-0")` before any feature registration. The discovery order (`POD_NAMESPACE/POD_NAME`, machine name, generated) is in [context.md](context.md).

### Design constraints

An incarnation is terminal once it leaves, reaches `DeadThreshold`, or its retained liveness entry is pruned. Every provider rejects later heartbeats for that incarnation. Recovery requires registering a higher incarnation, so a delayed process cannot resurrect its old ownership identity.

**Dead-owner recovery.** `services.AddDeadOwnerReclaimer<TReclaimer>()` registers the reclaimer as a singleton and hosts a background service that calls it on `NodeLeft` events and on a periodic reconcile of `Dead` owners. A repeat call for the same `TReclaimer` adds nothing. Each reclaimer type gets its own hosted service and logger category, `Headless.Coordination.DeadOwnerRecoveryBridge<TReclaimer>`, and implements the public `IDeadOwnerRecoveryBridge` marker. Coordination registers no reclaimer itself. Under `NullNodeMembership` the service sees no node leave and no dead node and never calls the reclaimer.

The reconcile cadence is resolved once at start: `null` becomes half of `DeadRetentionWindow` (15 seconds with default options), because relational providers keep a dead identity observable for only that window. An explicit value above that ceiling is clamped to it, with one warning (EventId 14, `DeadNodeReconcileIntervalClamped`). Under `NullNodeMembership` an explicit value is used as set. To run your own sweep on the same cadence, call `DeadOwnerReconcileInterval.Resolve(configured, coordination)`, which logs nothing.

### Install

```bash
dotnet add package Headless.Coordination
```

### Setup and use

Install a provider package and call `AddHeadlessCoordination(setup => setup.Use...)`, which returns the `IServiceCollection`. A custom `IMembershipStore` plugs into the same call; see [For store authors](#for-store-authors).

```csharp
services.AddHeadlessCoordination(setup =>
{
    setup.Configure(options => options.ClusterName = "orders");
    setup.UsePostgreSql(); // the connection from services.AddPostgreSqlSql(connectionString)
});

// Release the leases a dead node held; called on NodeLeft and on each reconcile tick.
services.AddDeadOwnerReclaimer<LeaseReclaimer>();

public sealed class LeaseReclaimer(ILeaseStore leases) : IDeadOwnerReclaimer
{
    public TimeSpan? ReconcileInterval => null; // half of DeadRetentionWindow

    public Task ReclaimAsync(IReadOnlyCollection<string> owners, CancellationToken cancellationToken) =>
        leases.ReleaseOwnedByAsync(owners, cancellationToken);
}
```

**Verify:** invalid thresholds fail options validation at host start. After start, `GetLivenessSnapshotAsync()` reports the local identity as `Alive` with its `Metadata`.

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ClusterName` | `default` | At most 128 characters. Membership is per cluster. |
| `Role` | `null` | Published in the node descriptor. |
| `Metadata` | empty | Write-once per incarnation; see [Node Metadata](#node-metadata). |
| `HeartbeatInterval` | 5 seconds | |
| `SuspicionThreshold` | 15 seconds | |
| `DeadThreshold` | 30 seconds | At least three heartbeat intervals. |
| `DeadRetentionWindow` | 30 seconds | At least two heartbeat intervals. How long relational providers keep a dead identity observable. |
| `SelfFenceMargin` | `null` | `null` uses the smaller of 5 seconds and one sixth of `DeadThreshold`. |
| `MembershipLostBehavior` | `StopApplication` | |
| `KeyPrefix` | `coordination:` | Redis key prefix. |

Set `HeartbeatInterval < SuspicionThreshold < DeadThreshold`. Startup validation fails otherwise.

`SelfFenceMargin` is how long before `DeadThreshold` the node fences itself. The derived default is 5 seconds with the default 30-second threshold, and 200 milliseconds with a 1.2-second one, and it always passes validation. An explicit margin must be zero or greater, and `DeadThreshold - SelfFenceMargin` must be at least twice `HeartbeatInterval`. Set it only to widen the margin for a store with slow or skewed writes, or to `TimeSpan.Zero` to fence exactly at `DeadThreshold`:

```csharp
services.AddHeadlessCoordination(setup =>
{
    setup.UsePostgreSql(connectionString);
    setup.Configure(options =>
    {
        options.HeartbeatInterval = TimeSpan.FromSeconds(2);
        options.SuspicionThreshold = TimeSpan.FromSeconds(6);
        options.DeadThreshold = TimeSpan.FromSeconds(12);
        options.SelfFenceMargin = TimeSpan.FromSeconds(3); // derived default would be 2 seconds
    });
});
```

`CoordinationStorageOptions.Schema` (default `"headless"`, the schema every Headless feature shares) names the database schema that holds the membership tables. Set it on the setup builder, not on a provider:

```csharp
services.AddHeadlessCoordination(setup =>
{
    setup.ConfigureStorage(storage => storage.Schema = "cluster_meta");
    setup.UsePostgreSql(connectionString); // or UseSqlServer(...)
});
```

`setup.ConfigureStorage(configuration)` binds the same options; pass the `Headless:Coordination:Storage` section to bind `Headless:Coordination:Storage:Schema`. The provider validates the schema at startup against its own identifier rules (PostgreSQL: 63-character unquoted identifiers; SQL Server: 128-character regular identifiers), so a name valid on one engine can fail on the other. Redis ignores the option.

### Runtime behavior

`AddHeadlessCoordination` registers the heartbeat service unless `DisableMembershipHeartbeat()` is called, whichever store backs it.

### Membership event log

`IMembershipEventLog` resolves on every host where `AddHeadlessCoordination` ran. `GetEntries()` returns a copy of the last 256 membership events this process published, oldest first. It reads memory only, so a dashboard can call it on every refresh.

Each `MembershipEventLogEntry` carries:

| Member | Meaning |
| --- | --- |
| `Sequence` | Unique per process, starting at 1, one higher per event. |
| `Event` | The `NodeMembershipEvent` that `WatchAsync` subscribers received. |
| `ObservedAt` | When this node published the event, from the registered `TimeProvider`. This is local observation time. A node ages into `Suspected` or `Dead` on the store clock, up to one heartbeat interval before this node sees it. |
| `State`, `LastHeartbeatAt` | The snapshot the event came from. `LastHeartbeatAt` is a store-clock instant, so compare it only with other store-clock instants. Both are `null` for a node that disappeared from the snapshot and for `LocalMembershipLost`. |
| `IsFirstObservation` | `true` for events from the first time this process saw an incarnation. At its first beat a process publishes `NodeJoined`, `NodeSuspected`, or `NodeLeft` for every node already in the store, all stamped with that beat's time. Show them as the starting state, not as activity at `ObservedAt`. |

Observed behavior:

- The log is this node's view, not the cluster's. Another node sees the same changes at other times, and a node that started later lacks the earlier ones. To show a cluster history, read each node's log.
- `NodeLeft` means the node left gracefully or died. The log does not tell the two apart.
- Without the hosted heartbeat (`DisableMembershipHeartbeat()`), the log records no snapshot events. A host that calls `HeartbeatAsync` itself still records `LocalMembershipLost` when the store rejects a beat.
- Poll with a cursor. An entry with a `Sequence` above your cursor is new. A gap between your cursor and the oldest returned `Sequence` means entries were evicted. Sequences restart at 1 when the process restarts, so reset the cursor when the newest `Sequence` is below it or the node's `Identity` changed.

**Verify:** after the first heartbeat, `GetEntries()` holds a `NodeJoined` entry for the local identity with `IsFirstObservation` set to `true`.

### Metadata agreement

`NodeMetadataAgreement.Compare(snapshots, key)` tells you whether the live nodes agree on one [metadata](#node-metadata) value. Pass the result of `GetLivenessSnapshotAsync()`. The method is pure and does no I/O.

```csharp
var snapshots = await membership.GetLivenessSnapshotAsync(cancellationToken);
var agreement = NodeMetadataAgreement.Compare(snapshots, "myapp.build");

if (!agreement.IsUnanimous)
{
    // agreement.Groups[0] holds the most common value; a Value of null lists nodes without the key.
}
```

- Only `Alive` and `Suspected` incarnations count. `Dead` ones are ignored.
- Values compare ordinally, so `V1` and `v1` are different values. A node without the key lands in a group whose `Value` is `null`, apart from a node whose value is the empty string.
- `Groups` comes largest first, then by ordinal value, with the `null` group last. Each group's `Nodes` is ordered by node id, then incarnation. The order does not depend on the input order.
- `IsUnanimous` is `true` only when one group exists and its `Value` is not `null`. With no alive or suspected incarnation, `Groups` is empty and `IsUnanimous` is `false`.
- The snapshot holds each node's current incarnation only, so a restarted node counts under its new incarnation. Earlier incarnations appear only as entries in the local event log.

**Verify:** `Compare` over a snapshot of nodes that all registered the same value returns `IsUnanimous` set to `true` and one group.

### Cluster summary

`CoordinationClusterSummary` resolves on every host where `AddHeadlessCoordination` ran. It is built from the bound `CoordinationOptions` on first resolve and calls no store.

| Member | Value |
| --- | --- |
| `ClusterName`, `Role` | From `CoordinationOptions`. |
| `ProviderName` | `PostgreSql`, `SqlServer`, `Sqlite`, or `Redis`, or the name a custom store's extension reports. |
| `HeartbeatInterval`, `SuspicionThreshold`, `DeadThreshold`, `DeadRetentionWindow` | From `CoordinationOptions`. |
| `SelfFenceMargin` | The margin the fence applies: the configured value, or the derived default when the option is `null`. |
| `MembershipHeartbeatEnabled` | `false` after `DisableMembershipHeartbeat()`. Such a host records no snapshot events in its log. |

**Verify:** resolve `CoordinationClusterSummary` from the host's services and assert `ProviderName` and `ClusterName`.

### For store authors

To back membership with another store, implement `IMembershipStore` and register it the way the shipped providers do: an `ICoordinationProviderOptionsExtension` whose `AddServices` calls `services.AddCoordinationCore<TStore>()`, handed to `AddHeadlessCoordination` through `RegisterExtension`. `AddCoordinationCore<TStore>()` registers the core services over your store and no heartbeat, so the setup builder's `Configure(...)`, its one-provider check, and `DisableMembershipHeartbeat()` apply to your store as they do to a shipped one. Applications never call the store; the membership service does.

```csharp
public sealed class MyCoordinationExtension : ICoordinationProviderOptionsExtension
{
    public string ProviderName => "MyStore"; // reported by CoordinationClusterSummary.ProviderName

    public void AddServices(IServiceCollection services)
    {
        services.AddCoordinationCore<MyMembershipStore>();
        // register what MyMembershipStore itself depends on here
    }
}

services.AddHeadlessCoordination(setup =>
{
    setup.Configure(configuration.GetSection("Headless:Coordination"));
    setup.RegisterExtension(new MyCoordinationExtension());
    // setup.DisableMembershipHeartbeat(); // only on a host that drives liveness itself
});
```

The extension's `ProviderName` is required: return a stable name for your store. A store package wraps the `RegisterExtension` call in its own `Use{Store}` extension on `HeadlessCoordinationSetupBuilder`, as `UseRedis` does.

Every member decides with the store's own clock, never the application clock, and must be safe to call concurrently.

| Member | What it must do |
| --- | --- |
| `AllocateIncarnationAsync(nodeId, ct)` | Return an incarnation strictly greater than every incarnation ever issued for `nodeId`. Keep the per-node generation forever: purging it would let a returning node reuse an incarnation. |
| `UpsertDescriptorAsync(descriptor, ct)` | In one guarded write, store the write-once `NodeDescriptor` (`Identity`, `HostName`, `Endpoints`, `Role`, `Metadata`) and an initial `Alive` liveness entry for the descriptor's incarnation, so the node is live before its first heartbeat. Return `false`, writing neither, when the incarnation is no longer current (another process allocated a newer one for the same node id) or the generation is missing. A `false` makes `RegisterAsync` throw `MembershipRegistrationRejectedException`. |
| `HeartbeatAsync(identity, ct)` | Refresh a live current incarnation. Return `false` when it is superseded, dead, gracefully left, missing, or pruned. Never recreate terminal liveness. |
| `LeaveAsync(identity, ct)` | Move the incarnation to the terminal left state. Do nothing for an identity that is already terminal, superseded, or absent, and never recreate liveness for it. |
| `ReadLivenessAsync(ct)` | Return every known node's `NodeLivenessSnapshot`, ordered by `Identity` ascending; the service does not re-sort. Report only the current incarnation of each node, classify with the store clock against `SuspicionThreshold` and `DeadThreshold`, and omit rows at or past the retention window (`DeadThreshold + DeadRetentionWindow`). |
| `ReadNodeLivenessAsync(identity, ct)` | Return the `NodeLivenessState?` that `ReadLivenessAsync` would give for that one identity at the same instant, or `null` when it is absent from the current-generation view (never registered, stale, superseded, or past retention). Read one row only: no pruning, no backfill. |
| `ReadLiveNodesAsync(ct)` | Return exactly the `Alive` identities `ReadLivenessAsync` would give, current generation only, ordered ascending by `node@incarnation`. Read-only. |

A relational store can build its statements on the SQL store kit; see [Store statement kit](sql.md#store-statement-kit-for-provider-authors).

**Verify:** a host built with your extension reaches `GetLiveNodesAsync()` with its own identity after start. Two hosts that register the same `NodeId` against your store make the second start fail with `MembershipRegistrationRejectedException`.

---

## Headless.Coordination.Sql

### API and behavior

- The relational membership store that the PostgreSQL, SQL Server, and SQLite providers share.
- `RelationalCoordinationOptions`, the options base the providers share: `ConnectionString` and `CommandTimeout` (positive, at most 10 minutes; default 30 seconds).
- A heartbeat is one fenced update: current incarnation, not left, and beat younger than `DeadThreshold`. It writes the later of the stored beat and the database clock, so a database clock that steps back never moves a beat backwards.
- `LeaveAsync` stamps the leave once. Leaving an incarnation that already left is a no-op.
- Every write runs in its own READ COMMITTED transaction and retries a transient fault raised before its commit (a deadlock, a serialization conflict, a lock timeout, a dropped connection), at most 3 attempts. A fault from the commit is never retried.
- Liveness reads are one statement each on their own connection, and retry any transient fault, at most 3 attempts.
- A liveness read never returns a row at or past the retention cutoff (`DeadThreshold + DeadRetentionWindow`), whether or not that row has been deleted yet. Expired rows are pruned at most once a minute per store. A prune that fails after its retries is logged as a warning and retried later; the read still returns.
- Cluster names and node ids compare ordinally on every engine: `node-a` and `Node-A` are two nodes.
- **Backups.** Incarnations come from a counter in each node's generation row. A database restored from a backup rolls those counters back, so incarnations issued after the backup are issued again while rows in other stores may still carry them as owners. Restore coordination together with the stores that hold its owner tags, or not at all.

### Design constraints

None beyond the provider sections.

### Install

```bash
dotnet add package Headless.Coordination.Sql
```

### Setup and use

The provider packages reference it. Install the PostgreSQL, SQL Server, or SQLite provider instead.

**Verify:** see the provider's Verify step.

### Configuration

None.

### Runtime behavior

None.

---

## Headless.Coordination.PostgreSql

### API and behavior

- The relational store over PostgreSQL. Liveness classification uses `clock_timestamp()`.
- The membership tables are one schema step (`Coordination/1`) applied by the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts). Tables and columns are snake_case, and every statement names its tables as `"schema"."table"`, so `search_path` does not matter.

### Design constraints

Point the connection at the primary. A read from a lagging replica can classify a live node as dead.

### Install

```bash
dotnet add package Headless.Coordination.PostgreSql
```

### Setup and use

```csharp
// Optional: pin the node id instead of discovering it from POD_NAME or the machine name.
services.AddHeadlessHostIdentity(options => options.HostName = "orders-worker-0");

services.AddHeadlessCoordination(setup =>
{
    setup.Configure(options =>
    {
        options.ClusterName = "orders";
    });

    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = connectionString;
    });
    // or reuse the connection from services.AddPostgreSqlSql(connectionString): setup.UsePostgreSql();
});
```

The parameterless overload and the shared `headless` schema are described in [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

**Verify:** `dotnet MyApp.dll resources provision -k Schema` reports `Ok` for the `PostgreSql/headless/Coordination` resource. A host started before that fails, naming the resource and `resources provision`. After start, the node appears in `GetLiveNodesAsync()`.

### Configuration

Configure shared `CoordinationOptions` with `setup.Configure(...)`. Configure `PostgreSqlCoordinationOptions.ConnectionString`, optional `DataSource`, and `CommandTimeout` with `setup.UsePostgreSql(...)`.

Set the schema with `setup.ConfigureStorage(storage => storage.Schema = "…")` (default `"headless"`). The schema runner creates the schema when absent.

### Runtime behavior

`resources provision` creates the tables, and a host start only verifies them (see [Resource provisioning](utilities.md#resource-provisioning)). The identity that runs `resources provision` needs PostgreSQL DDL permission.

---

## Headless.Coordination.Redis

### API and behavior

- Incarnation allocation takes the larger of the current value plus one and the Redis server time in microseconds, so an incarnation allocated after a reset or a lossy failover still exceeds every earlier one, provided the server clock did not step back.
- Heartbeat, read, leave, and cleanup scripts use Redis `TIME`.
- A heartbeat whose generation key is missing (the store was flushed or failed over without it) is refused like a superseded one, and the store logs a `RedisCoordinationStoreReset` error.
- Leave never recreates a member that is absent, pruned, or superseded.
- Generation counters are never purged.

### Design constraints

**Run on a Redis that never evicts coordination keys.** The `:gen:<node-id>` counters carry no TTL, so an `allkeys-*` `maxmemory-policy` can evict a live node's counter under memory pressure. The next heartbeat then fails, the node treats its membership as lost, and under the default `MembershipLostBehavior.StopApplication` the host stops. Use an instance or logical database with `noeviction` or a `volatile-*` policy; coordination keys carry no TTL, so `volatile-*` never evicts them.

**Run on a Redis with persistence** (AOF, `appendfsync everysec` or stricter). The counters are the only record of which incarnations were issued. A reset (`FLUSHALL`, a restart without persistence) or a failover to a replica that missed recent writes loses or rolls them back. Every live node's next heartbeat then fails and logs `RedisCoordinationStoreReset`, and under `StopApplication` every host stops.

**Dead and left nodes stay visible longer than on relational stores.** Redis keeps Dead and Left descriptors for `RedisKnownNodeRetention` (default 7 days), so `GetLivenessSnapshotAsync` keeps returning them with `State = Dead` until then. Filter by `NodeLivenessState`. The relational providers prune them shortly after `DeadThreshold + DeadRetentionWindow`. Lower `RedisKnownNodeRetention` to match.

Keys use a cluster hash tag around `ClusterName`, so one cluster's keys share a Redis Cluster slot.

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
    });

    setup.UseRedis(options =>
    {
        options.RedisCleanupInterval = TimeSpan.FromMinutes(5);
    });
});
```

**Verify:** no provisioning step and no health check. When Redis stays unreachable, registration fails the host start after its retries. After start, the node appears in `GetLiveNodesAsync()`.

### Configuration

Configure shared `CoordinationOptions` with `setup.Configure(...)`. Configure `RedisCleanupInterval` (default 5 minutes) and `RedisKnownNodeRetention` (default 7 days) with `setup.UseRedis(...)`. `RedisKnownNodeRetention` is treated as at least `DeadThreshold + DeadRetentionWindow`.

### Runtime behavior

Registers the Redis membership store and a cleanup hosted service, and requires an `IConnectionMultiplexer` registration. Each Lua script loads on the first operation that runs it, which costs that operation one extra round trip.

---

## Headless.Coordination.SqlServer

### API and behavior

- The relational store over SQL Server. Liveness classification uses `SYSUTCDATETIME()`. Every instant is stored as `datetimeoffset(7)`.
- The membership tables are one schema step (`Coordination/1`) applied by the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts). Tables and columns are PascalCase.

### Design constraints

The provider avoids `MERGE`. Under a large concurrent startup SQL Server can still choose a writer as deadlock victim (`1205`); the store retries the whole transaction.

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
    });

    setup.UseSqlServer(options =>
    {
        options.ConnectionString = connectionString;
    });
    // or reuse the connection from services.AddSqlServerSql(connectionString): setup.UseSqlServer();
});
```

The parameterless overload and the shared `headless` schema are described in [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

**Verify:** `dotnet MyApp.dll resources provision -k Schema` reports `Ok` for the `SqlServer/headless/Coordination` resource. A host started before that fails, naming the resource and `resources provision`. After start, the node appears in `GetLiveNodesAsync()`.

### Configuration

Configure shared `CoordinationOptions` with `setup.Configure(...)`. Configure `ConnectionString` and `CommandTimeout` with `setup.UseSqlServer(...)`.

Set the schema with `setup.ConfigureStorage(storage => storage.Schema = "…")`. The default is `"headless"`, not `dbo`. The schema runner creates the schema when absent.

### Runtime behavior

`resources provision` creates the tables, and a host start only verifies them. The identity that runs `resources provision` needs SQL Server DDL permission.

---

## Headless.Coordination.Sqlite

### API and behavior

- The relational store over SQLite. Liveness classification uses SQLite's `'now'`: the clock of the host that runs the statement, at millisecond resolution.
- The membership tables are one schema step (`Coordination/1`) applied by the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts). Tables and columns are snake_case.

### Design constraints

SQLite has no row locks. Every store transaction holds the database write lock, so heartbeats and registrations of every node serialize on the file. Liveness reads take only a shared lock and do not wait for a heartbeat. Every node must run on the host that holds the file, which also makes that host's clock the one clock all nodes judge liveness by. A writer that waits longer than the connection's `Default Timeout` (30 seconds) fails with `SQLITE_BUSY`, which the store retries in a fresh transaction. See [sql.md § SQLite in the kit](sql.md#sqlite-in-the-kit).

### Install

```bash
dotnet add package Headless.Coordination.Sqlite
```

### Setup and use

```csharp
services.AddHeadlessCoordination(setup =>
{
    setup.Configure(options => options.ClusterName = "orders");
    setup.UseSqlite(options => options.ConnectionString = "Data Source=/var/lib/app/app.db");
    // or reuse the connection from services.AddSqliteSql(connectionString): setup.UseSqlite();
});
```

**Verify:** `dotnet MyApp.dll resources provision -k Schema` reports `Ok` for the `Sqlite/headless/Coordination` resource. A host started before that fails, naming the resource and `resources provision`.

### Configuration

Configure `ConnectionString` and `CommandTimeout` with `setup.UseSqlite(...)`. Use a database file: a private `:memory:` database vanishes between the store's connections.

Set the schema with `setup.ConfigureStorage(storage => storage.Schema = "…")` (default `"headless"`). SQLite has no schemas, so the schema becomes the tables' name prefix: `headless_coordination_liveness`. It is validated against PostgreSQL's identifier rules.

### Runtime behavior

`resources provision` creates the tables, and a host start only verifies them.

---

## Headless.Cluster.Dashboard

### API and behavior

- The dashboard's Cluster module: the incarnations of the liveness snapshot with their state, role, last heartbeat, and metadata agreement, this node's membership event log, and the serving host's health checks with their last 30 results.
- It reads `INodeMembership`, `IMembershipEventLog`, `CoordinationClusterSummary`, `NodeMetadataAgreement`, and `IHealthCheckMonitor`. It stores nothing.
- The routes, commands, and views are in the [Cluster module](dashboards/cluster-module.md) topic.

### Install

```bash
dotnet add package Headless.Cluster.Dashboard
```

### Setup and use

Add the module inside `AddHeadlessDashboard`. Register Coordination with a provider for the Cluster view, and the host's health checks before `AddCluster()` for the Health view; either half alone works.

```csharp
using Headless.Cluster;

builder.Services.AddHeadlessCoordination(coordination => coordination.UsePostgreSql(connectionString));
builder.Services.AddHealthChecks();
builder.Services.AddHeadlessDashboard(dashboard => dashboard.WithHostAuthentication().AddCluster());
```

**Verify:** `{base}/cluster` lists this node; a host with neither Coordination nor health checks fails to start naming `AddHeadlessCoordination`.

### Configuration

| `ClusterDashboardOptions` member | Default | Description |
| --- | --- | --- |
| `ViewPolicy`, `OperatePolicy` | the shell's | The host policies for reading the module and for its probe commands. |
| `ChangePageSize` | 200 | The most node changes one live read returns, 1 to 1000. |

### Runtime behavior

- Coordination counts as registered when `CoordinationClusterSummary` resolves, so Messaging's default do-nothing membership does not turn the Cluster view on.
- No check runs in the background. Checks run only when an operator presses Probe now or Probe all, and the Health view reads `Not probed yet` before the first probe.
- The Health view shows the replica that served the request. Serve the dashboard from one replica, or with sticky sessions.
- The event log is this node's local view, stamped with its clock; it restarts with the process.
- Live node changes reach every node: each compares the store's snapshot with the cursor, which means the same thing on every node. A heartbeat alone is not a change.
