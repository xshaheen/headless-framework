---
domain: Dashboards
packages:
---

# Leases module

> Part of [Dashboards](../dashboards.md). The Leases module from `Headless.Leases.Dashboard`: registration and host shapes, object kinds, counts and the palette, API routes, views, and the lock-scope rule.

## Register the module

```bash
dotnet add package Headless.Leases.Dashboard
```

`AddLeases()` adds the Leases module under the id `leases`, in the `Platform` product tab, with one sidebar entry, `Leases`. It shows two halves, and serves whichever the host registers: fenced leases (`AddHeadlessFencing(...)` with a provider) and distributed locks (`AddHeadlessDistributedLocks(...)` with a provider). The halves may be registered before or after `AddLeases()`.

```csharp
using Headless.DistributedLocks;
using Headless.Fencing;
using Headless.Leases;

builder.Services.AddHeadlessFencing(fencing => fencing.UsePostgreSql(connectionString));
builder.Services.AddHeadlessDistributedLocks(locks => locks.UsePostgreSql(connectionString));
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "ops.view"; host.OperatePolicy = "ops.operate"; })
    .AddLeases());

app.MapHeadlessDashboard();
```

**Verify:** a signed-in viewer's `GET {base}/api/leases/summary` answers `200`, with `fencing` set when Fencing is registered and `locks` set when a lock provider is; a host with neither fails to start with a message naming `AddHeadlessFencing` and `AddHeadlessDistributedLocks`.

| Host shape | Fenced leases view | Distributed locks view | Sidebar count | Palette |
| --- | --- | --- | --- | --- |
| Fencing and a lock provider | Live | Live, with the scope note | Live leases | Lease id and lock resource |
| Fencing only | Live | `No distributed lock provider is registered`, naming `AddHeadlessDistributedLocks(...)` | Live leases | Lease id |
| Lock provider only | Opens the Distributed locks view; a direct visit names `AddHeadlessFencing(...)` | Live, with the scope note | Held locks | Lock resource |
| Neither, or only a `NullDistributedLock` | Startup fails | | | |

The module reads the unkeyed `IDistributedLock`. A `NullDistributedLock` counts as no lock provider, and the keyed locks Jobs and Messaging register for their own slots are not listed. Reader-writer locks and semaphores have no list API, so the module does not show them.

| `LeasesDashboardOptions` member | Default | Description |
| --- | --- | --- |
| `ViewPolicy`, `OperatePolicy` | the shell's | As for every module. The module has no operate-only action, so only `ViewPolicy` gates what it serves. |

`AddLeases(Action<LeasesDashboardOptions>)`, `AddLeases(Action<LeasesDashboardOptions, IServiceProvider>)`, and `AddLeases(IConfiguration)` configure them.

## Object kinds and topics

`fenced-lease` (a lease, by the id `{tenant}/{kind}/{resource}` with each segment URL-escaped and an empty first segment for the host scope, such as `/ledger-export/batch%2F7`) and `lock` (a held lock, by its resource name). Take a lease id from a response; never build one. The list topics are `leases.fenced-lease` and `leases.lock`, and nothing publishes on them: the views read again when the operator presses Refresh or returns to the tab.

## Counts and the palette

The sidebar's `leases` count is the live fenced-lease count, read with the inspector's cap, so it reads `10000+` past 10,000. On a host without Fencing it is the provider's held-lock count. The palette resolves an exact lease id and an exact lock resource; it does no text search. A string that is both returns both. The module adds no Needs you items: an expired lease ends through a takeover or a sweep on its own.

## API

The module's routes live at `{base}/api/leases`, JSON with camelCase names and enums as strings. A field documented as possibly `null` is written as `null`. Every lease time is the database clock's. A bad value answers `400` with a problem naming the field; a failed store read answers `5xx`, never an empty page. No route writes to the lease store or the lock provider.

