---
domain: Dashboards
packages:
---

# Dashboards a host writes

> Part of [Dashboards](../dashboards.md). Protect a dashboard the host serves outside the shell with the per-dashboard authentication primitives the shell is built on.

A dashboard a host writes itself, outside the shell, authenticates through these primitives: the keyed `IAuthService`, the authentication scheme and policy that check a dashboard's own credential, the host-mode session, its view and operate policies, the sign-in endpoints, and the endpoint conventions that apply them. Host mode follows the same [host authentication options](../dashboards.md#host-authentication-options) and [host session rules](../dashboards.md#host-sessions) as the shell.

- Mount the dashboard on the host's endpoints, so the host's `UseAuthorization` evaluates the dashboard's own rule and never the host's fallback policy. A host fallback policy that requires a tenant claim, for example, does not lock out a tenantless operator.
- Each dashboard owns its authentication. A host that runs the shell and its own dashboard can give them different modes and credentials; neither registration overrides the other.

## Setup

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

**Verify:** anonymous `GET /reports/api/auth/info` answers `200` with the configured `mode`. A request to `/reports/api/runs` without a session answers `401`. Incomplete credentials or invalid host options fail the start with `OptionsValidationException`.

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

## Configuration

`AuthConfig` exposes `Mode` (`AuthMode.None`, `Basic`, `ApiKey`, or `Host`), `BasicCredentials`, `ApiKey`, and `Host`. Registration validates each named configuration's selected mode at startup; incomplete credentials or invalid host options fail with `OptionsValidationException`. `AuthConfig.Host` is a `DashboardHostAuthenticationOptions`, read only in host mode; its members are in [Host authentication options](../dashboards.md#host-authentication-options). The default `SessionCookieName` of a dashboard a host writes is `__Host-` and the dashboard name in lower case, such as `__Host-reports.dashboard`.

## Runtime behavior

- Credential comparisons are constant-time.
- Authentication is per dashboard. `AddDashboardAuthentication(name, …)` configures the `AuthConfig` options instance named `name` and registers a scoped `IAuthService` keyed by `name`; the dashboard's authentication scheme and its `api/auth/info` endpoint resolve the service by that key. Nothing registers an unkeyed `IAuthService` or a raw `AuthConfig` singleton. To replace a dashboard's `IAuthService`, register yours with `AddKeyedScoped<IAuthService, TService>(name)` before the dashboard registers; the registration keeps an existing keyed service.
- `RequireDashboardAuthentication(name)` reads the named `AuthConfig` when the host builds its endpoints. `None` allows anonymous access. `Host` requires `{name}.View`: a dashboard session whose user passes `ViewPolicy`, or the host's default policy when it is empty, answering `401` without a session and `403` when the policy refuses. Basic and API key require a user the dashboard's scheme signed in, and refuse any other request with an empty `401`. The scheme checks a credential only on an endpoint that convention marked with its name, so a host whose only registered scheme is a dashboard's (which ASP.NET Core then makes the default) does not check or log a dashboard credential on its own requests.
- `RequireDashboardOperation(name)` marks operator actions. In host mode it adds `{name}.Operate`, which requires `OperatePolicy`, or `ViewPolicy` when it is empty. Other modes add nothing: Basic and API key share one credential for viewing and operating.
- On success in Basic or API key mode, `HttpContext.User` for that request is the dashboard identity: one `Name` claim with the `AuthResult.Username`, authentication type `name`. It replaces a host sign-in on that request. In host mode `HttpContext.User` is the session's copy of the host user.
- In Basic and API-key modes, the `access_token` query parameter counts as a credential only on a request routed to a SignalR hub endpoint (its routing metadata, not its path, decides), because a browser cannot set headers on a WebSocket. Every other request must send the `Authorization` header. Host mode accepts no query credential.
- Every state-changing request to such a dashboard (`POST`, `PUT`, `PATCH`, `DELETE`) must carry an `X-Requested-With` header, or it gets `400`. That covers its API, `api/auth/session`, and `api/auth/sign-out`, in every mode. A cross-site `<form>` cannot set the header, and a cross-site script that sets it needs a CORS preflight the dashboard refuses.
- `auth/sign-in`, `api/auth/info`, `api/auth/session`, and `api/auth/sign-out` are anonymous; `auth/sign-in`, `api/auth/session`, and `api/auth/sign-out` answer `404` outside host mode.
- The host pipeline must run `UseAuthorization` between routing and endpoints (a `WebApplication` adds it when the host does not), and `UseAuthentication` before it for host authentication. A host that calls `UseAuthentication()` must also call `UseAuthorization()` after it: the `UseAuthorization` a `WebApplication` adds on its own runs ahead of the host's `UseAuthentication`, so host authentication would not have signed the user in yet.
