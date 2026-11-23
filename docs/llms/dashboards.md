---
domain: Dashboards
packages: Dashboard
---

# Dashboards

> One operations shell that serves feature modules under one base path and one sign-in, plus the authentication primitives for a dashboard a host writes.

## Orientation

`Headless.Dashboard` has two parts:

- **The shell.** `services.AddHeadlessDashboard(...)` registers it and `app.MapHeadlessDashboard()` mounts it, by default at `/headless`. It serves every registered module under that base path with one sign-in, one session cookie, one live stream, and one command pipeline. It stores nothing: each module reads its own store. The package ships no module of its own. `Headless.Jobs.Dashboard` adds the Jobs module (`AddJobs()`), and `Headless.Messaging.Dashboard` adds the Messaging module (`AddMessaging()`).
- **Authentication for a dashboard a host writes.** The per-dashboard primitives the shell is built on, `AddDashboardAuthentication(name, ...)` and its endpoint conventions, protect a dashboard the host serves outside the shell.

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
- Never put production credentials or API keys in source, examples, logs, URLs, or frontend storage.
- `WithNoAuth()` and `AuthMode.None` make a dashboard public. Use them only in an isolated development environment.

## Choosing an authentication mode

| Mode | Shell builder call | Use when | Trade-off |
| --- | --- | --- | --- |
| Host | `WithHostAuthentication(...)` | The application already signs users in, with a cookie, OpenID Connect, or a bearer token | Separate view and operate policies per module, server-side session with idle and absolute expiry; same origin only |
| Basic | `WithBasicAuth(user, password)` | A small standalone deployment needs simple credentials | One shared credential for viewing and operating every module; the application owns secret storage and rotation |
| API key | `WithApiKey(key)` | Automation or a controlled operator client needs bearer access | One shared credential for viewing and operating every module; the application owns key distribution and rotation |
| None | `WithNoAuth()` | Isolated local development only | No access control |

There is no custom-validator mode. To sign operators in with your own credential check, write an ASP.NET Core authentication handler, register it in the host, and use host mode over it.

## Topics

| Read when | Topic |
| --- | --- |
| You add the Jobs module, call its API, or migrate from `MapHeadlessJobsDashboard` | [Jobs module](dashboards/jobs-module.md) |
| You add the Messaging module, call its API, forward between nodes, run a dashboard-only host, or migrate from `MapHeadlessMessagingDashboard` | [Messaging module](dashboards/messaging-module.md) |
| You implement a module's server side, read the live stream protocol, write an operator-log sink, or need command and Operation rules | [Modules](dashboards/modules.md) |
| You protect a dashboard the host serves itself, outside the shell, with `AddDashboardAuthentication` | [Dashboards a host writes](dashboards/host-written.md) |

---

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

**Verify:** an `AddHeadlessDashboard` delegate that picks no authentication mode throws `InvalidOperationException` at registration. Start the host: a host that registers the shell and never maps it fails to start with `InvalidOperationException`. Anonymous `GET /headless/api/auth/info` answers `200` with the shell's `mode`; in host mode a signed-in user's `authenticated` reads `true` after `GET /headless/auth/sign-in`.

`MapHeadlessDashboard()` returns an `IEndpointConventionBuilder` for the whole dashboard group; add host conventions such as rate limiting to it. It throws `InvalidOperationException` when the shell is not registered, is already mapped, or its base path is the root.

### Shell options

`HeadlessDashboardSetupBuilder`, the `AddHeadlessDashboard` delegate's argument, sets:

