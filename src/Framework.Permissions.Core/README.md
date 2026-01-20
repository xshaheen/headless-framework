# Framework.Permissions.Core

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
dotnet add package Framework.Permissions.Core
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Requires: TimeProvider, ICache, IResourceLock, IGuidGenerator
builder.Services.AddPermissionsManagementCore(options =>
{
    options.CacheKeyPrefix = "permissions:";
});

// Register permission definition providers
builder.Services.AddPermissionDefinitionProvider<OrderPermissionProvider>();

// Add storage (e.g., Entity Framework)
builder.Services.AddPermissionsManagementDbContextStorage<AppDbContext>();
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

```csharp
services.AddPermissionsManagementCore(options =>
{
    options.CacheKeyPrefix = "permissions:";
});
```

## Dependencies

- `Framework.Permissions.Abstractions`
- `Framework.Domain`
- `Framework.Caching.Abstractions`
- `Framework.ResourceLocks.Abstractions`

## Side Effects

- Registers `IPermissionManager` as transient
- Registers permission stores as singletons
- Starts `PermissionsInitializationBackgroundService` hosted service
- Registers cache invalidation handler
