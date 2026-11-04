---
domain: Dashboards
packages: Dashboard.Authentication
---

# Dashboards

> Shared authentication for the Jobs and Messaging operational dashboards.

## Orientation

Applications normally receive `Headless.Dashboard.Authentication` transitively through `Headless.Jobs.Dashboard` or `Headless.Messaging.Dashboard`. Configure authentication through the owning dashboard builder. Reference the package directly only when building dashboard infrastructure that consumes its shared contracts.

Read [jobs.md](jobs.md) for the Jobs dashboard and [messaging.md](messaging.md) for the Messaging dashboard. This guide owns only their shared authentication boundary.

## Agent Rules

- Treat every dashboard as an operations surface. Require host, Basic, or API-key authentication outside isolated local development.
- Prefer host authentication when the application already signs its users in. It keeps identity, audit, and revocation in the host's security boundary, and separates viewing from operator actions.
- Write host view and operate policies from claims only. The dashboard evaluates them against the claims it copied into its own session, not against the host's bearer token or request. A policy that lists authentication schemes passes only for a session issued from one of those schemes.
- Serve a host-mode dashboard from the host's own origin. Host mode fails at startup with `SetCorsOrigins`, `SetCorsPolicy`, or the Jobs `SetBackendDomain`.
- Give every instance of a multi-instance host one shared Data Protection key ring, for example with `Headless.DataProtection.Blobs`. Without it, a session issued by one instance is refused by the next.
- Call `HttpContext.SignOutHeadlessDashboardAsync(name)` from the host's own sign-out, for each dashboard, so dashboard sessions end with the application session.
- Never embed production credentials or API keys in source, examples, logs, URLs, or frontend storage.
- `AuthMode.None` makes the dashboard public. Use it only in an isolated development environment.
- Configure authentication through the owning dashboard builder (`UseDashboard(...)` or `AddDashboard(...)`), then mount the dashboard on the host's endpoints with `MapHeadlessMessagingDashboard()` or `MapHeadlessJobsDashboard()`. That call is the only mount: a host that registers a dashboard and never maps it fails to start.
- Mount on the host's endpoints, so the host's `UseAuthorization` evaluates the dashboard's own rule and never the host's fallback policy. A host fallback policy that requires a tenant claim, for example, does not lock out a tenantless operator.
- Each dashboard owns its authentication. A host that runs the Jobs and Messaging dashboards can give them different modes and credentials; neither registration overrides the other.

## Choosing an authentication mode

| Mode | Builder call | Use when | Trade-off |
| --- | --- | --- | --- |
| Host | `WithHostAuthentication(...)` | The application already signs users in, with a cookie, OpenID Connect, or a bearer token | Separate view and operate policies, server-side session with idle and absolute expiry; same origin only; the Messaging gateway does not relay to other nodes |
| Basic | `WithBasicAuth(user, password)` | A small standalone deployment needs simple credentials | One shared credential for viewing and operating; the application owns secret storage and rotation |
| API key | `WithApiKey(key)` | Automation or a controlled operator client needs bearer access | One shared credential for viewing and operating; the application owns key distribution and rotation |
| None | `WithNoAuth()` | Isolated local development only | No access control |

There is no custom-validator mode. To sign operators in with your own credential check, write an ASP.NET Core authentication handler, register it in the host, and use host mode over it.

## Headless.Dashboard.Authentication

Shared authentication primitives used by both dashboard packages: the per-dashboard `IAuthService`, the authentication scheme and policy that check a dashboard's own credential, the host-mode session, its view and operate policies, the sign-in endpoints, and the endpoint conventions that apply them.

### Setup

Install an owning dashboard package and configure its builder. Host mode with separate view and operate policies, and an OpenID Connect sign-in for browsers:

