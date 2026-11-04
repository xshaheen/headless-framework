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

- Treat every dashboard as an operations surface. Require host authentication, Basic authentication, API-key authentication, or a custom validator outside isolated local development.
- Prefer host authentication when the application already has an authorization policy. It keeps identity, audit, and revocation in the host's security boundary.
- Never embed production credentials or API keys in source, examples, logs, or frontend configuration.
- `AuthMode.None` makes the dashboard public. Use it only in an isolated development environment.
- Configure authentication through the owning dashboard builder (`UseDashboard(...)` or `AddDashboard(...)`), then mount the dashboard on the host's endpoints with `MapHeadlessMessagingDashboard()` or `MapHeadlessJobsDashboard()`. That call is the only mount: a host that registers a dashboard and never maps it fails to start.
- Mount on the host's endpoints, so the host's `UseAuthorization` evaluates the dashboard's own rule and never the host's fallback policy. A host fallback policy that requires a tenant claim, for example, does not lock out a tenantless operator.
- Each dashboard owns its authentication. A host that runs the Jobs and Messaging dashboards can give them different modes and credentials; neither registration overrides the other.

## Choosing an authentication mode

| Mode | Use when | Trade-off |
| --- | --- | --- |
| Host | The application already authenticates users | Reuses a host authorization policy; preferred for production |
| Basic | A small standalone deployment needs simple credentials | Application owns secret storage and rotation |
| API key | Automation or a controlled operator client needs bearer access | Application owns key distribution and rotation |
| Custom | Existing identity cannot be expressed as a host policy | Application owns validator correctness and auditability |
| None | Isolated local development only | No access control |

## Headless.Dashboard.Authentication

Shared authentication primitives used by both dashboard packages: the per-dashboard `IAuthService`, the authentication scheme and policy that check a dashboard's own credential, and the endpoint convention that applies them.

### Setup

Install an owning dashboard package and configure its builder:

```csharp
builder.Services.AddAuthorizationBuilder().AddPolicy(
    "DashboardPolicy",
    policy => policy.RequireAuthenticatedUser()
);

builder.Services.AddHeadlessMessaging(setup =>
{
    // plus exactly one transport and one storage provider; see messaging.md
    setup.UseDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication("DashboardPolicy");
        dashboard.WithSessionTimeout(30);
    });
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHeadlessMessagingDashboard();
```

For a standalone integration:

```bash
dotnet add package Headless.Dashboard.Authentication
```

Register the authentication under a name your dashboard owns, and protect the dashboard's endpoints with the same name:

```csharp
builder.Services.AddDashboardAuthentication("Reports.Dashboard", config =>
{
    config.Mode = AuthMode.ApiKey;
    config.ApiKey = builder.Configuration["Dashboard:ApiKey"];
});

var app = builder.Build();

var reports = app.MapGroup("/reports");
// The sign-in flow calls these before it has a credential.
reports.MapGet("/api/auth/info", ReportsEndpoints.GetAuthInfo).AllowAnonymous();
// Every other endpoint requires the "Reports.Dashboard" credential; the host's UseAuthorization checks it.
reports.MapGroup("/api").RequireDashboardAuthentication("Reports.Dashboard").MapGet("/runs", ReportsEndpoints.GetRuns);
```

`AddDashboardAuthentication(name, IConfiguration)` and `AddDashboardAuthentication(name, Action<AuthConfig, IServiceProvider>)` take the same name. A blank name throws `ArgumentException`. Each registration adds an authentication scheme and an authorization policy, both named `name`; registering one name twice adds them once.

### Configuration

`AuthConfig` exposes `Mode`, `BasicCredentials`, `ApiKey`, `CustomValidator`, `SessionTimeoutMinutes`, and `HostAuthorizationPolicy`. Registration validates each named configuration's selected mode at startup; incomplete credentials fail with `OptionsValidationException`.

The dashboard builders expose `WithNoAuth()`, `WithBasicAuth(...)`, `WithApiKey(...)`, `WithHostAuthentication(...)`, `WithCustomAuth(...)`, and `WithSessionTimeout(...)`.

### Design and runtime behavior

