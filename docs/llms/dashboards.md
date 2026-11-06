---
domain: Dashboards
packages: Dashboard
---

# Dashboards

> One operations shell that serves feature modules under one base path and one sign-in, plus the shared authentication of the Messaging dashboard.

## Orientation

`Headless.Dashboard` has two parts:

- **The shell.** `services.AddHeadlessDashboard(...)` registers it and `app.MapHeadlessDashboard()` mounts it, by default at `/headless`. It serves every registered module under that base path with one sign-in, one session cookie, one live stream, and one command pipeline. It stores nothing: each module reads its own store. The package ships one module, the diagnostic module (`AddDiagnostics()`), an in-memory store of fake runs that exercises every module capability. `Headless.Jobs.Dashboard` adds the Jobs module (`AddJobs()`); see [Jobs module](#jobs-module).
- **Shared authentication for the Messaging dashboard.** `Headless.Messaging.Dashboard` keeps its own Vue dashboard in this release, mounted by `MapHeadlessMessagingDashboard()`. It is not a shell module yet. It receives this package transitively and configures its authentication through its own builder; see [Messaging and standalone dashboards](#messaging-and-standalone-dashboards).

A host can run the shell and the Messaging dashboard side by side. Each has its own authentication mode, credentials, and session cookie.

Read [jobs.md](jobs.md) for Headless Jobs and [messaging.md](messaging.md) for the Messaging dashboard.

## Agent Rules

- Treat every dashboard as an operations surface. Require host, Basic, or API-key authentication outside isolated local development.
- Choose the shell's authentication mode explicitly: `WithHostAuthentication`, `WithBasicAuth`, `WithApiKey`, or `WithNoAuth`. `AddHeadlessDashboard` throws `InvalidOperationException` when the delegate calls none of them.
- Call `AddHeadlessDashboard` once per host and add every module inside that one call. A second call throws.
- Call `app.MapHeadlessDashboard()` on the host's endpoints after `UseAuthentication()` and `UseAuthorization()`. A host that registers the shell and never maps it fails to start. The base path cannot be the root.
- Prefer host authentication when the application already signs its users in. It keeps identity, audit, and revocation in the host's security boundary, and separates viewing from operator actions.
- Write host view and operate policies from claims only. The dashboard evaluates them against the claims it copied into its own session, not against the host's bearer token or request. A policy that lists authentication schemes passes only for a session issued from one of those schemes.
- Serve a host-mode dashboard from the host's own origin. The shell is same-origin in every mode, because its session cookie is host-only. Host mode fails at startup with the Messaging dashboard's `SetCorsOrigins` or `SetCorsPolicy`.
- Give every instance of a multi-instance host one shared Data Protection key ring, for example with `Headless.DataProtection.Blobs`. Sessions in every mode, and the shell's Operation tokens, are protected by that key ring. Without it, a session or an Operation token issued by one instance is refused by the next.
- Call `HttpContext.SignOutHeadlessDashboardAsync()` for the shell, and `SignOutHeadlessDashboardAsync(name)` for the Messaging dashboard, from the host's own sign-out, so dashboard sessions end with the application session.
- The shell's document replaces any `Content-Security-Policy` header already on its response. Add the sources a module or the host needs through `ConfigureContentSecurityPolicy`, never by loosening the policy in host middleware.
- Never embed production credentials or API keys in source, examples, logs, URLs, or frontend storage.
- `WithNoAuth()` and `AuthMode.None` make a dashboard public. Use them only in an isolated development environment.
- The diagnostic module registers nothing outside the Development environment unless `DiagnosticsOptions.Enabled` is `true`. Do not enable it in production: it serves fake data.

## Choosing an authentication mode

| Mode | Shell builder call | Use when | Trade-off |
| --- | --- | --- | --- |
| Host | `WithHostAuthentication(...)` | The application already signs users in, with a cookie, OpenID Connect, or a bearer token | Separate view and operate policies per module, server-side session with idle and absolute expiry; same origin only; the Messaging gateway does not relay to other nodes |
| Basic | `WithBasicAuth(user, password)` | A small standalone deployment needs simple credentials | One shared credential for viewing and operating every module; the application owns secret storage and rotation |
| API key | `WithApiKey(key)` | Automation or a controlled operator client needs bearer access | One shared credential for viewing and operating every module; the application owns key distribution and rotation |
| None | `WithNoAuth()` | Isolated local development only | No access control |

The Messaging dashboard builder takes the same four calls. There is no custom-validator mode. To sign operators in with your own credential check, write an ASP.NET Core authentication handler, register it in the host, and use host mode over it.

## Headless.Dashboard

The dashboard shell, its module contract, its diagnostic module, and the authentication that the shell and the Messaging dashboard share.

### Register the shell

```bash
dotnet add package Headless.Dashboard
```

Host mode with separate view and operate policies, an OpenID Connect sign-in for browsers, and the diagnostic module:

```csharp
using Headless.Dashboard;
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
    dashboard.AddDiagnostics(); // registers nothing outside Development
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

builder.Services.AddHeadlessDashboard(dashboard =>
{
    dashboard.WithBasicAuth("operator", builder.Configuration["Dashboard:Password"]!);
    dashboard.AddDiagnostics();
    dashboard.Contributions(contributions => contributions.Disable("diagnostics.requeue-failed"));
    dashboard.Contributions(builder.Configuration.GetSection("Headless:Dashboard:Contributions"));
});

builder.Services.Configure<HeadlessDashboardOptions>(options => options.AggregationTimeout = TimeSpan.FromSeconds(3));
```

### Shell sign-in and sessions

- The shell's session cookie is `__Host-headless-dashboard-` followed by the host's application name, lower-cased with every character that is not an ASCII letter or digit replaced by `-`, so two applications on one host name keep separate sessions. `SessionCookieName` overrides it in host mode. The cookie is `Path=/`, `HttpOnly`, `Secure`, `SameSite=Strict`, and host-only.
- **Host mode.** A session is issued by `GET {base}/auth/sign-in?returnUrl=`, which challenges `ChallengeScheme` when no host user is signed in, or by `POST {base}/api/auth/session` with the host's own credential. The [Host sessions](#host-sessions) rules apply to the shell as they do to the Messaging dashboard. A user gets a session when they pass the shell's `ViewPolicy` or any module's view policy.
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

Attention, Overview, lookup, related objects, and counts call every module in parallel, list only the modules the user may view, and wait at most `AggregationTimeout` for each. A module that times out or throws answers `stale: true` in its place. Counts stop at 10,000 and report `capped: true`. `api/counts` answers `400` without `product`, `404` for a product no registered module joins, and `403` when the user may view none of the product's modules.

A `DashboardAttentionItem` can also say how to resolve it and whether it is still growing:

- **`RecommendedCommandId` and `GroupFilter`.** The id of one of the module's commands, such as its Requeue, and the filter, in the module's own Operation filter shape, that selects every object of the item's group. The item's recommended command (`Requeue 23`) runs as an Operation over `GroupFilter` from the module's own filtered list, never from the Attention popover: the operator gives a reason, the server counts the scope, and nothing runs before the confirm. The shell offers the button only when both members are set, the module implements `IDashboardOperationRunner`, and the host has not disabled the command or hidden it from the UI. A user without the command's permission sees it disabled with `Needs operate permission`.
- **`RecentCount`.** How many objects joined the item in the module's latest interval, such as the last hour. Above zero, the cause is still growing.
- **`LastSeenAt`.** When the cause last occurred, beside `Since`, when it began.

`GET {base}/api/needs-you` returns these as `recommended` (`{ command, filter }`, where `command` is described for the signed-in user as `GET {base}/api/{module}/commands` describes it, or `null`), `recentCount`, and `lastSeenAt`. The diagnostic module recommends Requeue over each failure group and counts the runs that failed in the last hour.

The interfaces grow by new capability interfaces and default interface members. An existing member never gains a parameter; the request and result records grow by `init` members instead.

A module package wraps `AddModule` in its own extension, the way `AddDiagnostics` does:

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

The page reads live changes from one server-sent-events stream at `GET {base}/api/stream?topics=`, with topics `{module}.{kind}`, comma-separated or repeated, at most 64. The open tabs of one dashboard share that stream: one tab holds it, chosen through the Web Locks API, and relays its events to the others over `BroadcastChannel`.

- **Events.** `subscribed`, `change` (module, kind, id, topic, version, and the formatted projection), `module.stale`, `attention.changed`, `heartbeat`, and `reset`. Each stream receives only modules the user may view and only the topics it asked for.
- **Resume.** Each node keeps its recent events in a bounded ring. A reconnect sends its last event id (the `Last-Event-ID` header, or the `lastEventId` query parameter) and resumes from it. When the node no longer holds that position, the stream sends `reset` and the page refetches its views.
- **Pause.** The top bar's Pause holds this tab's data changes (`change`, `attention.changed`, `reset`, and `subscribed`) while the stream stays open for the other tabs, and the live state reads `Paused · N updates`; Resume applies them in the order they came. A module going stale, session end, sign-out from another tab, and the result of the operator's own commands, which return through the API, still apply at once. Pause is not remembered across reloads.
- **Cross-node changes.** While a node has at least one stream open, it reads every module's `IDashboardChangeSource` each `ChangePollInterval`, starting from `GetStartCursorAsync`. No broker is involved: the module's store is the only shared state. A node with no open stream reads nothing. A read that throws marks that module's views `Stale`; the reader backs off and retries with the same cursor.
- **What a change source's store must provide.** A change marker stamped by the store (a sequence or the store's clock) in the same statement as every write the dashboard shows; an index on it; reads from the cursor minus an overlap window longer than the store's longest write transaction; and a bounded page per read. The shell drops a change whose `Version` it already sent for that object.
- **Same-node changes.** `IDashboardEventPublisher`, a singleton the shell registers, pushes a change to this node's streams at once, after the module's store commits it. Changes to one object within 250 milliseconds coalesce into one event. A version not newer than the last sent is dropped; versions compare as integers when both parse as one, and ordinally otherwise, so use a store sequence or a fixed-width timestamp. Other nodes still learn of the change from their own reads.
- **Closing.** A stream closes at the session ticket's expiry, at its next revalidation in host mode when `Revalidate` is set, and after 5 minutes under Basic and API key. The browser reconnects, which checks the session, credential, and module permissions again. A request whose `Origin` is not the shell's own gets `403`. A stream never delays the host's shutdown.

### Commands and Operations

A module's `IDashboardCommandSource.Commands` declares its commands once, at startup. Each `DashboardCommand` has an `Id` prefixed with the module id (`jobs.requeue`), a `Label`, the `ObjectKind` it acts on, and the rules the shell enforces:

| Member | Default | Effect |
| --- | --- | --- |
| `Permission` | `Operate` | `Operate` needs the module's operate policy; `View` needs only its view policy. |
| `Capability` | none | A named capability such as `jobs.delete`. When the host registers an authorization policy with that name, the command needs that policy instead of the operate policy. |
| `Confirmation` | `None` | `Confirm` (a dialog) or `Typed` (type the row count). A confirmed command over more than 100 rows is always `Typed`. |
| `InverseCommandId` | none | The command that undoes this one. A command with an inverse runs without confirmation, and the shell offers Undo. |
| `RequiresReason` | `false` | The operator must give a reason, at most 1,000 characters. |
| `Visibility` | `Ui \| Api` | The channels that may call it. |
| `Arguments` | none | `DashboardCommandArgument`s: a small JSON-Schema subset (`Text`, `WholeNumber`, `Number`, `Toggle`; required, allowed values, minimum, maximum, maximum length) that the shell renders as a form and validates on the server. |

The shell owns the endpoints under `{base}/api/{module}/commands`:

- `GET` lists the module's commands for the caller. `GET allowed?kind=&id=` asks `GetAllowedCommandsAsync` which commands each object allows now (at most 500 objects), so the page disables a control with the module's reason instead of offering an action the module would refuse.
- `POST {commandId}` runs the command over `ids` in a JSON body that may also carry `arguments`, `reason`, `confirmed`, and `typedCount`. At most 100,000 rows per call.
- The shell checks every rule before `ExecuteAsync` runs. A command the host disabled, or one the call's channel may not use, answers `404`. A failed permission answers `403`. The body, reason, argument schema, and confirmation answer a `400` validation problem naming each field, with the broken rule in its `rules` extension. The page sends `X-Headless-Dashboard-Channel: ui`; a call without it is an API call. The channel is claimed, not proven, so it decides only which commands are offered and what the log records.
- A module that refuses the whole call, because the object is not in a state the command applies to, returns a `DashboardCommandResult` with `Refusal` set and changes nothing; the shell answers `409` with the reason as the problem's `detail` and writes no operator-log entry.

A command over a filter runs as an **Operation**, through the module's `IDashboardOperationRunner`:

1. `POST {commandId}/confirm` with a `filter` calls `PrepareAsync`, which counts the scope, returns a sample of five rows, and reads a high-water mark from the store. The shell answers the exact count, the sample, the confirmation the count needs, and a scope token signed with Data Protection and valid for one hour. A scope over 100,000 rows is refused, so the operator narrows the filter.
2. `POST {commandId}/chunks` with the `token` and the previous `cursor` calls `RunChunkAsync` for the next 500 rows. Each chunk checks everything again: the command is still enabled, the user still holds its permission, and the token is intact, unexpired, and issued to this user for this module and command.

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

### Diagnostic module

`AddDiagnostics()` adds a module with the id `diagnostics`: an in-memory `DiagnosticsStore` of fake runs that implements every capability above, including commands (`{id}.fail`, `{id}.requeue`, `{id}.add-note`), Operations, and change reads. Its API at `{base}/api/{id}` lists runs newest first, one keyset page at a time (`GET runs?limit=&after=`: each page carries a `nextCursor` that `after` takes, `null` on the last page, and a run added while paging never shifts a later page), reads one (`GET runs/{runId}`), and adds a queued run (`POST runs`, an operator action). It adds no table, cache key, or channel to the host, and starts empty.

| `DiagnosticsOptions` member | Default | Description |
| --- | --- | --- |
| `Id` / `Instance(id)` | `diagnostics` | The instance's id. Add a second instance under another id, such as `diag-b`. |
| `Enabled` | `false` | Serves outside Development. In Development the module always registers; elsewhere `AddDiagnostics` registers nothing unless this is `true`, so the module is absent and its routes answer `404`. |
| `UseStore(store)` | a store of its own | Shares one `DiagnosticsStore` across hosts, as a deployment shares a database. |
| `ViewPolicy`, `OperatePolicy` | the shell's | As for every module. |

`AddDiagnostics(IConfiguration)` binds `Id`, `Enabled`, `ViewPolicy`, and `OperatePolicy`. The id, `Enabled`, and the store are read once, when `AddDiagnostics` runs.

### Module front end

The shell is a React application. A module's front end is a small entry plus lazy route chunks that import React, React Router, TanStack Query, and React Aria from the shell through an import map, never from their own bundle. The SDK (`@headless/dashboard-sdk`) and the Vite preset (`@headless/dashboard-vite`) are private packages of the repository-root npm workspace, under `src/Headless.Dashboard/wwwroot`, where the diagnostic module is built; they are not published to npm in this release.

A module's entry exports a `defineModule` call. The build reads the literal itself to write `manifest.json`, so every static field (ids, labels, paths, permissions, event names) must be a literal, not computed:

```ts
import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'diagnostics',
  product: 'diagnostics',
  sidebar: [{ entries: [{ id: 'diagnostics.nav.runs', label: 'Runs', route: '', icon: 'runs' }] }],
  routes: [
    { path: '', lazy: () => import('./views/runs-list.tsx') },
    { path: 'runs/:runId', lazy: () => import('./views/run-page.tsx') },
  ],
  objectKinds: {
    run: { route: (id: string) => `runs/${encodeURIComponent(id)}` },
  },
  commands: [
    { id: 'diagnostics.requeue-failed', label: 'Requeue failed runs', requires: 'operate', run: ({ navigate }) => navigate('?status=Failed&operation=requeue') },
  ],
})
```

`id` equals the .NET module id. `product` names the tab the module joins, one of `jobs`, `messaging`, `cluster`, `platform`, `dev-inbox`, or `diagnostics`; the shell shows one tab per product a host registered, in that order, and modules that name one product share its tab, their sidebar sections in registration order. A module whose manifest names another product fails the host's start.

`sidebar` lists the module's sections in display order. A section has an optional `label` (a section without one draws no heading) and its `entries`. An entry has an `id` (a contribution id the host can disable or order, such as `jobs.nav.failed`), a `label`, a `route` relative to the module with an optional query (`?status=Failed`), exactly one of `icon` (an icon key) or `state` (the colour role of its dot: `sched`, `enq`, `proc`, `ok`, `fail`, or `off`), an optional `countKey` naming its count in the module's `IDashboardCountSource` answer, and an optional `requires`. The build fails on a computed `sidebar`, an unknown `product` or `state`, or an entry with both or neither of `icon` and `state`.

Sidebar entries and client commands take `requires: 'view' | 'operate'`. Each route's `import()` becomes its own chunk. The palette already lists every sidebar entry as `Go to <module> <label>`, so a command that only navigates to an entry's route repeats it; the palette drops a command whose label matches a sidebar entry's label, ignoring case.

| SDK entry | Contents |
| --- | --- |
| `@headless/dashboard-sdk` | `defineModule`, the dashboard and module context hooks (`useDashboard`, `useModule`), and `Button` |
| `/components` | The design-system components. The list-and-detail frame: `PageFrame` and `usePageFrame`, `DetailPanel`, `FieldGrid` with `GridField`, `HistoryList`, and `ExceptionBlock` (see [List views](#list-views)). The data display blocks: `StatStrip` and `StatCard`, `PageHeader`, `FilterChips` and `FilterChip`, `SegmentedControl`, `Tag`, and `BarChart`. Also `Inspector` with editable `InspectorField`s (a select commits on change, text on `Enter` or blur, with an optional server preview while typing), `Timeline` (stored events in the same select-into-the-inspector grid as a table), `ScheduleStrip` (SVG marks for the next fires in the scheduler's zone, `[`/`]` widen and narrow it), `Dialog`, form fields, `CommandPalette`, `PayloadView`, status marks, live regions (`LiveIndicator` reads `Paused · N updates` when given the waiting count), toasts, the keymap hooks, and `below`, the named breakpoints as media conditions for script |
| `/table` | `DataTable`: virtualized, with keyset load-more, column priority, a density remembered per view, and rows that arrive above held behind `N new`. Given a page frame (`frame`), it marks the frame's open row `aria-selected`, adds a checkbox column for the frame's checked rows in a `multiple` list, tags the rows a bulk action refused, and pins its first column while the rest scroll sideways inside the table. It is its own entry, so the browser loads it with the first view that lists rows, not with the shell |
| `/data` | The API client and hooks (`useDashboardApi`, `useDashboardList`, `useDashboardKeysetList`, `useDashboardObject`, `useNewRows`), change patching, and capped counts |
| `/stream` | `useTopics`, `useStreamEvent`, `useStreamStatus`, and the stream client |
| `/commands` | `CommandAction` (its `recovery` prop makes it the command `R` runs for the selection, and every allowed action also appears in the palette; given per-row refusals it runs only the allowed rows), `useAllowedActions` (answers per row: the call gives a reason when any row is refused, so an action given it as `disabledReason` never sends a refused row; `.refused(commandId)` gives each refused row's reason; every row is refused, saying why, until the server answers or when the check fails), `ActionConfirm`, `SchemaForm`, the commands API, and the Operation runner and panel |
| `/router` | React Router, re-exported so every module uses the shell's one router |
| `/query` | TanStack Query, re-exported so every module reads the shell's one data client |
| `/testing` | `renderModule` |

#### List views

A list view composes the SDK's frame; a module writes no layout of its own.

- `usePageFrame({ viewId, rows, matches })` holds the view's state: the open row (`openId`, the only list state in the URL, as `?open=<id>`), the row cursor, the checked rows (`checked`, `setChecked`; view state that survives live updates but not a reload), and the refusals of the last bulk action (`refused`, `setRefused`, `revealed`, `reveal`). A change to the checked rows clears the refusals. `J`/`K`, and the arrow keys in a `DataTable`, move the cursor and an open panel follows it, replacing its history entry (`open(id, replace)`), so Back closes the panel rather than walking the rows; `Enter` opens a row with one new entry, `X` checks the cursor row without opening it, and `Esc` closes a popover, then the panel, then the checked rows.
- `PageFrame` lays out `stats`, `header`, the list in its own scroll region, and `panel`, the `DetailPanel` of the open row, docked under the list behind a resize handle (arrow keys move it 16 px, 64 px with Shift; the height is remembered per view). At 1280 px and wider the panel can move to the side; below 768 px it opens full screen. While it is docked below, toasts float above it.
- `DetailPanel` has a title row with the state, tabs, actions, detach to the object's page, and close. `FieldGrid` shows labels and mono values with optional inline edits; `HistoryList` shows an object's state changes; `ExceptionBlock` shows an exception's type, message, and frames with `Full stack` and `Copy`.
- `StatStrip` holds up to six `StatCard`s: a label, a served value, a state role dot, an optional served caption, and an optional `to` link to the filtered list. It draws no sparkline: a trend appears only once a series is served.
- `PageHeader` takes the title (with an optional state dot), the served count, a one-sentence `description`, filters as children, and `actions` at the inline end. `FilterChips` groups `FilterChip`s, each a toggle with a name and count (`pressed`, `onPress`; `failing` colours the count) or a set filter with `Clear` (`onClear`).
- `SegmentedControl` is one choice among a few, as pressed-state buttons; the table's density toggle uses it. `Tag` is a short fact on an object or row: `pill`, `small`, `filled`, or `square`, with an optional state role dot.
- `BarChart` stacks served series per column, drawn from zero, as one SVG `role="img"` whose label is a sentence naming the series, the columns' range, and the tallest bar; `mini` drops the axes.

A bulk action runs on the checked rows the server allows. Pass `CommandAction` the checked ids, `refused={allowed.refused(command.id)}`, `onRefused={frame.setRefused}`, and `onViewRefused={frame.reveal}`, and give the `DataTable` the same `frame`. With 2 of 5 rows refused the button reads `Requeue 3 of 5` and sends only the 3; the toast reads `Requeue: 3 done · 2 refused` with `View`, which highlights the refused rows and scrolls to the first; each refused row carries its reason as a tag in its own row. With every row refused the button is disabled with the shared reason, or `None of these can run now` when the reasons differ, and `R` announces it. Given `refused`, the action ignores `disabledReason`. Without `refused`, pass `disabledReason={allowed(command.id)}`: any refused row disables the action with the server's reason, as on a single object. The confirm and the run use the rows checked when the action was pressed; when the checked rows change before the run answers, the toast still reports it, but the refusals are not tagged and `View` is not offered.

The module's `vite.config.ts` uses the preset, which writes and validates `manifest.json` beside the entry and fails the build when the bundle contains React, React Aria, TanStack Query, or React Router code, or `eval` or an inline script:

```ts
import { fileURLToPath } from 'node:url'
import { dashboardModule } from '@headless/dashboard-vite'
import { defineConfig } from 'vite'

export default defineConfig(
  dashboardModule({
    root: fileURLToPath(new URL('.', import.meta.url)),
    entry: 'src/module.ts',
    outDir: '../../dist/modules/diagnostics',
  }),
)
```

The workspace build then holds the output to the size budgets in `packages/vite/budget.json`, measured as gzip at maximum compression: the shell's first load at most 235,000 bytes, the shell and every SDK entry together at most 250,000 bytes, each module entry at most 10,000 bytes, each view chunk at most 50,000 bytes, and the fonts at most 60,000 bytes. A budget overrun fails the build. The dashboard ships in English, so the shell build keeps only React Aria's `en-US` strings (its labels and screen-reader announcements); React Aria falls back to them for any other locale.

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

**Related and payload.** A run relates to the other runs with its correlation id (`jobs.same-correlation`), its chain (`jobs.chain`), and its cron job (`jobs.cron-job`); a run with a stored W3C trace parent carries its trace id, so the shell offers `Open trace`. Its payload is its stored request as JSON, or `Unsupported contract version N` when this host registers another version. A run and an attempt carry their exception as `exceptionMessage` and `exceptionFrame`: the module reads the JSON the executor stores (the outer message and the root exception's top frame) into the two, and a reason the store wrote itself, such as `Lease lapsed while running!`, arrives as the message with no frame.

**API** at `{base}/api/jobs`, JSON with camelCase names and enums as strings:

| Route | Answer |
| --- | --- |
| `GET runs?kind=&function=&status=&cronJobId=&correlationId=&fingerprint=&from=&before=&actionable=&newest=&limit=&after=` | `{ items, count, countCapped, nextCursor }`: one keyset page of runs, newest first unless `newest=false`, `limit` up to 200, a count capped at 10,000; `after` takes `nextCursor` |
| `GET runs/{id}` | `{ run, children }`, or `404` |
| `GET runs/{id}/attempts` | `{ items: [{ number, node, startedAt, endedAt, outcome, exceptionMessage, exceptionFrame, failureFingerprint }] }`, or `404` |
| `GET cron-jobs?function=&fires=` | `{ items }`: every cron job with its plain reading; with `fires`, each active one also carries that many next fires (at most 50), as a Function page draws them on its strip |
| `GET cron-jobs/{id}?fires=` | one cron job with its next fires (default 5, at most 50; none while paused), or `404` |
| `GET cron-preview?expression=&timeZoneId=&count=` | `{ timeZoneId, reading, nextFires, validationMessage }` for a draft schedule |
| `GET functions` | `{ items: [{ name, contractVersion, priority, maxConcurrency, requestType, cronExpression, claimedBy, succeeded, failed, running, waiting, lastExecutedAt, cronJobs }] }`, counts over 24 hours |
| `GET nodes` | `{ items: [{ identity, state, role, claimedFunctions }] }`: live nodes; without node membership, this host |
| `POST time-jobs` | an operator action: `{ function, executionTime, timeZoneId, request, description, retries, retryIntervals, children: [{ ..., runCondition }] }` answers `201 { id }`; a time without an offset is read in `timeZoneId`, else the scheduler's zone; a bad body answers `400` |
| `POST cron-jobs` | an operator action: `{ function, expression, timeZoneId, request, description }` answers `201 { id }` |

**Views.** The package embeds the module's front end, so the Jobs tab's sidebar reaches four views:

| View | Shows | Acts |
| --- | --- | --- |
| Runs (`{base}/jobs`) | One live list of time jobs and occurrences, filtered in the URL by kind, function, status, time range, correlation id, failure fingerprint, and cron job. The inspector shows the selected run's fields, its attempts with node and exception, and its payload on request. | `Requeue`, `Cancel`, and `Delete` on the selection, and over every matching run as an Operation; `Add time job` and `Add chain` |
| Run page (`runs/{id}`) | The run's header, fields, attempt timeline, chain steps, and payload on request | The same run actions |
| Cron jobs and cron job page (`cron-jobs`, `cron-jobs/{id}`) | Each cron job's expression, plain reading, and status; the page adds the schedule strip, the next five fires in the cron job's zone, and its occurrences | `Run now`, `Pause` / `Resume`, `Add cron job`; the expression and zone edit in place, showing the server's reading and next fires before they commit, and stay read-only for a cron job defined in code |
| Functions and function page (`functions`, `functions/{name}`) | Each function's contract version, priority, concurrency, the live nodes whose run filter claims it, its 24-hour counts, strip, cron jobs, and runs | None |
| Nodes (`nodes`) | Each live node's liveness and the functions its run filter claims | None |

A run with a pending cancel reads `Cancelling…` until the job stops, and a run an action finished on reads `Requeuing…`, `Cancelling…`, or `Deleting…` until its change arrives. `T` on a selected run, and `Trace` beside its correlation id, open the runs list filtered by that correlation id. The palette adds `Go to runs`, `Requeue failed runs`, `Add time job`, `Add chain`, and `Add cron job`. A viewer sees every action disabled with `Needs operate permission`. Per-node scheduler state, and starting or stopping a scheduler, are not part of the module yet.

#### Migrate from `MapHeadlessJobsDashboard`

The Jobs Vue dashboard, its SignalR live-update hub, and its endpoints under `/jobs/dashboard` are removed. The Jobs module replaces them inside the shell, with live changes over the shell's stream. To migrate a host:

1. Delete `AddDashboard(...)` from the `AddHeadlessJobs(...)` delegate, and delete `app.MapHeadlessJobsDashboard()`.
2. Add `AddJobs()` to `AddHeadlessDashboard(...)`, and map the shell with `app.MapHeadlessDashboard()` if the host does not already.
3. Move authentication to the shell: its `WithHostAuthentication`, `WithBasicAuth`, `WithApiKey`, or `WithNoAuth` call covers every module. For separate Jobs rights in host mode, set `ViewPolicy` and `OperatePolicy` on `JobsDashboardOptions`.
4. Point bookmarks and links at `{shell base}/jobs` (by default `/headless/jobs`). Nothing serves `/jobs/dashboard` any more.
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

### Messaging and standalone dashboards

The Messaging Vue dashboard, and a standalone dashboard a host writes itself, authenticate through this package's per-dashboard primitives: the keyed `IAuthService`, the authentication scheme and policy that check a dashboard's own credential, the host-mode session, its view and operate policies, the sign-in endpoints, and the endpoint conventions that apply them. The rules below apply to those dashboards; the shell reuses the host-session rules.

- Configure authentication through the owning dashboard builder (`UseDashboard(...)`), then mount the dashboard on the host's endpoints with `MapHeadlessMessagingDashboard()`. That call is the only mount: a host that registers the dashboard and never maps it fails to start.
- Mount on the host's endpoints, so the host's `UseAuthorization` evaluates the dashboard's own rule and never the host's fallback policy. A host fallback policy that requires a tenant claim, for example, does not lock out a tenantless operator.
- Each dashboard owns its authentication. A host that runs the shell and the Messaging dashboard can give them different modes and credentials; neither registration overrides the other.

#### Setup

Install an owning dashboard package and configure its builder. Host mode with separate view and operate policies, and an OpenID Connect sign-in for browsers:

```csharp
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
    .AddPolicy("MessagingViewers", policy => policy.RequireRole("support", "operator"))
    .AddPolicy("MessagingOperators", policy => policy.RequireRole("operator"));

builder.Services.AddHeadlessMessaging(setup =>
{
    // plus exactly one transport and one storage provider; see messaging.md
    setup.UseDashboard(dashboard =>
        dashboard.WithHostAuthentication(host =>
        {
            host.ViewPolicy = "MessagingViewers";
            host.OperatePolicy = "MessagingOperators";
            host.ChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
    );
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHeadlessMessagingDashboard();

// The host's own sign-out also ends the dashboard session.
app.MapPost("/account/sign-out", async (HttpContext context) =>
{
    await context.SignOutHeadlessDashboardAsync(MessagingDashboardOptionsBuilder.AuthenticationName);
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/");
});
```

The dashboard's Sign in link opens `{base path}/auth/sign-in`. A host user who is already signed in gets a dashboard session at once. Otherwise the dashboard challenges `ChallengeScheme` once, and the identity provider returns the user to the dashboard.

An application whose SPA signs in with a bearer token has no cookie for the dashboard to read. It exchanges the token once for the dashboard session, then navigates to the dashboard:

```js
const response = await fetch('/messaging/dashboard/api/auth/session', {
  method: 'POST',
  headers: {
    Authorization: `Bearer ${accessToken}`,
    'X-Requested-With': 'XMLHttpRequest',
  },
});

if (response.status === 204) {
  window.location.assign('/messaging/dashboard/');
}
```

The exchange answers `204` and sets the session cookie, `401` when no host scheme authenticated the request, and `403` when the user fails the view policy. The token never goes into a URL or browser storage.

For a standalone integration:

```bash
dotnet add package Headless.Dashboard
```

Register the authentication under a name your dashboard owns, map the sign-in endpoints, and protect the dashboard's endpoints with the same name:

```csharp
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
```

`AddDashboardAuthentication(name, IConfiguration)` and `AddDashboardAuthentication(name, Action<AuthConfig, IServiceProvider>)` take the same name. A blank name throws `ArgumentException`. Each registration adds, once per name, an authentication scheme and an authorization policy named `name` for Basic and API key, and the host-mode session scheme `{name}.Session` with the policies `{name}.View` and `{name}.Operate`. `MapDashboardAuthEndpoints`, `RequireDashboardAuthentication`, and `RequireDashboardOperation` throw `InvalidOperationException` for a name no registration added.

#### Configuration

`AuthConfig` exposes `Mode`, `BasicCredentials`, `ApiKey`, and `Host`. Registration validates each named configuration's selected mode at startup; incomplete credentials or invalid host options fail with `OptionsValidationException`.

The dashboard builders expose `WithNoAuth()`, `WithBasicAuth(username, password)`, `WithApiKey(apiKey)`, and `WithHostAuthentication(Action<DashboardHostAuthenticationOptions>? configure = null)`. `AuthConfig.Host` is a `DashboardHostAuthenticationOptions`, read only in host mode:

| Option | Default | Description |
| --- | --- | --- |
| `ViewPolicy` | the host's default policy | Host policy a user must pass to get a session and to read from the dashboard. |
| `OperatePolicy` | `ViewPolicy` | Host policy a session must also pass for operator actions, the dashboard's state-changing requests. |
| `ChallengeScheme` | none | Host scheme `auth/sign-in` challenges when no host user is signed in, such as OpenID Connect. Without it the dashboard offers no Sign in link, and users get a session through the exchange. |
| `SessionCookieName` | `__Host-` and the dashboard name in lower case, such as `__Host-headless-messaging-dashboard` | Set it when two applications on one host name mount the same dashboard. It must be a cookie-name token. |
| `SessionIdleTimeout` | 30 minutes | A session with no authenticated dashboard request for this long ends. Must not exceed `SessionLifetime`. |
| `SessionLifetime` | 8 hours | Absolute lifetime from sign-in, whatever the activity. |
| `RevalidationInterval` | none | How often `Revalidate` runs for one session. Required when `Revalidate` is set. |
| `Revalidate` | none | `Func<HttpContext, ClaimsPrincipal, ValueTask<bool>>` that confirms the session's user is still allowed in; `false` ends the session. |

#### Host sessions

- Each dashboard keeps its own session cookie: `Path=/`, `HttpOnly`, `Secure`, `SameSite=Strict`, and host-only. In host mode the dashboard API authenticates with that cookie alone. The host's own cookie or bearer token does not open them.
- A session is issued by `GET {base}/auth/sign-in?returnUrl=` or `POST {base}/api/auth/session`. Both copy the host user's authenticated identities and claims, with their name and role claim types, into the session. A user who fails the view policy, or whom `Revalidate` refuses, gets `403` and no cookie. The host's bearer token is not copied.
- `auth/sign-in` follows only a local `returnUrl` under the dashboard's base path with no `.` or `..` segment; any other value goes to `{base}/`. It challenges `ChallengeScheme` at most once per sign-in: when the user comes back still not signed in, it answers `401` instead of challenging again.
- `POST {base}/api/auth/sign-out` deletes the dashboard cookie only. When it fails, the SPA reports the failure and keeps the operator signed in. Otherwise the SPA shows a signed-out page and does not sign in again until the operator chooses Sign in, even while the host session lasts. `HttpContext.SignOutHeadlessDashboardAsync(name)` deletes the same cookie from host code.
- The host user is found through the host's schemes, never the dashboard's. `auth/sign-in` authenticates the schemes `ViewPolicy` lists, when it lists any; otherwise `ChallengeScheme`, then the default authenticate scheme. The exchange authenticates the `ViewPolicy` schemes, then the default authenticate scheme.
- A session ends at `SessionLifetime` after sign-in, and after `SessionIdleTimeout` with no authenticated dashboard request. A request made after half the idle window has passed renews the cookie. The SPA's polling counts as activity, so an open Messaging tab stays signed in until the absolute lifetime. A renewal never moves the cookie's expiry past `SessionLifetime`.
- A short-lived bearer token exchanged for a session becomes a session that can last up to `SessionLifetime`, after the token itself expires. A host that revokes tokens, disables accounts, or removes roles should set `Revalidate` and `RevalidationInterval`, and should call `SignOutHeadlessDashboardAsync` from its sign-out. Both act on the next dashboard request.
- Policies evaluate against the copied claims. A requirement that reads the request, a header, or the bearer token fails. A policy that lists `AuthenticationSchemes` fails closed: it passes only when one of those schemes authenticated the user the session was issued for.
- The session cookie is protected by the host's Data Protection key ring. Every instance serving the dashboard must share that key ring; `Headless.DataProtection.Blobs` persists it to blob storage (see [api.md](api.md#headlessdataprotectionblobs)).
- With `Path=/`, the browser sends the session cookie on every request to the host, not only dashboard requests. It carries the copied claims, so a principal with very many claims can make request headers too large.
- `GET {base}/api/auth/info` is anonymous. It reports `mode`, `enabled`, `signInAvailable`, and, in host mode, the current session's `authenticated`, `name`, and `canOperate`. It never exposes credentials. The SPA decides whether to show the sign-in page from this call, because the `SameSite=Strict` cookie can be missing from the first page load after an identity-provider redirect.
- The SPA hides write actions when `canOperate` is `false`. The server refuses them with `403` either way.

#### Design and runtime behavior

- Credential comparisons are constant-time.
- Authentication is per dashboard. `AddDashboardAuthentication(name, …)` configures the `AuthConfig` options instance named `name` and registers a scoped `IAuthService` keyed by `name`; the dashboard's authentication scheme and the `/api/auth/info` and `/api/auth/validate` endpoints resolve the service by that key. Nothing registers an unkeyed `IAuthService` or a raw `AuthConfig` singleton.
- `RequireDashboardAuthentication(name)` reads the named `AuthConfig` when the host builds its endpoints. `None` allows anonymous access. `Host` requires `{name}.View`: a dashboard session whose user passes `ViewPolicy`, or the host's default policy when it is empty, answering `401` without a session and `403` when the policy refuses. Basic and API key require a user the dashboard's scheme signed in, and refuse any other request with an empty `401`. The scheme checks a credential only on an endpoint that convention marked with its name, so a host whose only registered scheme is a dashboard's (which ASP.NET Core then makes the default) does not check or log a dashboard credential on its own requests.
- `RequireDashboardOperation(name)` marks operator actions. In host mode it adds `{name}.Operate`, which requires `OperatePolicy`, or `ViewPolicy` when it is empty. Other modes add nothing: Basic and API key share one credential for viewing and operating.
- On success in Basic or API key mode, `HttpContext.User` for that request is the dashboard identity: one `Name` claim with the `AuthResult.Username`, authentication type `name`. It replaces a host sign-in on that request, so a Messaging operator action records the dashboard credential's user. In host mode `HttpContext.User` is the session's copy of the host user.
- The Messaging dashboard's name is `MessagingDashboardOptionsBuilder.AuthenticationName` (`"Headless.Messaging.Dashboard"`). To replace a dashboard's `IAuthService`, register yours with `AddKeyedScoped<IAuthService, TService>(name)` before the dashboard registers; the registration keeps an existing keyed service.
- In Basic and API-key modes, the `access_token` query parameter counts as a credential only on a request routed to a SignalR hub endpoint (its routing metadata, not its path, decides), because a browser cannot set headers on a WebSocket. Every other request must send the `Authorization` header. Host mode accepts no query credential.
- Every state-changing request to such a dashboard (`POST`, `PUT`, `PATCH`, `DELETE`) must carry an `X-Requested-With` header, or it gets `400`. That covers its API, `api/auth/session`, and `api/auth/sign-out`, in every mode. A cross-site `<form>` cannot set the header, and a cross-site script that sets it needs a CORS preflight the dashboard refuses.
- Each dashboard maps a `{base path}` group: its `/api` group carries the dashboard's rule; `/auth/sign-in`, `/api/auth/info`, `/api/auth/session`, `/api/auth/sign-out`, `/api/auth/validate`, Messaging's `/api/health`, the SPA shell, and its assets are anonymous. `auth/sign-in`, `api/auth/session`, and `api/auth/sign-out` answer `404` outside host mode. An unmatched `{base path}/api/...` path is a `404`, never the shell, so no path under `/api/` is exempt because of its extension or a segment such as `/assets/` or `/negotiate`. Any other unmatched `GET` under the base path returns the shell, so client routes deep-link. `MapHeadlessMessagingDashboard()` refuses the root base path, where the shell would answer every unmatched `GET` in the host.
- The host pipeline must run `UseAuthorization` between routing and endpoints (a `WebApplication` adds it when the host does not), and `UseAuthentication` before it for host authentication. A host that calls `UseAuthentication()` must also call `UseAuthorization()` after it: the `UseAuthorization` a `WebApplication` adds on its own runs ahead of the host's `UseAuthentication`, so host authentication would not have signed the user in yet. The dashboards attach CORS metadata only when `SetCorsOrigins` or `SetCorsPolicy` configured a policy, which only Basic and API key allow; then the host must also run `UseCors()`. Add host-specific behavior, such as rate limiting, to the builder the map method returns.
- The SPA shell carries only the base path, the mode, whether auth is enabled, whether sign-in is available, and, for Messaging, the polling interval. Host mode stores nothing in the browser beyond the HttpOnly session cookie; Basic and API key keep the entered credential in the SPA's browser storage. The browser does not enforce a session timeout: the server ends sessions.
- In host mode the Messaging gateway proxy does not relay requests to other nodes: it serves each request from the node that received it, and clears the node-selection cookies. The SPA hides node switching. Basic and API key relay as before, because the proxy forwards the `Authorization` header and never the `Cookie` header.