| Member | Default | Description |
| --- | --- | --- |
| `WithHostAuthentication(configure?)` | | Host mode. `configure` sets a `DashboardHostAuthenticationOptions`; see [Host authentication options](#host-authentication-options) for its members. |
| `WithBasicAuth(username, password)` / `WithApiKey(apiKey)` / `WithNoAuth()` | | The other modes. Exactly one mode call is required. |
| `SetBasePath(path)` | `/headless` | The path under the host's `PathBase`. The root fails validation at startup. |
| `SetTraceLinkTemplate(template)` | none | A link to the host's tracing tool with `{traceId}`. An object that carries a W3C trace id then offers `Open in tracing tool`. A template without `{traceId}` fails validation. |
| `ConfigureContentSecurityPolicy(configure)` | | Adds sources to the document's policy; see [Serving and Content Security Policy](#serving-and-content-security-policy). |
| `Contributions(configure)` / `Contributions(IConfiguration)` | | Disables or reorders module contributions by id. |
| `AddModule<TModule, TOptions>(id)` | | Adds a module. Module packages call it from their own `Add{Module}` extension. |
| `Services` | | The host's `IServiceCollection`, for a module package's registrations. |

`HeadlessDashboardOptions` holds the rest, validated at startup. Change them with `services.Configure<HeadlessDashboardOptions>(...)`:

| Option | Default | Description |
| --- | --- | --- |
| `AggregationTimeout` | 2 seconds | How long attention, Overview, id lookup, traces, and related objects wait for each module. A module that has not answered, or that throws, shows as `Stale` while the others' answers are served. Must be positive. |
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

### Trace page

`{base}/trace#<id>` shows every object that carries one correlation id, from every module that serves traces and the user may view, as one tree ordered by time: a job run, the message it published, each subscriber's received generation under that message, and a job a consumer scheduled under that generation. Open it with `Open trace` or `T` on a run or a message, or paste an id into Go to id and pick `Open trace for <id>`. Select a row to open its object.

- **The id stays out of URLs.** It rides in the fragment, which the browser never sends, and the page asks `POST {base}/api/trace` with the id in the body. A module list the page links to receives the id as history state, never as a query string. The id is matched exactly: `c-42` and `c-42 ` are two ids.
- **Limits.** Each module lists at most its own `TraceLimit` objects, oldest first: the Jobs module's [`JobsDashboardOptions.TraceLimit`](dashboards/jobs-module.md#register-the-module) and the Messaging module's [`MessagingDashboardOptions.TraceLimit`](dashboards/messaging-module.md#register-the-module), each 500 by default and 1 to 10,000. Past the limit the module's count reads `N+`, such as `500+`, and links to its own list filtered by the id.
- **`Stale`.** A module that did not answer within `AggregationTimeout`, or that failed, reads `Stale: it did not answer in time.` with Retry. The other modules' rows still show. A Messaging store or node that did not answer is named beside the module's count, `<source> did not answer`.
- **`N module(s) hidden by your permissions`.** Modules that serve traces but that the user may not view are counted, never named, and their objects are left out.
- **Tags.** `parent not on this page`: the object's cause is outside this trace, past a limit, or in a hidden module. `consumer not recorded`: a message published from a consumed message whose subscriber row is not on the page, or not the only one, sits under the published message instead. `clock skew`: the object is stamped before its parent and stays under it. `cycle`: a repeated cause, broken at the first repeat.

Correlation flows on its own: a message a running job publishes takes the run's correlation id, and a job a consumer schedules takes the message's. Pass an explicit id only to join work Headless did not start. The [Modules](dashboards/modules.md#traces) topic covers serving traces from your own module.

**Verify:** `POST {base}/api/trace` with `{ "correlationId": "<id>" }` answers one section per module that serves traces with `stale: false`, and the page at `{base}/trace#<id>` shows the job run, its message, and each subscriber's generation under it.

### Shell sign-in and sessions

- The shell's session cookie is `__Host-headless-dashboard-` followed by the host's application name, lower-cased with every character that is not an ASCII letter or digit replaced by `-`, so two applications on one host name keep separate sessions. `SessionCookieName` overrides it in host mode. The cookie is `Path=/`, `HttpOnly`, `Secure`, `SameSite=Strict`, and host-only.
- **Host mode.** A session is issued by `GET {base}/auth/sign-in?returnUrl=`, which challenges `ChallengeScheme` when no host user is signed in, or by `POST {base}/api/auth/session` with the host's own credential. The [Host sessions](#host-sessions) rules apply. A user gets a session when they pass the shell's `ViewPolicy` or any module's view policy.
- **Basic and API key.** The shell's sign-in page at `{base}/sign-in` posts the credential once to `api/auth/session`; the browser then holds only the session cookie. The cookie carries a fingerprint of the credential, so changing the password or key ends every session issued under the old one at its next request. A script can still send the credential as an `Authorization` header (`Basic`, or `Bearer` for the key) on every request.
- **Per-module access, host mode.** Each module's options derive from `DashboardModuleOptions`, whose `ViewPolicy` and `OperatePolicy` override the shell's. An empty module `ViewPolicy` uses the shell's view policy. An empty module `OperatePolicy` uses the module's `ViewPolicy` when set, else the shell's operate policy. A user may operate a module only if they may also view it. The page lists only the modules the session may view, and the server refuses the rest. Basic and API key share one credential that views and operates every module.
- `GET {base}/api/auth/info` is anonymous and reports the mode and the session. `POST {base}/api/auth/sign-out` deletes the cookie. `HttpContext.SignOutHeadlessDashboardAsync()` deletes it from host code.
- Every state-changing request (`POST`, `PUT`, `PATCH`, `DELETE`) must carry an `X-Requested-With` header, or it gets `400`.

### Host authentication options

Options validate at startup; invalid host options fail with `OptionsValidationException`.

`WithHostAuthentication(Action<DashboardHostAuthenticationOptions>? configure = null)` sets a `DashboardHostAuthenticationOptions`. A dashboard a host writes reads the same type from `AuthConfig.Host`.

| Option | Default | Description |
| --- | --- | --- |
| `ViewPolicy` | the host's default policy | Host policy a user must pass to get a session and to read from the dashboard. |
| `OperatePolicy` | `ViewPolicy` | Host policy a session must also pass for operator actions, the dashboard's state-changing requests. |
| `ChallengeScheme` | none | Host scheme `auth/sign-in` challenges when no host user is signed in, such as OpenID Connect. Without it the dashboard offers no Sign in link, and users get a session through the exchange. |
| `SessionCookieName` | The shell's is in [Shell sign-in and sessions](#shell-sign-in-and-sessions); a dashboard a host writes uses `__Host-` and its name in lower case, such as `__Host-reports.dashboard` | Set it when two applications on one host name mount the same dashboard. It must be a cookie-name token. |
| `SessionIdleTimeout` | 30 minutes | A session with no authenticated dashboard request for this long ends. Must not exceed `SessionLifetime`. |
| `SessionLifetime` | 8 hours | Absolute lifetime from sign-in, whatever the activity. |
| `RevalidationInterval` | none | How often `Revalidate` runs for one session. Required when `Revalidate` is set. |
| `Revalidate` | none | `Func<HttpContext, ClaimsPrincipal, ValueTask<bool>>` that confirms the session's user is still allowed in; `false` ends the session. |

### Host sessions

These rules hold for the shell and for a dashboard a host writes.

- Each dashboard keeps its own session cookie: `Path=/`, `HttpOnly`, `Secure`, `SameSite=Strict`, and host-only. In host mode the dashboard API authenticates with that cookie alone. The host's own cookie or bearer token does not open them.
- A session is issued by `GET {base}/auth/sign-in?returnUrl=` or `POST {base}/api/auth/session`. Both copy the host user's authenticated identities and claims, with their name and role claim types, into the session. A user who fails the view policy, or whom `Revalidate` refuses, gets `403` and no cookie. The host's bearer token is not copied.
- `auth/sign-in` follows only a local `returnUrl` under the dashboard's base path with no `.` or `..` segment; any other value goes to `{base}/`. It challenges `ChallengeScheme` at most once per sign-in: when the user comes back still not signed in, it answers `401` instead of challenging again.
- `POST {base}/api/auth/sign-out` deletes the dashboard cookie only. `HttpContext.SignOutHeadlessDashboardAsync(name)` deletes the same cookie from host code.
- The host user is found through the host's schemes, never the dashboard's. `auth/sign-in` authenticates the schemes `ViewPolicy` lists, when it lists any; otherwise `ChallengeScheme`, then the default authenticate scheme. The exchange authenticates the `ViewPolicy` schemes, then the default authenticate scheme.
- A session ends at `SessionLifetime` after sign-in, and after `SessionIdleTimeout` with no authenticated dashboard request. A request made after half the idle window has passed renews the cookie. A renewal never moves the cookie's expiry past `SessionLifetime`.
- A short-lived bearer token exchanged for a session becomes a session that can last up to `SessionLifetime`, after the token itself expires. A host that revokes tokens, disables accounts, or removes roles should set `Revalidate` and `RevalidationInterval`, and should call `SignOutHeadlessDashboardAsync` from its sign-out. Both act on the next dashboard request.
- Policies evaluate against the copied claims. A requirement that reads the request, a header, or the bearer token fails. A policy that lists `AuthenticationSchemes` fails closed: it passes only when one of those schemes authenticated the user the session was issued for.
- The session cookie is protected by the host's Data Protection key ring. Every instance serving the dashboard must share that key ring; `Headless.DataProtection.Blobs` persists it to blob storage (see [Headless.DataProtection.Blobs](api.md#headlessdataprotectionblobs)).
- With `Path=/`, the browser sends the session cookie on every request to the host, not only dashboard requests. It carries the copied claims, so a principal with very many claims can make request headers too large.
- `GET {base}/api/auth/info` is anonymous. It reports `mode`, `enabled`, `signInAvailable`, and, in host mode, the current session's `authenticated`, `name`, and `canOperate`. It never exposes credentials. A frontend decides whether to show its sign-in page from this call, because the `SameSite=Strict` cookie can be missing from the first page load after an identity-provider redirect.

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
