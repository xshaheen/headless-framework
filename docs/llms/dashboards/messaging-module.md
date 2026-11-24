---
domain: Dashboards
packages:
---

# Messaging module

> Part of [Dashboards](../dashboards.md). The Messaging module from `Headless.Messaging.Dashboard`: registration, options, object kinds, commands, API routes, Ping, forwarding to other nodes, a dashboard-only host, and the migration from `MapHeadlessMessagingDashboard`.

## Register the module

```bash
dotnet add package Headless.Messaging.Dashboard
```

`AddMessaging()` adds the Messaging module (`Headless.Messaging.Dashboard`) under the id `messaging`. It needs `AddHeadlessMessaging(...)` with a storage in the same host; a host that adds it without `AddHeadlessMessaging` fails to start with a message naming that call. It reads the main store, named `primary`, and every additional outbox registered with `AddOutbox(name)`, by that name. It replaces the removed Messaging Vue dashboard; see [the migration steps](#migrate-from-mapheadlessmessagingdashboard).

```csharp
using Headless.Dashboard;
using Headless.Jobs;
using Headless.Messaging.Dashboard;

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UsePostgreSql(builder.Configuration.GetConnectionString("Orders")!);
    setup.AddOutbox("billing").UsePostgreSql(builder.Configuration.GetConnectionString("Billing")!);
});
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "ops.view"; host.OperatePolicy = "ops.operate"; })
    .AddJobs()
    .AddMessaging(messaging => messaging.NodeName = builder.Configuration["NodeName"]!));

app.MapHeadlessDashboard();
```

**Verify:** a host that adds the module without `AddHeadlessMessaging` fails to start with a message naming that call. Once it starts, a signed-in viewer's `GET {base}/api/messaging/nodes` answers `200` with this node and the stores it reads.

| `MessagingDashboardOptions` member | Default | Description |
| --- | --- | --- |
| `ViewPolicy`, `OperatePolicy` | the shell's | As for every module. |
| `NodeName` | the machine name | This node's name in every object id. Give each node a stable name without `~` or white space. |
| `ChangeOverlapWindow` | 5 s | How far back each change read of a store reads again. It must exceed the longest transaction that enlists an outbox write: a row that commits later than this after its write is not streamed, and shows on the next read of its list. |
| `ChangePageSize` | 200 | The most changed rows one change read returns per store, 1 to 1000. |
| `StoreTimeout` | 1.5 s | How long a read that covers several stores waits for one of them before naming it unavailable. Keep it below the shell's `AggregationTimeout`. |
| `SearchTimeout` | 10 s | How long a content or correlation id search waits for one store. A store that has not answered by then is cancelled and the search answers `incomplete: true` with what the other stores found. Greater than 0 and under 30 seconds, the time a node waits for a search it relays. |
| `TraceLimit` | 500 | The most published messages, received generations, and scheduled deliveries the trace page lists for one correlation id, over every store and node together, oldest first. Past it the page shows the count as `N+` and links the published list searched by the id. 1 to 10,000. |
| `Forwarding.Key` | `null` | The key every node of the deployment shares, at least 32 characters. It never leaves the node: each forwarded call is signed with it (see [Key mode](#key-mode-signed-calls)). Setting it, or `Forwarding.Policy`, turns forwarding on; see [Forwarding to other nodes](#forwarding-to-other-nodes). |
| `Forwarding.PreviousKey` | `null` | The key being rotated out. A node admits calls signed with it as well as with `Key`, and never signs with it. At least 32 characters, different from `Key`, and only with `Key` set. |
| `Forwarding.MaxClockSkew` | `00:01:00` | How far a signed call's timestamp may be from this node's clock, either way. Greater than 0 and at most 10 minutes. |
| `Forwarding.Policy` | `null` | A host authorization policy that admits a calling node through the host's own service-identity scheme, used instead of the key. Set one of the two, not both. |
| `Forwarding.AllowInsecurePeers` | `false` | Lets this node call a peer that discovery lists at an `http://` address. |
| `Forwarding.MaxResponseBytes` | `4194304` (4 MiB) | The largest answer this node reads from a peer; a larger one fails the call as `not-answering`. Must be greater than 0. Raise it when a message payload shown through forwarding can be larger. |
| `ForwardedAccess` | `View` | The most a call another node forwards here may do: `View` reads only, `Operate` also runs the commands the serving node decided its operator may run. |

`AddMessaging(Action<MessagingDashboardOptions>)`, `AddMessaging(Action<MessagingDashboardOptions, IServiceProvider>)`, and `AddMessaging(IConfiguration)` configure them.

## Object kinds, ids, and topics

`published`, `received`, `circuit` (a consumer's circuit breaker on one node, by `{node}~{consumer}`), and `node` (by its `NodeName`). A row's object id is `{node}~{store}~{storage id}`, such as `node-a~billing~0192f3c1-…`: the node that served it by its `NodeName`, the store (`primary` for the main store), and the row's storage id. Stream topics are `messaging.published` and `messaging.received`; the stores are shared by every node, so a change carries no `node` and goes to the plain topic. A change's projection is the row as its list shows it (`id`, `store`, `storageId`, `messageId`, `name`, `lane`, `status`, `consumer`, `retries`, `added`, `expiresAt`, `nextRetryAt`, `changedAt`, `failureFingerprint`, `tenantId`, `isHeld`, `terminal`), and its version is the row's change marker in ticks. A deleted row goes out with no projection. A circuit's changes go out on `messaging.circuit` from the node that holds it: a state transition, a failure count that moved, or a circuit the node stopped tracking. The projection is the circuit as `GET circuits` lists it, every field written, so a closed circuit carries `openedAt` and `estimatedRemainingOpenDuration` as `null`; a circuit the node no longer tracks goes out with no projection. Its version is the change's time in ticks, raised when needed so each change of a node is later than the one before.

## Several stores

Counts, Needs you, Overview, lookups, lists, and the change stream read every store at once, each bounded by `StoreTimeout`. A store that fails or does not answer in time is named in `unavailable` while the others answer; the module is never marked stale for one store. The change cursor keeps one position per store and table, so a slow store neither holds the others back nor loses its place, and a store that was down starts from its current position once it answers.

## Live changes

A write this node's senders and consumers make reaches its streams at once: the pipeline reports the write after it landed, and the module reads the row back with its marker. A write another node makes, or one inside an application transaction, arrives through this node's change read of the stores, within `ChangePollInterval` plus the read. A circuit breaker lives in one node's memory, so no store carries its changes: the breaker reports each one to the module, which publishes it to that node's streams at once. The Consumers view and the Overview's Circuits card patch the circuit in place and read `GET circuits` on no timer while every circuit is closed. The time an open circuit has left moves with no change behind it, so they read the list again every 5 seconds while a circuit is open. `GET metrics/realtime` stays polled every 5 seconds: a new second is measured whether or not anything changed, so there is no change to send.

## Sidebar counts

One capped count per key; a published count adds up every store:

| Key | Rows |
| --- | --- |
| `published.all`, `received.all` | every row of the table |
| `published.succeeded`, `received.succeeded` | sent / consumed |
| `published.failed`, `received.failed` | failed for good: `Failed` with no retry pending |
| `published.retrying`, `received.retrying` | failed with a retry pending: the failed count less the terminal count |
| `published.delayed`, `published.scheduled`, `published.queued` | waiting for their delivery time / stored and waiting for the first send / claimed by a sender |
| `received.held` | held by an operator |
| `hosts.unknown-lanes` | rows of every store and table whose lane this host does not recognize |

## Attention and Overview

One item per failure fingerprint of the failures that ended for good: per consumer for received rows (`2 messages received by billing.charge-consumer failed for good`, pointing at the `consumer`), per store for published rows. Items of one cause share a `GroupKey` across stores, and each carries its list filter `{ store, status, terminalOnly, consumer, fingerprint }`. An item's id is `failed:{kind}:{fingerprint}:{consumer}:{store}`, and its `GroupKey` is the same without `:{store}`. `kind` is `published` or `received`; the fingerprint is `v{n}:{token}`, so it holds exactly one colon, or is empty, with none, for failures recorded without one; the consumer is empty for a published row and may itself contain colons, so read the store as the last segment, the kind and the fingerprint from the front, and the consumer as what lies between. An item another node answers through forwarding has `{node}:` before its `GroupKey`. One warning item per store and table holding rows in an unknown lane, with the id `unknown-lane:{store}:{kind}`. The Overview shows the messages that failed for good, the messages waiting for a retry, and this node's consume rate over the last minute, which is left out while the node measured no second of it.

## Lookup and payload

The palette accepts a full object id, a bare storage id, or a message id. A storage id or a message id is looked up in every store, and each match is listed with its store in its title; a message id matches byte for byte, trailing spaces included, on every provider. An id naming another node finds nothing on this node. A payload is the row's stored envelope as JSON.

## Trace

The shell's trace page (`POST {base}/api/trace`) lists the module's rows that carry one correlation id: each published message, each received generation, and each scheduled delivery, from every store and, with forwarding on, every node discovery lists. Each store and each node is read at once with the oldest `TraceLimit` plus one rows, bounded by `StoreTimeout`. The answer keeps the oldest `TraceLimit` by added time and sets `limited` when more existed. Nodes that share a store return the same rows, and each row is listed once, labelled with the first node that returned it.

- **Placement.** A published message is keyed by its message id. A received generation is keyed by its own row and placed under the message it delivers. A message's causation names the message it was published from when a row of that message is on the trace, and otherwise a job run.
- **Times.** A row waiting to be sent or retried shows its due time, such as a delayed publish's delivery time. Any other row shows when it was added.
- **Entry points.** A message's Correlation group offers `Open trace` beside its correlation id, and `T` on an open message opens `{base}/trace#<id>`. A message with no correlation id shows `Not recorded`, and both are disabled with `No correlation id`. A received message whose foreign correlation id is over 200 characters shows it, and both are disabled with `Id longer than 200 characters`.
- **List link.** The answer links the `published` list with `{ "correlationId": "<id>" }` as history state, so the id never sits in a URL.
- **Unavailable sources.** A store that fails or runs out of time is named by its store name. A store whose monitoring API does not implement `IMonitoringApi.GetCorrelatedMessagesAsync` is named `{store} (correlation lookup not supported)`. A node that does not answer within `StoreTimeout` plus one second is named by its state, such as `Node orders-api is not answering`, and a store another node could not read is named `{store} on node {node}`. The other sources' rows still come back.
- **Forwarding.** A node asks each peer with a `POST` whose body carries the id and the limit, never a URL, at view access. A trace call another node forwarded reads only the receiving node's stores. A peer reads its own stores within its own `StoreTimeout`, and the caller waits one second longer, so a peer with one slow store still returns its other stores' rows and names the slow one. A peer that never answers holds the trace until the shell's `AggregationTimeout`; with the defaults (1.5 s and 2 s) the shell then shows the whole module stale, so raise `AggregationTimeout` above `StoreTimeout` plus one second when you want such a peer named instead.

**Verify:** `POST {base}/api/trace` with `{ "correlationId": "<id>" }` answers a `messaging` section with `stale: false` and one row per message of that id.

## API

The module's routes live at `{base}/api/messaging`. Every route but Ping is a read, so a viewer without a named operator can call it:

| Route | Answer |
| --- | --- |
| `GET published?store=&status=&terminal=&retrying=&name=&fingerprint=&lane=&messageId=&from=&before=&sort=&direction=&limit=&after=` | `{ items, count, countCapped, nextCursor, unavailable, incomplete }`: one keyset page merged over every store unless `store` names one. `count` and `countCapped` come with the first page only (the read without `after`), summed over every store that answered; a later page leaves both out and reads no count, so keep the first page's count. `limit` up to 200; `retrying=true` lists failures with a retry pending; `lane` is `Bus` or `Queue`; `from` (inclusive) and `before` (exclusive) bound the added time; `sort` is `added` (the default, `descending` unless `direction` says otherwise: newest first) or `name` (`ascending` by default), each followed by the storage id; `after` takes `nextCursor` and is refused under another sort, direction, filter, or search. A `Failed` row carries `reason`: the line of its stored failure that names its cause, `Type: message`, cut at 240 characters (the exception a consumer threw, not the `SubscriberExecutionFailedException` it is stored inside), read by one more read per store for the page's failed rows; a store that fails that read leaves its rows without a `reason`, and a row that is not failed never has one |
| `GET received?status=&terminal=&retrying=&consumer=&name=&fingerprint=&held=&lane=&messageId=&from=&before=&sort=&direction=&limit=&after=` | the same over the main store's received rows |
| `POST published/search`, `POST received/search` | the list's answer for a search. The body is `{ content?, correlationId?, … }` with the list's query fields as body fields (`store`, `status`, `lane`, `from`, `sort`, `after`, and the rest); `?node=` stays in the query. `content` matches the stored envelope, headers and body, as a literal substring without regard to case; `correlationId` matches the correlation id column exactly and by ordinal comparison (`C-42`, `c-42` and `c-42 ` are three ids), and not a body that merely holds the value. An index serves it. A row written before the store added that column, and a row whose id is over 200 characters, is never found by it. Set either or both. A content search has no index, so each store reads every row its other filters leave, for at most `SearchTimeout`: a store out of time sets `incomplete: true` and is not named in `unavailable`, and its rows come on a later page. Rows carry no content, so a search serves nothing of the body it matched. The text travels only in the body, never in a URL, the cursor (a digest stands for it), or a log line the module writes |
| `GET messages/{kind}/{id}` | `{ row, headers, content, exceptionInfo }`, or `404` |
| `GET received/generations?messageId=&consumer=` | `{ items }`: every received row of the message, oldest first, each with `generation: { number, incarnationId, origin, isCurrent, replayOfIncarnationId, replayOfId, expiresAt, recovery }` from the store's inbox operations. `number` is `0` for the first delivery and one more per re-execution; `origin` is `original` or `replay`; a replay names the incarnation it re-executed and, while the store keeps that row, its object id in `replayOfId`; `expiresAt` is when retention may delete the ended generation, absent while it runs; `recovery` is `orphaned` while no host has the consumer registered, else `routable`. A row has no `generation` when the store keeps none for it, when the store has no inbox operations, or past the first 200 generations. No field claims a delivery guarantee: the store records none |
| `GET scheduled?store=&name=&page=&perPage=` | `{ store, supported, items, totalCount, reason }`; a provider without scheduled-delivery operations answers `supported: false` with the reason, not an error |
| `GET receipts?actor=&targetKind=&operationType=&from=&before=&limit=&after=` | `{ items, nextCursor }`: one keyset page of the operator ledger in the main store, newest first, every audited intervention including a refused one; `actor` matches ordinally, `targetKind` (`Inbox`, `ScheduledDelivery`, `Published`) and `operationType` (such as `Hold`, `Resend`) ignore case, `from` is inclusive and `before` exclusive, `limit` up to 500; `after` takes `nextCursor`, which is absent on the last page |
| `GET consumers`, `GET contracts` | `{ items }`: the consumers and the message contracts of the host manifest's `messaging` section; each consumer's message names its implementation in `consumerType` and the method that handles it in `handlerMethod` (`ConsumeAsync`, or `RespondAsync` for a responder), and a consumer tuned `Sequential()` carries `sequential: true`, which its panel shows as `Ordering: One at a time per key`, and `activeHost`, how hosts share it: `keyRouted` (the transport routes each key to one consumer), `broker` (the broker delivers it to one consumer at a time), `lock` (the host that holds the messaging distributed lock takes it in), or `none` (every host listens) |
| `GET consumers/sequential?identity=&lane=` | `{ intake, holders, waiting }` for a competing consumer tuned `Sequential()`, or `404` for any other; `lane` is `bus` or `queue` (rule `enum`). Under `intake: lock`, `holders` has one entry per subscription (the consumer on the Bus lane, each message on the Queue lane): `{ subscription, state, node, waiting, reason }`, where `state` is `held` (with the holder's `node` and the count of the consumer's messages waiting to run there, as the `headless.messaging.sequential.waiting` gauge reports it), `none` (the lock store says no host holds it), or `unavailable` (with the `reason`: the lock store failed or did not answer within 3 seconds, or no node the serving node can reach claims the lease). Every node reads the lock store and asks each node discovery lists for the holder, so any node names the same holder; without forwarding, only a holder that is the serving node is named. Under any other intake no one host holds it: `holders` is empty and `waiting` is the serving node's own count. `waiting` is absent while a node runs no dispatcher. The panel reads it each time it opens and shows `Active host` (the node, `None`, or `Unavailable: <reason>`) and the waiting count; a consumer that is not sequential shows neither |
| `GET circuits` | `{ items: [{ id, consumer, lane, state, consecutiveFailures, failureThreshold, openedAt, estimatedRemainingOpenDuration, version }] }`; `version` is the version of the circuit's latest change, the one its stream change carries; `consumer` is the consumer identity, as `GET consumers` names it, and `lane` (`Bus` or `Queue`) the lane the circuit guards, because one identity trips independently on each lane; `id` is the circuit's object id, `{node}~{circuit key}`, which its commands take |
| `GET backlog` | `{ asOf, stores: [{ store, publishedPending, oldestPublishedSeconds, receivedPending, oldestReceivedSeconds }], unavailable }`: the stored work each store still holds waiting, as `IMonitoringApi.GetBacklogAsync` counts it: rows not yet sent to the broker and rows not yet handled, each with how long its oldest has waited, absent when none waits. Every store is read at once within the store timeout; a store that fails or runs out of time is named in `unavailable` and has no entry, so it never reads as an empty backlog. An additional outbox has no `receivedPending`, because it holds no received rows |
| `GET providers` | `{ transports: { bus, queue }, outboxes: [{ name, storage }] }`: the broker name of each lane's transport and the storage provider of each outbox, as the host manifest's `messaging` section names them, never an endpoint or a connection; a host without a transport on a lane leaves that lane out |
| `GET nodes` | `{ items: [{ name, isLocal, stores, state, instances }], discovery }`: this node and the stores it reads, then every node discovery lists, with `stores` empty; a listed node's `state` is what the last forwarded call to it found (`answering`, `not-answering`, `key-rejected`, `policy-denied`, `view-only`, `insecure-peer`), absent before the first call and for this node; `instances` lists each discovered instance of a listed node as `{ address, port, tags }`, in discovery order: the address and port discovery gave (`port` is `0` when the address carries it) and its comma-separated tags as a list, leaving out an instance whose address is not `http` or `https`, and is absent for this node, which discovery does not list; `discovery` says what the last discovery read found, absent while forwarding is off. A dashboard-only host leaves itself out. This route is always answered by the serving node |
| `POST nodes/ping` | `{ items: [{ node, outcome, latencyMs, state, status, reason }], discovery }`: Ping, which needs operate permission; see [Ping](#ping) |
| `GET metrics/realtime` | `{ seconds, publishedPerSecond, consumedPerSecond, consumeElapsedMs }`: the last 300 seconds; a second this node did not measure, such as one before it started, is `null`, never `0` |
| `GET metrics/hourly` | `{ interval, zone, asOf, hourStarts, stores: [{ store, published, received }], unavailable }`: the last 24 hours, cut at UTC hours (`interval` is `PT1H`, `zone` is `UTC`); `hourStarts` holds each hour's start, oldest first, the last being the current, still running hour. Each series is `{ succeeded, failed }`, one count per hour in `hourStarts` order: rows added in that hour that are in that status now. Every store is read at once within the store timeout; a store that fails or runs out of time has no `published` and no `received` and is named in `unavailable`, so it never reads as zero. An additional outbox has no `received`, because it holds no received rows. An hour the store did not count, as when the hour turned between this node's clock read and the store's, is `null` |

A read refuses bad input with the `400` validation problem commands answer: `errors` names each refused field with its message, the `rules` extension names the rule it broke, and `detail` is the first message. The fields are `status` (not a message status), `retrying` (combined with `status` or `terminal`), `before` (not later than `from`), `after` (not a cursor the list returned, or one read with another sort, direction, filter, or search), `targetKind` and `operationType` (not a member, case ignored), all with rule `invalid`; `lane`, `sort`, and `direction` (not a member) with rule `enum`; `messageId` on `received/generations` with rule `required`; and, on a search, `content` with rule `required` when neither `content` nor `correlationId` is set, `content` (over 1000 characters) or `correlationId` (over 200) with rule `invalid`, and `body` with rule `json` when the body is not a JSON object. The query is checked before the store, so a bad query with an unknown `store` answers `400`, not `404`.

## Commands

Every command runs through the shell's command routes at `{base}/api/messaging/commands`, so a viewer sees each one disabled with `Needs operate permission` and a call answers `403`. A call on one object the rules refuse changes nothing and answers `409` with the reason, such as `Release first`. A command that writes a row needs an operator with a stable name, because the store records who acted; a user without one sees `Sign in with a named account`. Re-execute, Purge, Re-send, Delete, Revoke, and Force open ask for a confirmation; no single-row command needs a reason, but a re-send or delete records the reason given on its receipt with the operator's name.

| Command | On | Allowed when |
| --- | --- | --- |
| `messaging.re-execute` | `received` | failed for good and not held (`Release first`); runs the consumer again as a new generation |
| `messaging.purge` | `received` | failed for good and not held (`Held — release first`) |
| `messaging.hold` / `messaging.release` | `received` | finished and not held / held; each is the other's inverse |
| `messaging.re-send` | `published` | `Failed` with no retry pending (`Not failed`) |
| `messaging.delete` | `published` | not waiting for a send or a retry |
| `messaging.dispatch-now` | `published` | a pending scheduled delivery (`Not a pending scheduled delivery`) that no sender has leased (`Leased by a node — try again shortly`) |
| `messaging.revoke` | `published` | a pending scheduled delivery, leased or not |
| `messaging.reset-circuit` / `messaging.force-open` | `circuit` | not closed / not open (`Already closed`, `Already open`) |
| `messaging.reset-backpressure` | `node` | the node runs a retry processor (`No retry processor on this node`) that is backed off (`Not backed off`) |

A row that is still being processed is refused with `Still processing`, and a row that changed between its read and the write with `Changed since it was read`. The inbox commands act on an inbox generation: a received row stored without one, by a store that predates generations, is refused with `Not supported by this store`, as is a scheduled command on a provider without scheduled-delivery operations. Re-send and Delete also work on a published row of an additional outbox: the change runs on that outbox's store, and its receipt, which names the outbox in `OutboxName`, goes to the main store's ledger. The two writes are not atomic, so a receipt that fails after the row changed leaves the row `Done` with the note `The receipt was not recorded: …` in its reason, and the change stands. The scheduled-delivery commands on an outbox row are refused with `Not supported for additional outboxes`, because scheduled-delivery operations live in the main store.

Circuit and node commands act on the node that serves the call, write no receipt, and are recorded only in the operator log. With forwarding off, an id naming another node, for a row as for a circuit or node, is refused with `Served by another node`; with forwarding on, the command goes to that node, as [Forwarding to other nodes](#forwarding-to-other-nodes) describes. With Kubernetes discovery (an `INodeDiscoveryProvider` registered and no `CurrentNodeIdentity`), a call reaches whichever pod the Service routes it to, so both are disabled with `Acts on one pod; Kubernetes discovery addresses a Service`.

Re-execute, Purge, Re-send, and Delete run over a filter as Operations, which always need a reason (a confirm without one answers `400` with `errors.reason`). The filter is the list scope `{ store, status, terminalOnly, consumer, name, fingerprint, lane, messageId, from, before }`, read as the list reads them: `lane` is `Bus` or `Queue`, `from` is inclusive and `before` exclusive on the added time, and a `before` not later than `from`, or another lane, answers `400` on `filter`. The filter also takes the list's search, `content` (at most 1,000 characters) and `correlationId` (at most 200), matched as the search matches them, so `Select all matching` works on a searched list too. The text travels only in the confirm's body and the encrypted scope token, never a URL. The operator log records the filter with each searched value replaced by `(redacted)`, and the module writes it in no log line. A search reads every row its other filters leave, so the confirm's count and each chunk's read wait at most `SearchTimeout`: a confirm out of time answers `400` on `filter` and confirms nothing, and a chunk out of time answers `400` on `cursor` or `token`, changes no row it had not reached, and the Operation panel shows it stopped incomplete with the reason. Each chunk reads its rows again with the search, so a row is changed only while it still matches. A Re-send or Delete Operation whose `store` names an additional outbox runs over that outbox, with each receipt in the main store; Re-execute and Purge run over the main store only, and a filter naming an outbox or an unknown store answers `400` on `filter`. Confirm freezes the scope on the store's latest change marker, and each chunk writes a row only while its marker is unchanged, so a row another chunk, node, or earlier run already changed is skipped with its reason, never written twice; an Operation stopped part-way and run again on another node changes each row once. A re-execute scope leaves held rows out. One row's failure, such as a main-store receipt that could not be written, is reported as `Failed` on that row while the Operation runs on.

## Screens

The Dashboard shows what needs attention, the stored messages by status as one donut per kind (with shares only while every count behind them is exact, else the counts as a list), the live five minutes as a rate chart and a consume-latency chart on one time axis, the last 24 hours per hour (a store that did not answer is named, never drawn as zero), and the newest failures. The Published and Received lists search content in the page header, on `Enter` or Search only; the correlation id, message id, lane, and an added-time range with presets sit behind More filters. The content and correlation id searches live in the view's state, never in the URL, and show as chips to clear; the message id, lane, and range are in the URL. A search that ran out of time says so, with Retry, and never reads as no match. The lists sort by name or added time on the server and show the message id and the exact time beside the relative one. A message's panel groups its fields as Identity, Delivery (for a published message also the transport it is sent through, `default` for the host's default transport), and Correlation (read from the headers, with `Open trace` beside the correlation id; `T` opens the same trace page), shows the body without the envelope's headers, and, for a received message, each inbox generation with its number, current or replay origin, expiry, and recovery. Scheduled deliveries filter by message name; Consumers show each message's implementing type and handler method and filter by message; Nodes show each instance's address, port, and tags, and Ping one node or every node, each answer on its node's row.

The package embeds the module's front end, which declares the sidebar and its count keys.

## Ping

`POST nodes/ping` asks this node to reach other Messaging nodes and says, per node, whether each answered. The body is `{ "node": "<name>" }` to ping one node, or `{}` (or no body) to ping every node discovery lists, in discovery order. A ping changes nothing, yet it makes this node call others, so it needs the module's operate permission, as a command does: a viewer gets `403`. It is not a command: it asks for no confirmation and writes no operator-log entry.

- **Discovered nodes only.** A name resolves against discovery entries, as `?node=` does, so a caller cannot point this node at an address of its choosing. A name discovery does not list answers `404` with `Discovery lists no Messaging node named '{node}'.` and no call leaves the node. A name that is this node answers `400` on field `node` (rule `invalid`); "every node" means every other node. With forwarding off, discovery lists none, so `{}` answers an empty `items`.
- **One signed call per node.** Each ping is a `GET` to the node's `/peer/ping`, sent as every forwarded call is (signed in key mode), and the node answers only a call its forwarding admits. At most 8 nodes are pinged at once, and each node gets 3 seconds, across all its discovered instances, to answer.
- **One result per node.** `outcome` is `answered`, with the round trip in `latencyMs`; `timeout`, when the node did not answer in its 3 seconds, with `reason`; or `failed`, with the node's `state` from the table in [Forwarding to other nodes](#forwarding-to-other-nodes) and its `reason`. A node that answers without serving the ping, such as one on a build without it, is `failed` with `state: answering` and the node's status code in `status`.
- **No fan-out from a forwarded call.** A ping another node forwards answers `404`, so a ping never travels further than one hop.

## Forwarding to other nodes

A node whose stores another node cannot read, such as a service with its own database, shows its rows on every other node through forwarding: the serving node sends the request to the node that owns the rows, which reads its own stores, formats its own payloads, and answers. Forwarding is off until `Forwarding.Key` or `Forwarding.Policy` is set, and needs an `INodeDiscoveryProvider` (Consul or Kubernetes discovery) to list the nodes. Give every node of the deployment the same mode, and make each discovery entry's name the node's `NodeName`.

```csharp
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "ops.view"; host.OperatePolicy = "ops.operate"; })
    .AddMessaging(messaging =>
    {
        messaging.NodeName = "orders-api";
        messaging.Forwarding.Key = builder.Configuration["Dashboard:ForwardingKey"]; // 32+ characters, from a secret store
        messaging.ForwardedAccess = MessagingForwardedAccess.Operate; // default View: forwarded calls only read
    }));
```

**Verify:** an operator's `POST {base}/api/messaging/nodes/ping` with `{}` answers one item per discovered node with `outcome: answered`; a node whose key differs reads `failed` with `state: key-rejected`.

- **Admission.** A forwarded call is admitted by the scheme `HeadlessDashboardForwarding`: with `Policy` set, the host's policy decides and the key handler is not used; otherwise the call must carry a valid signature under the shared key. The operator and access headers are read only after admission, and only on the Messaging module's endpoints, so a browser that sends them without a signature is judged by its own session. The outbound calls use the named `HttpClient` `Headless.Messaging.Dashboard.Forwarding`; in policy mode, configure it to present the node's service identity, such as a client certificate.
- **Access.** The serving node checks its own operator's view and operate permissions and sends the result. The peer acts at the lower of that decision and its own `ForwardedAccess`, and records the operator as `{name} via {serving node}`, such as `ops via orders-api`, on receipts and in the operator log. A forwarded write that asserts view access is refused with `403`, and so is a write whose operator is a placeholder rather than a named account.
- **Trust boundary.** An admitted node is trusted: a peer believes the operator name and the access decision a node it admitted sends, so a node that holds the key, or passes the policy, can read every peer's rows and, where the peer's `ForwardedAccess` is `Operate`, run commands as any named operator. In key mode the key never leaves the node, so an address someone adds to discovery receives only signed calls it cannot replay. It still receives the forwarded call itself, the operator's name and the request, and can answer with forged rows the serving node shows as that node's: give discovery its own access control (Consul ACLs, a Kubernetes namespace only the deployment writes) and keep the key in a secret store. Where discovery is not trusted, or a peer allows `Operate`, use policy mode with a service identity the peer verifies, such as mutual TLS through the named `HttpClient`'s client certificate, so admission rests on a credential the receiving node checks rather than on a shared secret.
- **The key stays secret.** It never appears in a request, the manifest, logs, startup errors, or responses. Neither does a signature or the signed string: a refused call gets only the `key-rejected` refusal.
- **Which node answers.** A read names its node with `?node=`; a command or Operation names it through the object ids or the filter's `node`. A node name resolves only against discovery entries: a name discovery does not list answers `404` with `Discovery lists no Messaging node named '{node}'.` and no call leaves the node, so a caller cannot point a node at an address of its choosing. A call another node forwarded is always served where it lands, and a node never forwards to itself.
- **Relayed paths.** A relayed read sends the peer the path segments this node routed on, each escaped again, so an id reads the same on both nodes and an escaped slash stays inside its segment. A segment that is `.` or `..`, or that holds a backslash, `?`, `#`, or a control character once decoded, answers `400` with the validation problem on field `path` (rule `invalid`), and no call leaves the node. A relayed read never reaches another node's `/peer` endpoints, which carry only node-to-node calls.
- **Relayed answers.** The peer's status code and body pass through, but never its content type: a success is served as `application/json` and any other status as `application/problem+json`, always with `X-Content-Type-Options: nosniff`, so a peer cannot have the dashboard's origin serve HTML or script.
- **No redirects.** The forwarding client follows no redirect, because a redirect would carry the signed call and the operator's name to whatever host the peer names. A peer that answers `3xx` counts as `not-answering`. A host that replaces the named client's primary handler, as policy mode does for a client certificate, keeps `AllowAutoRedirect = false` on it.
- **Answer size.** A peer's answer is read up to `Forwarding.MaxResponseBytes` (4 MiB by default); a larger one is not read, and the peer counts as `not-answering`.
- **No local fallback.** A peer that cannot answer is never replaced with this node's rows. A read answers `502` with the state in `X-Headless-Node-State` and the problem's detail; a command answers `409` with the same sentence.

| State (`X-Headless-Node-State`) | What the operator reads |
| --- | --- |
| `not-answering` | `Node {node} is not answering`: no answer in time, a connection failure, a redirect, or an answer larger than `Forwarding.MaxResponseBytes` |
| `key-rejected` | `Node {node} rejected this node's forwarding key` (the peer answered `401`: a signature under another key, a clock off by more than `MaxClockSkew`, a replay, or a node on a build that predates signing) |
| `policy-denied` | `Node {node} denied this node by its forwarding policy` |
| `view-only` | `Node {node} accepts view-only forwarded calls`: the peer keeps the default `ForwardedAccess`; its commands show disabled with this reason |
| `insecure-peer` | `Node {node} is an insecure peer: its address is not https, and AllowInsecurePeers is off`; no request is sent |

- **HTTPS.** A peer whose discovery address is `http://` gets no request unless `AllowInsecurePeers` is set, because the operator's name, the rows, and in policy mode the credential would cross the network in clear text. Set `Scheme = "https"` on `ConsulDiscoveryOptions` or `K8sDiscoveryOptions`.
- **Needs you** lists every node's items, each title ending in ` (node {node})`; another node's item ids and group keys are prefixed with `{node}:` and its filter names the node. Each peer's answer is cached for 30 seconds. A peer that cannot answer becomes one critical item, `node-state:{node}`, titled with its state; two discovery entries with the same name are one node and give one item.
- **Live changes.** A stream on this node that subscribes to `messaging.received@{node}`, `messaging.published@{node}`, or `messaging.circuit@{node}` makes this node read that peer's change feed about once a second while the topic is watched, and stops with the last watcher. A topic that names a node discovery has not listed sends that node no call and marks nothing stale: this node checks discovery about once a second and starts reading the node's feed once discovery lists it. While the peer answers that it has more, this node reads its next page in the same second, up to 10 reads, so a burst on the peer is not spread over many seconds. The changes arrive on the node topic. The peer's feed carries its circuit changes with its rows, and a peer that restarted sends every circuit as it is now, so a circuit the earlier process left open does not stay open in the views. A peer that stops answering or leaves discovery marks only its own views stale (`node.stale`), within 5 seconds of leaving discovery, and loses its cursor. A peer that no longer knows the cursor, such as after a restart, answers `410`; the stream starts a new cursor and sends `node.reset`, so that node's views refetch.
- **Id lookup.** A lookup of a full object id, such as `orders-api~primary~{id}`, asks only the node the id names; any other lookup asks every peer. Each peer's answer is reused for 5 seconds, and lookups of the same text at the same time share one call. The reused answers of a peer are dropped when this node runs a command or an Operation chunk on it or relays a change from it; a peer that did not answer is asked again on the next lookup.
- **Operations.** Confirm and every chunk of an Operation whose filter names another node run on that node, which holds them to its own limits and checks, as it holds a local Operation. Its scope step counts up to the shell's 100,000-row limit, whatever the serving node asks, refuses a larger scope with the validation problem a local confirm answers (field `filter`, rule `maxCount`), and signs the scope it counted into the marker it answers; the serving node's confirm then answers that same problem. The serving node keeps its scope token and sends the signed marker back on each chunk with the cursor, the operator, the reason, and the arguments. The peer runs a chunk only over a scope it signed, at the shell's 500-row chunk size, and checks the reason and the arguments against the command as a local confirm does; a refusal is the validation problem a local call gets. The peer's ledger records each chunk as `{name} via {serving node}`. The marker is protected with the peer's Data Protection key ring, so every instance of one node shares it, as the shell's Operation tokens already need.

A host refuses to start when forwarding runs under `WithNoAuth()`, when the key or the previous key is shorter than 32 characters or blank, when the previous key equals the key or is set without one, when `MaxClockSkew` is not greater than 0 or above 10 minutes, when both a key and a policy are set, or when the policy is not registered, has no authentication scheme, uses the forwarding scheme, or is also the dashboard's view or operate policy. None of these messages quotes the key.

### Key mode: signed calls

Each forwarded call carries `X-Headless-Forwarding-Signature: v1;ts={unix seconds};nonce={16 random bytes, base64url};sig={base64url HMAC-SHA256}`. The HMAC key is derived from `Forwarding.Key` with HKDF-SHA256 and a fixed purpose label. The signed string joins with `\n` the version tag, the timestamp, the nonce, the method, the request target as sent (escaped path and query), the hex SHA-256 of the body, and the hop, operator, and access headers. The receiving node reads the body once, within the host's request size limit, and hands the same bytes to the endpoint.

A peer admits the call only when all of these hold, and otherwise answers `401` with `X-Headless-Forwarding-Refusal: key-rejected` and a fixed reason:

- The signature header is present and well formed. A call that carries the `X-Headless-Forwarding-Key` header of older builds is refused, signed or not; there is no fallback.
- The timestamp is within `Forwarding.MaxClockSkew` (60 seconds by default) of the peer's `TimeProvider`, either way. Keep every node's clock on NTP; a node whose clock drifts past the skew is rejected by every peer.
- The signature matches under `Key`, or under `PreviousKey` when set, compared in fixed time.
- The nonce has not been used inside the window. Each node remembers the nonces it admitted in memory, up to 100,000 at once, and refuses new calls rather than forget one still in its window. The memory is per node: two instances behind one discovery address do not share it.

A proxy between nodes must forward the request target and the signed headers unchanged, or the signature no longer matches.

#### Rotating the key

A node signs with `Key` and admits `Key` and `PreviousKey`, so a rotation that never refuses a call takes three rolling restarts. Finish each roll on every node before you start the next:

1. Set `PreviousKey` to the new key, keeping `Key` as it is. Every node now admits the new key and still signs with the old one.
2. Swap them: `Key` to the new key, `PreviousKey` to the old one. A rolled node signs with the new key, which the nodes not yet rolled admit since step 1.
3. Remove `PreviousKey`.

Rolling straight to step 2 also works, but until the roll ends, a node that already signs with the new key is refused by the nodes that have not restarted yet.

#### Upgrading

A node on a build that sends the raw key and a node that signs cannot talk to each other: each rejects the other's calls as `key-rejected`. Upgrade every node of a deployment together, or accept that forwarding between old and new nodes fails until the rollout ends.

## Dashboard-only host

`AddMessagingStandalone(...)` adds the Messaging module to a host that has no Messaging stores of its own and shows the nodes discovery lists, each through forwarding. It needs forwarding (`Forwarding.Key` or `Forwarding.Policy`) and a registered discovery provider; without either the host fails to start with a message naming the missing piece. A read without `?node=` opens on the first node discovery lists; with none reachable it answers `503` with `No Messaging node reachable.` and the discovery state, such as `Service discovery lists no other Messaging node.` A command that names no node is refused, because this host holds no rows.

```csharp
using Headless.Dashboard;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.K8s;

builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "ops.view"; host.OperatePolicy = "ops.operate"; })
    .AddMessagingStandalone(
        messaging =>
        {
            messaging.NodeName = "ops-dashboard";
            messaging.Forwarding.Key = builder.Configuration["Dashboard:ForwardingKey"];
        },
        k8s => k8s.Scheme = "https")); // Headless.Messaging.Dashboard.K8s
```

**Verify:** without forwarding or a discovery provider the host fails to start, naming the missing piece. Once it starts, `GET {base}/api/messaging/nodes` lists the discovered nodes; with none reachable, a read without `?node=` answers `503`.

`AddMessagingStandalone(Action<MessagingDashboardOptions>)`, its `IServiceProvider` and `IConfiguration` overloads, and the Kubernetes overload above take the same options.

## Migrate from `MapHeadlessMessagingDashboard`

The Messaging Vue dashboard, its endpoints under `/messaging/dashboard`, and its node switcher are removed. The Messaging module replaces them inside the shell, with live changes over the shell's stream. To migrate a host:

1. Delete `UseDashboard(...)` from the `AddHeadlessMessaging(...)` delegate, and delete `app.MapHeadlessMessagingDashboard()`.
2. Add `AddMessaging()` to `AddHeadlessDashboard(...)`, and map the shell with `app.MapHeadlessDashboard()` if the host does not already.
3. Move authentication to the shell: its `WithHostAuthentication`, `WithBasicAuth`, `WithApiKey`, or `WithNoAuth` call covers every module. For separate Messaging rights in host mode, set `ViewPolicy` and `OperatePolicy` on `MessagingDashboardOptions`.
4. Point bookmarks and links at `{shell base}/messaging` (by default `/headless/messaging`). Nothing serves `/messaging/dashboard` any more.
5. Remove the `SetBasePath`, `SetCorsOrigins`, `SetCorsPolicy`, and `SetStatsPollingInterval` settings: the shell is same-origin, serves every module under its own base path, and streams changes instead of polling. Replace `SignOutHeadlessDashboardAsync(MessagingDashboardOptionsBuilder.AuthenticationName)` in the host's sign-out with `SignOutHeadlessDashboardAsync()`, which ends the shell's session.
6. Replace node switching (the `messaging.node` cookie and the gateway proxy that relayed to the chosen node) with [Forwarding to other nodes](#forwarding-to-other-nodes), and `AddMessagingDashboardStandalone(...)` with `AddMessagingStandalone(...)`. `MessagingDashboardCache`, `ConsulDiscoveryOptions.MatchPath`, and `OperatorActorRequiredProblem` are removed with no replacement: Consul now registers a TCP health check for each node.

```csharp
using Headless.Dashboard;
using Headless.Messaging.Dashboard;

builder.Services.AddHeadlessMessaging(setup => setup.UsePostgreSql(builder.Configuration.GetConnectionString("Orders")!));
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "ops.view"; host.OperatePolicy = "ops.operate"; })
    .AddMessaging());

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHeadlessDashboard();
```
