---
domain: Dashboards
packages:
---

# Cluster module

> Part of [Dashboards](../dashboards.md). The Cluster module from `Headless.Cluster.Dashboard`: registration and host shapes, object kinds, commands, Needs you and counts, live changes, API routes, and views.

## Register the module

```bash
dotnet add package Headless.Cluster.Dashboard
```

`AddCluster()` adds the Cluster module under the id `cluster`, in the `Cluster` product tab. It shows two halves, and serves whichever the host registers: Coordination membership (`AddHeadlessCoordination(...)` with a provider) and the host's health checks (`AddHealthChecks()`). Register the health checks before `AddCluster()`: the call then registers the health check monitor (`AddHeadlessHealthCheckMonitor()`) that the Health view and the probe commands read.

```csharp
using Headless.Cluster;
using Headless.Coordination;

builder.Services.AddHeadlessCoordination(coordination => coordination.UsePostgreSql(connectionString));
builder.Services.AddHealthChecks();
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "ops.view"; host.OperatePolicy = "ops.operate"; })
    .AddCluster());

app.MapHeadlessDashboard();
```

**Verify:** a signed-in viewer's `GET {base}/api/cluster/summary` answers `200`, with `coordination` set when Coordination is registered and `health.monitor: true` when the monitor is; a host with neither Coordination nor health checks fails to start with a message naming `AddHeadlessCoordination` and `AddHealthChecks`.

| Host shape | Cluster view | Health view | Commands |
| --- | --- | --- | --- |
| Coordination and health checks registered before `AddCluster()` | Live | Live | Allowed for operators |
| Coordination only | Live | `Health checks are not registered`, naming `AddHealthChecks()` | Disabled |
| Health checks only | `Coordination is not registered`, naming `AddHeadlessCoordination(...)` | Live | Allowed for operators |
| Health checks registered after `AddCluster()`, no monitor | As above | A banner naming `AddHeadlessHealthCheckMonitor()`; health routes answer `409` | Disabled with that remedy |
| Neither | Startup fails | | |

A feature that registers its own health checks, such as Jobs or Messaging, adds them when its `AddHeadless…` call runs. Their checks still appear when the monitor is registered, because the monitor reads every registration when it starts; only the decision to register the monitor reads what is registered at the `AddCluster()` call. Call `AddHeadlessHealthCheckMonitor()` yourself when you cannot register a check first.

| `ClusterDashboardOptions` member | Default | Description |
| --- | --- | --- |
| `ViewPolicy`, `OperatePolicy` | the shell's | As for every module. |
| `ChangePageSize` | 200 | The most node changes one live read returns, 1 to 1000. |

`AddCluster(Action<ClusterDashboardOptions>)`, `AddCluster(Action<ClusterDashboardOptions, IServiceProvider>)`, and `AddCluster(IConfiguration)` configure them.

## Object kinds and topics

`node` (an incarnation, by its `node@incarnation` identity), `check` (a health check, by its registration name), and `health` (the serving host, by its label, which only `cluster.probe-all` acts on). Stream topics are `cluster.node` and `cluster.check`. Changes carry no projection, so open views read the object again. The palette's id lookup resolves an exact identity, a bare node id (every incarnation of it in the snapshot), and a check name, case-insensitively.

## Commands

| Command | On | Does |
| --- | --- | --- |
| `cluster.probe` | `check` | Runs each named check. A row is `Done` with the check's status as its reason whatever that status is, or `Skipped` with `No check named '<id>'` |
| `cluster.probe-all` | `health` | Runs every check once. Its one row's reason is `<overall status> · <n> checks`. Send the serving host's label, `summary.host`, as the id |

Both need operate permission, ask for no confirmation or reason, and are written to the operator log. Without the monitor both are refused with `409` and the remedy, and the log gets no entry. Cancelling the request cancels the probe, which then records nothing.

## Attention and counts

A check whose latest result is `Degraded` (Warning) or `Unhealthy` (Critical) is one item in the shell's `Health checks` section, dated from the first result of its current run of that status. A `Suspected` (Warning) or `Dead` (Critical) node is one work item, dated from its last heartbeat. A relational store prunes a dead incarnation shortly after `DeadRetentionWindow`, but Redis keeps it for its known-node retention, so dead nodes stay in Needs you longer on Redis.

The sidebar counts are `nodes`, `Alive`, `Suspected`, and `Dead` from one snapshot read, and `checks`, `Degraded`, and `Unhealthy` from the checks' latest results. A half the host does not serve answers no key, so its entries read `–`. When the membership store does not answer, Needs you and the counts keep the health answers and name `coordination` as unavailable.

## Live changes

