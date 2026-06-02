# Headless.Permissions.Core

Core implementation of permission management with caching, providers, and authorization.

## Problem Solved

Provides the full permission management implementation including hierarchical grant resolution (User > Role > Store), caching, background initialization, and ASP.NET Core authorization integration.

## Key Features

- `PermissionManager` - Full implementation of `IPermissionManager`
- Grant providers: User, Role, Store
- Static and dynamic permission definition stores
- Permission grant caching with invalidation
- Background service for permission initialization
- ASP.NET Core authorization requirements
- `AlwaysAllowPermissionManager` for testing

## Installation

```bash
dotnet add package Headless.Permissions.Core
dotnet add package Headless.Permissions.Storage.EntityFramework # or PostgreSql / SqlServer
```

## Quick Start

`AddHeadlessPermissions(...)` registers the management core automatically, so a storage
provider is all you need. Register the required services (`TimeProvider`, `ICache`,
`IDistributedLock`, `IGuidGenerator`) first, then call `AddHeadlessPermissions`.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Requires: TimeProvider, ICache, IDistributedLock, IGuidGenerator

// Register permission definition providers
builder.Services.AddPermissionDefinitionProvider<OrderPermissionProvider>();

// Add management core + storage in one call (Entity Framework shown)
builder.Services.AddHeadlessPermissions(setup => setup.UseEntityFramework<AppDbContext>());
```

### Authorization Requirement

```csharp
[Authorize]
[HasPermission("Orders.Edit")]
public async Task<IActionResult> EditOrder(Guid id) { }
```

## Permission Resolution

Permission resolution follows AWS IAM-style rules:

1. **Explicit Deny Overrides All Grants** - If ANY provider returns `Prohibited`, permission is denied regardless of other grants
2. **Grant if No Denials** - Permission granted if at least one provider grants and no provider denies
3. **Default Deny** - If no provider grants permission, access is denied

### Resolution Examples

```csharp
// Scenario 1: Explicit denial overrides grant
await permissionManager.GrantToUserAsync("Orders.Delete", userId); // User granted
await permissionManager.RevokeFromRoleAsync("Orders.Delete", role); // Role denied

var result = await permissionManager.GetAsync("Orders.Delete", currentUser);
// result.IsGranted = false (explicit deny overrides)
```

```csharp
// Scenario 2: Multiple grants
await permissionManager.GrantToRoleAsync("Orders.View", role);
await permissionManager.GrantToUserAsync("Orders.View", userId);

var result = await permissionManager.GetAsync("Orders.View", currentUser);
// result.IsGranted = true
// result.Providers contains both Role and User providers
```

### Permission States

- **Granted** - Record exists with `IsGranted = true`
- **Prohibited** - Record exists with `IsGranted = false` (explicit denial)
- **Undefined** - No record exists (default deny)

## Configuration

### Options

Tune the management options through `setup.ConfigureManagement(...)` inside the
`AddHeadlessPermissions` block, next to `ConfigureStorage`:

```csharp
services.AddHeadlessPermissions(setup =>
{
    setup.ConfigureManagement(options =>
    {
        options.CrossApplicationsCommonLockKey = "permissions:common_update_lock";
    });
    setup.UseEntityFramework<AppDbContext>();
});
```

The `(options, IServiceProvider)` overload is available when configuration needs resolved
services. `services.Configure<PermissionManagementOptions>(...)` also works and composes with
the auto-registration regardless of order.

## Dependencies

- `Headless.Permissions.Abstractions`
- `Headless.Domain`
- `Headless.Caching.Abstractions`
- `Headless.DistributedLocks.Abstractions`

## Side Effects

- Registers `IPermissionManager` as transient
- Registers permission stores as singletons
- Starts `PermissionsInitializationBackgroundService` hosted service
- Registers cache invalidation handler
