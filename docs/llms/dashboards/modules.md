---
domain: Dashboards
packages:
---

# Modules

> Part of [Dashboards](../dashboards.md). The .NET contract of a dashboard module: `IDashboardModule`, its capability interfaces, the live stream, peer calls, and operator commands and Operations.

A module is one feature's data and UI under the shell. `Headless.Jobs.Dashboard`, `Headless.Messaging.Dashboard`, and `Headless.Cluster.Dashboard` ship the three built-in modules. The front-end SDK a module's UI is built with (`@headless/dashboard-sdk` and `@headless/dashboard-vite`) is not published to npm in this release. A module without a readable embedded `manifest.json`, which that SDK's build writes, fails the host's start. Read this page to understand what the built-in modules do, or to implement the server side of a module.

## Implement `IDashboardModule`

| Member | Description |
| --- | --- |
| `Id` | The module's URL segment, API group (`{base}/api/{id}`), and stream topic prefix. It must equal the id it was registered under: a lower-case kebab token. `api`, `auth`, `m`, `needs-you`, `sign-in`, `shell`, and `trace` are reserved. |
| `DisplayName` | The name the shell shows. |
| `Assets` | A `DashboardModuleAssets` whose `Files` provider is rooted at the module's front-end build, with `manifest.json` at the root. Use a provider over resources embedded in the module's assembly. |
| `MapEndpoints(endpoints)` | Maps the module's own API on `endpoints.Routes`, at `{base}/api/{id}`, behind the module's view policy. Wrap each state-changing endpoint in `endpoints.RequireOperate(...)` to require the operate policy too. The shell does not add it, so an unwrapped write is open to every viewer. Every state-changing request needs `X-Requested-With`. The `commands` segment belongs to the shell. |

The shell resolves the module as a singleton, so its members must be thread-safe. It reads `Id`, `DisplayName`, `Assets`, and every declared list (commands, related groups, formatter kinds) once, when it builds its manifest at startup. Those reads must not open a connection or run a query.

A module package wraps `AddModule` in its own extension, the way `AddJobs` does:

```csharp
using Headless.Dashboard;
using Microsoft.Extensions.FileProviders;

public sealed class ReportsDashboardOptions : DashboardModuleOptions;

public sealed class ReportsDashboardModule : IDashboardModule, IDashboardOverviewSource
{
    public string Id => "reports";

    public string DisplayName => "Reports";

    // The module's front-end build, embedded in this assembly under the Reports.Dashboard resource namespace.
    public DashboardModuleAssets Assets { get; } =
        new() { Files = new EmbeddedFileProvider(typeof(ReportsDashboardModule).Assembly, "Reports.Dashboard") };

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        endpoints.Routes.MapGet("/runs", () => Results.Ok(Array.Empty<string>()));
        endpoints.RequireOperate(endpoints.Routes.MapPost("/runs/{id}/cancel", (string id) => Results.NoContent()));
    }

    public Task<IReadOnlyList<DashboardFigure>> GetOverviewAsync(
        DashboardOverviewRequest request,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<DashboardFigure> figures = [new DashboardFigure { Id = "reports.failed", Label = "Failed", Value = 0 }];
        return Task.FromResult(figures);
    }
}

public static class SetupReportsDashboard
{
    extension(HeadlessDashboardSetupBuilder dashboard)
    {
        public HeadlessDashboardSetupBuilder AddReports(Action<ReportsDashboardOptions>? configure = null)
        {
            dashboard.Services.AddOptions<ReportsDashboardOptions>().Configure(options => configure?.Invoke(options));

            return dashboard.AddModule<ReportsDashboardModule, ReportsDashboardOptions>("reports");
        }
    }
}
```

**Verify:** start the host. `AddModule` throws `ArgumentException` for an id that is not a lower-case kebab token or is reserved, and `InvalidOperationException` for an id already added. A module with no readable `manifest.json`, or whose manifest names another id, fails the start with `InvalidOperationException`. A module built against another major version of the dashboard SDK, or a newer one, is refused: the page shows the refusal with both versions, and the module's endpoints are not mapped.