Each node reads the membership snapshot on the shell's change interval and compares it with the cursor, so a node's state, role, or metadata change, a new incarnation, and an incarnation that left the snapshot reach every node's open views within the interval plus a second. A heartbeat alone is not a change; the Cluster view reads the list again every heartbeat interval (at least 5 seconds) while it is open, so last heartbeats stay current. Health results live in each process: a probe's result goes out to the views on the node that ran it as soon as the probe completes.

Serve the dashboard from one replica, or with sticky sessions: the Health view, its counts, and its Needs you items describe whichever replica answered, and a read from another replica can show `Not probed yet` after a probe. `summary.host` and `health.host` name the replica, and the Health view shows it beside its count.

## API

The module's routes live at `{base}/api/cluster`, JSON with camelCase names and enums as strings. A field documented as possibly `null` is written as `null`. Durations are whole milliseconds in `…Milliseconds` fields. A bad `state` or `status` value answers `400` with rule `enum`.

| Route | Answer |
| --- | --- |
| `GET summary` | `{ host, coordination, health }`. `coordination` is `{ clusterName, providerName, role, localNode, heartbeatIntervalMilliseconds, suspicionThresholdMilliseconds, deadThresholdMilliseconds, deadRetentionWindowMilliseconds, selfFenceMarginMilliseconds, membershipHeartbeatEnabled }`, or `null` without Coordination. `health` is `{ registered: true, monitor }`, or `null` without health checks |
| `GET nodes?state=` | `{ items: [{ id, nodeId, incarnation, state, role, lastHeartbeatAt, isLocal, ownerTag, metadata }] }`, ordered by node id, then incarnation. `state` is `Alive`, `Suspected`, or `Dead`. Without Coordination every node route answers `404` naming `AddHeadlessCoordination` |
| `GET nodes/{id}` | `{ node, agreement: [{ key, isUnanimous, value, matches, compared, groups }], events }` for one incarnation: how many compared live nodes hold its value, how many were compared, and the groups that hold another value or lack the key. `404` when the identity is not in the snapshot |
| `GET events` | `{ observer, clock: "local", items: [{ sequence, kind, node, observedAt, state, lastHeartbeatAt, isFirstObservation }] }`: this node's membership event log, newest first, stamped with this node's clock |
| `GET metadata-agreement` | `{ keys: [{ key, isUnanimous, groups: [{ value, nodes }] }] }` for every key an alive or suspected node carries, sorted. A `null` group value is the nodes that lack the key |
| `GET health` | `{ host, status, probedAt, totals: { healthy, degraded, unhealthy, notProbed }, liveness: { healthy, total }, slowest: { name, durationMilliseconds } }`. `status`, `probedAt`, and `slowest` are `null` before the first probe; `liveness` is `null` with no liveness check. Without health checks the health routes answer `404`; without the monitor, `409` naming `AddHeadlessHealthCheckMonitor()` |
| `GET health/checks?status=` | `{ items: [{ name, tags, failureStatus, timeoutMilliseconds, isLiveness, latest }] }`, ordered by name. `latest` is `{ status, durationMilliseconds, description, completedAt }`, or `null` before the check's first probe. `status` is `Healthy`, `Degraded`, `Unhealthy`, or `NotProbed` |
| `GET health/checks/{name}` | `{ check, results }` with up to 30 results, newest first; the name matches case-insensitively. `404` for an unknown name |

The health routes read recorded results only. No read runs a check: checks run only through the probe commands.

## Views

| View | Shows | Acts |
| --- | --- | --- |
| Cluster (`{base}/cluster`, each state at `?state=<state>`) | Alive, Suspected, and Dead counts and the heartbeat, suspicion, and dead thresholds; every incarnation with its role, state, and last heartbeat, the serving node tagged `This node`; and the metadata keys whose values differ across live nodes. The open node (`?open=<id>`) docks under the list: Overview, Metadata (each key with how many live nodes match; a key that differs on every one of three or more nodes, such as a pod name, reads `Differs on every node`), and Events (this node's log for it, captioned with the observer and `local clock`). A node that left the store reads `This node is no longer in the membership store.` | None |
| Health (`health`, each status at `?status=<status>`) | Overall status, totals, liveness, and the slowest probe, with liveness and the slowest probe only once a result exists; every check with its tags, latest status (`Not probed` before its first probe), duration, last probe, and failure status. The header reads `N checks · probed <time>`, or `N checks · Not probed yet`, beside a `Host <host>` tag. The open check (`?open=<name>`) docks under the list: Overview and History (its recent results with a summary such as `Healthy for 22 probes, then Degraded for the last 8`) | `Probe all` in the header; `Probe now` on the open check. A viewer sees both disabled with `Needs operate permission` |

An open row that leaves the active filter stays in the panel, and its row stays with `No longer matches the filter` until the panel closes.
