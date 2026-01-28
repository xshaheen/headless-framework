# Headless.Permissions.Abstractions

Defines the unified interface for permission management across different providers.

## Problem Solved

Provides a provider-agnostic permission management API, enabling dynamic permission checking with support for multiple grant providers (User, Role, Store) without changing application code.

## Key Features

- `IPermissionManager` - Core interface for permission operations
- `IPermissionDefinitionProvider` - Define permissions in code
- `IPermissionDefinitionManager` - Manage permission definitions
- Grant provider names (User, Role)
- `PermissionDefinition` and `PermissionGroupDefinition` models
- `MultiplePermissionGrantResult` for batch checks

## Installation

```bash
dotnet add package Headless.Permissions.Abstractions
```

## Usage

```csharp
public sealed class OrderService(IPermissionManager permissions, ICurrentUser currentUser)
{
    public async Task DeleteOrderAsync(Guid orderId, CancellationToken ct)
    {
        var result = await permissions.GetAsync("Orders.Delete", currentUser, cancellationToken: ct);

        if (!result.IsGranted)
            throw new ForbiddenException();

        // Delete order...
    }
}
```

### Defining Permissions

```csharp
public class OrderPermissionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup("Orders");

        group.AddPermission("Orders.View");
        group.AddPermission("Orders.Create");
        group.AddPermission("Orders.Edit");
        group.AddPermission("Orders.Delete");
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

- `Headless.BuildingBlocks`

## Side Effects

None.