- Credential comparisons are constant-time.
- Authentication is per dashboard. `AddDashboardAuthentication(name, …)` configures the `AuthConfig` options instance named `name` and registers a scoped `IAuthService` keyed by `name`; the dashboard's authentication scheme, the `/api/auth/info` and `/api/auth/validate` endpoints, and the Jobs hub resolve the service by that key. Nothing registers an unkeyed `IAuthService` or a raw `AuthConfig` singleton.
- `RequireDashboardAuthentication(name)` reads the named `AuthConfig` when the host builds its endpoints. `None` allows anonymous access. `Host` requires `HostAuthorizationPolicy`, or the host's default policy when it is empty. Basic, API key, and Custom require a user the dashboard's scheme signed in, and refuse any other request with an empty `401`. The scheme checks a credential only on an endpoint that convention marked with its name, so a host whose only registered scheme is a dashboard's (which ASP.NET Core then makes the default) does not check or log a dashboard credential on its own requests.
- On success in Basic, API key, or Custom mode, `HttpContext.User` for that request is the dashboard identity: one `Name` claim with the `AuthResult.Username`, authentication type `name`. It replaces a host sign-in on that request, so a Messaging operator action records the dashboard credential's user.
- The Jobs dashboard's name is `DashboardOptionsBuilder.AuthenticationName` (`"Headless.Jobs.Dashboard"`) and the Messaging dashboard's is `MessagingDashboardOptionsBuilder.AuthenticationName` (`"Headless.Messaging.Dashboard"`). To replace one dashboard's `IAuthService`, register yours with `AddKeyedScoped<IAuthService, TService>(name)` before the dashboard registers; the registration keeps an existing keyed service.
- In Basic, API-key, and Custom modes, the `access_token` query parameter counts as a credential only on a request routed to a SignalR hub endpoint (its routing metadata, not its path, decides). Every other request must send the `Authorization` header. Host mode differs; see [Jobs live-update hub](#jobs-live-update-hub).
- Each dashboard maps a `{base path}` group: its `/api` group and the Jobs hub carry the dashboard's rule; `/api/auth/info`, `/api/auth/validate`, Messaging's `/api/health`, the SPA shell, and its assets are anonymous. An unmatched `{base path}/api/...` path is a `404`, never the shell, so no path under `/api/` is exempt because of its extension or a segment such as `/assets/` or `/negotiate`. Any other unmatched `GET` under the base path returns the shell, so client routes deep-link. Both map methods refuse the root base path, where the shell would answer every unmatched `GET` in the host.
- The host pipeline must run `UseAuthorization` between routing and endpoints (a `WebApplication` adds it when the host does not), and `UseAuthentication` before it for host authentication. The dashboards attach CORS metadata only when `SetCorsOrigins` or `SetCorsPolicy` configured a policy; then the host must also run `UseCors()`. Add host-specific behavior, such as rate limiting, to the builder the map method returns.
- `AuthInfo` exposes mode, enabled state, and session timeout to the frontend; it never exposes credentials.
- Both dashboard SPAs enforce `WithSessionTimeout(minutes)` in the browser: the sign-in time is stored with the credentials, and once it is older than the timeout the SPA clears them and returns to the login page with "Session expired. Please log in again." It checks at load, on every navigation, and before every API request. Mode `None` and a non-positive timeout never expire. The timeout is a client-side convenience, not a server credential lifetime: a credential copied out of the browser stays valid until the host changes it.
- With `WithCustomAuth(...)`, the login page asks for one credential and sends it unchanged as the `Authorization` header (and as the Jobs hub's `access_token`); the validator receives that header value.

### Jobs live-update hub

The Jobs dashboard pushes job status, progress, and node changes over a SignalR hub at `{base path}/job-notification-hub`. The SPA connects over WebSockets only, and a browser cannot set headers on a WebSocket, so it sends the signed-in credential as the `access_token` query parameter. The hub carries the API's rule, so the host's `UseAuthorization` refuses an unauthorized negotiate or connect request with `401` before SignalR creates a connection: an unauthenticated client never joins the hub and hears no broadcast. The hub checks the connection again when it opens. A refused client sees changes only after a reload. When auth is on, the SPA opens the hub only after sign-in, with the credential just stored, and closes it on sign-out or session timeout.

| Mode | `access_token` the SPA sends | How the server checks it |
| --- | --- | --- |
| None | nothing | Allowed. |
| Basic | The Base64 `user:password` credential | Compared with the configured credential. |
| API key | `Bearer:<key>` (a raw key is also accepted) | Compared with the configured key. |
| Custom | The credential, unchanged | Passed to the custom validator. |
| Host | The host access key from the login page, if any | Copied into the `Authorization` header before the host's authentication runs. A cookie sign-in needs no token. |

Under host authentication the hub endpoint carries the same authorization policy as the dashboard API (`WithHostAuthentication(policy)`, or the default policy): a host user the policy refuses gets no live updates either. The copy runs before routing, so it matches by path: only a request whose path ends with the hub path or its negotiate path (a host path base in front is allowed), that has no `Authorization` header, and whose token has no control characters. Any other request with `access_token` is left as it is, so the dashboard API never accepts a query credential.