```csharp
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
dotnet add package Headless.Dashboard.Authentication
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

### Configuration

`AuthConfig` exposes `Mode`, `BasicCredentials`, `ApiKey`, and `Host`. Registration validates each named configuration's selected mode at startup; incomplete credentials or invalid host options fail with `OptionsValidationException`.

The dashboard builders expose `WithNoAuth()`, `WithBasicAuth(username, password)`, `WithApiKey(apiKey)`, and `WithHostAuthentication(Action<DashboardHostAuthenticationOptions>? configure = null)`. `AuthConfig.Host` is a `DashboardHostAuthenticationOptions`, read only in host mode:

| Option | Default | Description |
| --- | --- | --- |
| `ViewPolicy` | the host's default policy | Host policy a user must pass to get a session and to read from the dashboard. |
| `OperatePolicy` | `ViewPolicy` | Host policy a session must also pass for operator actions, the dashboard's state-changing requests. |
| `ChallengeScheme` | none | Host scheme `auth/sign-in` challenges when no host user is signed in, such as OpenID Connect. Without it the dashboard offers no Sign in link, and users get a session through the exchange. |
| `SessionCookieName` | `__Host-` and the dashboard name in lower case: `__Host-headless-jobs-dashboard`, `__Host-headless-messaging-dashboard` | Set it when two applications on one host name mount the same dashboard. It must be a cookie-name token. |
| `SessionIdleTimeout` | 30 minutes | A session with no authenticated dashboard request for this long ends. Must not exceed `SessionLifetime`. |
| `SessionLifetime` | 8 hours | Absolute lifetime from sign-in, whatever the activity. |
| `RevalidationInterval` | none | How often `Revalidate` runs for one session. Required when `Revalidate` is set. |
| `Revalidate` | none | `Func<HttpContext, ClaimsPrincipal, ValueTask<bool>>` that confirms the session's user is still allowed in; `false` ends the session. |

### Host sessions

- Each dashboard keeps its own session cookie: `Path=/`, `HttpOnly`, `Secure`, `SameSite=Strict`, and host-only. In host mode the dashboard API and the Jobs hub authenticate with that cookie alone. The host's own cookie or bearer token does not open them.
- A session is issued by `GET {base}/auth/sign-in?returnUrl=` or `POST {base}/api/auth/session`. Both copy the host user's authenticated identities and claims, with their name and role claim types, into the session. A user who fails the view policy gets `403` and no cookie. The host's bearer token is not copied.
- `auth/sign-in` follows only a local `returnUrl` under the dashboard's base path; any other value goes to `{base}/`. It challenges `ChallengeScheme` at most once per sign-in: when the user comes back still not signed in, it answers `401` instead of challenging again.
- `POST {base}/api/auth/sign-out` deletes the dashboard cookie only. The SPA then shows a signed-out page and does not sign in again until the operator chooses Sign in, even while the host session lasts. `HttpContext.SignOutHeadlessDashboardAsync(name)` deletes the same cookie from host code.
- The host user is found through the host's schemes, never the dashboard's. `auth/sign-in` authenticates the schemes `ViewPolicy` lists, when it lists any; otherwise `ChallengeScheme`, then the default authenticate scheme. The exchange authenticates the `ViewPolicy` schemes, then the default authenticate scheme.
- A session ends at `SessionLifetime` after sign-in, and after `SessionIdleTimeout` with no authenticated dashboard request. A request made after half the idle window has passed renews the cookie. The SPA's polling counts as activity, so an open Messaging tab stays signed in until the absolute lifetime; a Jobs tab that only listens to the hub expires at the idle timeout.
- A short-lived bearer token exchanged for a session becomes a session that can last up to `SessionLifetime`, after the token itself expires. A host that revokes tokens, disables accounts, or removes roles should set `Revalidate` and `RevalidationInterval`, and should call `SignOutHeadlessDashboardAsync` from its sign-out.
- Policies evaluate against the copied claims. A requirement that reads the request, a header, or the bearer token fails. A policy that lists `AuthenticationSchemes` fails closed: it passes only when one of those schemes authenticated the user the session was issued for.
- The session cookie is protected by the host's Data Protection key ring. Every instance serving the dashboard must share that key ring; `Headless.DataProtection.Blobs` persists it to blob storage (see [api.md](api.md#headlessdataprotectionblobs)).
- With `Path=/`, the browser sends the session cookie on every request to the host, not only dashboard requests. It carries the copied claims, so a principal with very many claims can make request headers too large.
- `GET {base}/api/auth/info` is anonymous. It reports `mode`, `enabled`, `signInAvailable`, and, in host mode, the current session's `authenticated`, `name`, and `canOperate`. It never exposes credentials. The SPA decides whether to show the sign-in page from this call, because the `SameSite=Strict` cookie can be missing from the first page load after an identity-provider redirect.
- The SPA hides write actions when `canOperate` is `false`. The server refuses them with `403` either way.

### Design and runtime behavior

- Credential comparisons are constant-time.
- Authentication is per dashboard. `AddDashboardAuthentication(name, …)` configures the `AuthConfig` options instance named `name` and registers a scoped `IAuthService` keyed by `name`; the dashboard's authentication scheme, the `/api/auth/info` and `/api/auth/validate` endpoints, and the Jobs hub resolve the service by that key. Nothing registers an unkeyed `IAuthService` or a raw `AuthConfig` singleton.
- `RequireDashboardAuthentication(name)` reads the named `AuthConfig` when the host builds its endpoints. `None` allows anonymous access. `Host` requires `{name}.View`: a dashboard session whose user passes `ViewPolicy`, or the host's default policy when it is empty, answering `401` without a session and `403` when the policy refuses. Basic and API key require a user the dashboard's scheme signed in, and refuse any other request with an empty `401`. The scheme checks a credential only on an endpoint that convention marked with its name, so a host whose only registered scheme is a dashboard's (which ASP.NET Core then makes the default) does not check or log a dashboard credential on its own requests.
- `RequireDashboardOperation(name)` marks operator actions. In host mode it adds `{name}.Operate`, which requires `OperatePolicy`, or `ViewPolicy` when it is empty. Other modes add nothing: Basic and API key share one credential for viewing and operating.
- On success in Basic or API key mode, `HttpContext.User` for that request is the dashboard identity: one `Name` claim with the `AuthResult.Username`, authentication type `name`. It replaces a host sign-in on that request, so a Messaging operator action records the dashboard credential's user. In host mode `HttpContext.User` is the session's copy of the host user.
- The Jobs dashboard's name is `DashboardOptionsBuilder.AuthenticationName` (`"Headless.Jobs.Dashboard"`) and the Messaging dashboard's is `MessagingDashboardOptionsBuilder.AuthenticationName` (`"Headless.Messaging.Dashboard"`). To replace one dashboard's `IAuthService`, register yours with `AddKeyedScoped<IAuthService, TService>(name)` before the dashboard registers; the registration keeps an existing keyed service.
- In Basic and API-key modes, the `access_token` query parameter counts as a credential only on a request routed to a SignalR hub endpoint (its routing metadata, not its path, decides). Every other request must send the `Authorization` header. Host mode accepts no query credential; see [Jobs live-update hub](#jobs-live-update-hub).
- Every state-changing request to either dashboard (`POST`, `PUT`, `PATCH`, `DELETE`) must carry an `X-Requested-With` header, or it gets `400`. That covers both APIs, `api/auth/session`, and `api/auth/sign-out`, in every mode. A cross-site `<form>` cannot set the header, and a cross-site script that sets it needs a CORS preflight the dashboard refuses.
- Each dashboard maps a `{base path}` group: its `/api` group and the Jobs hub carry the dashboard's rule; `/auth/sign-in`, `/api/auth/info`, `/api/auth/session`, `/api/auth/sign-out`, `/api/auth/validate`, Messaging's `/api/health`, the SPA shell, and its assets are anonymous. `auth/sign-in`, `api/auth/session`, and `api/auth/sign-out` answer `404` outside host mode. An unmatched `{base path}/api/...` path is a `404`, never the shell, so no path under `/api/` is exempt because of its extension or a segment such as `/assets/` or `/negotiate`. Any other unmatched `GET` under the base path returns the shell, so client routes deep-link. Both map methods refuse the root base path, where the shell would answer every unmatched `GET` in the host.
- The host pipeline must run `UseAuthorization` between routing and endpoints (a `WebApplication` adds it when the host does not), and `UseAuthentication` before it for host authentication. A host that calls `UseAuthentication()` must also call `UseAuthorization()` after it: the `UseAuthorization` a `WebApplication` adds on its own runs ahead of the host's `UseAuthentication`, so host authentication would not have signed the user in yet. The dashboards attach CORS metadata only when `SetCorsOrigins` or `SetCorsPolicy` configured a policy, which only Basic and API key allow; then the host must also run `UseCors()`. Add host-specific behavior, such as rate limiting, to the builder the map method returns.
- The SPA shell carries only the base path, the mode, whether auth is enabled, whether sign-in is available, and, for Messaging, the polling interval. Host mode stores nothing in the browser beyond the HttpOnly session cookie; Basic and API key keep the entered credential in the SPA's browser storage. The browser does not enforce a session timeout: the server ends sessions.
- In host mode the Messaging gateway proxy does not relay requests to other nodes: it serves each request from the node that received it, and clears the node-selection cookies. The SPA hides node switching. Basic and API key relay as before, because the proxy forwards the `Authorization` header and never the `Cookie` header.

### Jobs live-update hub

The Jobs dashboard pushes job status, progress, and node changes over a SignalR hub at `{base path}/job-notification-hub`. The SPA connects over WebSockets only. The hub carries the API's view rule, so the host's `UseAuthorization` refuses an unauthorized negotiate or connect request before SignalR creates a connection: an unauthenticated client never joins the hub and hears no broadcast. When auth is on, the SPA opens the hub only after sign-in and closes it on sign-out.

| Mode | Credential the SPA sends | How the server checks it |
| --- | --- | --- |
| None | nothing | Allowed. |
| Basic | The Base64 `user:password` credential as the `access_token` query parameter | Compared with the configured credential. |
| API key | `Bearer:<key>` as `access_token` (a raw key is also accepted) | Compared with the configured key. |
| Host | The dashboard session cookie; no query token | The session must pass the view policy. A present `Origin` header that names another origin gets `403`. |

In host mode the hub closes an open connection when the session ticket it connected with expires. The SPA then reconnects with the current cookie, or returns to the sign-in page when reconnecting answers `401`. The `Origin` check matters because a sibling subdomain on the same site still receives a `SameSite=Strict` cookie on a WebSocket handshake.
