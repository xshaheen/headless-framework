---
domain: Dashboards
packages: Dashboard
---

# Dashboards

> One operations shell that serves feature modules under one base path and one sign-in, plus the authentication primitives for a dashboard a host writes.

## Orientation

`Headless.Dashboard` has two parts:

- **The shell.** `services.AddHeadlessDashboard(...)` registers it and `app.MapHeadlessDashboard()` mounts it, by default at `/headless`. It serves every registered module under that base path with one sign-in, one session cookie, one live stream, and one command pipeline. It stores nothing: each module reads its own store. The package ships no module of its own. `Headless.Jobs.Dashboard` adds the Jobs module (`AddJobs()`), and `Headless.Messaging.Dashboard` adds the Messaging module (`AddMessaging()`); see [Jobs module](#jobs-module) and [Messaging module](#messaging-module).
- **Authentication for a dashboard a host writes.** The per-dashboard primitives the shell is built on, `AddDashboardAuthentication(name, ...)` and its endpoint conventions, protect a dashboard the host serves outside the shell; see [Dashboards a host writes](#dashboards-a-host-writes).

Read [jobs.md](jobs.md) for Headless Jobs and [messaging.md](messaging.md) for Headless Messaging.

## Agent Rules

- Treat every dashboard as an operations surface. Require host, Basic, or API-key authentication outside isolated local development.
- Choose the shell's authentication mode explicitly: `WithHostAuthentication`, `WithBasicAuth`, `WithApiKey`, or `WithNoAuth`. `AddHeadlessDashboard` throws `InvalidOperationException` when the delegate calls none of them.
- Call `AddHeadlessDashboard` once per host and add every module inside that one call. A second call throws.
- Call `app.MapHeadlessDashboard()` on the host's endpoints after `UseAuthentication()` and `UseAuthorization()`. A host that registers the shell and never maps it fails to start. The base path cannot be the root.
- Prefer host authentication when the application already signs its users in. It keeps identity, audit, and revocation in the host's security boundary, and separates viewing from operator actions.
- Write host view and operate policies from claims only. The dashboard evaluates them against the claims it copied into its own session, not against the host's bearer token or request. A policy that lists authentication schemes passes only for a session issued from one of those schemes.
- Serve a host-mode dashboard from the host's own origin. The shell is same-origin in every mode, because its session cookie is host-only.
- Give every instance of a multi-instance host one shared Data Protection key ring, for example with `Headless.DataProtection.Blobs`. Sessions in every mode, and the shell's Operation tokens, are protected by that key ring. Without it, a session or an Operation token issued by one instance is refused by the next.
- Call `HttpContext.SignOutHeadlessDashboardAsync()` for the shell, and `SignOutHeadlessDashboardAsync(name)` for a dashboard a host writes, from the host's own sign-out, so dashboard sessions end with the application session.
- The shell's document replaces any `Content-Security-Policy` header already on its response. Add the sources a module or the host needs through `ConfigureContentSecurityPolicy`, never by loosening the policy in host middleware.
- Never embed production credentials or API keys in source, examples, logs, URLs, or frontend storage.
- `WithNoAuth()` and `AuthMode.None` make a dashboard public. Use them only in an isolated development environment.

## Choosing an authentication mode

| Mode | Shell builder call | Use when | Trade-off |
| --- | --- | --- | --- |
| Host | `WithHostAuthentication(...)` | The application already signs users in, with a cookie, OpenID Connect, or a bearer token | Separate view and operate policies per module, server-side session with idle and absolute expiry; same origin only |
| Basic | `WithBasicAuth(user, password)` | A small standalone deployment needs simple credentials | One shared credential for viewing and operating every module; the application owns secret storage and rotation |
| API key | `WithApiKey(key)` | Automation or a controlled operator client needs bearer access | One shared credential for viewing and operating every module; the application owns key distribution and rotation |
| None | `WithNoAuth()` | Isolated local development only | No access control |

There is no custom-validator mode. To sign operators in with your own credential check, write an ASP.NET Core authentication handler, register it in the host, and use host mode over it.

## Headless.Dashboard

The dashboard shell, its module contract, and the authentication primitives the shell is built on.

### Register the shell

```bash
dotnet add package Headless.Dashboard
```

Host mode with separate view and operate policies, an OpenID Connect sign-in for browsers, and the Jobs module from `Headless.Jobs.Dashboard`:

```csharp
using Headless.Dashboard;
using Headless.Jobs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(options => builder.Configuration.Bind("Oidc", options));

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("DashboardViewers", policy => policy.RequireRole("support", "operator"))
    .AddPolicy("DashboardOperators", policy => policy.RequireRole("operator"));

builder.Services.AddHeadlessDashboard(dashboard =>
{
    dashboard.WithHostAuthentication(host =>
    {
        host.ViewPolicy = "DashboardViewers";
        host.OperatePolicy = "DashboardOperators";
        host.ChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    });
    dashboard.SetTraceLinkTemplate("https://apm.example.com/trace/{traceId}");
    dashboard.AddJobs();
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHeadlessDashboard(); // serves /headless

// The host's own sign-out also ends the dashboard session.
app.MapPost("/account/sign-out", async (HttpContext context) =>
{
    await context.SignOutHeadlessDashboardAsync();
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/");
});
```

`MapHeadlessDashboard()` returns an `IEndpointConventionBuilder` for the whole dashboard group; add host conventions such as rate limiting to it. It throws `InvalidOperationException` when the shell is not registered, is already mapped, or its base path is the root.

### Shell options

`HeadlessDashboardSetupBuilder`, the `AddHeadlessDashboard` delegate's argument, sets:

| Member | Default | Description |
| --- | --- | --- |
| `WithHostAuthentication(configure?)` | | Host mode. `configure` sets a `DashboardHostAuthenticationOptions`; see [Configuration](#configuration) for its members. |
| `WithBasicAuth(username, password)` / `WithApiKey(apiKey)` / `WithNoAuth()` | | The other modes. Exactly one mode call is required. |
| `SetBasePath(path)` | `/headless` | The path under the host's `PathBase`. The root fails validation at startup. |
| `SetTraceLinkTemplate(template)` | none | A link to the host's tracing tool with `{traceId}`. An object that carries a W3C trace id then offers Open trace. A template without `{traceId}` fails validation. |
| `ConfigureContentSecurityPolicy(configure)` | | Adds sources to the document's policy; see [Serving and Content Security Policy](#serving-and-content-security-policy). |
| `Contributions(configure)` / `Contributions(IConfiguration)` | | Disables or reorders module contributions by id. |
| `AddModule<TModule, TOptions>(id)` | | Adds a module. Module packages call it from their own `Add{Module}` extension. |
| `Services` | | The host's `IServiceCollection`, for a module package's registrations. |

`HeadlessDashboardOptions` holds the rest, validated at startup. Change them with `services.Configure<HeadlessDashboardOptions>(...)`:

| Option | Default | Description |
| --- | --- | --- |
| `AggregationTimeout` | 2 seconds | How long attention, Overview, id lookup, and related objects wait for each module. A module that has not answered, or that throws, shows as `Stale` while the others' answers are served. Must be positive. |
| `ChangePollInterval` | 2 seconds | How often each node reads every module's changes while the node has a live stream open. Must be positive. |
| `HeartbeatInterval` | 2 seconds | How often an open stream sends `heartbeat`. The page marks a stream `Stale` after 5 seconds without an event, so it must be under 5 seconds. |

Contribution overrides name the ids modules declare: sidebar entries, client commands, server commands, and related-object groups. Disable or order them in code, or bind the `Disable` (a list of ids) and `Order` (a list of id lists) keys from configuration. Both forms add up. An id that no registered module declares fails the host's start, so a typo never leaves a contribution enabled. A disabled server command also answers `404` at its endpoint. A sidebar entry moves only within its own section, and a section whose every entry is disabled is dropped, so `Disable("jobs.nav.failed")` removes the Failed entry and leaves the rest of the States section.

```csharp
using Headless.Dashboard;
using Headless.Jobs;

builder.Services.AddHeadlessDashboard(dashboard =>
{
    dashboard.WithBasicAuth("operator", builder.Configuration["Dashboard:Password"]!);
    dashboard.AddJobs();
    dashboard.Contributions(contributions => contributions.Disable("jobs.nav.failed"));
    dashboard.Contributions(builder.Configuration.GetSection("Headless:Dashboard:Contributions"));
});

builder.Services.Configure<HeadlessDashboardOptions>(options => options.AggregationTimeout = TimeSpan.FromSeconds(3));
```

### Shell sign-in and sessions

- The shell's session cookie is `__Host-headless-dashboard-` followed by the host's application name, lower-cased with every character that is not an ASCII letter or digit replaced by `-`, so two applications on one host name keep separate sessions. `SessionCookieName` overrides it in host mode. The cookie is `Path=/`, `HttpOnly`, `Secure`, `SameSite=Strict`, and host-only.
- **Host mode.** A session is issued by `GET {base}/auth/sign-in?returnUrl=`, which challenges `ChallengeScheme` when no host user is signed in, or by `POST {base}/api/auth/session` with the host's own credential. The [Host sessions](#host-sessions) rules apply to the shell as they do to a dashboard a host writes. A user gets a session when they pass the shell's `ViewPolicy` or any module's view policy.
- **Basic and API key.** The shell's sign-in page at `{base}/sign-in` posts the credential once to `api/auth/session`; the browser then holds only the session cookie. The cookie carries a fingerprint of the credential, so changing the password or key ends every session issued under the old one at its next request. A script can still send the credential as an `Authorization` header (`Basic`, or `Bearer` for the key) on every request.
- **Per-module access, host mode.** Each module's options derive from `DashboardModuleOptions`, whose `ViewPolicy` and `OperatePolicy` override the shell's. An empty module `ViewPolicy` uses the shell's view policy. An empty module `OperatePolicy` uses the module's `ViewPolicy` when set, else the shell's operate policy. A user may operate a module only if they may also view it. The page lists only the modules the session may view, and the server refuses the rest. Basic and API key share one credential that views and operates every module.
- `GET {base}/api/auth/info` is anonymous and reports the mode and the session. `POST {base}/api/auth/sign-out` deletes the cookie. `HttpContext.SignOutHeadlessDashboardAsync()` deletes it from host code.
- Every state-changing request (`POST`, `PUT`, `PATCH`, `DELETE`) must carry an `X-Requested-With` header, or it gets `400`.

### Modules

A module is one feature's data and UI under the shell. Its .NET side implements `IDashboardModule`:

| Member | Description |
| --- | --- |
| `Id` | The module's URL segment, API group (`{base}/api/{id}`), and stream topic prefix. It must equal the id it was registered under: a lower-case kebab token. `api`, `auth`, `m`, `needs-you`, `sign-in`, and `shell` are reserved. |
| `DisplayName` | The name the shell shows. |
| `Assets` | A `DashboardModuleAssets` whose `Files` provider is rooted at the module's front-end build, with `manifest.json` at the root. Use a provider over resources embedded in the module's assembly. |
| `MapEndpoints(endpoints)` | Maps the module's own API on `endpoints.Routes`, at `{base}/api/{id}`, behind the module's view policy. Wrap each state-changing endpoint in `endpoints.RequireOperate(...)` to require the operate policy too: the shell does not add it, so an unwrapped write is open to every viewer. Every state-changing request needs `X-Requested-With`. The `commands` segment belongs to the shell. |

The shell resolves the module as a singleton, so its members must be thread-safe. It reads `Id`, `DisplayName`, `Assets`, and every declared list (commands, related groups, formatter kinds) once, when it builds its manifest at startup; those reads must not open a connection or run a query.

Everything else is an optional capability interface on the same class, found by a type check:

| Capability | The shell uses it for |
| --- | --- |
| `IDashboardAttentionSource` | The Attention popover and each product tab's dot: the items that need an operator, grouped by `GroupKey`. Called when the shell loads and whenever the module's attention changes, so keep it to one cheap query. |
| `IDashboardOverviewSource` | `GET {base}/api/overview` (the shell's frame no longer draws an Overview page; a product dashboard reads it): up to three `DashboardFigure`s per module, each with its comparison, and, through the default member `GetScheduleAsync`, the module's `DashboardScheduleTrack`s on the Overview's schedule strip. A comparison whose `Value` is `null` means the earlier period has nothing to compare; the shell shows its `Label` alone, so the label says why (`no runs finished in the previous 24 h`). |
| `IDashboardCountSource` | The counts beside sidebar entries: a `DashboardCount` (`Count`, `Capped`) per `countKey` the module's sidebar declares. `GET {base}/api/counts?product=` asks every module of one product and answers `{ product, counts: { key: { count, capped } }, stale }`; when two modules answer one key, the first registered wins. The shell refetches on live changes, so keep each count to one cheap, capped query. |
| `IDashboardObjectResolver` | The palette's pasted-id lookup (`GET {base}/api/resolve?q=`, at most 256 characters). Answer with an exact-id lookup, never a text search. |
| `IDashboardRelatedSource` | Related objects of any module's object, drawn as labelled groups (`GET {base}/api/related?module=&kind=&id=`). `GroupIds` declares every group id up front, because each is a contribution the host can disable or reorder. |
| `IDashboardPayloadSource` | One object's payload (`GET {base}/api/payload?module=&kind=&id=`). |
| `IDashboardPayloadFormatter` | Redacts and formats payloads of its `ObjectKinds` on the server, in responses and stream events, before they leave for the browser. Without one, JSON goes out as it is. Keep it free of I/O. |
| `IDashboardChangeSource` | The live stream's cross-node change reads; see [Live stream](#live-stream). |
| `IDashboardCommandSource` | Operator commands; see [Commands and Operations](#commands-and-operations). |
| `IDashboardOperationRunner` | Commands over a filter, run as an Operation; see [Commands and Operations](#commands-and-operations). |
| `IDashboardPeerAccess` | Node-to-node calls, such as a node forwarding a read or a command to the node that owns the data. `PeerAuthenticationScheme` and `PeerPolicy` name a scheme and a policy the module registers with the host; see [Peer calls](#peer-calls). |
| `IDashboardNodeTopicObserver` | Learning when this node's streams start and stop watching one of the module's node topics, `{module}.{kind}@{node}`; see [Live stream](#live-stream). |

Attention, Overview, lookup, related objects, and counts call every module in parallel, list only the modules the user may view, and wait at most `AggregationTimeout` for each. A module that times out or throws answers `stale: true` in its place.

A module that reads several stores or nodes never throws for one of them. It answers with what the others returned and names the sources that did not answer: `DashboardCount.UnavailableSources`, or, through the default members `GetAttentionAnswerAsync` and `GetOverviewAnswerAsync`, `DashboardAttentionAnswer.UnavailableSources` and `DashboardOverviewAnswer.UnavailableSources`. The shell sends them as `unavailable` beside the count or the module section, leaves the field out when every source answered, and keeps `stale` for a whole module that failed. The sidebar shows a partial count as `48,120+`, with the sources in its accessible name: `48,120+ · outbox-2 unavailable`. A module that never overrides the default members answers as before. Counts stop at 10,000 and report `capped: true`. `api/counts` answers `400` without `product`, `404` for a product no registered module joins, and `403` when the user may view none of the product's modules.

A `DashboardAttentionItem` can also say how to resolve it and whether it is still growing:

- **`RecommendedCommandId` and `GroupFilter`.** The id of one of the module's commands, such as its Requeue, and the filter, in the module's own Operation filter shape, that selects every object of the item's group. The item's recommended command (`Requeue 23`) runs as an Operation over `GroupFilter` from the module's own filtered list, never from the Attention popover: the operator gives a reason, the server counts the scope, and nothing runs before the confirm. The shell offers the button only when both members are set, the module implements `IDashboardOperationRunner`, and the host has not disabled the command or hidden it from the UI. A user without the command's permission sees it disabled with `Needs operate permission`.
- **`RecentCount`.** How many objects joined the item in the module's latest interval, such as the last hour. Above zero, the cause is still growing.
- **`LastSeenAt`.** When the cause last occurred, beside `Since`, when it began.

`GET {base}/api/needs-you` returns these as `recommended` (`{ command, filter }`, where `command` is described for the signed-in user as `GET {base}/api/{module}/commands` describes it, or `null`), `recentCount`, and `lastSeenAt`.

The interfaces grow by new capability interfaces and default interface members. An existing member never gains a parameter; the request and result records grow by `init` members instead.

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

`AddModule` throws `ArgumentException` for an id that is not a lower-case kebab token or is reserved, and `InvalidOperationException` for an id already added. At startup the shell reads each module's `manifest.json` and refuses a module built against another major version of the dashboard SDK, or a newer one: the page shows the refusal with both versions, and the module's endpoints are not mapped.

### Live stream

The page reads live changes from one server-sent-events stream at `GET {base}/api/stream?topics=`, with topics `{module}.{kind}` or `{module}.{kind}@{node}`, comma-separated or repeated, at most 64. The open tabs of one dashboard share that stream: one tab holds it, chosen through the Web Locks API, and relays its events to the others over `BroadcastChannel`.

- **Events.** `subscribed`, `change` (module, kind, id, the node when the object has one, topic, version, and the formatted projection), `module.stale`, `attention.changed`, `node.stale`, `node.reset`, `heartbeat`, and `reset`. Each stream receives only modules the user may view and only the topics it asked for, matched exactly.
- **Node topics.** An object whose `ObjectRef.Node` is set, because one node's own store holds it, changes on `{module}.{kind}@{node}` only, such as `messaging.received@node-b`; a stream subscribed to `@node-b` never receives `@node-c`'s changes, and one subscribed to `messaging.received` receives neither. A module that implements `IDashboardNodeTopicObserver` hears `OnNodeTopicWatched` when the first stream on this node subscribes to one of its node topics and `OnNodeTopicUnwatched` when the last one closes; both must return at once. `IDashboardEventPublisher.PublishNodeStale(module, node, stale)` and `PublishNodeReset(module, node)` send `node.stale` and `node.reset` to the streams subscribed to a topic of that module for that node, so only that node's views show `Stale` or refetch; a stream that opens while a node is stale is told at once.
- **Resume.** Each node keeps its recent events in a bounded ring. A reconnect sends its last event id (the `Last-Event-ID` header, or the `lastEventId` query parameter) and resumes from it. When the node no longer holds that position, the stream sends `reset` and the page refetches its views.
- **Pause.** The top bar's Pause holds this tab's data changes (`change`, `attention.changed`, `reset`, and `subscribed`) while the stream stays open for the other tabs, and the live state reads `Paused · N updates`; Resume applies them in the order they came. A module going stale, session end, sign-out from another tab, and the result of the operator's own commands, which return through the API, still apply at once. Pause is not remembered across reloads.
- **Cross-node changes.** While a node has at least one stream open, it reads every module's `IDashboardChangeSource` each `ChangePollInterval`, starting from `GetStartCursorAsync`. No broker is involved: the module's store is the only shared state. A node with no open stream reads nothing. A read that throws marks that module's views `Stale`; the reader backs off and retries with the same cursor. A read that reaches only some sources returns a `DashboardChangeBatch` whose `UnavailableSources` names the others; the module stays live, and `module.stale` carries them as `unavailable` with `stale: false`.
- **What a change source's store must provide.** A change marker stamped by the store (a sequence or the store's clock) in the same statement as every write the dashboard shows; an index on it; reads from the cursor minus an overlap window longer than the store's longest write transaction; and a bounded page per read. The shell drops a change whose `Version` it already sent for that object.
- **Same-node changes.** `IDashboardEventPublisher`, a singleton the shell registers, pushes a change to this node's streams at once, after the module's store commits it. Changes to one object within 250 milliseconds coalesce into one event. A version not newer than the last sent is dropped; versions compare as integers when both parse as one, and ordinally otherwise, so use a store sequence or a fixed-width timestamp. Other nodes still learn of the change from their own reads.
- **Closing.** A stream closes at the session ticket's expiry, at its next revalidation in host mode when `Revalidate` is set, and after 5 minutes under Basic and API key. The browser reconnects, which checks the session, credential, and module permissions again. A request whose `Origin` is not the shell's own gets `403`. A stream never delays the host's shutdown.

### Peer calls

A module that implements `IDashboardPeerAccess` admits calls signed in by its `PeerAuthenticationScheme` on its own API at `{base}/api/{id}`, its command endpoints, and its Operation endpoints, beside the dashboard's own sign-in, in every mode except `WithNoAuth`. Such a call needs no session: its principal must pass `PeerPolicy`, and then skips the view, operate, and command permissions, because the node that forwarded the call checked them for its own user. A peer the policy refuses gets `403`; a call with neither a session nor a peer credential still gets `401`. The scheme admits nothing on another module's endpoints or on the shell's own. Register the scheme and the policy before `MapHeadlessDashboard()`; mapping fails when the scheme is missing.

### Commands and Operations

A module's `IDashboardCommandSource.Commands` declares its commands once, at startup. Each `DashboardCommand` has an `Id` prefixed with the module id (`jobs.requeue`), a `Label`, the `ObjectKind` it acts on, and the rules the shell enforces:

| Member | Default | Effect |
| --- | --- | --- |
| `Permission` | `Operate` | `Operate` needs the module's operate policy; `View` needs only its view policy. |
| `Capability` | none | A named capability such as `jobs.delete`. When the host registers an authorization policy with that name, the command needs that policy instead of the operate policy. |
| `Confirmation` | `None` | `Confirm` (a dialog) or `Typed` (type the row count). A confirmed command over more than 100 rows is always `Typed`. |
| `InverseCommandId` | none | The command that undoes this one. A command with an inverse runs without confirmation, and the shell offers Undo. |
| `RequiresReason` | `false` | The operator must give a reason, at most 1,000 characters. An Operation always needs one. |
| `Visibility` | `Ui \| Api` | The channels that may call it. |
| `Arguments` | none | `DashboardCommandArgument`s: a small JSON-Schema subset (`Text`, `WholeNumber`, `Number`, `Toggle`; required, allowed values, minimum, maximum, maximum length) that the shell renders as a form and validates on the server. |

The shell owns the endpoints under `{base}/api/{module}/commands`:

- `GET` lists the module's commands for the caller. `GET allowed?kind=&id=` asks `GetAllowedCommandsAsync` which commands each object allows now (at most 500 objects), so the page disables a control with the module's reason instead of offering an action the module would refuse.
- `POST {commandId}` runs the command over `ids` in a JSON body that may also carry `arguments`, `reason`, `confirmed`, and `typedCount`. At most 100,000 rows per call.
- The shell checks every rule before `ExecuteAsync` runs. A command the host disabled, or one the call's channel may not use, answers `404`. A failed permission answers `403`. The body, reason, argument schema, and confirmation answer a `400` validation problem naming each field, with the broken rule in its `rules` extension. The page sends `X-Headless-Dashboard-Channel: ui`; a call without it is an API call. The channel is claimed, not proven, so it decides only which commands are offered and what the log records.
- A module that refuses the whole call, because the object is not in a state the command applies to, returns a `DashboardCommandResult` with `Refusal` set and changes nothing; the shell answers `409` with the reason as the problem's `detail` and writes no operator-log entry.

A command over a filter runs as an **Operation**, through the module's `IDashboardOperationRunner`:

1. `POST {commandId}/confirm` with a `filter` and a `reason` calls `PrepareAsync`, which counts the scope, returns a sample of five rows, and reads a high-water mark from the store. Every Operation confirm needs a reason, whatever the command's `RequiresReason` says, because one call changes many rows; a confirm without one answers `400` with `errors.reason`. The shell answers the exact count, the sample, the confirmation the count needs, and a scope token signed with Data Protection and valid for one hour. A scope over 100,000 rows is refused, so the operator narrows the filter.
2. `POST {commandId}/chunks` with the `token` and the previous `cursor` calls `RunChunkAsync` for the next 500 rows. Each chunk checks everything again: the command is still enabled, the user still holds its permission, and the token is intact, unexpired, and issued to this user for this module and command.

A runner refuses input it does not accept by throwing `ArgumentException` before it changes a row: a filter from `PrepareAsync`, or a mark or cursor from `RunChunkAsync`. The shell answers a `400` validation problem with the exception's message on `filter`, `cursor`, or `token` (rule `invalid`), and writes no operator-log entry. Any other exception from a chunk is logged as a failed chunk and propagates to the host's error handling.

The dashboard stores no Operation. The token carries its scope and the cursor its progress, so any node that shares the key ring serves any chunk, and Stop is the browser not asking for the next one. The module's store must therefore make each chunk safe alone: a time each row entered its current state, stamped by the store; a high-water mark from that time, so a row that enters the state during the run stays out; a conditional update that re-checks the filter, the state, and the mark inside the update itself; and an index that serves the walk as a range seek. Report a row an earlier chunk already changed as `Skipped`.

Every command call the shell runs, every Operation confirm, and every chunk writes an `OperatorLogEntry` (time, actor, channel, module, command, action, ids or scope, count, reason, done, skipped, failed, Operation id, and the cursor a chunk started from). A refused call writes none. The default sink writes each entry as a structured `ILogger` event in the `Headless.Dashboard.OperatorLog` category. Add sinks as singletons; each entry goes to every sink in registration order, after the change it records was made, so a sink that throws fails the request but does not undo the change:

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

### Serving and Content Security Policy

- Every path under the base path that no other endpoint claims returns the shell document, so the sign-in page and every deep link load the same shell. An unmatched `{base}/api/...` path answers `404`, never the document. The document and build files are anonymous; the document carries only what the session allows.
- The document is written with `Cache-Control: no-store` and a fresh nonce per response. Build files under `{base}/assets/` and `{base}/m/{id}/` are content-hashed, served with `Cache-Control: public, max-age=31536000, immutable` and an ETag, and precompressed with Brotli and gzip. A module's `manifest.json` is not served.
- The document's policy is `default-src 'self'`; `script-src` with the nonce and `'strict-dynamic'`; `style-src 'self'` with the nonce; `img-src 'self' data:`; `font-src 'self'`; `connect-src 'self'`; `frame-ancestors 'none'`; `base-uri 'none'`; and `object-src 'none'`. `ConfigureContentSecurityPolicy` adds `ScriptSources`, `StyleSources`, `ImageSources`, `FontSources`, and `ConnectSources`. A source that is empty or contains white space, a control character, `;`, or `,` throws `InvalidOperationException` when the document is written. Under `'strict-dynamic'` an added script source does not load a parser-inserted script; module code loads through `import()`.
- The document sets `Content-Security-Policy` when it writes, replacing any value already on the response. A host middleware that sets the header later, from `Response.OnStarting`, overwrites the shell's policy and breaks the page; exclude the base path from it.

```csharp
using Headless.Dashboard;

builder.Services.AddHeadlessDashboard(dashboard =>
{
    dashboard.WithApiKey(builder.Configuration["Dashboard:ApiKey"]!);
    dashboard.ConfigureContentSecurityPolicy(policy => policy.ImageSources.Add("https://cdn.example.com"));
});
```

### Module front end

The shell is a React application. A module's front end is a small entry plus lazy route chunks that import React, React Router, TanStack Query, and React Aria from the shell through an import map, never from their own bundle. The SDK (`@headless/dashboard-sdk`) and the Vite preset (`@headless/dashboard-vite`) are private packages of the repository-root npm workspace, under `src/Headless.Dashboard/wwwroot`; they are not published to npm in this release.

The SDK's components are shadcn's React Aria components, copied into the SDK and kept on the SDK's own props: React Aria still supplies the behaviour (press, focus, keyboard, overlays, the data grid), and shadcn supplies the look. Tailwind CSS compiles only the SDK's own sources, inside the shell build, into one stylesheet that maps shadcn's semantic classes (`bg-background`, `text-foreground`, `border`, `ring`, `primary`, `muted`, `destructive`, `card`, `popover`) onto the dashboard's tokens in `tokens.css`. Dark is the default: the `dark:` variant applies everywhere except under `data-theme="light"`. Classes use logical properties, so a right-to-left page mirrors. The stylesheet injects no `<style>` tag, so it works under the document's nonce-based Content Security Policy. A module ships no CSS and runs no Tailwind build; it composes SDK components and writes no SDK class name on its own elements. The module preset's build fails when a module's manifest lists a stylesheet, and each module's lint configuration rejects a `className` that names an `hd-` class.

A module's entry exports a `defineModule` call. The build reads the literal itself to write `manifest.json`, so every static field (ids, labels, paths, permissions, event names) must be a literal, not computed:

```ts
import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'reports',
  product: 'platform',
  sidebar: [{ entries: [{ id: 'reports.nav.runs', label: 'Runs', route: '', icon: 'runs' }] }],
  routes: [
    { path: '', lazy: () => import('./views/runs-list.tsx') },
    { path: 'runs/:runId', lazy: () => import('./views/run-page.tsx') },
  ],
  objectKinds: {
    run: { route: (id: string) => `runs/${encodeURIComponent(id)}` },
  },
  commands: [
    { id: 'reports.requeue-failed', label: 'Requeue failed runs', requires: 'operate', run: ({ navigate }) => navigate('?status=Failed&operation=requeue') },
  ],
})
```

`id` equals the .NET module id. `product` names the tab the module joins, one of `jobs`, `messaging`, `cluster`, `platform`, or `dev-inbox`; the shell shows one tab per product a host registered, in that order, and modules that name one product share its tab, their sidebar sections in registration order. A module whose manifest names another product fails the host's start.

`sidebar` lists the module's sections in display order. A section has an optional `label` (a section without one draws no heading) and its `entries`. An entry has an `id` (a contribution id the host can disable or order, such as `jobs.nav.failed`), a `label`, a `route` relative to the module with an optional query (`?status=Failed`), exactly one of `icon` (an icon key) or `state` (the colour role of its dot: `sched`, `enq`, `proc`, `ok`, `fail`, or `off`), an optional `countKey` naming its count in the module's `IDashboardCountSource` answer, and an optional `requires`. The build fails on a computed `sidebar`, an unknown `product` or `state`, or an entry with both or neither of `icon` and `state`.

Sidebar entries and client commands take `requires: 'view' | 'operate'`. Each route's `import()` becomes its own chunk. The palette already lists every sidebar entry as `Go to <module> <label>`, so a command that only navigates to an entry's route repeats it; the palette drops a command whose label matches a sidebar entry's label, ignoring case.

| SDK entry | Contents |
| --- | --- |
| `@headless/dashboard-sdk` | `defineModule`, the dashboard and module context hooks (`useDashboard`, `useModule`), and `Button` |
| `/components` | The design-system components. The list-and-detail frame: `PageFrame` and `usePageFrame`, `DetailPanel`, `FieldGrid` with `GridField`, `HistoryList`, and `ExceptionBlock` (see [List views](#list-views)). The data display blocks: `StatStrip` and `StatCard`, `PageHeader`, `FilterChips` and `FilterChip`, `SegmentedControl`, `Tag`, `BarChart`, and the dashboard `Card` with `CardRow` inside `Cards`. The text blocks a view composes instead of writing class names: `Banner`, `Text` (tones for a hint, faint text, a label, a title, an error, and a footnote), `Fields` and `Actions` (a labelled group of fields and a row of actions), `Skeleton`, and `EmptyState`. Also `Timeline` (stored events in a grid where arrows move and `Enter` selects an event, focus staying in the timeline), `ScheduleStrip` (SVG marks for the next fires in the scheduler's zone, `[`/`]` widen and narrow it), `Dialog`, `Popover` (a button that opens a small floating dialog, such as a panel's further actions), form fields, `CommandPalette`, `PayloadView`, status marks, live regions (`LiveIndicator` reads `Paused · N updates` when given the waiting count), toasts, the keymap hooks, and `below`, the named breakpoints as media conditions for script |
| `/table` | `DataTable`: virtualized, with keyset load-more, column priority, a density remembered per view, and rows that arrive above held behind `N new`. A column with a `sortKey` has a sort button in its header that sets `aria-sort` and asks the list for ascending, then descending. `SearchField` debounces its text, or in submit mode sends it only on `Enter` or its Search button. `ListSort`, `ListRange`, and `ListPageRequest` type what the list hook passes to its page reader. Given a page frame (`frame`), it marks the frame's open row `aria-selected`, adds a checkbox column for the frame's checked rows in a `multiple` list, tags the rows a bulk action refused, and pins its first column while the rest scroll sideways inside the table. It is its own entry, so the browser loads it with the first view that lists rows, not with the shell |
| `/date-range` | `DateRangeField`, a list's day range typed or picked on a calendar: it picks whole days in the display time zone and sends them as UTC instants with an exclusive next-midnight end, which a list passes to its keyset hook as `range`. It is its own entry, so the calendar loads with the first view that filters by date, not with every view that lists rows |
| `/data` | The API client and hooks (`useDashboardApi`, `useDashboardList`, `useDashboardKeysetList`, `useDashboardObject`, `useNewRows`), change patching, and capped counts. `useDashboardKeysetList` keys its cache on the sort, search, and range as well as the filter: any change drops the cursor, reloads the first page, and aborts the old scope's page in flight, so its late answer never shows; while the first page of a new sort or search loads, the old rows stay on screen |
| `/icons` | A curated set of Lucide icons for stat cards, card titles, actions, and empty states. Icons are monochrome, 15 px, and decorative; a status is never an icon, only a dot and a word. `StatCard`, `Card`, `EmptyState`, and `Button` take an `icon`; a `Button` with an icon and no label is a square icon button named by its `aria-label`. The shell loads them with its first page, because its toasts draw the close icon |
| `/charts` | `Chart`: one API for a line, an area, a stacked bar, and a donut, drawn from served series. A point the server did not measure is `null` and draws as a gap, never as zero. Each chart is an SVG `role="img"` whose label names the series, unit, interval, range, and zone, has a tooltip with exact values, and a toggle to a table of the same data. A donut shows percentages only when its parts are `complete` and no count is capped; otherwise it shows the counts as a table. It is its own entry, so the chart library loads with the first view that draws a chart |
| `/stream` | `useTopics`, `useStreamEvent`, `useStreamStatus`, and the stream client |
| `/commands` | `CommandAction` (its `recovery` prop makes it the command `R` runs for the selection, and every allowed action also appears in the palette; given per-row refusals it runs only the allowed rows), `useAllowedActions` (answers per row: the call gives a reason when any row is refused, so an action given it as `disabledReason` never sends a refused row; `.refused(commandId)` gives each refused row's reason; every row is refused, saying why, until the server answers or when the check fails), `ActionConfirm`, `SchemaForm`, the commands API, and the Operation runner and panel |
| `/router` | React Router, re-exported so every module uses the shell's one router |
| `/query` | TanStack Query, re-exported so every module reads the shell's one data client |
| `/testing` | `renderModule` |

#### List views

A list view composes the SDK's frame; a module writes no layout of its own. A module ships no CSS and no `style` objects: the lint configuration of the shell's workspace and of the Jobs SPA rejects a `style` attribute or a `.css` import in module source. The shell draws a page whose root is a `PageFrame` or a `DetailPanel` flush against the sidebar and the top bar; the frame pads itself.

- `usePageFrame({ viewId, rows, matches })` holds the view's state: the open row (`openId`, the only list state in the URL, as `?open=<id>`), the row cursor, the checked rows (`checked`, `setChecked`; view state that survives live updates but not a reload), and the refusals of the last bulk action (`refused`, `setRefused`, `revealed`, `reveal`). A change to the checked rows clears the refusals. `J`/`K`, and the arrow keys in a `DataTable`, move the cursor and an open panel follows it, replacing its history entry (`open(id, replace)`), so Back closes the panel rather than walking the rows; `Enter` opens a row with one new entry, `X` checks the cursor row without opening it, and `Esc` closes a popover, then the panel, then the checked rows.
- `PageFrame` lays out `stats`, `header`, the list in its own scroll region, and `panel`, the `DetailPanel` of the open row, docked under the list behind a resize handle (arrow keys move it 16 px, 64 px with Shift; the height is remembered per view). At 1280 px and wider the panel can move to the side; below 768 px it opens full screen. While it is docked below, toasts float above it.
- `DetailPanel` has a title row with the state, tabs, actions, detach to the object's page, and close. The title is the object's name in mono; while the `PageFrame` has `form` set, it reads in the sans face, as a form's title (`Add chain`). Rendered outside a `PageFrame` it is the object's own page, the detached form of the same panel, with no close or side control. A tab's content with two children fills the panel body's two columns. `FieldGrid` shows labels and mono values with optional inline edits (a select commits on change, text on `Enter` or blur, with an optional server preview while typing); `HistoryList` shows an object's state changes; `ExceptionBlock` shows an exception's type (left out when the server does not serve one), message, and stored stack with `Full stack` and `Copy`; a stack line that starts `at` reads as a frame with its type, method, file, and line coloured, and any other line, such as an inner exception's `Type: message`, reads as a heading. `PayloadView` colours JSON (keys, strings, numbers, booleans, and null) and keeps large integers exact; content that is not JSON stays plain. In both, `Copy` copies the stored text, not what is drawn.
- `StatStrip` holds up to six `StatCard`s: a label, a served value, a state role dot, an optional served caption, and an optional `to` link to the filtered list. It draws no sparkline: a trend appears only once a series is served.
- `PageHeader` takes the title (with an optional state dot), the served count, a one-sentence `description`, filters as children, and `actions` at the inline end. `FilterChips` groups `FilterChip`s, each a toggle with a name and count (`pressed`, `onPress`; `failing` colours the count) or a set filter with `Clear` (`onClear`).
- `SegmentedControl` is one choice among a few, as pressed-state buttons; the table's density toggle uses it. `Tag` is a short fact on an object or row: `pill`, `small`, `filled`, or `square`, with an optional state role dot.
- `Card` is a product Dashboard's block: a heading, an optional served caption, one trailing control (an `All` link), and `CardRow`s (state dot, name, faint meta, value at the inline end, actions, and an optional second line such as a progress bar), or its `empty` text when it holds none. A row's first line never wraps: the name ends in an ellipsis instead. `Cards` lays cards in columns that wrap to one at narrow widths.
- `BarChart` stacks served series per column, drawn from zero, as one SVG `role="img"` whose label is a sentence naming the series, the columns' range, and the tallest bar; `mini` drops the axes.

A bulk action runs on the checked rows the server allows. Pass `CommandAction` the checked ids, `refused={allowed.refused(command.id)}`, `onRefused={frame.setRefused}`, and `onViewRefused={frame.reveal}`, and give the `DataTable` the same `frame`. With 2 of 5 rows refused the button reads `Requeue 3 of 5` and sends only the 3; the toast reads `Requeue: 3 done · 2 refused` with `View`, which highlights the refused rows and scrolls to the first; each refused row carries its reason as a tag in its own row. With every row refused the button is disabled with the shared reason, or `None of these can run now` when the reasons differ, and `R` announces it. Given `refused`, the action ignores `disabledReason`. Without `refused`, pass `disabledReason={allowed(command.id)}`: any refused row disables the action with the server's reason, as on a single object. The confirm and the run use the rows checked when the action was pressed; when the checked rows change before the run answers, the toast still reports it, but the refusals are not tagged and `View` is not offered.

A `Button` with `disabledReason` stays focusable and ignores presses. The reason is the button's description (`aria-describedby`), so a screen reader reads it with the button; on screen it shows under the button on hover and focus, so it never widens or wraps the row the button sits in.

The module's `vite.config.ts` uses the preset, which writes and validates `manifest.json` beside the entry and fails the build when the bundle contains React, React Aria, TanStack Query, or React Router code, or `eval` or an inline script:

```ts
import { fileURLToPath } from 'node:url'
import { dashboardModule } from '@headless/dashboard-vite'
import { defineConfig } from 'vite'

export default defineConfig(
  dashboardModule({
    root: fileURLToPath(new URL('.', import.meta.url)),
    entry: 'src/module.ts',
    outDir: '../../dist/modules/reports',
  }),
)
```

The workspace build then holds the output to the size budgets in `packages/vite/budget.json`, measured as gzip at maximum compression: the shell's first load at most 350,000 bytes, the shell and every SDK entry together at most 450,000 bytes, each module entry at most 20,000 bytes, each view chunk at most 150,000 bytes, and the fonts at most 60,000 bytes. A budget overrun fails the build. The dashboard ships in English, so the shell build keeps only React Aria's `en-US` strings (its labels and screen-reader announcements); React Aria falls back to them for any other locale.

`renderModule(definition, options)` from `@headless/dashboard-sdk/testing` mounts a module in a test shell, as the dashboard would: its routes, a session, its permissions (`canView` and `canOperate`, both `true` by default), the data client, an Operation host, toasts, and a fake stream. The `api` handler answers the module's API calls; throw a `DashboardApiError` to answer with a status. The result adds `stream` (push events as the server would), `requests` (every API call in order), and `navigate(path)`.

### Jobs module

`AddJobs()` adds the Jobs module (`Headless.Jobs.Dashboard`) under the id `jobs`. It needs `AddHeadlessJobs(...)` in the same host and takes no entity type arguments.

```csharp
using Headless.Jobs;

builder.Services.AddHeadlessJobs(jobs => jobs.AddModule<MyApp.JobsModule>());
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "ops.view"; host.OperatePolicy = "ops.operate"; })
    .AddJobs(jobs => jobs.OperatePolicy = "jobs.operate"));

app.MapHeadlessDashboard();
```

| `JobsDashboardOptions` member | Default | Description |
| --- | --- | --- |
| `ViewPolicy`, `OperatePolicy` | the shell's | As for every module. |
| `ChangeOverlapWindow` | 5 s | How far back each change read reads again. Make it longer than the Jobs store's slowest single write. |
| `ChangePageSize` | 200 | The most changed rows one change read returns, 1 to 1000. |

`AddJobs(Action<JobsDashboardOptions>)`, `AddJobs(Action<JobsDashboardOptions, IServiceProvider>)`, and `AddJobs(IConfiguration)` configure them.

**Object kinds and topics.** `run` (a time job or a cron occurrence, by its id), `cron-job`, `function` (by name), and `node`. Stream topics are `jobs.run` and `jobs.cron-job`; a change's projection is the run or cron job as its list row shows it, and its version is the row's change marker in ticks. A deleted row goes out with no projection, so open views refetch it and show `This run no longer exists`.

**Commands.** Requeue, Cancel, and Delete need a reason and a confirmation (typed past 100 rows) and run over a filter as Operations. A call on one object the rules refuse changes nothing and answers `409` with the reason, such as `Paused — resume first`.

| Command | On | Allowed when |
| --- | --- | --- |
| `jobs.requeue` | `run` | failed, cancelled, or skipped, and the requeue rules accept it |
| `jobs.cancel` | `run` | a queued or running time job with no cancel requested yet |
| `jobs.delete` | `run` | not running, and not a keyed time job |
| `jobs.run-now` | `cron-job` | not paused, and not running when the cron job forbids overlap; the done row's `reason` is the new run's id |
| `jobs.pause` / `jobs.resume` | `cron-job` | active / paused and not removed from code; each is the other's inverse |
| `jobs.edit-cron` | `cron-job` | not defined in code; arguments `expression` (required) and `timeZoneId` |

An Operation's filter is `{ kind, functions, statuses, cronJobId, correlationId, fingerprint, from, before, actionableOnly }`. Requeue and Delete freeze on the store's latest change marker at confirm and write each row only while its marker is unchanged, so a run requeued by one chunk that fails again is not touched again; Cancel freezes on creation at confirm and needs no marker, because a running job advances its marker as it reports progress.

**Attention and Overview.** One item per function and failure fingerprint (`3 failed runs of billing.charge`), recommending `jobs.requeue` over the group's actionable failures; one item per registered function that no live node's run filter claims (`No live node runs billing.charge`), pointing at its `function` object. The Overview shows the share of runs that succeeded, failed runs, and runs that started later than the scheduler's fallback interval after they were due, each over the last 24 hours against the 24 before. The succeeded share is left out while no run finished in the last 24 hours, so an idle system does not read as 0 % succeeded, and compares with nothing, labelled `no runs finished in the previous 24 h`, while no run finished in the 24 hours before. The Overview's schedule strip gets one track per function that has cron jobs: its occurrences due in the last hour in their status's state role (`sched`, `enq`, `proc`, `ok`, `fail`, or `off`), and the next fires of its active cron jobs in the next hour; a past tick opens its run and a fire opens its cron job.

**Related and payload.** A run relates to the other runs with its correlation id (`jobs.same-correlation`), its chain (`jobs.chain`), and its cron job (`jobs.cron-job`); a run with a stored W3C trace parent carries its trace id, so the shell offers `Open trace`. Its payload is its stored request as JSON, or `Unsupported contract version N` when this host registers another version. A run and an attempt carry their exception as `exceptionType`, `exceptionMessage`, and `exceptionStack`: the module reads the JSON the executor stores (the outer exception's type and message, then its frames and every inner exception as a `Type: message` line with its own frames, up to five inner exceptions) into the three, and a reason the store wrote itself, such as `Lease lapsed while running!`, arrives as the message with no type or stack.

**API** at `{base}/api/jobs`, JSON with camelCase names and enums as strings:

| Route | Answer |
| --- | --- |
| `GET runs?kind=&function=&status=&cronJobId=&correlationId=&fingerprint=&from=&before=&actionable=&newest=&limit=&after=` | `{ items, count, countCapped, nextCursor }`: one keyset page of runs, newest first unless `newest=false`, `limit` up to 200, a count capped at 10,000; `after` takes `nextCursor` |
| `GET runs/{id}` | `{ run, children }`, or `404` |
| `GET runs/{id}/attempts` | `{ items: [{ number, node, startedAt, endedAt, outcome, exceptionType, exceptionMessage, exceptionStack, failureFingerprint }] }`, or `404` |
| `GET cron-jobs?function=&fires=` | `{ items }`: every cron job with its plain reading; with `fires`, each active one also carries that many next fires (at most 50), as a Function page draws them on its strip |
| `GET cron-jobs/{id}?fires=` | one cron job with its next fires (default 5, at most 50; none while paused), or `404` |
| `GET cron-preview?expression=&timeZoneId=&count=` | `{ timeZoneId, reading, nextFires, validationMessage }` for a draft schedule |
| `GET functions` | `{ items: [{ name, contractVersion, priority, maxConcurrency, requestType, cronExpression, claimedBy, succeeded, failed, running, waiting, lastExecutedAt, cronJobs }] }`, counts over 24 hours |
| `GET nodes` | `{ items: [{ identity, state, role, claimedFunctions }] }`: live nodes; without node membership, this host |
| `POST time-jobs` | an operator action: `{ function, executionTime, timeZoneId, request, description, retries, retryIntervals, children: [{ ..., runCondition }] }` answers `201 { id }`; a time without an offset is read in `timeZoneId`, else the scheduler's zone; a bad body answers `400` |
| `POST cron-jobs` | an operator action: `{ function, expression, timeZoneId, request, description }` answers `201 { id }` |

**Views.** The package embeds the module's front end, so the Jobs tab's sidebar reaches these views:

| View | Shows | Acts |
| --- | --- | --- |
| Dashboard (`{base}/jobs`) | The stat strip (the Idle, Queued, and In progress counts the sidebar shows, then the Overview's succeeded share, failed runs, and late starts over 24 hours with their comparisons), the runs in progress with their reported progress, the next fires of the active cron jobs, and the Jobs attention items | `Cancel` per running run; `Add time job`; `Add recurring job` when no cron job exists |
| Runs (`runs`, each state at `runs?status=<JobStatus>`) | The stat strip, then one live list of time jobs and occurrences, titled by its state with its served count and one sentence on what the state holds, filtered in the URL by kind, function, status, time range, correlation id, failure fingerprint, and cron job. The Failed list adds a chip per failure group, labelled by function with its count, that filters by that function and its fingerprint. The `status` reads case-insensitively, and a value that names no status lists every run. The open run (`?open=<id>`) docks under the list: Overview (exception, fields, and history from its attempts), Arguments, and Chain. | `Requeue`, `Cancel`, and `Delete` on the open run and on the checked runs the server allows, each refused row tagged with its reason; over every matching run as an Operation; `Add time job` and `Add chain` open one form in the docked panel (`?create=time-job` or `?create=chain`), switched by `Single job | Chain` without losing what was typed, a chain's later steps beside the first with the step each runs after and its run condition |
| Run page (`runs/{id}`) | The run panel as its own page | The same run actions |
| Recurring jobs and cron job page (`cron-jobs`, `cron-jobs/{id}`) | The stat strip, then each cron job's function, description, expression with its plain reading, and next run, or a `Paused` tag. The open one (`?open=<id>`) docks under the list: Schedule (its fields beside the next five fires in its zone) and Occurrences (the last 24 hours as outcome bars per hour, then each occurrence newest first). The page is the same panel on its own. The last run per cron job is not served, so the list shows none. | `Run now`, `Pause` / `Resume`; `Add recurring job` opens its form in the docked panel (`?create=cron-job`); the expression and zone edit in place, showing the server's reading and next fires before they commit, and stay read-only for a cron job defined in code |
| Functions and function page (`functions`, `functions/{name}`) | The stat strip, then each function's kind (`cron` with its declared expression, or `time`), request type, priority, running count over its concurrency limit, the live nodes whose run filter claims it (or a `no node runs it` tag), and its 24-hour counts. The open one (`?open=<name>`) docks under the list: Overview (its contract version and facts beside where it runs and the recurring jobs that fire it) and Runs (its latest runs). The page is the same panel on its own. | `Add time job`, `Add recurring job` |
| Servers (`nodes`) | The stat strip, then each live node's role, liveness, and the functions its run filter claims; the open one (`?open=<identity>`) docks under the list with the same facts and links to its functions. The server serves no scheduler state, workers, queues, or registration time per node, so the view shows none. | None |

A run with a pending cancel reads `Cancelling…` until the job stops, and a run an action finished on reads `Requeuing…`, `Cancelling…`, or `Deleting…` until its change arrives. `T` on an open run, and `Trace` beside its correlation id, open the runs list filtered by that correlation id. `G R` opens the runs list. The palette adds `Go to runs`, `Requeue failed runs`, `Add time job`, `Add chain`, and `Add cron job`, which opens `Add recurring job`. A viewer sees every action disabled with `Needs operate permission`. Per-node scheduler state, and starting or stopping a scheduler, are not part of the module yet.

#### Migrate from `MapHeadlessJobsDashboard`

The Jobs Vue dashboard, its SignalR live-update hub, and its endpoints under `/jobs/dashboard` are removed. The Jobs module replaces them inside the shell, with live changes over the shell's stream. To migrate a host:

1. Delete `AddDashboard(...)` from the `AddHeadlessJobs(...)` delegate, and delete `app.MapHeadlessJobsDashboard()`.
2. Add `AddJobs()` to `AddHeadlessDashboard(...)`, and map the shell with `app.MapHeadlessDashboard()` if the host does not already.
3. Move authentication to the shell: its `WithHostAuthentication`, `WithBasicAuth`, `WithApiKey`, or `WithNoAuth` call covers every module. For separate Jobs rights in host mode, set `ViewPolicy` and `OperatePolicy` on `JobsDashboardOptions`.
4. Point bookmarks and links at `{shell base}/jobs` (by default `/headless/jobs`), the Jobs Dashboard; the runs list is at `{shell base}/jobs/runs`. Nothing serves `/jobs/dashboard` any more.
5. Remove the `SetBasePath`, `SetBackendDomain`, `SetCorsOrigins`, `SetCorsPolicy`, and `ConfigureDashboardJsonOptions` settings: the shell is same-origin, serves every module under its own base path, and owns its JSON. Replace `SignOutHeadlessDashboardAsync("Headless.Jobs.Dashboard")` in the host's sign-out with `SignOutHeadlessDashboardAsync()`, which ends the shell's session.

```csharp
using Headless.Jobs;

builder.Services.AddHeadlessJobs(jobs => jobs.AddModule<MyApp.JobsModule>());
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "ops.view"; host.OperatePolicy = "ops.operate"; })
    .AddJobs());

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHeadlessDashboard();
```

### Messaging module

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

| `MessagingDashboardOptions` member | Default | Description |
| --- | --- | --- |
| `ViewPolicy`, `OperatePolicy` | the shell's | As for every module. |
| `NodeName` | the machine name | This node's name in every object id. Give each node a stable name without `~` or white space. |
| `ChangeOverlapWindow` | 5 s | How far back each change read of a store reads again. It must exceed the longest transaction that enlists an outbox write: a row that commits later than this after its write is not streamed, and shows on the next read of its list. |
| `ChangePageSize` | 200 | The most changed rows one change read returns per store, 1 to 1000. |
| `StoreTimeout` | 1.5 s | How long a read that covers several stores waits for one of them before naming it unavailable. Keep it below the shell's `AggregationTimeout`. |
| `Forwarding.Key` | `null` | The key every node of the deployment shares, at least 32 characters. Sent in `X-Headless-Forwarding-Key` and compared in fixed time. Setting it, or `Forwarding.Policy`, turns forwarding on; see [Forwarding to other nodes](#forwarding-to-other-nodes). |
| `Forwarding.Policy` | `null` | A host authorization policy that admits a calling node through the host's own service-identity scheme, used instead of the key. Set one of the two, not both. |
| `Forwarding.AllowInsecurePeers` | `false` | Lets this node call a peer that discovery lists at an `http://` address. |
| `ForwardedAccess` | `View` | The most a call another node forwards here may do: `View` reads only, `Operate` also runs the commands the serving node decided its operator may run. |

`AddMessaging(Action<MessagingDashboardOptions>)`, `AddMessaging(Action<MessagingDashboardOptions, IServiceProvider>)`, and `AddMessaging(IConfiguration)` configure them.

**Object kinds, ids, and topics.** `published`, `received`, `circuit` (a consumer's circuit breaker on one node, by `{node}~{consumer}`), and `node` (by its `NodeName`). A row's object id is `{node}~{store}~{storage id}`, such as `node-a~billing~0192f3c1-…`: the node that served it by its `NodeName`, the store (`primary` for the main store), and the row's storage id. Stream topics are `messaging.published` and `messaging.received`; the stores are shared by every node, so a change carries no `node` and goes to the plain topic. A change's projection is the row as its list shows it (`id`, `store`, `storageId`, `messageId`, `name`, `lane`, `status`, `consumer`, `retries`, `added`, `expiresAt`, `nextRetryAt`, `changedAt`, `failureFingerprint`, `tenantId`, `isHeld`, `terminal`), and its version is the row's change marker in ticks. A deleted row goes out with no projection.

**Several stores.** Counts, Needs you, Overview, lookups, lists, and the change stream read every store at once, each bounded by `StoreTimeout`. A store that fails or does not answer in time is named in `unavailable` while the others answer; the module is never marked stale for one store. The change cursor keeps one position per store and table, so a slow store neither holds the others back nor loses its place, and a store that was down starts from its current position once it answers.

**Live changes.** A write this node's senders and consumers make reaches its streams at once: the pipeline reports the write after it landed, and the module reads the row back with its marker. A write another node makes, or one inside an application transaction, arrives through this node's change read of the stores, within `ChangePollInterval` plus the read.

**Sidebar counts.** One capped count per key; a published count adds up every store:

| Key | Rows |
| --- | --- |
| `published.all`, `received.all` | every row of the table |
| `published.succeeded`, `received.succeeded` | sent / consumed |
| `published.failed`, `received.failed` | failed for good: `Failed` with no retry pending |
| `published.retrying`, `received.retrying` | failed with a retry pending: the failed count less the terminal count |
| `published.delayed`, `published.scheduled`, `published.queued` | waiting for their delivery time / stored and waiting for the first send / claimed by a sender |
| `received.held` | held by an operator |
| `hosts.unknown-lanes` | rows of every store and table whose lane this host does not recognize |

**Attention and Overview.** One item per failure fingerprint of the failures that ended for good: per consumer for received rows (`2 messages received by billing.charge-consumer failed for good`, pointing at the `consumer`), per store for published rows. Items of one cause share a `GroupKey` across stores, and each carries its list filter `{ store, status, terminalOnly, consumer, fingerprint }`. An item's id is `failed:{kind}:{fingerprint}:{consumer}:{store}`, and its `GroupKey` is the same without `:{store}`. `kind` is `published` or `received`; the fingerprint is `v{n}:{token}`, so it holds exactly one colon, or is empty, with none, for failures recorded without one; the consumer is empty for a published row and may itself contain colons, so read the store as the last segment, the kind and the fingerprint from the front, and the consumer as what lies between. An item another node answers through forwarding has `{node}:` before its `GroupKey`. One warning item per store and table holding rows in an unknown lane, with the id `unknown-lane:{store}:{kind}`. The Overview shows the messages that failed for good, the messages waiting for a retry, and this node's consume rate over the last minute, which is left out while the node measured no second of it.

**Lookup and payload.** The palette accepts a full object id, a bare storage id, or a message id. A storage id or a message id is looked up in every store, and each match is listed with its store in its title; a message id matches byte for byte, trailing spaces included, on every provider. An id naming another node finds nothing on this node. A payload is the row's stored envelope as JSON.

**API** at `{base}/api/messaging`. Every route is a read, so a viewer without a named operator can call it:

| Route | Answer |
| --- | --- |
| `GET published?store=&status=&terminal=&retrying=&name=&fingerprint=&limit=&after=` | `{ items, count, countCapped, nextCursor, unavailable }`: one keyset page, newest first, merged over every store unless `store` names one; `limit` up to 200; `retrying=true` lists failures with a retry pending |
| `GET received?status=&terminal=&retrying=&consumer=&name=&fingerprint=&held=&limit=&after=` | the same over the main store's received rows |
| `GET messages/{kind}/{id}` | `{ row, headers, content, exceptionInfo }`, or `404` |
| `GET received/generations?messageId=&consumer=` | `{ items }`: every received row of the message, oldest first |
| `GET scheduled?store=&name=&page=&perPage=` | `{ store, supported, items, totalCount, reason }`; a provider without scheduled-delivery operations answers `supported: false` with the reason, not an error |
| `GET receipts?actor=&targetKind=&operationType=&from=&before=&limit=&after=` | `{ items, nextCursor }`: one keyset page of the operator ledger in the main store, newest first, every audited intervention including a refused one; `actor` matches ordinally, `targetKind` (`Inbox`, `ScheduledDelivery`, `Published`) and `operationType` (such as `Hold`, `Resend`) ignore case, `from` is inclusive and `before` exclusive, `limit` up to 500; `after` takes `nextCursor`, which is absent on the last page |
| `GET consumers`, `GET contracts` | `{ items }`: the consumers and the message contracts of the host manifest's `messaging` section |
| `GET circuits` | `{ items: [{ id, consumer, state, consecutiveFailures, failureThreshold, openedAt, estimatedRemainingOpenDuration }] }`; `id` is the circuit's object id, `{node}~{consumer}`, which its commands take |
| `GET nodes` | `{ items: [{ name, isLocal, stores, state }], discovery }`: this node and the stores it reads, then every node discovery lists, with `stores` empty; a listed node's `state` is what the last forwarded call to it found (`answering`, `not-answering`, `key-rejected`, `policy-denied`, `view-only`, `insecure-peer`), absent before the first call and for this node; `discovery` says what the last discovery read found, absent while forwarding is off. A dashboard-only host leaves itself out. This route is always answered by the serving node |
| `GET metrics/realtime` | `{ seconds, publishedPerSecond, consumedPerSecond, consumeElapsedMs }`: the last 300 seconds; a second this node did not measure, such as one before it started, is `null`, never `0` |

**Commands.** Every command runs through the shell's command routes at `{base}/api/messaging/commands`, so a viewer sees each one disabled with `Needs operate permission` and a call answers `403`. A call on one object the rules refuse changes nothing and answers `409` with the reason, such as `Release first`. A command that writes a row needs an operator with a stable name, because the store records who acted; a user without one sees `Sign in with a named account`. Re-execute, Purge, Re-send, Delete, Revoke, and Force open ask for a confirmation; no single-row command needs a reason, but a re-send or delete records the reason given on its receipt with the operator's name.

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

Re-execute, Purge, Re-send, and Delete run over a filter as Operations, which always need a reason (a confirm without one answers `400` with `errors.reason`). The filter is the list scope `{ store, status, terminalOnly, consumer, name, fingerprint }`. A Re-send or Delete Operation whose `store` names an additional outbox runs over that outbox, with each receipt in the main store; Re-execute and Purge run over the main store only, and a filter naming an outbox or an unknown store answers `400` on `filter`. Confirm freezes the scope on the store's latest change marker, and each chunk writes a row only while its marker is unchanged, so a row another chunk, node, or earlier run already changed is skipped with its reason, never written twice; an Operation stopped part-way and run again on another node changes each row once. A re-execute scope leaves held rows out. One row's failure, such as a main-store receipt that could not be written, is reported as `Failed` on that row while the Operation runs on.

The module's front end is built into `wwwroot/dist/modules/messaging` and embedded in the package, which serves it through `MessagingAssets`; its `manifest.json` there declares the sidebar and its count keys. The package's build runs the npm build, so `wwwroot/dist` is not committed.

#### Forwarding to other nodes

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

- **Admission.** A forwarded call is admitted by the scheme `HeadlessDashboardForwarding`: with `Policy` set, the host's policy decides and the key handler is not used; otherwise the call must carry the shared key. The operator and access headers are read only after admission, and only on the Messaging module's endpoints, so a browser that sends them without the key is judged by its own session. The outbound calls use the named `HttpClient` `Headless.Messaging.Dashboard.Forwarding`; in policy mode, configure it to present the node's service identity, such as a client certificate.
- **Access.** The serving node checks its own operator's view and operate permissions and sends the result. The peer acts at the lower of that decision and its own `ForwardedAccess`, and records the operator as `{name} via {serving node}`, such as `ops via orders-api`, on receipts and in the operator log. A forwarded write that asserts view access is refused with `403`, and so is a write whose operator is a placeholder rather than a named account.
- **The key stays secret.** It never appears in the manifest, logs, startup errors, or responses, and is compared in fixed time.
- **Which node answers.** A read names its node with `?node=`; a command or Operation names it through the object ids or the filter's `node`. A node name resolves only against discovery entries: a name discovery does not list answers `404` with `Discovery lists no Messaging node named '{node}'.` and no call leaves the node, so a caller cannot point a node at an address of its choosing. A call another node forwarded is always served where it lands, and a node never forwards to itself.
- **No local fallback.** A peer that cannot answer is never replaced with this node's rows. A read answers `502` with the state in `X-Headless-Node-State` and the problem's detail; a command answers `409` with the same sentence.

| State (`X-Headless-Node-State`) | What the operator reads |
| --- | --- |
| `not-answering` | `Node {node} is not answering` |
| `key-rejected` | `Node {node} rejected this node's forwarding key` (the peer answered `401`) |
| `policy-denied` | `Node {node} denied this node by its forwarding policy` |
| `view-only` | `Node {node} accepts view-only forwarded calls`: the peer keeps the default `ForwardedAccess`; its commands show disabled with this reason |
| `insecure-peer` | `Node {node} is an insecure peer: its address is not https, and AllowInsecurePeers is off`; no request is sent |

- **HTTPS.** A peer whose discovery address is `http://` gets no request unless `AllowInsecurePeers` is set, because the credential and the operator's name would cross the network in clear text. Set `Scheme = "https"` on `ConsulDiscoveryOptions` or `K8sDiscoveryOptions`.
- **Needs you** lists every node's items, each title ending in ` (node {node})`; another node's item ids and group keys are prefixed with `{node}:` and its filter names the node. Each peer's answer is cached for 30 seconds. A peer that cannot answer becomes one critical item, `node-state:{node}`, titled with its state; two discovery entries with the same name are one node and give one item.
- **Live changes.** A stream on this node that subscribes to `messaging.received@{node}` or `messaging.published@{node}` makes this node read that peer's change feed about once a second while the topic is watched, and stops with the last watcher. The changes arrive on the node topic. A peer that stops answering or leaves discovery marks only its own views stale (`node.stale`), within 5 seconds of leaving discovery, and loses its cursor. A peer that no longer knows the cursor, such as after a restart, answers `410`; the stream starts a new cursor and sends `node.reset`, so that node's views refetch.
- **Operations.** Confirm and every chunk of an Operation whose filter names another node run on that node. The serving node keeps the scope token and sends the filter, the marker, the cursor, the operator, and the reason, so the peer's ledger records each chunk as `{name} via {serving node}`.

A host refuses to start when forwarding runs under `WithNoAuth()`, when the key is shorter than 32 characters or blank, when both a key and a policy are set, or when the policy is not registered, has no authentication scheme, uses the forwarding scheme, or is also the dashboard's view or operate policy. None of these messages quotes the key.

#### Dashboard-only host

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

`AddMessagingStandalone(Action<MessagingDashboardOptions>)`, its `IServiceProvider` and `IConfiguration` overloads, and the Kubernetes overload above take the same options.

#### Migrate from `MapHeadlessMessagingDashboard`

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

### Dashboards a host writes

A dashboard a host writes itself, outside the shell, authenticates through this package's per-dashboard primitives: the keyed `IAuthService`, the authentication scheme and policy that check a dashboard's own credential, the host-mode session, its view and operate policies, the sign-in endpoints, and the endpoint conventions that apply them. The shell reuses the host-session rules below.

- Mount the dashboard on the host's endpoints, so the host's `UseAuthorization` evaluates the dashboard's own rule and never the host's fallback policy. A host fallback policy that requires a tenant claim, for example, does not lock out a tenantless operator.
- Each dashboard owns its authentication. A host that runs the shell and its own dashboard can give them different modes and credentials; neither registration overrides the other.

#### Setup

```bash
dotnet add package Headless.Dashboard
```

Register the authentication under a name your dashboard owns, map the sign-in endpoints, and protect the dashboard's endpoints with the same name:

```csharp
using Headless.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

builder.Services.AddDashboardAuthentication("Reports.Dashboard", config =>
{
    config.Mode = AuthMode.Host;
    config.Host.ViewPolicy = "ReportViewers";
    config.Host.OperatePolicy = "ReportOperators";
});

var app = builder.Build();

var reports = app.MapGroup("/reports");
// api/auth/info, api/auth/session, api/auth/sign-out, and auth/sign-in. Host mode needs them: they issue the session.
reports.MapDashboardAuthEndpoints("Reports.Dashboard");

// Every other endpoint requires the "Reports.Dashboard" session; the host's UseAuthorization checks it.
var api = reports.MapGroup("/api").RequireDashboardAuthentication("Reports.Dashboard");
api.MapGet("/runs", ReportsEndpoints.GetRuns);
api.MapPost("/runs/{id}/cancel", ReportsEndpoints.CancelRun).RequireDashboardOperation("Reports.Dashboard");

// The host's own sign-out also ends the dashboard session.
app.MapPost("/account/sign-out", async (HttpContext context) =>
{
    await context.SignOutHeadlessDashboardAsync("Reports.Dashboard");
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/");
});
```

`AddDashboardAuthentication(name, IConfiguration)` and `AddDashboardAuthentication(name, Action<AuthConfig, IServiceProvider>)` take the same name. A blank name throws `ArgumentException`. Each registration adds, once per name, an authentication scheme and an authorization policy named `name` for Basic and API key, and the host-mode session scheme `{name}.Session` with the policies `{name}.View` and `{name}.Operate`. `MapDashboardAuthEndpoints`, `RequireDashboardAuthentication`, and `RequireDashboardOperation` throw `InvalidOperationException` for a name no registration added.

`GET {base}/auth/sign-in` gives a host user who is already signed in a dashboard session at once. Otherwise it challenges `ChallengeScheme` once, and the identity provider returns the user to the dashboard. An application whose SPA signs in with a bearer token has no cookie for the dashboard to read. It exchanges the token once for the dashboard session, then navigates to the dashboard:

```js
const response = await fetch('/reports/api/auth/session', {
  method: 'POST',
  headers: {
    Authorization: `Bearer ${accessToken}`,
    'X-Requested-With': 'XMLHttpRequest',
  },
});

if (response.status === 204) {
  window.location.assign('/reports/');
}
```

The exchange answers `204` and sets the session cookie, `401` when no host scheme authenticated the request, and `403` when the user fails the view policy. The token never goes into a URL or browser storage. The shell's `{base}/api/auth/session` works the same way.

#### Configuration

`AuthConfig` exposes `Mode`, `BasicCredentials`, `ApiKey`, and `Host`. Registration validates each named configuration's selected mode at startup; incomplete credentials or invalid host options fail with `OptionsValidationException`.

The shell builder exposes `WithNoAuth()`, `WithBasicAuth(username, password)`, `WithApiKey(apiKey)`, and `WithHostAuthentication(Action<DashboardHostAuthenticationOptions>? configure = null)`. `AuthConfig.Host` is a `DashboardHostAuthenticationOptions`, read only in host mode:

| Option | Default | Description |
| --- | --- | --- |
| `ViewPolicy` | the host's default policy | Host policy a user must pass to get a session and to read from the dashboard. |
| `OperatePolicy` | `ViewPolicy` | Host policy a session must also pass for operator actions, the dashboard's state-changing requests. |
| `ChallengeScheme` | none | Host scheme `auth/sign-in` challenges when no host user is signed in, such as OpenID Connect. Without it the dashboard offers no Sign in link, and users get a session through the exchange. |
| `SessionCookieName` | `__Host-` and the dashboard name in lower case, such as `__Host-reports.dashboard`; the shell's default is in [Shell sign-in and sessions](#shell-sign-in-and-sessions) | Set it when two applications on one host name mount the same dashboard. It must be a cookie-name token. |
| `SessionIdleTimeout` | 30 minutes | A session with no authenticated dashboard request for this long ends. Must not exceed `SessionLifetime`. |
| `SessionLifetime` | 8 hours | Absolute lifetime from sign-in, whatever the activity. |
| `RevalidationInterval` | none | How often `Revalidate` runs for one session. Required when `Revalidate` is set. |
| `Revalidate` | none | `Func<HttpContext, ClaimsPrincipal, ValueTask<bool>>` that confirms the session's user is still allowed in; `false` ends the session. |

#### Host sessions

- Each dashboard keeps its own session cookie: `Path=/`, `HttpOnly`, `Secure`, `SameSite=Strict`, and host-only. In host mode the dashboard API authenticates with that cookie alone. The host's own cookie or bearer token does not open them.
- A session is issued by `GET {base}/auth/sign-in?returnUrl=` or `POST {base}/api/auth/session`. Both copy the host user's authenticated identities and claims, with their name and role claim types, into the session. A user who fails the view policy, or whom `Revalidate` refuses, gets `403` and no cookie. The host's bearer token is not copied.
- `auth/sign-in` follows only a local `returnUrl` under the dashboard's base path with no `.` or `..` segment; any other value goes to `{base}/`. It challenges `ChallengeScheme` at most once per sign-in: when the user comes back still not signed in, it answers `401` instead of challenging again.
- `POST {base}/api/auth/sign-out` deletes the dashboard cookie only. `HttpContext.SignOutHeadlessDashboardAsync(name)` deletes the same cookie from host code.
- The host user is found through the host's schemes, never the dashboard's. `auth/sign-in` authenticates the schemes `ViewPolicy` lists, when it lists any; otherwise `ChallengeScheme`, then the default authenticate scheme. The exchange authenticates the `ViewPolicy` schemes, then the default authenticate scheme.
- A session ends at `SessionLifetime` after sign-in, and after `SessionIdleTimeout` with no authenticated dashboard request. A request made after half the idle window has passed renews the cookie. A renewal never moves the cookie's expiry past `SessionLifetime`.
- A short-lived bearer token exchanged for a session becomes a session that can last up to `SessionLifetime`, after the token itself expires. A host that revokes tokens, disables accounts, or removes roles should set `Revalidate` and `RevalidationInterval`, and should call `SignOutHeadlessDashboardAsync` from its sign-out. Both act on the next dashboard request.
- Policies evaluate against the copied claims. A requirement that reads the request, a header, or the bearer token fails. A policy that lists `AuthenticationSchemes` fails closed: it passes only when one of those schemes authenticated the user the session was issued for.
- The session cookie is protected by the host's Data Protection key ring. Every instance serving the dashboard must share that key ring; `Headless.DataProtection.Blobs` persists it to blob storage (see [api.md](api.md#headlessdataprotectionblobs)).
- With `Path=/`, the browser sends the session cookie on every request to the host, not only dashboard requests. It carries the copied claims, so a principal with very many claims can make request headers too large.
- `GET {base}/api/auth/info` is anonymous. It reports `mode`, `enabled`, `signInAvailable`, and, in host mode, the current session's `authenticated`, `name`, and `canOperate`. It never exposes credentials. A frontend decides whether to show its sign-in page from this call, because the `SameSite=Strict` cookie can be missing from the first page load after an identity-provider redirect.

#### Design and runtime behavior

- Credential comparisons are constant-time.
- Authentication is per dashboard. `AddDashboardAuthentication(name, …)` configures the `AuthConfig` options instance named `name` and registers a scoped `IAuthService` keyed by `name`; the dashboard's authentication scheme and its `api/auth/info` endpoint resolve the service by that key. Nothing registers an unkeyed `IAuthService` or a raw `AuthConfig` singleton. To replace a dashboard's `IAuthService`, register yours with `AddKeyedScoped<IAuthService, TService>(name)` before the dashboard registers; the registration keeps an existing keyed service.
- `RequireDashboardAuthentication(name)` reads the named `AuthConfig` when the host builds its endpoints. `None` allows anonymous access. `Host` requires `{name}.View`: a dashboard session whose user passes `ViewPolicy`, or the host's default policy when it is empty, answering `401` without a session and `403` when the policy refuses. Basic and API key require a user the dashboard's scheme signed in, and refuse any other request with an empty `401`. The scheme checks a credential only on an endpoint that convention marked with its name, so a host whose only registered scheme is a dashboard's (which ASP.NET Core then makes the default) does not check or log a dashboard credential on its own requests.
- `RequireDashboardOperation(name)` marks operator actions. In host mode it adds `{name}.Operate`, which requires `OperatePolicy`, or `ViewPolicy` when it is empty. Other modes add nothing: Basic and API key share one credential for viewing and operating.
- On success in Basic or API key mode, `HttpContext.User` for that request is the dashboard identity: one `Name` claim with the `AuthResult.Username`, authentication type `name`. It replaces a host sign-in on that request. In host mode `HttpContext.User` is the session's copy of the host user.
- In Basic and API-key modes, the `access_token` query parameter counts as a credential only on a request routed to a SignalR hub endpoint (its routing metadata, not its path, decides), because a browser cannot set headers on a WebSocket. Every other request must send the `Authorization` header. Host mode accepts no query credential.
- Every state-changing request to such a dashboard (`POST`, `PUT`, `PATCH`, `DELETE`) must carry an `X-Requested-With` header, or it gets `400`. That covers its API, `api/auth/session`, and `api/auth/sign-out`, in every mode. A cross-site `<form>` cannot set the header, and a cross-site script that sets it needs a CORS preflight the dashboard refuses.
- `auth/sign-in`, `api/auth/info`, `api/auth/session`, and `api/auth/sign-out` are anonymous; `auth/sign-in`, `api/auth/session`, and `api/auth/sign-out` answer `404` outside host mode.
- The host pipeline must run `UseAuthorization` between routing and endpoints (a `WebApplication` adds it when the host does not), and `UseAuthentication` before it for host authentication. A host that calls `UseAuthentication()` must also call `UseAuthorization()` after it: the `UseAuthorization` a `WebApplication` adds on its own runs ahead of the host's `UseAuthentication`, so host authentication would not have signed the user in yet.
