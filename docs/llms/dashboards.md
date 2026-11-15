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

A module's own endpoints refuse bad input the way commands do, so the SPA reads every refusal one way: a `400` validation problem whose `errors` name each refused field as the JSON body spells it (`children[0].function`), with a `rules` extension naming the rule each field broke, and `detail` holding the first message. `DashboardValidation.Problem(ValidationResult)` builds it from a FluentValidation result: a field's rule is the error code the validator set with `WithErrorCode`, `required` for `NotEmpty` and `NotNull`, and `invalid` otherwise. `DashboardValidation.Problem(field, rule, message)` refuses one field, for a check that needs the host, such as an unregistered function name. The rule names the shell and both modules use are constants on `DashboardValidation` (`RequiredRule`, `InvalidRule`, `EnumRule`, `MinimumRule`, `MaximumRule`, `MaxLengthRule`, `FormatRule`, and `JsonRule`, which a malformed body breaks under the field `BodyField`, `body`); pass them to `WithErrorCode` or `Problem` so a module's refusals read like the shell's. Keep a refusal that depends on stored state, such as a run that can no longer be cancelled, out of the validator: it belongs to the command's rules, which check it again when the command applies.

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

The page reads live changes from one server-sent-events stream at `GET {base}/api/stream?topics=`, with topics `{module}.{kind}` or `{module}.{kind}@{node}`, comma-separated or repeated, at most 64. The open tabs of one dashboard and one signed-in user share that stream (the lock and channel names carry a hash of the user and their permissions, never the name itself, so a new user's tab never follows a tab still holding the previous user's stream after a sign-out through the host app): one tab holds it, chosen through the Web Locks API, and relays its events to the others over `BroadcastChannel`. A sign-out from the dashboard travels on a separate channel named by the base path alone, so it closes the stream in every tab at that base path, including a tab that loaded under other permissions or another display name and holds its own stream. A tab leaves on `pagehide`, so the stream drops the topics only it watched; a page restored from the back-forward cache reloads. A topic stays on the stream for 5 seconds after the last view in a tab lets go of it, so a route change that drops a topic and takes it back does not reopen the stream.

- **Events.** `subscribed`, `change` (module, kind, id, the node when the object has one, topic, version, and the formatted projection), `module.stale`, `attention.changed`, `node.stale`, `node.reset`, `heartbeat`, and `reset`. Each stream receives only modules the user may view and only the topics it asked for, matched exactly.
- **Node topics.** An object whose `ObjectRef.Node` is set, because one node's own store holds it, changes on `{module}.{kind}@{node}` only, such as `messaging.received@node-b`; a stream subscribed to `@node-b` never receives `@node-c`'s changes, and one subscribed to `messaging.received` receives neither. A module that implements `IDashboardNodeTopicObserver` hears `OnNodeTopicWatched` when the first stream on this node subscribes to one of its node topics and `OnNodeTopicUnwatched` when the last one closes; both must return at once. `IDashboardEventPublisher.PublishNodeStale(module, node, stale)` and `PublishNodeReset(module, node)` send `node.stale` and `node.reset` to the streams subscribed to a topic of that module for that node, so only that node's views show `Stale` or refetch; a stream that opens while a node is stale is told at once. Every connection opens with `subscribed` and then replays the modules and nodes out of date now, so the page clears its stale reports on `subscribed`: a node the server forgot, or a reader that recovered without sending `stale: false`, reads live again after the next reconnect.
- **Resume.** Each node keeps its recent events in a bounded ring. A reconnect sends its last event id (the `Last-Event-ID` header, or the `lastEventId` query parameter) and resumes from it. When the node no longer holds that position, the stream sends `reset` and the page refetches every query, including reads no change patches, such as a node list, a chart's series, or a count.
- **Pause.** The top bar's Pause holds this tab's data changes (`change`, `attention.changed`, `reset`, `subscribed`, and `node.reset`) while the stream stays open for the other tabs, and the live state reads `Paused · N updates`; Resume applies them in the order they came. A tab holds at most 500 events: past that, the held events give way to one `reset`, so Resume refetches every query instead of losing changes the server never sends again, and the count of waiting updates stays exact. A module going stale, session end, sign-out from another tab, and the result of the operator's own commands, which return through the API, still apply at once. Pause is not remembered across reloads.
- **New rows.** A change to an object no loaded list holds joins the list's `N new` control. Each topic keeps at most 500 such rows; past that the oldest goes, and the control reads `500+ new` (`useNewRows` returns `isCapped`) until the operator shows them, which reads the list again.
- **Versions.** The page orders versions as the server does, as integers when both are digits and ordinally otherwise, through one rule, `isNewerVersion` from `/data`: a change applies only to a holder older than it, a holder with no version is older than any version, and a fetched row with no version is the server's answer now.
- **Cross-node changes.** While a node has at least one stream open, it reads every module's `IDashboardChangeSource` each `ChangePollInterval`, starting from `GetStartCursorAsync`. No broker is involved: the module's store is the only shared state. A node with no open stream reads nothing. A read that throws marks that module's views `Stale`; the reader backs off and retries with the same cursor. A read that reaches only some sources returns a `DashboardChangeBatch` whose `UnavailableSources` names the others; the module stays live, and `module.stale` carries them as `unavailable` with `stale: false`.
- **What a change source's store must provide.** A change marker stamped by the store (a sequence or the store's clock) in the same statement as every write the dashboard shows; an index on it; reads from the cursor minus an overlap window longer than the store's longest write transaction; and a bounded page per read. A read that returns a full page sets `DashboardChangeBatch.HasMore`, and the node reads the next page in the same interval, up to 10 reads, so a burst larger than a page does not leave the module further behind on every interval. The shell drops a change whose `Version` it already sent for that object.
- **Same-node changes.** `IDashboardEventPublisher`, a singleton the shell registers, pushes a change to this node's streams at once, after the module's store commits it. Changes to one object within 250 milliseconds coalesce into one event. A version not newer than the last sent is dropped; versions compare as integers when both parse as one, and ordinally otherwise, so use a store sequence or a fixed-width timestamp. Other nodes still learn of the change from their own reads.
- **Closing.** A stream closes at the session ticket's expiry, at its next revalidation in host mode when `Revalidate` is set, and after 5 minutes under Basic and API key. The browser reconnects, which checks the session, credential, and module permissions again. When the browser gives up on a refused reconnect, the page waits out a back-off that doubles up to 30 seconds, each wait drawn between half and all of it so the tabs a node restart dropped do not return in the same second, then asks the endpoint for its status: `401` ends the session in every tab, which lands on the sign-in page as session ended, and any other answer, or none within 5 seconds, reopens the stream. A request whose `Origin` is not the shell's own gets `403`. A stream never delays the host's shutdown.

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
| `Arguments` | none | `DashboardCommandArgument`s: a small JSON-Schema subset (`Text`, `WholeNumber`, `Number`, `Toggle`, `DateTime`, `Json`; required, allowed values, minimum, maximum, maximum length, not before now) that the shell renders as a form and validates on the server. |

Two argument kinds carry text with a meaning the shell checks:

- `DateTime` is ISO 8601 text with an offset, such as `2026-12-15T08:00:00Z` or `2026-12-15T10:00:00+02:00`; read it with `JsonElement.GetDateTimeOffset()`. Text without an offset, or with a `+0200` offset that has no colon, is refused with rule `format`, because the server cannot tell which zone it meant. `NotBeforeNow` refuses an instant before the registered `TimeProvider`'s now with rule `minimum`. The schema states the kind as `"type": "string", "format": "date-time"`, plus `"notBeforeNow": true` when set. The form reads the date and time to the minute in the dashboard's display time zone and sends the UTC instant. A date typed without its time, on an optional argument as on a required one, is refused as incomplete rather than sent as omitted.
- `Json` is JSON text, such as a request body, at most `MaxLength` characters. The server parses it and refuses text that does not parse with rule `format`, even from an API call that skipped the form. The schema states it as `"type": "string", "contentMediaType": "application/json"`. The form edits it as text and shows it in the payload colours once it parses.

A row result can name the object the command created for it, such as the run a duplicate made: set `DashboardRowResult.Created` to its `ObjectRef`. The shell returns it as the row's `created` (`module`, `kind`, `id`, and `node` when set) and leaves the field out of a row without one. The SDK's `CommandsApi.run` returns it on `RowResult.created`, so a page can open the new object.

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

When a filter carries text an operator searched for, set `DashboardOperationScope.LoggedFilter` from `PrepareAsync` to the filter with that text replaced by a marker, such as `"content": "(redacted)"`. The shell signs the full filter into the encrypted token, so every chunk still runs with the search, and records `LoggedFilter` as the `Scope` of the confirm and of every chunk. Leave it `null` and the log records the filter as sent. Use a fixed marker, not a hash of the text: a plain hash of short, guessable text can be reversed by guessing.

