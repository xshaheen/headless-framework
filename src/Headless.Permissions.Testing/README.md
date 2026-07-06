# Headless.Permissions.Testing

Test-only doubles that bypass all permission and authorization checks.

## Problem Solved

Integration tests often need to exercise endpoints without wiring up real grants. This package supplies always-allow replacements for `IPermissionManager` and `IAuthorizationService`, kept out of `Headless.Permissions.Core` so the production surface never ships an authorization bypass.

## Key Features

- `services.AddAlwaysAllowAuthorization()` — replaces `IPermissionManager` with `AlwaysAllowPermissionManager` and `IAuthorizationService` with `AlwaysAllowAuthorizationService`, granting every permission and authorizing every request
- `AlwaysAllowPermissionManager` — `IPermissionManager` that reports every permission as granted; `SetAsync` / `DeleteAsync` are no-ops
- `AlwaysAllowAuthorizationService` — `IAuthorizationService` that returns `AuthorizationResult.Success()` for every call

## Installation

```bash
dotnet add package Headless.Permissions.Testing
```

## Quick Start

```csharp
// In an integration-test host builder, after AddHeadlessPermissions:
builder.Services.AddAlwaysAllowAuthorization();
```

## Configuration

None.

## Dependencies

- `Headless.Permissions.Core`
- `Headless.Hosting`
- `Microsoft.AspNetCore.Authorization`

## Side Effects

- Replaces the registered `IPermissionManager` with `AlwaysAllowPermissionManager` (singleton)
- Replaces the registered `IAuthorizationService` with `AlwaysAllowAuthorizationService` (singleton)
