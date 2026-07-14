# Headless.Dashboard.Authentication

Shared authentication package for Headless dashboards (Jobs and Messaging).

## Problem Solved

Provides a unified authentication system with 5 modes so both Jobs and Messaging dashboards share the same auth model and login UI without duplicating code.

## Key Features

- **5 Auth Modes**: None, Basic, API Key, Host, Custom
- **Constant-Time Comparison**: Prevents timing attacks on credentials
- **Auth Middleware**: Protects API endpoints while allowing static file access
- **Frontend Config**: Provides auth info to frontend for adaptive login UI
- **DI Registration**: `SetupDashboardAuthentication.AddDashboardAuthentication(...)` binds `AuthConfig` (validated on start via a FluentValidation validator) and registers `IAuthService`

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

## Registration

`AddDashboardAuthentication` exposes the standard overload trio and registers `AuthConfig` (with validation on start), the resolved `AuthConfig` value, and a scoped `IAuthService`:

```csharp
// Bind from configuration
builder.Services.AddDashboardAuthentication(builder.Configuration.GetSection("Dashboard:Auth"));

// Or configure inline
builder.Services.AddDashboardAuthentication(cfg =>
{
    cfg.Mode = AuthMode.ApiKey;
    cfg.ApiKey = "secret";
});
```

`AuthConfig` controls credentials, API key, custom validation, host authorization policy, and `SessionTimeoutMinutes`. `AuthConfig.Validate()` throws when the selected mode requires a credential or validator that has not been configured.

Add the middleware to protect `/api/*` paths:

```csharp
app.UseMiddleware<AuthMiddleware>();
```

## Types

- `AuthMode` — enum of supported modes
- `AuthConfig` — configuration (credentials, API key, validator, session timeout); its imperative `Validate()` is mirrored by an internal FluentValidation validator wired into the options pipeline
- `IAuthService` / `AuthService` — authenticate requests, return `AuthResult`; `AuthenticateAsync(HttpContext, CancellationToken)` accepts an optional cancellation token
- `AuthMiddleware` — protects `/api/*` paths, skips static files; passes `HttpContext.RequestAborted` to `AuthenticateAsync`
- `AuthResult` — success/failure with username
- `AuthInfo` — mode/enabled/timeout for frontend
- `SetupDashboardAuthentication` — `AddDashboardAuthentication` registration extensions

## Dependencies

- `Microsoft.AspNetCore.App` (framework reference)
- `Headless.Checks`
- `Headless.Extensions`
- `Headless.Hosting`

## Side Effects

`AddDashboardAuthentication` registers `IOptions<AuthConfig>` (validated on start), a singleton `AuthConfig` resolved from those options, and a scoped `IAuthService` (`AuthService`).

## Used By

- `Headless.Jobs.Dashboard`
- `Headless.Messaging.Dashboard`

## Side Effects

- No services are registered when this package is referenced by itself.
- Owning dashboard packages register `AuthConfig`, `IAuthService`, and `AuthMiddleware` when dashboard authentication is enabled.
- `AuthMiddleware` protects `/api/*` dashboard endpoints while allowing static files, SignalR negotiate endpoints, and auth metadata endpoints through.
