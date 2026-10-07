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
- Configure authentication through `UseDashboard(...)`; do not add `AuthMiddleware` manually when the owning dashboard package already wires it.
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

Shared authentication primitives and middleware used by both dashboard packages.

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
```

For a standalone integration:

```bash
dotnet add package Headless.Dashboard.Authentication
```

Register the authentication under a name your dashboard owns, and pass the same name to the middleware:

```csharp
builder.Services.AddDashboardAuthentication("Reports.Dashboard", config =>
{
    config.Mode = AuthMode.ApiKey;
    config.ApiKey = builder.Configuration["Dashboard:ApiKey"];
});

var app = builder.Build();

app.Map("/reports", dashboardApp =>
{
    dashboardApp.UseRouting();
    // Protects the branch's /api/* requests with the "Reports.Dashboard" configuration.
    dashboardApp.UseMiddleware<AuthMiddleware>("Reports.Dashboard");
    dashboardApp.UseEndpoints(endpoints => { /* the dashboard's endpoints */ });
});
```

`AddDashboardAuthentication(name, IConfiguration)` and `AddDashboardAuthentication(name, Action<AuthConfig, IServiceProvider>)` take the same name. A blank name throws `ArgumentException`.

### Configuration

`AuthConfig` exposes `Mode`, `BasicCredentials`, `ApiKey`, `CustomValidator`, `SessionTimeoutMinutes`, and `HostAuthorizationPolicy`. Registration validates each named configuration's selected mode at startup; incomplete credentials fail with `OptionsValidationException`.

The dashboard builders expose `WithNoAuth()`, `WithBasicAuth(...)`, `WithApiKey(...)`, `WithHostAuthentication(...)`, `WithCustomAuth(...)`, and `WithSessionTimeout(...)`.

### Design and runtime behavior

- Credential comparisons are constant-time.
- Authentication is per dashboard. `AddDashboardAuthentication(name, …)` configures the `AuthConfig` options instance named `name` and registers a scoped `IAuthService` keyed by `name`; `AuthMiddleware`, the `/api/auth/info` and `/api/auth/validate` endpoints, and the Jobs hub resolve the service by that key. Nothing registers an unkeyed `IAuthService` or a raw `AuthConfig` singleton.
- The Jobs dashboard's name is `DashboardOptionsBuilder.AuthenticationName` (`"Headless.Jobs.Dashboard"`) and the Messaging dashboard's is `MessagingDashboardOptionsBuilder.AuthenticationName` (`"Headless.Messaging.Dashboard"`). To replace one dashboard's `IAuthService`, register yours with `AddKeyedScoped<IAuthService, TService>(name)` before the dashboard registers; the registration keeps an existing keyed service.
- In Basic, API-key, and Custom modes, the `access_token` query parameter counts as a credential only on a request routed to a SignalR hub endpoint (its routing metadata, not its path, decides). Every other request must send the `Authorization` header. Host mode differs; see [Jobs live-update hub](#jobs-live-update-hub).
- The middleware protects dashboard API paths while allowing the static application shell and authentication metadata needed to render the login flow.
- `AuthInfo` exposes mode, enabled state, and session timeout to the frontend; it never exposes credentials.
- Both dashboard SPAs enforce `WithSessionTimeout(minutes)` in the browser: the sign-in time is stored with the credentials, and once it is older than the timeout the SPA clears them and returns to the login page with "Session expired. Please log in again." It checks at load, on every navigation, and before every API request. Mode `None` and a non-positive timeout never expire. The timeout is a client-side convenience, not a server credential lifetime: a credential copied out of the browser stays valid until the host changes it.
- With `WithCustomAuth(...)`, the login page asks for one credential and sends it unchanged as the `Authorization` header (and as the Jobs hub's `access_token`); the validator receives that header value.

### Jobs live-update hub

The Jobs dashboard pushes job status, progress, and node changes over a SignalR hub at `{base path}/job-notification-hub`. The SPA connects over WebSockets only, and a browser cannot set headers on a WebSocket, so it sends the signed-in credential as the `access_token` query parameter. The hub authenticates with the Jobs dashboard's own auth service and closes a connection that fails; the dashboard then shows changes only after a reload.

| Mode | `access_token` the SPA sends | How the server checks it |
| --- | --- | --- |
| None | nothing | Allowed. |
| Basic | The Base64 `user:password` credential | Compared with the configured credential. |
| API key | `Bearer:<key>` (a raw key is also accepted) | Compared with the configured key. |
| Custom | The credential, unchanged | Passed to the custom validator. |
| Host | The host access key from the login page, if any | Copied into the `Authorization` header before the host's authentication runs. A cookie sign-in needs no token. |

Under host authentication the hub endpoint carries the same authorization policy as the dashboard API (`WithHostAuthentication(policy)`, or the default policy): a host user the policy refuses gets no live updates either. The copy runs before routing, so it matches by path: only a request whose path ends with the hub path or its negotiate path (a host path base in front is allowed), that has no `Authorization` header, and whose token has no control characters. Any other request with `access_token` is left as it is, so the dashboard API never accepts a query credential.
