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

Most applications get this package transitively through `Headless.Jobs.Dashboard` or `Headless.Messaging.Dashboard`.

## Quick Start

Configure authentication through the owning dashboard package:

```csharp
builder.Services.AddDashboard(dashboard =>
{
    dashboard.WithBasicAuth("admin", "secret");
});

builder.Services.AddHeadlessMessaging(messaging =>
{
    messaging.UseDashboard(dashboard => dashboard.WithApiKey("dashboard-api-key"));
});
```

Custom dashboard hosts can register the shared primitives directly:

```csharp
services.AddSingleton(new AuthConfig { Mode = AuthMode.None });
services.AddSingleton<IAuthService, AuthService>();

app.UseMiddleware<AuthMiddleware>();
```

## Configuration

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

## Side Effects

- No services are registered by this package on its own.
- Owning dashboard packages register `AuthConfig`, `IAuthService`, and `AuthMiddleware` when dashboard authentication is enabled.
- `AuthMiddleware` protects `/api/*` dashboard endpoints while allowing static files, SignalR negotiate endpoints, and auth metadata endpoints through.