## Capabilities

Everything beyond `IDashboardModule` is an optional capability interface on the same class, found by a type check:

| Capability | The shell uses it for |
| --- | --- |
| `IDashboardAttentionSource` | The Attention popover and each product tab's dot: the items that need an operator, grouped by `GroupKey`. Called when the shell loads and whenever the module's attention changes, so keep it to one cheap query. |
| `IDashboardOverviewSource` | `GET {base}/api/overview`, which a product dashboard reads: up to three `DashboardFigure`s per module, each with its comparison, and, through the default member `GetScheduleAsync`, the module's `DashboardScheduleTrack`s on the Overview's schedule strip. A comparison whose `Value` is `null` means the earlier period has nothing to compare; the shell shows its `Label` alone, so make the label say why (`no runs finished in the previous 24 h`). |
| `IDashboardCountSource` | The counts beside sidebar entries: a `DashboardCount` (`Count`, `Capped`) per `countKey` the module's sidebar declares. `GET {base}/api/counts?product=` asks every module of one product and answers `{ product, counts: { key: { count, capped } }, stale }`; when two modules answer one key, the first registered wins. The shell refetches on live changes, so keep each count to one cheap, capped query. |
| `IDashboardObjectResolver` | The palette's pasted-id lookup (`POST {base}/api/resolve` with `{ "query": "<id>" }`, trimmed, at most 256 characters). Answer with an exact-id lookup, never a text search. A `GET` answers `405`. |
| `IDashboardTraceSource` | The trace page: every object that carries one correlation id, drawn as one tree across modules; see [Traces](#traces). Match the id exactly and ordinally through an index, return at most the module's own limit of items oldest first, and set `Limited` when more existed. |
| `IDashboardRelatedSource` | Related objects of any module's object, drawn as labelled groups (`GET {base}/api/related?module=&kind=&id=`). `GroupIds` declares every group id up front, because each is a contribution the host can disable or reorder. |
| `IDashboardPayloadSource` | One object's payload (`GET {base}/api/payload?module=&kind=&id=`). |
| `IDashboardPayloadFormatter` | Redacts and formats payloads of its `ObjectKinds` on the server, in responses and stream events, before they reach the browser. Without one, JSON goes out as it is. Keep it free of I/O. |
| `IDashboardChangeSource` | The live stream's cross-node change reads; see [Live stream](#live-stream). |
| `IDashboardCommandSource` | Operator commands; see [Commands and Operations](#commands-and-operations). |
| `IDashboardOperationRunner` | Commands over a filter, run as an Operation; see [Commands and Operations](#commands-and-operations). |
| `IDashboardPeerAccess` | Node-to-node calls, such as a node forwarding a read or a command to the node that owns the data. `PeerAuthenticationScheme` and `PeerPolicy` name a scheme and a policy the module registers with the host; see [Peer calls](#peer-calls). |
| `IDashboardNodeTopicObserver` | Learning when this node's streams start and stop watching one of the module's node topics, `{module}.{kind}@{node}`; see [Live stream](#live-stream). |

The interfaces grow by new capability interfaces and default interface members. An existing member never gains a parameter; request and result records grow by `init` members instead.

### Aggregated reads

Attention, Overview, lookup, traces, related objects, and counts call every module in parallel, list only the modules the user may view, and wait at most `AggregationTimeout` for each. A module that times out or throws answers `stale: true` in its place.

A module that reads several stores or nodes never throws for one of them. It answers with what the others returned and names the sources that did not answer: `DashboardCount.UnavailableSources`, or, through the default members `GetAttentionAnswerAsync` and `GetOverviewAnswerAsync`, `DashboardAttentionAnswer.UnavailableSources` and `DashboardOverviewAnswer.UnavailableSources`. The shell sends them as `unavailable` beside the count or the module section, leaves the field out when every source answered, and keeps `stale` for a whole module that failed. The sidebar shows a partial count as `48,120+`, with the sources in its accessible name: `48,120+ · outbox-2 unavailable`. A module that never overrides the default members answers as before.

Counts stop at 10,000 and report `capped: true`. `api/counts` answers `400` without `product`, `404` for a product no registered module joins, and `403` when the user may view none of the product's modules.

### Traces

`POST {base}/api/trace` with `{ "correlationId": "<id>" }` asks every `IDashboardTraceSource` the user may view for `GetTraceAsync`. The id is never trimmed, so `c-42` and `c-42 ` are two ids. An empty id, one over 200 characters, or a body that is not JSON answers `400`. The id travels only in the body, and the shell never writes it to a log at Information level.

Each `DashboardTraceItem` carries its own causal `Key` and the `Causation` key it stored, each a `DashboardTraceKey` of kind `Run` (a job run id), `Message` (a published message id), or `Delivery` (a received generation's storage id). Give a received generation `DeliversMessageId`. Give every item a `Store` and `StorageId`: the shell lists an item once when two nodes that share a store return it. The shell places each item by these rules:

- A received generation sits under the published message it delivers, not under its `Causation`, which names the publisher's parent.
- Any other item sits under the item whose `Key` equals its `Causation`, of the same kind.
- A `Message` causation sits under that message's received generation when exactly one is on the page. Otherwise it sits under the published message, tagged `consumer-not-recorded`.
- An item whose cause is not on the page is a root tagged `parent-not-on-page`. The tag never names a hidden module.
- A cycle breaks at the first repeated item, which becomes a root tagged `cycle`.
- Roots and siblings sort by `At`. An item whose `At` is `null`, because it has no time yet, such as a chain run waiting on its parent, sorts after every timed item. A child stamped before its parent stays under it, tagged `clock-skew`.

The answer is `{ modules, rows, hidden }`:

- `modules` holds one section per module: `id`, `displayName`, `stale`, `unavailable` (from `DashboardTraceAnswer.UnavailableSources`, left out when every source answered), `limited`, `count`, and `list` (`{ path, state }` from `ListLink`: the module's own list, opened with `state` as history state so the id stays out of the URL).
- `rows` holds the tree depth first: `module`, `target`, `title`, `status`, `statusRole`, `at` (`null` for an item with no time yet, which the page shows as `Waiting on its parent`), `timestampKind` (`ran`, `due`, or `added`), `node`, `depth`, and `tags`.
- `hidden` counts the trace-capable modules the user may not view.

The page's address is `{base}/trace#<id>`: the id rides in the fragment, which the browser never sends to the server. In a module view, link to it with `OpenTrace` (`@headless/dashboard-sdk/components`), which renders `Open trace`, or disabled with `No correlation id` or `Id longer than 200 characters`, and bind `T` with `useTraceKey(correlationId)`. Build any other link with `traceAddress(id)`, never by hand. A signed-out visit to a trace signs in and lands on the same trace, and the sign-in's `returnUrl` carries no fragment. Go to id and the palette offer `Open trace for <id>` after the objects a pasted text resolves to, for text of one token and at most 200 characters, and Go to id's Enter opens it when no object matches.

**Verify:** `POST {base}/api/trace` for a correlation id your module stores returns its items under `rows`, with the module's section `stale: false`.

### Attention items

A `DashboardAttentionItem` can also say how to resolve it and whether it is still growing:

- **`RecommendedCommandId` and `GroupFilter`.** The id of one of the module's commands, such as its Requeue, and the filter, in the module's own Operation filter shape, that selects every object of the item's group. The recommended command (`Requeue 23`) runs as an Operation over `GroupFilter` from the module's own filtered list, never from the Attention popover: the operator gives a reason, the server counts the scope, and nothing runs before the confirm. The shell offers the button only when both members are set, the module implements `IDashboardOperationRunner`, and the host has not disabled the command or hidden it from the UI. A user without the command's permission sees it disabled with `Needs operate permission`.
- **`RecentCount`.** How many objects joined the item in the module's latest interval, such as the last hour. Above zero, the cause is still growing.
- **`LastSeenAt`.** When the cause last occurred, beside `Since`, when it began.
- **`Kind`.** `DashboardAttentionKind.Work`, the default, for work in the module, such as failed runs. Set `DashboardAttentionKind.HealthCheck` for a check of the host's health, such as a store that does not answer. The Attention popover lists health checks in one `Health checks` section after the product sections, each row led by its module's display name. A module whose items are all health checks gets no section of its own, unless it did not answer. Health checks still count toward the Attention total, the pill's dot, and their product tab's dot.

`GET {base}/api/needs-you` returns these as `recommended` (`{ command, filter }`, where `command` is described for the signed-in user as `GET {base}/api/{module}/commands` describes it, or `null`), `recentCount`, `lastSeenAt`, and `kind` (`work` or `health-check`).

### HTML previews

To show captured HTML, such as an email a host sent in development, return it from a module endpoint with `DashboardResults.HtmlPreview(html)`. Never write the HTML with `Results.Content`: no policy covers a module's own response, so that document runs its scripts with the dashboard's origin whenever it is opened outside the frame. The result writes the HTML as `text/html; charset=utf-8` under its own policy: a CSP `sandbox` with `default-src 'none'`, inline styles, `data:` images and fonts, `base-uri 'none'`, `form-action 'none'`, and `frame-ancestors 'self'`, plus `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, and `Referrer-Policy: no-referrer`. The document's `<style>` blocks and `style` attributes apply. Its scripts, forms, and remote images and fonts do not run or load, even when the operator opens the address in its own tab. Only a page of the host's own origin may frame it, so any page on that origin, not only the dashboard, can embed it for a signed-in operator; the endpoint's authorization decides who may read it.

Inline each `cid:` part as a `data:` URI before you return the HTML. A `cid:` or remote image shows as missing. The module's view loads the endpoint in the SDK's sandboxed preview frame, which reads it like any module `GET`, with the session cookie and no `X-Requested-With`.

```csharp
public interface ICapturedEmails
{
    Task<string?> FindHtmlAsync(string id, CancellationToken cancellationToken);
}

public static class InboxEndpoints
{
    public static void MapEmailPreview(DashboardModuleEndpoints endpoints)
    {
        endpoints.Routes.MapGet(
            "/emails/{id}/html",
            async (string id, ICapturedEmails emails, CancellationToken cancellationToken) =>
                await emails.FindHtmlAsync(id, cancellationToken) is { } html
                    ? DashboardResults.HtmlPreview(html)
                    : Results.NotFound()
        );
    }
}
```

`HtmlPreview` throws `ArgumentNullException` for a `null` document; answer `404` for a message that does not exist, as the example does.

**Verify:** `GET {base}/api/{module}/emails/{id}/html` answers `200` with `Content-Security-Policy` starting `sandbox; default-src 'none'`. Open the address in the browser: the email's styles show, and its scripts do not run.

### Validation refusals

A module's own endpoints refuse bad input the way commands do, so the page reads every refusal one way: a `400` validation problem whose `errors` name each refused field as the JSON body spells it (`children[0].function`), with a `rules` extension naming the rule each field broke, and `detail` holding the first message.

- `DashboardValidation.Problem(ValidationResult)` builds it from a FluentValidation result. A field's rule is the error code the validator set with `WithErrorCode`, `required` for `NotEmpty` and `NotNull`, and `invalid` otherwise.
- `DashboardValidation.Problem(field, rule, message)` refuses one field, for a check that needs the host, such as an unregistered function name.
- The rule names are constants on `DashboardValidation`: `RequiredRule`, `InvalidRule`, `EnumRule`, `MinimumRule`, `MaximumRule`, `MaxLengthRule`, `FormatRule`, and `JsonRule`, which a malformed body breaks under the field `BodyField` (`body`). Pass them to `WithErrorCode` or `Problem`.
- Keep a refusal that depends on stored state, such as a run that can no longer be cancelled, out of the validator. It belongs to the command's rules, which check it again when the command applies.

## Live stream

The page reads live changes from one server-sent-events stream at `GET {base}/api/stream?topics=`, with topics `{module}.{kind}` or `{module}.{kind}@{node}`, comma-separated or repeated, at most 64. The open tabs of one dashboard and one signed-in user share one stream. How the page shares, pauses, and patches it is described with the front end in the repository, not here.

- **Events.** `subscribed`, `change` (module, kind, id, the node when the object has one, topic, version, and the formatted projection), `module.stale`, `attention.changed`, `node.stale`, `node.reset`, `heartbeat`, and `reset`. Each stream receives only modules the user may view and only the topics it asked for, matched exactly.
- **Node topics.** An object whose `ObjectRef.Node` is set, because one node's own store holds it, changes on `{module}.{kind}@{node}` only, such as `messaging.received@node-b`. A stream subscribed to `@node-b` never receives `@node-c`'s changes, and one subscribed to `messaging.received` receives neither. A module that implements `IDashboardNodeTopicObserver` hears `OnNodeTopicWatched` when the first stream on this node subscribes to one of its node topics and `OnNodeTopicUnwatched` when the last one closes; both must return at once. `IDashboardEventPublisher.PublishNodeStale(module, node, stale)` and `PublishNodeReset(module, node)` send `node.stale` and `node.reset` to the streams subscribed to a topic of that module for that node, so only that node's views show `Stale` or refetch; a stream that opens while a node is stale is told at once. Every connection opens with `subscribed` and then replays the modules and nodes that are out of date now.
- **Resume.** Each node keeps its recent events in a bounded ring. A reconnect sends its last event id (the `Last-Event-ID` header, or the `lastEventId` query parameter) and resumes from it. When the node no longer holds that position, the stream sends `reset` and the page refetches every query.
- **Cross-node changes.** While a node has at least one stream open, it reads every module's `IDashboardChangeSource` each `ChangePollInterval`, starting from `GetStartCursorAsync`. No broker is involved: the module's store is the only shared state. A node with no open stream reads nothing. A read that throws marks that module's views `Stale`; the reader backs off and retries with the same cursor. A read that reaches only some sources returns a `DashboardChangeBatch` whose `UnavailableSources` names the others; the module stays live, and `module.stale` carries them as `unavailable` with `stale: false`.
- **What a change source's store must provide.** A change marker stamped by the store (a sequence or the store's clock) in the same statement as every write the dashboard shows; an index on it; reads from the cursor minus an overlap window longer than the store's longest write transaction; and a bounded page per read. A read that returns a full page sets `DashboardChangeBatch.HasMore`, and the node reads the next page in the same interval, up to 10 reads. The shell drops a change whose `Version` it already sent for that object.
- **Same-node changes.** `IDashboardEventPublisher`, a singleton the shell registers, pushes a change to this node's streams at once, after the module's store commits it. Changes to one object within 250 milliseconds coalesce into one event. A version not newer than the last sent is dropped; versions compare as integers when both parse as one, and ordinally otherwise, so use a store sequence or a fixed-width timestamp. Other nodes still learn of the change from their own reads.
- **Closing.** A stream closes at the session ticket's expiry, at its next revalidation in host mode when `Revalidate` is set, and after 5 minutes under Basic and API key. The browser reconnects, which checks the session, credential, and module permissions again; a refused reconnect that answers `401` ends the session in every tab. A request whose `Origin` is not the shell's own gets `403`. A stream never delays the host's shutdown.

## Peer calls

A module that implements `IDashboardPeerAccess` admits calls signed in by its `PeerAuthenticationScheme` on its own API at `{base}/api/{id}`, its command endpoints, and its Operation endpoints, beside the dashboard's own sign-in, in every mode except `WithNoAuth`.

- Such a call needs no session. Its principal must pass `PeerPolicy`, and then skips the view, operate, and command permissions, because the node that forwarded the call checked them for its own user.
- A peer the policy refuses gets `403`. A call with neither a session nor a peer credential still gets `401`.
- The scheme admits nothing on another module's endpoints or on the shell's own.
- Register the scheme and the policy before `MapHeadlessDashboard()`; mapping fails when the scheme is missing.

## Commands and Operations

A module's `IDashboardCommandSource.Commands` declares its commands once, at startup. Each `DashboardCommand` has an `Id` prefixed with the module id (`jobs.requeue`), a `Label`, the `ObjectKind` it acts on, and the rules the shell enforces:

| Member | Default | Effect |
| --- | --- | --- |
| `Permission` | `Operate` | `Operate` needs the module's operate policy; `View` needs only its view policy. |
| `Capability` | none | A named capability such as `jobs.delete`. When the host registers an authorization policy with that name, the command needs that policy instead of the operate policy. |
| `Confirmation` | `None` | `Confirm` (a dialog) or `Typed` (type the row count). A confirmed command over more than 100 rows is always `Typed`. |
| `InverseCommandId` | none | The command that undoes this one. A command with an inverse runs without confirmation, and the shell offers Undo. |
| `RequiresReason` | `false` | The operator must give a reason, at most 1,000 characters. An Operation always needs one. |
| `Visibility` | `Ui \| Api` | The channels that may call it. |
| `Arguments` | none | `DashboardCommandArgument`s: a small JSON Schema subset (`Text`, `WholeNumber`, `Number`, `Toggle`, `DateTime`, `Json`; required, allowed values, minimum, maximum, maximum length, not before now) that the shell renders as a form and validates on the server. |

Two argument kinds carry text with a meaning the shell checks:

- **`DateTime`** is ISO 8601 text with an offset, such as `2026-12-15T08:00:00Z` or `2026-12-15T10:00:00+02:00`; read it with `JsonElement.GetDateTimeOffset()`. Text without an offset, or with a `+0200` offset that has no colon, is refused with rule `format`, because the server cannot tell which zone it meant. `NotBeforeNow` refuses an instant before the registered `TimeProvider`'s now with rule `minimum`. The schema states the kind as `"type": "string", "format": "date-time"`, plus `"notBeforeNow": true` when set. The form reads the date and time to the minute in the dashboard's display time zone and sends the UTC instant. A date typed without its time is refused as incomplete, on an optional argument as on a required one.
- **`Json`** is JSON text, such as a request body, at most `MaxLength` characters. The server parses it and refuses text that does not parse with rule `format`, even from an API call that skipped the form. The schema states it as `"type": "string", "contentMediaType": "application/json"`.

A row result can name the object the command created for it, such as the run a duplicate made: set `DashboardRowResult.Created` to its `ObjectRef`. The shell returns it as the row's `created` (`module`, `kind`, `id`, and `node` when set) and leaves the field out of a row without one.

### Command endpoints

The shell owns the endpoints under `{base}/api/{module}/commands`:

- `GET` lists the module's commands for the caller. `GET allowed?kind=&id=` asks `GetAllowedCommandsAsync` which commands each object allows now (at most 500 objects), so the page disables a control with the module's reason instead of offering an action the module would refuse.
- `POST {commandId}` runs the command over `ids` in a JSON body that may also carry `arguments`, `reason`, `confirmed`, and `typedCount`. At most 100,000 rows per call.
- The shell checks every rule before `ExecuteAsync` runs. A command the host disabled, or one the call's channel may not use, answers `404`. A failed permission answers `403`. The body, reason, argument schema, and confirmation answer a `400` validation problem naming each field, with the broken rule in its `rules` extension. The page sends `X-Headless-Dashboard-Channel: ui`; a call without it is an API call. The channel is claimed, not proven, so it decides only which commands are offered and what the log records.
- A module that refuses the whole call, because the object is not in a state the command applies to, returns a `DashboardCommandResult` with `Refusal` set and changes nothing. The shell answers `409` with the reason as the problem's `detail` and writes no operator-log entry.

### Operations

A command over a filter runs as an **Operation**, through the module's `IDashboardOperationRunner`:

1. `POST {commandId}/confirm` with a `filter` and a `reason` calls `PrepareAsync`, which counts the scope, returns a sample of five rows, and reads a high-water mark from the store. Every Operation confirm needs a reason, whatever the command's `RequiresReason` says, because one call changes many rows; a confirm without one answers `400` with `errors.reason`. The shell answers the exact count, the sample, the confirmation the count needs, and a scope token signed with Data Protection and valid for one hour. A scope over 100,000 rows is refused, so the operator narrows the filter.
2. `POST {commandId}/chunks` with the `token` and the previous `cursor` calls `RunChunkAsync` for the next 500 rows. Each chunk checks everything again: the command is still enabled, the user still holds its permission, and the token is intact, unexpired, and issued to this user for this module and command.

- A runner refuses input it does not accept by throwing `ArgumentException` before it changes a row: a filter from `PrepareAsync`, or a mark or cursor from `RunChunkAsync`. The shell answers a `400` validation problem with the exception's message on `filter`, `cursor`, or `token` (rule `invalid`), and writes no operator-log entry. Any other exception from a chunk is logged as a failed chunk and propagates to the host's error handling.
- The dashboard stores no Operation. The token carries its scope and the cursor its progress, so any node that shares the key ring serves any chunk, and Stop is the browser not asking for the next one. The module's store must therefore make each chunk safe alone: a time each row entered its current state, stamped by the store; a high-water mark from that time, so a row that enters the state during the run stays out; a conditional update that re-checks the filter, the state, and the mark inside the update itself; and an index that serves the walk as a range seek. Report a row an earlier chunk already changed as `Skipped`.
- When a filter carries text an operator searched for, set `DashboardOperationScope.LoggedFilter` from `PrepareAsync` to the filter with that text replaced by a marker, such as `"content": "(redacted)"`. The shell signs the full filter into the encrypted token, so every chunk still runs with the search, and records `LoggedFilter` as the `Scope` of the confirm and of every chunk. Leave it `null` and the log records the filter as sent. Use a fixed marker, not a hash of the text: a plain hash of short, guessable text can be reversed by guessing.

### Operator log

Every command call the shell runs, every Operation confirm, and every chunk writes an `OperatorLogEntry`: time, actor, channel, module, command, action, ids or scope, count, reason, done, skipped, failed, Operation id, and the cursor a chunk started from. A refused call writes none.

The default sink writes each entry as a structured `ILogger` event in the `Headless.Dashboard.OperatorLog` category. That event is an audit record: it carries the operator's identity (often an email) and the reason they typed, in plain text and not classified for log redaction. A host that must keep personal data out of its log store routes the operator log to its own sink instead.

Add sinks as singletons. Each entry goes to every sink in registration order, after the change it records was made, so a sink that throws fails the request but does not undo the change:

```csharp
using Headless.Dashboard;

builder.Services.AddSingleton<IDashboardOperatorLog, AuditOperatorLog>();

public sealed class AuditOperatorLog(ILogger<AuditOperatorLog> logger) : IDashboardOperatorLog
{
    public ValueTask WriteAsync(OperatorLogEntry entry, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "{Actor} ran {CommandId} on {Count} rows: {Done} done, {Failed} failed",
            entry.Actor,
            entry.CommandId,
            entry.Count,
            entry.Done,
            entry.Failed
        );

        return ValueTask.CompletedTask;
    }
}
```

**Verify:** run a command from the dashboard, then check that your sink received an `OperatorLogEntry` with the command's id.
