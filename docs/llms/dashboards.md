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

```csharp
builder.Services.AddDashboardAuthentication(config =>
{
    config.Mode = AuthMode.ApiKey;
    config.ApiKey = builder.Configuration["Dashboard:ApiKey"];
});
```

### Configuration

`AuthConfig` exposes `Mode`, `BasicCredentials`, `ApiKey`, `CustomValidator`, `SessionTimeoutMinutes`, and `HostAuthorizationPolicy`. Registration validates the selected mode at startup; incomplete credentials fail with `OptionsValidationException`.

The dashboard builders expose `WithNoAuth()`, `WithBasicAuth(...)`, `WithApiKey(...)`, `WithHostAuthentication(...)`, `WithCustomAuth(...)`, and `WithSessionTimeout(...)`.

### Design and runtime behavior

- Credential comparisons are constant-time.
- `IAuthService` is scoped. `AuthConfig` and validated options are singleton configuration.
- The middleware protects dashboard API paths while allowing the static application shell and authentication metadata needed to render the login flow.
- `AuthInfo` exposes mode, enabled state, and session timeout to the frontend; it never exposes credentials.
