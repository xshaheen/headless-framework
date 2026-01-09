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
- `Framework.Domains`
- `Framework.Caching.Abstractions`
- `Framework.ResourceLocks.Abstractions`

## Side Effects

- Registers `IPermissionManager` as transient
- Registers permission stores as singletons
- Starts `PermissionsInitializationBackgroundService` hosted service
- Registers cache invalidation handler
