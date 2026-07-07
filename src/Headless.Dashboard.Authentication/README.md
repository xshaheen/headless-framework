# Headless.Dashboard.Authentication

Shared authentication package for Headless dashboards (Jobs and Messaging).

## Problem Solved

Provides a unified authentication system with 5 modes so both Jobs and Messaging dashboards share the same auth model and login UI without duplicating code.

## Key Features

- **5 Auth Modes**: None, Basic, API Key, Host, Custom
- **Constant-Time Comparison**: Prevents timing attacks on credentials
- **Auth Middleware**: Protects API endpoints while allowing static file access
- **Frontend Config**: Provides auth info to frontend for adaptive login UI

## Installation

```bash
dotnet add package Headless.Dashboard.Authentication
```

Most applications get this package transitively through `Headless.Jobs.Dashboard` or `Headless.Messaging.Dashboard`. Add it directly only when you are building dashboard infrastructure that consumes the shared auth primitives.

## Quick Start

Use the auth modes through the dashboard package builders:

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseDashboard(dashboard =>
    {
        dashboard.WithBasicAuth("admin", "secret");
        dashboard.WithSessionTimeout(30);
    });
});
```

For host-owned authentication:

```csharp
builder.Services.AddAuthorizationBuilder().AddPolicy(
    "DashboardPolicy",
    policy => policy.RequireAuthenticatedUser()
);

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseDashboard(dashboard => dashboard.WithHostAuthentication("DashboardPolicy"));
});
```

## Configuration

`AuthConfig` exposes `Mode`, `BasicCredentials`, `ApiKey`, `CustomValidator`, `SessionTimeoutMinutes`, and `HostAuthorizationPolicy`. The dashboard builders set those values through methods such as `WithNoAuth()`, `WithBasicAuth(...)`, `WithApiKey(...)`, `WithHostAuthentication(...)`, `WithCustomAuth(...)`, and `WithSessionTimeout(...)`.

## Auth Modes

| Mode | Description |
|------|-------------|
| `None` | No authentication — public dashboard |
| `Basic` | Username/password (Base64 encoded) |
| `ApiKey` | Bearer token authentication |
| `Host` | Delegates to host app's authentication/authorization |
| `Custom` | Custom validation function |

`AuthConfig` controls credentials, API key, custom validation, host authorization policy, and `SessionTimeoutMinutes`. `AuthConfig.Validate()` throws when the selected mode requires a credential or validator that has not been configured.

- `AuthMode` — enum of supported modes
- `AuthConfig` — configuration (credentials, API key, validator, session timeout)
- `IAuthService` / `AuthService` — authenticate requests, return `AuthResult`
- `AuthMiddleware` — protects `/api/*` paths, skips static files
- `AuthResult` — success/failure with username
- `AuthInfo` — mode/enabled/timeout for frontend

## Dependencies

- `Microsoft.AspNetCore.App` (framework reference)

## Used By

- `Headless.Jobs.Dashboard`
- `Headless.Messaging.Dashboard`

## Side Effects

- No services are registered when this package is referenced by itself.
- Owning dashboard packages register `AuthConfig`, `IAuthService`, and `AuthMiddleware` when dashboard authentication is enabled.
- `AuthMiddleware` protects `/api/*` dashboard endpoints while allowing static files, SignalR negotiate endpoints, and auth metadata endpoints through.