| Route | Answer |
| --- | --- |
| `GET summary` | `{ fencing: { registered: true }, locks: { registered: true } }`. `fencing` is `null` without Fencing, and `locks` is `null` without a real lock provider. It reads no store, so a fencing outage leaves the Distributed locks view working |
| `GET fenced?state=&kind=&scope=&tenant=&prefix=&after=` | `{ items: [{ id, tenantId, kind, resource, generation, status, state, holder, takeoverCount, grantedAt, expiresAt, endedAt, progressContract, progressSizeBytes }], nextCursor, readAt }`, 50 per page, ordered by tenant (the host scope first), kind, and resource. `state` filters by `All`, `Live`, `Expired`, or `Ended`. `scope=host` reads the host scope and `tenant=<id>` one tenant; neither reads every tenant, and both answer `400`. `prefix` matches the resource literally and case-sensitively. Pass `nextCursor` as `after` for the next page; any other `after` answers `400`. Each item's `status` is the stored `LeaseFenceStatus` and `state` its group; neither comes from a clock. Without Fencing every `fenced` route answers `404` naming `AddHeadlessFencing` |
| `GET fenced/counts?kind=&scope=&tenant=&prefix=` | `{ live, expired, ended, total }`, each `{ count, capped }` for the same filter. A count stops at 10,000 with `capped: true` |
| `GET fenced/kinds` | `{ items }`: the kinds in use, ordinal. The answer is kept 30 seconds per process; a failed read is not kept |
| `GET fenced/lease?id=` | `{ lease, readAt }` for one lease. `404` when the lease has no row; `400` for an id the module did not mint |
| `GET locks?after=` | `{ items: [{ id, resource, leaseId, ttlMilliseconds }], nextCursor, total, readAt }`, 100 per page, ordered by resource. `leaseId` is `null` when the provider sees the lock but not its holder, and `ttlMilliseconds` is `null` for a lock with no expiry. `total` is the provider's count. Without a lock provider both lock routes answer `404` naming `AddHeadlessDistributedLocks` |
| `GET locks/lock?resource=` | `{ lock, readAt }` for one held lock. `404` when it is not held; `400` for a blank resource |

No response carries a lease's progress bytes or a lock's fencing token: a provider reports a token only on the holder's own handle. Each lock page reads the provider's whole list and pages it in memory.

## Views

| View | Shows | Acts |
| --- | --- | --- |
| Fenced leases (`{base}/leases`; filters at `?state=`, `?kind=`, `?scope=host` or `?tenant=<id>`, `?prefix=`) | Live, Expired, and Ended counts and the number of kinds; every lease with its resource, stored status (`Live`, `Expired`, `Settled`, `Released`, `Revoked`, `Abandoned`), kind, tenant (`Host` for the host scope), generation, granted and expiry times, takeovers, and progress as `<size> · <contract>`. The header says when the list was read. The open lease (`?open=<id>`) docks under the list with its holder (`No holder recorded` when the grant named none), takeovers, granted, expiry, and end times, and progress size and contract, and says why the screen has no Release action. A purged lease reads that it has no row any more | Refresh, which reads the list, the counts, and the open panel again |
| Distributed locks (`locks`) | The held-lock total; every lock with its resource, lease id (`—` when the provider reports none), and time to live as read (`No expiry` for none), which does not count down. The open lock (`?open=<resource>`) docks under the list; a lock no longer held says so | Refresh |

Neither view offers an action that changes a lease or a lock. Release and settle a lease from code that holds it, and delete ended leases with `IFencedLeases.PurgeAsync`. A relative time on screen is measured on the browser's clock and may disagree with the state the server reports; the state label is always the server's.

## Lock scope

The Distributed locks view always shows a note: Redis lists every holder across the cluster, while PostgreSQL, SQL Server, and in-memory providers list only the locks this host's handles hold. Behind a load balancer with one of those providers, the list is the replica that answered. Read the view per replica, or use Redis for a cluster-wide list.
