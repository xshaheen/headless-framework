# Headless.Permissions.Abstractions

Defines the unified interface for permission management across different grant providers and storage backends.

## Problem Solved

Provides a provider-agnostic permission management API, enabling dynamic permission checking with support for multiple grant providers (User, Role, and custom) without tying application code to a specific storage or grant strategy.

## Key Features

- `IPermissionManager` — resolves and mutates permission grants with AWS IAM-style semantics; `GetAsync` (single), `GetAllAsync` (all or by names), `SetAsync` (grant or prohibit, single or batch), `DeleteAsync` (remove all records for a principal)
- `PermissionManagerExtensions` — convenience helpers: `IsGrantedAsync` (boolean overloads), `GrantToUserAsync`, `RevokeFromUserAsync`, `SetToUserAsync`, `GrantToRoleAsync`, `RevokeFromRoleAsync`, `SetToRoleAsync`
- `IPermissionDefinitionProvider` — contributes permission groups and definitions at startup via `IPermissionDefinitionContext`
- `IPermissionDefinitionManager` — looks up and enumerates all defined permissions (`FindAsync`, `GetPermissionsAsync`, `GetGroupsAsync`)
- `IPermissionDefinitionContext` — mutable builder passed to `IPermissionDefinitionProvider.Define`; `AddGroup`, `GetGroup`, `GetGroupOrNull`, `RemoveGroup`, `GetPermissionOrDefault`
- `PermissionGroupDefinition` — named container for permissions; `AddChild`, `GetFlatPermissions`, `GetPermissionOrDefault`
- `PermissionDefinition` — single permission; `AddChild` for nesting, `Providers` list for restricting which grant providers can manage it
- `ICanAddChildPermission` — shared interface on both group and definition, enabling uniform `AddChild` calls in tree-building code
- `GrantedPermissionResult` — result of `GetAsync`; `Name`, `IsGranted`, `Providers` (the contributing grant providers with their keys)
- `GrantPermissionProvider` — identifies a contributing provider by `Name` and the `Keys` (user id or role names) that granted the permission
- `MultiplePermissionGrantResult` — `Dictionary<string, bool>` with `AllGranted` and `AllProhibited` shorthand properties; returned by batch `IsGrantedAsync`
- `PermissionGrantProviderNames` — constants `User` and `Role` for the built-in providers

## Installation

```bash
dotnet add package Headless.Permissions.Abstractions
```

## Quick Start

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

// Boolean shorthand
var canDelete = await permissions.IsGrantedAsync(currentUser, "Orders.Delete", ct);

// Batch check
var grantMap = await permissions.IsGrantedAsync(
    currentUser,
    ["Orders.View", "Orders.Edit", "Orders.Delete"],
    ct
);
if (grantMap.AllGranted) { /* all allowed */ }
```

### Defining Permissions

```csharp
public sealed class OrderPermissionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup("Orders");

        // Use AddChild (not AddPermission — that method does not exist)
        group.AddChild("Orders.View");
        group.AddChild("Orders.Create");
        group.AddChild("Orders.Edit");
        group.AddChild("Orders.Delete");

        // Nested children
        var billing = group.AddChild("Orders.Billing");
        billing.AddChild("Orders.Billing.Refund");
    }
}
```

## Configuration

None. This is an abstractions-only package.

## Dependencies

- `Headless.Core`

## Side Effects

None.
