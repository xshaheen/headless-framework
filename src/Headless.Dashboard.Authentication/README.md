# Headless.Dashboard.Authentication

Shared authentication package for Headless dashboards (Jobs and Messaging).

## Problem Solved

Provides a unified authentication system with 5 modes so both Jobs and Messaging dashboards share the same auth model and login UI without duplicating code.

## Key Features

- **5 Auth Modes**: None, Basic, API Key, Host, Custom
- **Constant-Time Comparison**: Prevents timing attacks on credentials
- **Auth Middleware**: Protects API endpoints while allowing static file access
- **Frontend Config**: Provides auth info to frontend for adaptive login UI

## Auth Modes

| Mode | Description |
|------|-------------|
| `None` | No authentication — public dashboard |
| `Basic` | Username/password (Base64 encoded) |
| `ApiKey` | Bearer token authentication |
| `Host` | Delegates to host app's authentication/authorization |
| `Custom` | Custom validation function |

## Types

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
