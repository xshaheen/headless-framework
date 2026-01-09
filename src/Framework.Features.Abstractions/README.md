# Framework.Features.Abstractions

Defines the unified interface for feature management and feature flags across different storage providers.

## Problem Solved

Provides a provider-agnostic feature management API, enabling dynamic feature toggling with support for multi-tenancy, editions, and hierarchical feature values without changing application code.

## Key Features

- `IFeatureManager` - Core interface for getting/setting feature values
- `IFeatureDefinitionProvider` - Define features in code
- `IFeatureDefinitionManager` - Manage feature definitions
- Feature value providers (Default, Edition, Tenant)
- `RequiresFeatureAttribute` - Attribute-based feature gating
- Hierarchical feature definitions with groups

## Installation

```bash
dotnet add package Framework.Features.Abstractions
```

## Usage

```csharp
public sealed class BillingService(IFeatureManager features)
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        var maxUsers = await features.GetAsync("MaxUsers", cancellationToken: ct).AnyContext();

        if (int.Parse(maxUsers.Value ?? "10") > 100)
        {
            // Premium feature logic
        }
    }
}
```

### Defining Features

```csharp
public class MyFeatureDefinitionProvider : IFeatureDefinitionProvider
{
    public void Define(IFeatureDefinitionContext context)
    {
        var group = context.AddGroup("App.Features");

        group.AddFeature("MaxUsers", defaultValue: "10");
        group.AddFeature("EnableReports", defaultValue: "false");
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.