Every command call the shell runs, every Operation confirm, and every chunk writes an `OperatorLogEntry` (time, actor, channel, module, command, action, ids or scope, count, reason, done, skipped, failed, Operation id, and the cursor a chunk started from). A refused call writes none. The default sink writes each entry as a structured `ILogger` event in the `Headless.Dashboard.OperatorLog` category. That event is an audit record, so it carries the operator's identity (often an email) and the reason they typed in plain text, unclassified for log redaction; a host that must keep personal data out of its log store routes the operator log to its own sink instead. Add sinks as singletons; each entry goes to every sink in registration order, after the change it records was made, so a sink that throws fails the request but does not undo the change:

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

The SDK's components are shadcn's React Aria components, copied into the SDK and kept on the SDK's own props: React Aria still supplies the behaviour (press, focus, keyboard, overlays, the data grid), and shadcn supplies the look. Tailwind CSS compiles only the SDK's and the shell's sources, inside the shell build, into one stylesheet that maps shadcn's semantic classes (`bg-background`, `text-foreground`, `border`, `ring`, `primary`, `muted`, `destructive`, `card`, `popover`) onto the dashboard's tokens in `tokens.css`. Dark is the default: the `dark:` variant applies everywhere except under `data-theme="light"`. Classes use logical properties, so a right-to-left page mirrors. The stylesheet injects no `<style>` tag, so it works under the document's nonce-based Content Security Policy. A module ships no CSS and runs no Tailwind build; it composes SDK components and sets no class on its own elements, because a utility class written in module source has no CSS behind it. The module preset's build fails when a module's manifest lists a stylesheet, and each module's lint configuration rejects any `className` attribute.

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
| `/components` | The design-system components. The list-and-detail frame: `PageFrame` and `usePageFrame`, `DetailPanel`, `FieldGrid` with `GridField`, `HistoryList`, and `ExceptionBlock` (see [List views](#list-views)). The data display blocks: `StatStrip` and `StatCard`, `PageHeader`, `FilterToolbar` with `FilterSelect`, `FilterChips` and `FilterChip`, `SegmentedControl`, `Tag`, `BarChart`, and the dashboard `Card` with `CardRow` inside `Cards`. The text blocks a view composes instead of writing class names: `Banner` (a one-line notice tinted by its `tone`: `info`, `warn` for data that may be out of date or incomplete, `error` for a failed load), `LoadError` (a failed load's error and Retry in the error tone; with `kept` it says the view still shows its last successful read), `StaleNotice` (`Stale` with Refresh in the warn tone while its `stale` prop holds, the data on screen kept, and nothing while `failed` holds, because the view's `LoadError` already offers the same read; the turn to stale is announced once, and not at all when the whole stream dropped, which the top bar's live state already says), `Text` (tones for a hint, faint text, a label, a title, an error, and a footnote; `as="h3"` is a section heading inside a detail panel, which renders as `h2` on the object's own page), `Fields` and `Actions` (a labelled group of fields and a row of actions), `Section` (a second block under a page's list, with its heading, count, and one-sentence `description` on the page gutter), `KeyValueTable` (names and their values in two aligned mono columns, such as a message's headers, with a copy control per value), `Tooltip` (a React Aria tooltip under a control, which an icon-only `Button` adds on its own), `Timestamp` (every timestamp: the short relative form, `4 min. ago`, in whole units counted toward zero and always numeric, so `1 day ago`, never `yesterday`; it moves on from one timer the whole page shares, which sleeps until the next text change; the exact time, zone, and offset in a tooltip on hover, keyboard focus, and press, because it is a button, whose press never reaches a table row behind it; `zone` reads the exact time in another zone, `missing` stands in for an unstored time, `due 42 sec. ago` marks a `scheduled` time that has passed, and `excludeFromTabOrder` keeps a widget with one roving tab stop, such as `Timeline`, at one stop), `CopyButton` (copies its `text`, says `Copied` in a polite live region and on its label, and falls back to the document's copy command when the browser refuses the clipboard API, saying so when that fails too; without a `label` it is the copy icon named by its `aria-label`), `QueryState` (a read as a view shows it: the skeleton once it has taken 150 ms, the error with Retry when the first read fails, the last rows kept under the error when a refresh fails, else what `children` draws from the data; it takes a TanStack Query result), `Skeleton` (a placeholder in the `shape` of what loads: `rows` inside a card or a panel, `table` for a list region on the page gutter, `fields`, `lines`, or `chart`), and `EmptyState` (an icon in a tile, a `title`, the explanation, and the next `action`, centred in the room it stands in for; every empty card, chart, tab, and section has one least height, and inside a flex column it takes what is left, so an empty `Card` fills its row with the state in the middle; `placement="page"` gives an empty list or page more room above and below, and `role="alert"` marks an object that is gone). Also `Timeline` (stored events in a grid where arrows move and `Enter` selects an event, focus staying in the timeline), `ScheduleStrip` (SVG marks for the next fires in the scheduler's zone, `[`/`]` widen and narrow it), `Dialog` (sized and placed in fractions of the visual viewport, which loses the keyboard's height while a phone's keyboard is up, so the keyboard shrinks the dialog instead of covering it; `role="alertdialog"` and `aria-describedby` when it asks for a decision before work goes on), `Sheet` (a modal panel against the inline-start edge over a scrim, spanning the visual viewport's height; Tab cycles inside it, its Close button, `Esc`, or the scrim shuts it, and focus goes back to whatever opened it; the shell's phone navigation, never an object to read or edit), `Meter`, `TrendChip`, `Popover` (a button, with an optional `icon`, that opens a small floating dialog, such as a panel's further actions), form fields (`TextInputField`, `SelectField`, and `CheckboxField` in a form, each sized to what it holds through `width`: `short` for a number, `medium` for a name, a zone, or a time, `full` for a sentence or JSON), `CommandPalette` (its search field draws the accent focus ring inside its edge, on every focus), `usePageTitle` (names the calling view's page for the document title; see [List views](#list-views)) with `useViewTitle`, the store the shell reads it from, `PayloadView`, status marks, `FailureReason` (a failure in one line for a list cell: the exception type without its namespace, in the failure colour, then its message, with the whole text as the title; it draws nothing without a type and a message) with `shortTypeName` (a .NET type's own name, without its namespace or a generic type's arguments) and `splitFailure` (reads a stored `Type: message` line apart, and keeps a line that opens with no type name ending in `Exception` or `Error` whole as the message), `Kbd` (a key cap, always left to right), live regions (`Announcer` is a polite region that stays in the page while its `message` changes, and a new `id` announces the same words again; `LiveIndicator` reads `Paused · N updates` when given the waiting count), toasts (announced from the one `Notifications` region, each a group named by its message), the keymap hooks, and `below`, the named breakpoints as media conditions for script |
| `/table` | `DataTable`: virtualized, with keyset load-more, column priority, rows at the density the operator chose in the account menu (`comfortable` or `compact`, one choice for every list, remembered per browser), an empty state centred in the list's region (`emptyIcon`, `emptyTitle`, the `emptyState` explanation, and the `emptyAction` next step), a faint dash in a cell that holds nothing, and rows that arrive above held behind `N new`. A column's `kind` sets its cells' face and alignment: `time`, `number`, and `duration` read in mono with tabular figures (the last two at the inline end), `id` is a mono identifier kept left to right, `status` is a dot and a word, and `prose`, the default, is sans text; `align` overrides the end. A column with a `sortKey` has a sort button in its header that sets `aria-sort` and asks the list for ascending, then descending. `SearchField` debounces its text, or in submit mode sends it only on `Enter` or its Search button. `ListSort`, `ListRange`, and `ListPageRequest` type what the list hook passes to its page reader. Given a page frame (`frame`), it marks the frame's open row `aria-selected`, adds a checkbox column for the frame's checked rows in a `multiple` list, and tags the rows a bulk action refused. It pins the checkbox column and its first column to the inline start while the rest scroll sideways inside the table, and pins a `rowActions` column to the inline end, so a row's actions never need a sideways scroll; a hairline marks that column's edge while content scrolls under it. The row header column never goes narrower than 192 px, twice the floor the other columns share, so on a phone the name stays readable and the status and time scroll under it. It is its own entry, so the browser loads it with the first view that lists rows, not with the shell |
| `/date-range` | `DateRangeField`, a list's time range, controlled through `value` and `onChange` as a `TimeRange`: a preset window that ends now (`{ kind: 'preset', preset: '24h' }`) or two whole ISO days, both included (`{ kind: 'custom', start, end }`), or `undefined` for none. Given `presets` (`timeRangePresets` holds 15 min, 1 hr, 24 hr, 7 days, and 30 days, labelled in the locale) it shows them as a `SegmentedControl` with `Custom`, which fills the days the preset covered in the display time zone; days typed or picked are sent only once both are whole and in order. Its calendar marks today in the display time zone and moves the mark at that zone's midnight, daylight saving included; `min` and `max` bound the pickable days, and an earliest day after the latest shows every day disabled. `compact` draws it as a chip for a page header's toolbar, reading its label, or the chosen preset or days once set, and opening a popover with the presets over the calendar and the typed start and end days; the calendar takes the focus, and `Tab` moves on to the typed days. `useTimeRangeSearch({ presets, defaultRange })` keeps the range in the URL through the shell's router, as `?range=7d` or `?from=2026-11-01&to=2026-11-10`, one history entry per change, keeping the other params; custom ranges store days, not instants, so a link means the same days in any display zone, a value it does not recognise reads as `defaultRange` (no range when left out), and the default writes no params. Pass `presets` and `defaultRange` from module scope. `useListRange(range)` turns a range into the `ListRange` a keyset list takes, in the display time zone with an exclusive next-midnight end; a preset resolves when the range changes, so its window ends when it was picked. `toListRange`, `toCustomTimeRange`, `timeRangeFromSearch`, and `timeRangeToSearch` are the same mappings as functions; and `DateTimeField`, a date and time to the minute in the display time zone, sent as a UTC instant, as `''` while untouched, or as `incompleteDateTime` while only some segments are typed, which `SchemaForm` loads on demand for a `DateTime` argument. It is its own entry, so the calendar loads with the first view that filters by date, not with every view that lists rows |
| `/combobox` | `FilterCombobox`, a list filter for choices too many to scan, such as every function a host runs: the operator types to narrow the list, then picks with the arrow keys and `Enter` or a press. It reads as `FilterSelect`'s chip does, with its value in the set state, and `emptyLabel` is both the placeholder and the choice that turns it off. Options with a `section` group under that heading. The list is virtualized, and a screen reader hears each option's place in the whole list across sections. It is its own entry, so React Aria's combobox loads with the first view that filters this way |
| `/data` | The API client and hooks (`useDashboardApi`, `useDashboardList`, `useDashboardKeysetList`, `useDashboardObject`, `useNewRows`), change patching, and capped counts. A stream change patches each cached copy of its object older than it, whatever the other copies hold, and a fetch keeps per row whichever of its row and the cached one has the newer version, so a request that lands after a newer change never puts the older row back; a change that arrived before a list's first page loaded applies to that row and leaves the new rows notice. `useDashboardKeysetList` keys its cache on the sort, search, and range as well as the filter: any change drops the cursor, reloads the first page, and aborts the old scope's page in flight, so its late answer never shows; while the first page of a new sort or search loads, the old rows stay on screen |
| `/locale` | `LocaleProvider`, which keeps dates Gregorian under every locale (`gregorianLocale` adds `-u-ca-gregory` to one such as `ar-SA` whose default calendar is another, keeping its language and digits), `useFormat` (number, count, `size` (`812 bytes`, `70 kB`, `1.5 MB`, `2.1 GB`, 1,024 to the step), `duration` (`340 ms`, `1.2 sec`, `3 min 4 sec`, `2 hr 5 min`; at most two units, a zero second unit left out, and every step rounded before its threshold, so 239,600 ms reads `4 min`, never `3 min 60 sec`), exact, and zoned time formatters in the display time zone; size and duration name their units through `Intl.NumberFormat` and join them with `Intl.ListFormat`, so they read in the locale), `TimeZoneField`, `useRelativeTime(at, style)`, relative text that re-renders exactly when it would change, from the one page-wide timer every `Timestamp` shares, and `formatRelativeTime(at, now, locale, style)` for a fixed `now`; both floor toward zero and stay numeric |
| `/icons` | A curated set of Lucide icons for stat cards, card titles, actions, and empty states. Icons are monochrome, 15 px, and decorative; a status is never an icon, only a dot and a word. `RiseIcon`, `FallIcon`, and `NoChangeIcon` lead a `TrendChip`'s change. `StatCard`, `Card` (its `icon` before the heading and its `emptyIcon` over its empty state; an element passed as `empty`, such as a skeleton or a load error, is shown as it is, without the icon), `EmptyState`, `Popover`, `Button`, and `CommandAction` take an `icon`; a `Button` with an icon and no label is a square icon button named by its `aria-label`, with that name, and its disabled reason, in a tooltip on hover and keyboard focus (`tooltip` replaces the text when the label carries an id), a row action's included. A tooltip opened by keyboard focus stays until focus moves or `Esc`; a click opens none. A tooltip flips above its control and shifts along the edge to stay inside the viewport, wrapping at a readable width. The window controls of a detail panel and the row actions of a table are icon buttons. The shell loads them with its first page, because its toasts draw the close icon |
| `/charts` | `Chart`: one API for a line, an area, a stacked bar, and a donut, drawn from served series. A point the server did not measure is `null` and draws as a gap, never as zero. A line, area, or stacked-bar range with no value above zero draws no axes: it reads `No <unit> in this range`, or its `empty` text. Each chart is an SVG `role="img"` whose label names the series, unit, interval, range, and zone, has a tooltip with exact values, and a toggle to a table of the same data. Under a right-to-left `dir` the drawing mirrors: time runs from the right, as the date labels beside it do, and the present's mark moves with it. A stacked bar given `present` (the bucket that holds now, and its axis label such as `Today`) marks that bucket on the axis and draws every later bucket outlined, and its table names those buckets `scheduled`. A donut shows percentages only when its parts are `complete` and no count is capped; otherwise it shows the counts as a table. A donut without `start` and `end` counts every item, and its label says `all {unit}`; its `asOf` is optional. It is its own entry, so the chart library loads with the first view that draws a chart |
| `/stream` | `useTopics`, `useStreamEvent`, `useStreamStatus`, `useModuleStale` (a module's `module.stale` report and its unavailable sources) and `useNodeStale` (one node's `node.stale` report), which read the last report even in a view mounted after it, `StaleFeed`, which the shell mounts once to feed them, and the stream client |
| `/commands` | `CommandAction` (its `recovery` prop makes it the command `R` runs for the selection, its `reach` prop states in the confirmation what the command reaches beyond the rows, its `icon` leads the label, and `appearance="icon"` draws it as that icon alone, named by the command's label, with the label and any refusal in its tooltip, as a panel's title row and a table's row actions draw every action but the primary one; every allowed action also appears in the palette; given per-row refusals it runs only the allowed rows), `useAllowedActions` (answers per row: the call gives a reason when any row is refused, so an action given it as `disabledReason` never sends a refused row; `.refused(commandId)` gives each refused row's reason; every row is refused, saying why, until the server answers or when the check fails), `ActionConfirm` (an alert dialog whose description is the action's `reach`; focus starts on the button that backs out, so an `Enter` pressed out of habit never runs the action, and while the call is in flight neither `Esc` nor a press outside closes it), `SchemaForm`, the commands API, and the Operation runner and panel |
| `/router` | React Router, re-exported so every module uses the shell's one router |
| `/query` | TanStack Query, re-exported so every module reads the shell's one data client |
| `/testing` | `renderModule` |
| `/testing/axe` | `axeViolations`, axe-core's WCAG 2.2 A and AA checks over the rendered page for a test; the module's test workspace adds `axe-core` |

#### List views

A list view composes the SDK's frame; a module writes no layout of its own. A module ships no CSS and no `style` objects: the lint configuration of the shell's workspace and of the Jobs SPA rejects a `style` attribute or a `.css` import in module source. The shell draws a page whose root is a `PageFrame` or a `DetailPanel` flush against the sidebar and the top bar; the frame pads itself.

- `usePageFrame({ viewId, rows, matches })` holds the view's state: the open row (`openId`, the only list state in the URL, as `?open=<id>`), the row cursor, the checked rows (`checked`, `setChecked`; view state that survives live updates but not a reload), and the refusals of the last bulk action (`refused`, `setRefused`, `revealed`, `reveal`). A change to the checked rows clears the refusals. `J`/`K`, and the arrow keys in a `DataTable`, move the cursor and an open panel follows it, replacing its history entry (`open(id, replace)`), so Back closes the panel rather than walking the rows; `Enter` opens a row with one new entry, `X` checks the cursor row without opening it, and `Esc` closes a popover, then the panel, then the checked rows.
- `PageFrame` lays out `stats`, `header`, the list in its own scroll region, and `panel`, the `DetailPanel` of the open row, docked under the list behind a resize handle (arrow keys move it 16 px, 64 px with Shift; the height is remembered per view). At 1280 px and wider the panel can move to the side; below 768 px it opens full screen. While it is docked below, toasts float above it.
- `DetailPanel` has one title row: the state, the name, the id with Copy (on a phone, on its own line under the name, wrapped rather than cut), then the tabs, the actions, and the window controls (Open in its own page, Move details to the side, Close details) as icon buttons with tooltips. Where the row is too narrow for both, the tabs and the controls drop to a second row whole. A tab whose `content` is `null` is left out, so a panel shows Exception, Arguments, Chain, Headers, or Body only when the object has one; `defaultTab` names the tab that opens first, such as a failed run's Exception, and a tab the operator picked stays picked from one object to the next. The title is the object's name in mono; while the `PageFrame` has `form` set, it reads in the sans face, as a form's title (`Add chain`). Rendered outside a `PageFrame` it is the object's own page, the detached form of the same panel, with no close or side control; there its name is the page's `h1`, and the section headings of `FieldGrid`, `HistoryList`, and `Text as="h3"` step up to `h2`, so the outline has no gap. A tab's content with two children fills the panel body's two columns. `FieldGrid` shows labels over mono values in aligned columns, two on a narrow column and up to its `columns` (4 by default) as the column it sits in widens, with optional inline edits (a select commits on change, text on `Enter` or blur, with an optional server preview while typing); a `label` makes it one named group, and several groups in one column read under their headings, each after a hairline; `HistoryList` shows an object's state changes; `ExceptionBlock` shows an exception's type (left out when the server does not serve one), message, and stored stack with `Full stack` and `Copy`; a stack line that starts `at` reads as a frame with its type, method, file, and line coloured, and any other line, such as an inner exception's `Type: message`, reads as a heading. `PayloadView` colours JSON (keys, strings, numbers, booleans, and null) and keeps large integers exact; content that is not JSON stays plain. In both, `Copy` copies the stored text, not what is drawn.
- `StatStrip` holds up to six `StatCard`s: a label, a served value, a state role dot, an optional served caption, an optional `trend`, and an optional `to` link to the filtered list. It draws no sparkline: a trend appears only once a series is served.
- `TrendChip` shows a change the server serves, as a ratio (`value={0.18}` reads `+18%`), in a `StatCard`'s `trend` slot or beside any figure; `children` replaces the percentage for another unit (`−1.1 pt`). Its tone follows `goodDirection`, not the sign: with `goodDirection="down"`, a falling failure rate reads in the success role and a rising one in the failure role; `neutral` keeps every change in the off role, and zero is always neutral. The arrow and the signed number sit in one `<bdi>` and the text keeps the foreground tone, so the tone is never the only cue and the sign stays with its number in a right-to-left line. The SDK never computes a change from two figures; pass only one the server serves.
- `Meter` reads a served quantity against a fixed scale (`min`, `max`), such as a queue's depth against its capacity; it is a React Aria meter whose accessible name is `label`. `valueLabel` is required, shown under the label, and announced in place of a percentage, so it carries what the colour means (`4,812 of 5,000 messages, over the warning level`). `thresholds` (`[{ from: 4000, tone: 'sched' }, { from: 5000, tone: 'fail' }]`) recolour the fill to the role of the last one the value has passed, and each one strictly inside the scale is drawn as a tick on the track, counted from the inline start. Work in flight that will finish is a `ProgressBar`.
- `PageHeader` is two rows. The title row takes the title (with an optional state dot), the served count, a one-sentence `description`, and `actions` at the inline end, so the primary action lines up with the title in every view. The `toolbar` row under it holds the chips and the filters: a `FilterToolbar` with its `chips` (the `FilterChips` of the Failed list's causes and of the set filters a link brought, which stay in view at every width), the list's `search` (a `compact` `SearchField`), `FilterSelect`s and a `compact` `DateRangeField` as its children, and `more` filters behind a `More filters` button, so the row stays one line at 1024 px; below 768 px the search keeps the width and every filter sits behind one `Filters` button, with `active` as its count. `FilterSelect` is a list filter as a chip that opens a React Aria select: off, it reads its `emptyLabel` (`Any kind`), which is also the choice that turns it off; on, it reads the filter's name faint and the chosen value in the set state a pressed chip has; `isRequired` makes it a choice that is always made, such as the node a view reads. `FilterChips` groups `FilterChip`s, each a toggle with a name and count (`pressed`, `onPress`; `failing` colours the count) or a set filter with `Clear` (`onClear`).
- `SegmentedControl` is one choice among a few, as pressed-state buttons, one control high wherever it sits. `DensityControl` is the account menu's row-density choice, and `useDensity` reads it. `Tag` is a short fact on an object or row: `pill`, `small`, `filled`, or `square`, with an optional state role dot.
- Every control on one row shares one height: buttons, filter chips, `FilterSelect`, segmented controls, and the `compact` form of `SearchField` and `DateRangeField`. Form fields in a detail panel keep their taller size and their labels above, and the form stops at a readable measure while each field sizes itself to what it holds through `width`.
- `Card` is a product Dashboard's block: a heading, an optional served caption, one trailing control (an `All` link), and `CardRow`s (state dot, name, faint meta, value at the inline end, actions, and an optional second line such as a progress bar), or its `empty` text when it holds none. Its `state` replaces the rows while they cannot be shown: `{ status: 'loading', label }` marks the card busy and names what loads, `{ status: 'empty', message, icon? }` says what would appear, and `{ status: 'error', message, onRetry }` is the server's message as an alert with Retry, so a failed load never reads as an empty card. A row keeps its whole name: the name is sized to its content, a fact beside it that does not fit moves under the name, and only a name or a fact wider than the card ends in an ellipsis, with the row's `title` (its first line as text) on hover. The values sit in one column of a fixed width, so the times line up down the card. Its first line is mono (names, counts, and times); its second line is sans prose. A row that holds a link lights up on hover and keyboard focus. A row whose name is a sentence rather than an identifier sets `prose`, and the name reads in sans while a `Mono` inside it keeps its face. Every card's caption is its served count with a noun (`4 running`, `3 items`) and its trailing control an `All` link to the list it summarizes. A `CardRow` with no `name` (and no dot, meta, value, or actions) is its second line alone, for a card that holds one block such as a chart under a hint. `Cards` lays cards in columns that wrap to one at narrow widths; cards in one row share its height while they hold rows, and an empty card stays as tall as its heading and empty state. A `wide` card spans two columns whenever two fit, such as a chart of days.
- `BarChart` stacks served series per column, drawn from zero, as one SVG `role="img"` whose label is a sentence naming the series, the columns' range, and the tallest bar; `mini` drops the axes. Under a right-to-left `dir` it draws the oldest column at the right, over its label.

A bulk action runs on the checked rows the server allows. Pass `CommandAction` the checked ids, `refused={allowed.refused(command.id)}`, `onRefused={frame.setRefused}`, and `onViewRefused={frame.reveal}`, and give the `DataTable` the same `frame`. With 2 of 5 rows refused the button reads `Requeue 3 of 5` and sends only the 3; the toast reads `Requeue: 3 done · 2 refused` with `View`, which highlights the refused rows and scrolls to the first; each refused row carries its reason as a tag in its own row. With every row refused the button is disabled with the shared reason, or `None of these can run now` when the reasons differ, and `R` announces it. Given `refused`, the action ignores `disabledReason`. Without `refused`, pass `disabledReason={allowed(command.id)}`: any refused row disables the action with the server's reason, as on a single object. The confirm and the run use the rows checked when the action was pressed; when the checked rows change before the run answers, the toast still reports it, but the refusals are not tagged and `View` is not offered.

A `Button` with `disabledReason` stays focusable and ignores presses. The reason is the button's description (`aria-describedby`), so a screen reader reads it with the button; on screen it shows in a tooltip on hover and focus, which never widens or wraps the row the button sits in. An icon-only button shows it in the same tooltip, under the button's name.

The shell titles each view by its sidebar entry, its product, and the served title (`Failed · Jobs · Dashboard`), and a polite live region says the title once when the path changes. A view that names its own page, such as one object's page, calls `usePageTitle(title)` from `/components`, and its name takes the sidebar entry's place: `billing.charge run · Jobs · Dashboard`. Pass `undefined` while the view still loads what it names; the shell then waits and says the title once the name arrives, so a screen reader never hears the list's title for the object's page. Pass a fallback once the load fails, such as `Run not found`, so the shell does not wait for a name that never comes. The name goes when the view unmounts; a filter or an open row, which change the search alone, is not a new view. When the control that navigated leaves with the old view, such as a sidebar link in the phone's menu or a panel's Open in its own page, focus lands on the view's canvas rather than the page body; focus still on a control stays there. Below 768 px the phone's Menu opens the sidebar as a modal sheet over a scrim: focus moves to its first entry and stays inside the sheet, and `Esc` or a press on the scrim closes it back to Menu. Go to id stays in the tab order at every width and says what a lookup found and what `Enter` does. A module says an outcome whose result lands away from the control pressed, such as a ping's, through an `Announcer`; a banner that appears with its text already in it may not be announced, so a module announces it from an `Announcer` and leaves `role` off the banner. Each chart's list toggle is named for its chart: `Show list: Throughput`.

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

The workspace build then holds the output to the size budgets in `packages/vite/budget.json`, measured as gzip at maximum compression: the shell's first load at most 350,000 bytes, the shell and every SDK entry together at most 500,000 bytes, each module entry at most 20,000 bytes, each view chunk at most 150,000 bytes, and the fonts at most 60,000 bytes. A budget overrun fails the build. The dashboard ships in English, so the shell build keeps only React Aria's `en-US` strings (its labels and screen-reader announcements); React Aria falls back to them for any other locale.

`renderModule(definition, options)` from `@headless/dashboard-sdk/testing` mounts a module in a test shell, as the dashboard would: its routes, a session, its permissions (`canView` and `canOperate`, both `true` by default), the data client, an Operation host, toasts, and a fake stream. The `api` handler answers the module's API calls; throw a `DashboardApiError` to answer with a status. The result adds `stream` (push events as the server would), `requests` (every API call in order), and `navigate(path)`. `axeViolations()` from `@headless/dashboard-sdk/testing/axe` lists every WCAG A and AA violation axe-core finds in the rendered page as `rule: element`, so `expect(await axeViolations()).toEqual([])` fails with what broke; contrast is not checked, because jsdom lays nothing out.

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

**Object kinds and topics.** `run` (a time job or a cron occurrence, by its id), `cron-job`, `function` (by name), and `node`. Stream topics are `jobs.run` and `jobs.cron-job`; a change's projection is the run or cron job as its list row shows it, a cron job without its `lastRun`, and its version is the row's change marker in ticks. A deleted row goes out with no projection, so open views refetch it and show `This run no longer exists`; a view whose first fetch lands older than such a change refetches once more instead of showing the older row.

**Commands.** Requeue, Cancel, and Delete need a reason and a confirmation (typed past 100 rows) and run over a filter as Operations. Edit, Duplicate, Force delete, and the cron job's Delete need a reason and a confirmation and run on one object at a time. A call on one object the rules refuse changes nothing and answers `409` with the reason, such as `Paused — resume first`.

| Command | On | Allowed when |
| --- | --- | --- |
| `jobs.requeue` | `run` | failed, cancelled, or skipped, and the requeue rules accept it |
| `jobs.cancel` | `run` | a queued or running time job with no cancel requested yet |
| `jobs.delete` | `run` | not running, and not a keyed time job |
| `jobs.run-now` | `cron-job` | not paused, and not running when the cron job forbids overlap; the done row's `reason` is the new run's id |
| `jobs.pause` / `jobs.resume` | `cron-job` | active / paused and not removed from code; each is the other's inverse |
| `jobs.edit-cron` | `cron-job` | not defined in code; arguments `expression` (required) and `timeZoneId` |
| `jobs.edit` | `run` | an idle unclaimed or queued time job that is not keyed and not a chain step; optional `executionTime` (ISO 8601 with offset), `request` (JSON, bound to the function's request type, refused when the stored contract version differs), `retries` (0–100), `retryIntervals` (JSON array of whole seconds); an argument left out keeps its stored value |
| `jobs.duplicate` | `run` | a finished standalone time job whose function this host registers under the stored contract version; the server copies the stored request, retries, and intervals into a new run with no business key; optional `executionTime`, not in the past, absent means now; the done row's `created` is the new run |
| `jobs.force-delete` | `run` | a running time job with cancel requested, not keyed and not in a chain; it removes the stored rows only, and the running code is not stopped; a job that finished after the read is refused |
| `jobs.delete-cron` | `cron-job` | not defined in code (a definition removed from code may be deleted) and no occurrence running; it removes the cron job with every stored occurrence, and the done row's `reason` is `N stored occurrences removed`; an occurrence started after the read refuses it |

An Operation's filter is `{ kind, functions, statuses, cronJobId, correlationId, fingerprint, from, before, actionableOnly }`. Requeue and Delete freeze on the store's latest change marker at confirm and write each row only while its marker is unchanged, so a run requeued by one chunk that fails again is not touched again; Cancel freezes on creation at confirm and needs no marker, because a running job advances its marker as it reports progress.

**Attention and Overview.** One item per function and failure fingerprint (`3 failed runs of billing.charge`), recommending `jobs.requeue` over the group's actionable failures; one item per registered function that no live node's run filter claims (`No live node runs billing.charge`), pointing at its `function` object. The Overview shows the share of runs that succeeded, failed runs, and runs that started later than the scheduler's fallback interval after they were due, each over the last 24 hours against the 24 before. The succeeded share is left out while no run finished in the last 24 hours, so an idle system does not read as 0 % succeeded, and compares with nothing, labelled `no runs finished in the previous 24 h`, while no run finished in the 24 hours before. The Overview's schedule strip gets one track per function that has cron jobs: its occurrences due in the last hour in their status's state role (`sched`, `enq`, `proc`, `ok`, `fail`, or `off`), and the next fires of its active cron jobs in the next hour; a past tick opens its run and a fire opens its cron job.

**Related and payload.** A run relates to the other runs with its correlation id (`jobs.same-correlation`), its chain (`jobs.chain`), and its cron job (`jobs.cron-job`); a run with a stored W3C trace parent carries its trace id, so the shell offers `Open trace`. Its payload is its stored request as JSON, or `Unsupported contract version N` when this host registers another version. A run and an attempt carry their exception as `exceptionType`, `exceptionMessage`, and `exceptionStack`: the module reads the JSON the executor stores (the outer exception's type and message, then its frames and every inner exception as a `Type: message` line with its own frames, up to five inner exceptions) into the three, and a reason the store wrote itself, such as `Lease lapsed while running!`, arrives as the message with no type or stack.

**API** at `{base}/api/jobs`, JSON with camelCase names and enums as strings. A route refuses bad input with the same `400` validation problem a command answers: `errors` names each refused field as the query or body spells it, with its messages (a chain step's field by its path, such as `children[0].function`), `rules` names the rule each field broke (`required`, `enum`, `minimum`, `maximum`, `maxLength`, `format`, `json`, or `invalid`), and `detail` repeats the first message. A malformed body is refused as the field `body` with the rule `json`, as a command's is. A query value that does not parse as its type (a GUID, date, number, or boolean) is refused by ASP.NET Core before the module runs, with its own `400`.

| Route | Answer |
| --- | --- |
| `GET runs?kind=&function=&status=&cronJobId=&correlationId=&fingerprint=&from=&before=&actionable=&search=&sort=&direction=&limit=&after=` | `{ items, count, countCapped, nextCursor }`: one keyset page of runs. `count` and `countCapped` come with the first page only, the one read without `after`; a later page leaves both out, so keep the first page's count. `sort` is `executionTime` (the default), `function`, or `status`, each followed by id. `direction` is `ascending` or `descending`; it defaults to `descending` for `executionTime` and to `ascending` for the other keys. `search` is a literal, case-insensitive substring of the function, id, business key, or description, so `%` and `_` match themselves; payloads are never searched. `from` is inclusive and `before` exclusive. `limit` goes up to 200 and the count is capped at 10,000. `after` takes `nextCursor`; a cursor sent with another sort, direction, filter, or search is refused as `after`, so read the first page again. Each run also carries `progressUpdatedAt` (absent until it reports progress), `retries`, and `retryIntervals` |
| `GET runs/{id}` | `{ run, children }`, or `404` |
| `GET runs/{id}/attempts` | `{ items: [{ number, node, startedAt, endedAt, outcome, exceptionType, exceptionMessage, exceptionStack, failureFingerprint }] }`, or `404` |
| `GET cron-jobs?function=&fires=` | `{ items }`: every cron job with its plain reading and its `lastRun: { id, status, executedAt }`, the latest occurrence that started or ended, by due time (see [Dashboard reads](jobs.md#dashboard-reads)). `id` is the occurrence, which opens as a run; `executedAt` is left out while it is in progress; `lastRun` is left out for a cron job that never ran, and a paused one keeps its own. The list reads every last run in one store read. With `fires`, each active one also carries that many next fires (at most 50), as a Function page draws them on its strip |
| `GET cron-jobs/{id}?fires=` | one cron job with its `lastRun` and its next fires (default 5, at most 50; none while paused), or `404` |
| `GET cron-preview?expression=&timeZoneId=&count=` | `{ timeZoneId, reading, nextFires, validationMessage }` for a draft schedule |
| `GET functions` | `{ items: [{ name, contractVersion, priority, maxConcurrency, requestType, cronExpression, claimedBy, succeeded, failed, running, waiting, lastExecutedAt, cronJobs }] }`, counts over 24 hours |
| `GET nodes` | `{ items: [{ identity, state, role, claimedFunctions, isLocal, maxConcurrency, running, runningCapped, lastHeartbeatAt, lastHostException, lastHostExceptionAt }] }`: live nodes; without node membership, this host. `isLocal` marks the node serving the request. `maxConcurrency` is the node's host limit, absent for a peer that publishes none. `running` counts its in-progress runs, capped at 10,000 with `runningCapped`. `lastHeartbeatAt` is the store-clock time of its latest accepted heartbeat, absent without membership (on Redis a node that left reports a backdated time). `lastHostException` and `lastHostExceptionAt` appear only on the serving node's row, in memory and lost on restart; a peer's exception is known only on that node |
| `GET series/daily?from=&to=&zone=&kind=&cronJobId=` | `{ zone, from, to, asOf, exact, days: [{ date, start, end, counts }] }`: one bucket per local day from `from` to `to`, both required and inclusive (`yyyy-MM-dd`), at most 92 days, in the IANA `zone` (default: the scheduler zone). `start` and `end` are the day's UTC bounds, so a daylight-saving day is 23 or 25 hours long. Runs are placed by execution time, not by when they ended as the Overview figures count them, so a future day counts the time jobs and occurrences scheduled on it. `counts` names every status with its count, `0` included, and is `null` (written, never left out) for a day the store could not read; `exact` is `false` when any day is `null`, so show a share only when it is `true`. `kind` (`TimeJob` or `CronJobOccurrence`) and `cronJobId` narrow the runs. A missing date, `to` before `from`, more than 92 days, an unknown zone, or an unknown kind answers `400` |
| `POST time-jobs` | an operator action: `{ function, executionTime, timeZoneId, request, description, retries, retryIntervals, children: [{ ..., runCondition }] }` answers `201 { id }`; a time without an offset is read in `timeZoneId`, else the scheduler's zone, and one a daylight-saving gap skips there, such as 02:30 on the night the clocks jump from 02:00 to 03:00, is refused at that step's `executionTime` with rule `invalid`. `function` is required and registered on this host in every step, `retries` is 0 to 100, each of `retryIntervals` is zero or more, and `request` is at most 65,536 characters and binds to the function's request type; an unknown `timeZoneId` is refused |
| `POST cron-jobs` | an operator action: `{ function, expression, timeZoneId, request, description, retries, retryIntervals }` answers `201 { id }`; each occurrence retries as a time job with the same fields does. It checks the fields a time job body checks, and refuses an `expression` that does not parse or never fires again as `expression` and a zone this host does not know as `timeZoneId` |

**Views.** The package embeds the module's front end, so the Jobs tab's sidebar reaches these views:

| View | Shows | Acts |
| --- | --- | --- |
| Dashboard (`{base}/jobs`) | The stat strip (the Idle, Queued, and In progress counts the sidebar shows, then the Overview's succeeded share, failed runs, and late starts over 24 hours with their comparisons), the runs in progress with their reported progress and, in the card's caption, how many workers the live nodes have (`2 running · 14 workers`, said only while every live node published its max concurrency), the next fires of the active cron jobs, and the Jobs attention items | `Cancel` per running run; `Add time job`; `Add recurring job` when no cron job exists |
| Runs (`runs`, each state at `runs?status=<JobStatus>`) | The stat strip, then one live list of time jobs and occurrences, titled by its state with its served count and one sentence on what the state holds, filtered in the URL by kind, function, status, correlation id, failure fingerprint, and cron job. One `Due` range with presets filters every list by when each run is due (its execution time), kept as `?range=24h` for a preset, which works out its window when the page opens so a shared link stays relative, or as `?from=2026-11-01&to=2026-11-04` days; a state list offers no range control and shows a range a link set as a chip to clear. The search (`?q=`, over function, id, business key, and description, never a payload) and the sort (`?sort=function&direction=descending`, the server's own names) are in the URL too, so a link or a reload shows the same list; a value the view cannot read falls back to no range or the newest-first order, and Clear filters keeps the status and the sort. The Failed list adds a chip per failure group, labelled by function with its count, and by the exception type of a loaded run of the group once there is one (`export.ledger · TimeoutException`), that filters by that function and its fingerprint, and reads each run by its reason: the exception's type without its namespace, then its message. Every list counts a run's attempts out of the ones it may take (`2 of 4`), blank for a run no node has picked up and for one that never ran: a skipped run, or one cancelled before a node picked it up. The `status` reads case-insensitively, and a value that names no status lists every run. The open run (`?open=<id>`) docks under the list: Overview (its fields in groups, and history from its attempts), Exception for a run that stored one (a failed run opens on it, with the whole stack), Arguments for a run that stored a payload, and Chain for a step or a run with steps after it. Requeue keeps its label; the other actions are icons with tooltips. | `Requeue`, `Cancel`, and `Delete` on the open run and on the checked runs the server allows, each refused row tagged with its reason; over every matching run as an Operation; `Add time job` and `Add chain` open one form in the docked panel (`?create=time-job` or `?create=chain`), switched by `Single job | Chain` without losing what was typed, a chain's later steps beside the first with the step each runs after and its run condition |
| Run page (`runs/{id}`) | The run panel as its own page | The same run actions |
| Recurring jobs and cron job page (`cron-jobs`, `cron-jobs/{id}`) | The stat strip, then each cron job's function, description, expression with its plain reading, last run, next run, or a `Paused` tag, and how its function's runs went in the last 24 hours (`6 succeeded · 2 failed`, the failed part opening the function's failed runs). Those counts cover every run of the function, not only this schedule's occurrences. The last run is the latest occurrence's status as a dot and a word, opening that run, then when it ended (`Succeeded · 12 min. ago`); one in progress shows its status alone, and a cron job that never ran shows a dash. A live change of a cron job carries no last run, so the list is read again once the occurrence changes that can move one settle. The open one (`?open=<id>`) docks under the list: Schedule (its fields beside the next five fires in its zone) and Occurrences (the last 24 hours as outcome bars per hour, then each occurrence newest first). The page is the same panel on its own. | `Run now`, `Pause` / `Resume`; `Add recurring job` opens its form in the docked panel (`?create=cron-job`); the expression and zone edit in place, showing the server's reading and next fires before they commit, and stay read-only for a cron job defined in code |
| Functions and function page (`functions`, `functions/{name}`) | The stat strip, then each function's kind (`cron` with its declared expression, or `time`), request type, priority, running count over its concurrency limit, the live nodes whose run filter claims it (or a `no node runs it` tag), and its 24-hour counts. The open one (`?open=<name>`) docks under the list: Overview (its contract version and facts beside where it runs and the recurring jobs that fire it) and Runs (its latest runs). The page is the same panel on its own. | `Add time job`, `Add recurring job` |
| Servers (`nodes`) | The stat strip, then each live node's role, liveness, busy workers against its max concurrency as a meter (`2 of 14 busy`; a node that published no max concurrency shows its running count alone), and the functions its run filter claims; the open one (`?open=<identity>`) docks under the list with the same facts and links to its functions. The server serves no scheduler state, queues, or registration time per node, so the view shows none. | None |

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
| `SearchTimeout` | 10 s | How long a content or correlation id search waits for one store. A store that has not answered by then is cancelled and the search answers `incomplete: true` with what the other stores found. Greater than 0 and under 30 seconds, the time a node waits for a search it relays. |
| `Forwarding.Key` | `null` | The key every node of the deployment shares, at least 32 characters. It never leaves the node: each forwarded call is signed with it (see [Key mode](#key-mode-signed-calls)). Setting it, or `Forwarding.Policy`, turns forwarding on; see [Forwarding to other nodes](#forwarding-to-other-nodes). |
| `Forwarding.PreviousKey` | `null` | The key being rotated out. A node admits calls signed with it as well as with `Key`, and never signs with it. At least 32 characters, different from `Key`, and only with `Key` set. |
| `Forwarding.MaxClockSkew` | `00:01:00` | How far a signed call's timestamp may be from this node's clock, either way. Greater than 0 and at most 10 minutes. |
| `Forwarding.Policy` | `null` | A host authorization policy that admits a calling node through the host's own service-identity scheme, used instead of the key. Set one of the two, not both. |
| `Forwarding.AllowInsecurePeers` | `false` | Lets this node call a peer that discovery lists at an `http://` address. |
| `Forwarding.MaxResponseBytes` | `4194304` (4 MiB) | The largest answer this node reads from a peer; a larger one fails the call as `not-answering`. Must be greater than 0. Raise it when a message payload shown through forwarding can be larger. |
| `ForwardedAccess` | `View` | The most a call another node forwards here may do: `View` reads only, `Operate` also runs the commands the serving node decided its operator may run. |

`AddMessaging(Action<MessagingDashboardOptions>)`, `AddMessaging(Action<MessagingDashboardOptions, IServiceProvider>)`, and `AddMessaging(IConfiguration)` configure them.

**Object kinds, ids, and topics.** `published`, `received`, `circuit` (a consumer's circuit breaker on one node, by `{node}~{consumer}`), and `node` (by its `NodeName`). A row's object id is `{node}~{store}~{storage id}`, such as `node-a~billing~0192f3c1-…`: the node that served it by its `NodeName`, the store (`primary` for the main store), and the row's storage id. Stream topics are `messaging.published` and `messaging.received`; the stores are shared by every node, so a change carries no `node` and goes to the plain topic. A change's projection is the row as its list shows it (`id`, `store`, `storageId`, `messageId`, `name`, `lane`, `status`, `consumer`, `retries`, `added`, `expiresAt`, `nextRetryAt`, `changedAt`, `failureFingerprint`, `tenantId`, `isHeld`, `terminal`), and its version is the row's change marker in ticks. A deleted row goes out with no projection. A circuit's changes go out on `messaging.circuit` from the node that holds it: a state transition, a failure count that moved, or a circuit the node stopped tracking. The projection is the circuit as `GET circuits` lists it, every field written, so a closed circuit carries `openedAt` and `estimatedRemainingOpenDuration` as `null`; a circuit the node no longer tracks goes out with no projection. Its version is the change's time in ticks, raised when needed so each change of a node is later than the one before.

**Several stores.** Counts, Needs you, Overview, lookups, lists, and the change stream read every store at once, each bounded by `StoreTimeout`. A store that fails or does not answer in time is named in `unavailable` while the others answer; the module is never marked stale for one store. The change cursor keeps one position per store and table, so a slow store neither holds the others back nor loses its place, and a store that was down starts from its current position once it answers.

**Live changes.** A write this node's senders and consumers make reaches its streams at once: the pipeline reports the write after it landed, and the module reads the row back with its marker. A write another node makes, or one inside an application transaction, arrives through this node's change read of the stores, within `ChangePollInterval` plus the read. A circuit breaker lives in one node's memory, so no store carries its changes: the breaker reports each one to the module, which publishes it to that node's streams at once. The Consumers view and the Overview's Circuits card patch the circuit in place and read `GET circuits` on no timer while every circuit is closed. The time an open circuit has left moves with no change behind it, so they read the list again every 5 seconds while a circuit is open. `GET metrics/realtime` stays polled every 5 seconds: a new second is measured whether or not anything changed, so there is no change to send.

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

**API** at `{base}/api/messaging`. Every route but Ping is a read, so a viewer without a named operator can call it:

| Route | Answer |
| --- | --- |
| `GET published?store=&status=&terminal=&retrying=&name=&fingerprint=&lane=&messageId=&from=&before=&sort=&direction=&limit=&after=` | `{ items, count, countCapped, nextCursor, unavailable, incomplete }`: one keyset page merged over every store unless `store` names one; `limit` up to 200; `retrying=true` lists failures with a retry pending; `lane` is `Bus` or `Queue`; `from` (inclusive) and `before` (exclusive) bound the added time; `sort` is `added` (the default, `descending` unless `direction` says otherwise: newest first) or `name` (`ascending` by default), each followed by the storage id; `after` takes `nextCursor` and is refused under another sort, direction, filter, or search. A `Failed` row carries `reason`: the line of its stored failure that names its cause, `Type: message`, cut at 240 characters (the exception a consumer threw, not the `SubscriberExecutionFailedException` it is stored inside), read by one more read per store for the page's failed rows; a store that fails that read leaves its rows without a `reason`, and a row that is not failed never has one |
| `GET received?status=&terminal=&retrying=&consumer=&name=&fingerprint=&held=&lane=&messageId=&from=&before=&sort=&direction=&limit=&after=` | the same over the main store's received rows |
| `POST published/search`, `POST received/search` | the list's answer for a search. The body is `{ content?, correlationId?, … }` with the list's query fields as body fields (`store`, `status`, `lane`, `from`, `sort`, `after`, and the rest); `?node=` stays in the query. `content` matches the stored envelope, headers and body, as a literal substring without regard to case; `correlationId` matches the correlation id header exactly, without regard to case, and not a body that merely holds the value. Set either or both. No index serves a search, so each store reads every row its other filters leave, for at most `SearchTimeout`: a store out of time sets `incomplete: true` and is not named in `unavailable`, and its rows come on a later page. Rows carry no content, so a search serves nothing of the body it matched. The text travels only in the body, never in a URL, the cursor (a digest stands for it), or a log line the module writes |
| `GET messages/{kind}/{id}` | `{ row, headers, content, exceptionInfo }`, or `404` |
| `GET received/generations?messageId=&consumer=` | `{ items }`: every received row of the message, oldest first, each with `generation: { number, incarnationId, origin, isCurrent, replayOfIncarnationId, replayOfId, expiresAt, recovery }` from the store's inbox operations. `number` is `0` for the first delivery and one more per re-execution; `origin` is `original` or `replay`; a replay names the incarnation it re-executed and, while the store keeps that row, its object id in `replayOfId`; `expiresAt` is when retention may delete the ended generation, absent while it runs; `recovery` is `orphaned` while no host has the consumer registered, else `routable`. A row has no `generation` when the store keeps none for it, when the store has no inbox operations, or past the first 200 generations. No field claims a delivery guarantee: the store records none |
| `GET scheduled?store=&name=&page=&perPage=` | `{ store, supported, items, totalCount, reason }`; a provider without scheduled-delivery operations answers `supported: false` with the reason, not an error |
| `GET receipts?actor=&targetKind=&operationType=&from=&before=&limit=&after=` | `{ items, nextCursor }`: one keyset page of the operator ledger in the main store, newest first, every audited intervention including a refused one; `actor` matches ordinally, `targetKind` (`Inbox`, `ScheduledDelivery`, `Published`) and `operationType` (such as `Hold`, `Resend`) ignore case, `from` is inclusive and `before` exclusive, `limit` up to 500; `after` takes `nextCursor`, which is absent on the last page |
| `GET consumers`, `GET contracts` | `{ items }`: the consumers and the message contracts of the host manifest's `messaging` section; each consumer's message names its implementation in `consumerType` and the method that handles it in `handlerMethod` (`ConsumeAsync`, or `RespondAsync` for a responder), and a consumer tuned `Sequential()` carries `sequential: true`, which its panel shows as `Ordering: One at a time per key` |
| `GET circuits` | `{ items: [{ id, consumer, lane, state, consecutiveFailures, failureThreshold, openedAt, estimatedRemainingOpenDuration, version }] }`; `version` is the version of the circuit's latest change, the one its stream change carries; `consumer` is the consumer identity, as `GET consumers` names it, and `lane` (`Bus` or `Queue`) the lane the circuit guards, because one identity trips independently on each lane; `id` is the circuit's object id, `{node}~{circuit key}`, which its commands take |
| `GET backlog` | `{ asOf, stores: [{ store, publishedPending, oldestPublishedSeconds, receivedPending, oldestReceivedSeconds }], unavailable }`: the stored work each store still holds waiting, as `IMonitoringApi.GetBacklogAsync` counts it: rows not yet sent to the broker and rows not yet handled, each with how long its oldest has waited, absent when none waits. Every store is read at once within the store timeout; a store that fails or runs out of time is named in `unavailable` and has no entry, so it never reads as an empty backlog. An additional outbox has no `receivedPending`, because it holds no received rows |
| `GET providers` | `{ transports: { bus, queue }, outboxes: [{ name, storage }] }`: the broker name of each lane's transport and the storage provider of each outbox, as the host manifest's `messaging` section names them, never an endpoint or a connection; a host without a transport on a lane leaves that lane out |
| `GET nodes` | `{ items: [{ name, isLocal, stores, state, instances }], discovery }`: this node and the stores it reads, then every node discovery lists, with `stores` empty; a listed node's `state` is what the last forwarded call to it found (`answering`, `not-answering`, `key-rejected`, `policy-denied`, `view-only`, `insecure-peer`), absent before the first call and for this node; `instances` lists each discovered instance of a listed node as `{ address, port, tags }`, in discovery order: the address and port discovery gave (`port` is `0` when the address carries it) and its comma-separated tags as a list, leaving out an instance whose address is not `http` or `https`, and is absent for this node, which discovery does not list; `discovery` says what the last discovery read found, absent while forwarding is off. A dashboard-only host leaves itself out. This route is always answered by the serving node |
| `POST nodes/ping` | `{ items: [{ node, outcome, latencyMs, state, status, reason }], discovery }`: Ping, which needs operate permission; see [Ping](#ping) |
| `GET metrics/realtime` | `{ seconds, publishedPerSecond, consumedPerSecond, consumeElapsedMs }`: the last 300 seconds; a second this node did not measure, such as one before it started, is `null`, never `0` |
| `GET metrics/hourly` | `{ interval, zone, asOf, hourStarts, stores: [{ store, published, received }], unavailable }`: the last 24 hours, cut at UTC hours (`interval` is `PT1H`, `zone` is `UTC`); `hourStarts` holds each hour's start, oldest first, the last being the current, still running hour. Each series is `{ succeeded, failed }`, one count per hour in `hourStarts` order: rows added in that hour that are in that status now. Every store is read at once within the store timeout; a store that fails or runs out of time has no `published` and no `received` and is named in `unavailable`, so it never reads as zero. An additional outbox has no `received`, because it holds no received rows. An hour the store did not count, as when the hour turned between this node's clock read and the store's, is `null` |

A read refuses bad input with the `400` validation problem commands answer: `errors` names each refused field with its message, the `rules` extension names the rule it broke, and `detail` is the first message. The fields are `status` (not a message status), `retrying` (combined with `status` or `terminal`), `before` (not later than `from`), `after` (not a cursor the list returned, or one read with another sort, direction, filter, or search), `targetKind` and `operationType` (not a member, case ignored), all with rule `invalid`; `lane`, `sort`, and `direction` (not a member) with rule `enum`; `messageId` on `received/generations` with rule `required`; and, on a search, `content` with rule `required` when neither `content` nor `correlationId` is set, `content` (over 1000 characters) or `correlationId` (over 200) with rule `invalid`, and `body` with rule `json` when the body is not a JSON object. The query is checked before the store, so a bad query with an unknown `store` answers `400`, not `404`.

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

Re-execute, Purge, Re-send, and Delete run over a filter as Operations, which always need a reason (a confirm without one answers `400` with `errors.reason`). The filter is the list scope `{ store, status, terminalOnly, consumer, name, fingerprint, lane, messageId, from, before }`, read as the list reads them: `lane` is `Bus` or `Queue`, `from` is inclusive and `before` exclusive on the added time, and a `before` not later than `from`, or another lane, answers `400` on `filter`. The filter also takes the list's search, `content` (at most 1,000 characters) and `correlationId` (at most 200), matched as the search matches them, so `Select all matching` works on a searched list too. The text travels only in the confirm's body and the encrypted scope token, never a URL. The operator log records the filter with each searched value replaced by `(redacted)`, and the module writes it in no log line. A search reads every row its other filters leave, so the confirm's count and each chunk's read wait at most `SearchTimeout`: a confirm out of time answers `400` on `filter` and confirms nothing, and a chunk out of time answers `400` on `cursor` or `token`, changes no row it had not reached, and the Operation panel shows it stopped incomplete with the reason. Each chunk reads its rows again with the search, so a row is changed only while it still matches. A Re-send or Delete Operation whose `store` names an additional outbox runs over that outbox, with each receipt in the main store; Re-execute and Purge run over the main store only, and a filter naming an outbox or an unknown store answers `400` on `filter`. Confirm freezes the scope on the store's latest change marker, and each chunk writes a row only while its marker is unchanged, so a row another chunk, node, or earlier run already changed is skipped with its reason, never written twice; an Operation stopped part-way and run again on another node changes each row once. A re-execute scope leaves held rows out. One row's failure, such as a main-store receipt that could not be written, is reported as `Failed` on that row while the Operation runs on.

**Screens.** The Dashboard shows what needs attention, the stored messages by status as one donut per kind (with shares only while every count behind them is exact, else the counts as a list), the live five minutes as a rate chart and a consume-latency chart on one time axis, the last 24 hours per hour (a store that did not answer is named, never drawn as zero), and the newest failures. The Published and Received lists search content in the page header, on `Enter` or Search only; the correlation id, message id, lane, and an added-time range with presets sit behind More filters. The content and correlation id searches live in the view's state, never in the URL, and show as chips to clear; the message id, lane, and range are in the URL. A search that ran out of time says so, with Retry, and never reads as no match. The lists sort by name or added time on the server and show the message id and the exact time beside the relative one. A message's panel groups its fields as Identity, Delivery, and Correlation (read from the headers), shows the body without the envelope's headers, and, for a received message, each inbox generation with its number, current or replay origin, expiry, and recovery. Scheduled deliveries filter by message name; Consumers show each message's implementing type and handler method and filter by message; Nodes show each instance's address, port, and tags, and Ping one node or every node, each answer on its node's row.

The module's front end is built into `wwwroot/dist/modules/messaging` and embedded in the package, which serves it through `MessagingAssets`; its `manifest.json` there declares the sidebar and its count keys. The package's build runs the npm build, so `wwwroot/dist` is not committed.

#### Ping

`POST nodes/ping` asks this node to reach other Messaging nodes and says, per node, whether each answered. The body is `{ "node": "<name>" }` to ping one node, or `{}` (or no body) to ping every node discovery lists, in discovery order. A ping changes nothing, yet it makes this node call others, so it needs the module's operate permission, as a command does: a viewer gets `403`. It is not a command: it asks for no confirmation and writes no operator-log entry.

- **Discovered nodes only.** A name resolves against discovery entries, as `?node=` does, so a caller cannot point this node at an address of its choosing. A name discovery does not list answers `404` with `Discovery lists no Messaging node named '{node}'.` and no call leaves the node. A name that is this node answers `400` on field `node` (rule `invalid`); "every node" means every other node. With forwarding off, discovery lists none, so `{}` answers an empty `items`.
- **One signed call per node.** Each ping is a `GET` to the node's `/peer/ping`, sent as every forwarded call is (signed in key mode), and the node answers only a call its forwarding admits. At most 8 nodes are pinged at once, and each node gets 3 seconds, across all its discovered instances, to answer.
- **One result per node.** `outcome` is `answered`, with the round trip in `latencyMs`; `timeout`, when the node did not answer in its 3 seconds, with `reason`; or `failed`, with the node's `state` from the table in [Forwarding to other nodes](#forwarding-to-other-nodes) and its `reason`. A node that answers without serving the ping, such as one on a build without it, is `failed` with `state: answering` and the node's status code in `status`.
- **No fan-out from a forwarded call.** A ping another node forwards answers `404`, so a ping never travels further than one hop.

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

##### Key mode: signed calls

Each forwarded call carries `X-Headless-Forwarding-Signature: v1;ts={unix seconds};nonce={16 random bytes, base64url};sig={base64url HMAC-SHA256}`. The HMAC key is derived from `Forwarding.Key` with HKDF-SHA256 and a fixed purpose label. The signed string joins with `\n` the version tag, the timestamp, the nonce, the method, the request target as sent (escaped path and query), the hex SHA-256 of the body, and the hop, operator, and access headers. The receiving node reads the body once, within the host's request size limit, and hands the same bytes to the endpoint.

A peer admits the call only when all of these hold, and otherwise answers `401` with `X-Headless-Forwarding-Refusal: key-rejected` and a fixed reason:

- The signature header is present and well formed. A call that carries the `X-Headless-Forwarding-Key` header of older builds is refused, signed or not; there is no fallback.
- The timestamp is within `Forwarding.MaxClockSkew` (60 seconds by default) of the peer's `TimeProvider`, either way. Keep every node's clock on NTP; a node whose clock drifts past the skew is rejected by every peer.
- The signature matches under `Key`, or under `PreviousKey` when set, compared in fixed time.
- The nonce has not been used inside the window. Each node remembers the nonces it admitted in memory, up to 100,000 at once, and refuses new calls rather than forget one still in its window. The memory is per node: two instances behind one discovery address do not share it.

A proxy between nodes must forward the request target and the signed headers unchanged, or the signature no longer matches.

**Rotating the key.** A node signs with `Key` and admits `Key` and `PreviousKey`, so a rotation that never refuses a call takes three rolling restarts. Finish each roll on every node before you start the next:

1. Set `PreviousKey` to the new key, keeping `Key` as it is. Every node now admits the new key and still signs with the old one.
2. Swap them: `Key` to the new key, `PreviousKey` to the old one. A rolled node signs with the new key, which the nodes not yet rolled admit since step 1.
3. Remove `PreviousKey`.

Rolling straight to step 2 also works, but until the roll ends, a node that already signs with the new key is refused by the nodes that have not restarted yet.

**Upgrading.** A node on a build that sends the raw key and a node that signs cannot talk to each other: each rejects the other's calls as `key-rejected`. Upgrade every node of a deployment together, or accept that forwarding between old and new nodes fails until the rollout ends.

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
