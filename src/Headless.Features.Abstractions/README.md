# Headless.Features.Abstractions

Defines the unified interface for feature management and feature flags across different storage providers.

## Problem Solved

Provides a provider-agnostic feature management API, enabling dynamic feature toggling with support for multi-tenancy, editions, and hierarchical feature values without changing application code.

## Key Features

- `IFeatureManager` — reads and writes feature values across the registered provider chain; supports single-feature and bulk queries with optional provider targeting and fallback
- `IFeatureDefinitionProvider` — contributes feature groups and feature definitions at startup via `IFeatureDefinitionContext`
- `IFeatureDefinitionManager` — looks up and enumerates all registered feature definitions
- `FeatureDefinition` — describes a feature's name, default value, display metadata, allowed providers, and child features (tree structure); implements `ICanAddChildFeature` for fluent `AddChild(...)`
- `FeatureDefinitionCreateOptions` — initializer-based feature metadata with a required `Name`; optional values remain additive without constructor churn
- `FeatureGroupDefinition` — organizes related `FeatureDefinition` instances; supports `GetFlatFeatures()` for depth-first enumeration; also implements `ICanAddChildFeature`
- `ICanAddChildFeature` — shared fluent contract (`AddChild(...)`) implemented by both `FeatureGroupDefinition` and `FeatureDefinition` (renamed from `ICanCreateChildFeature`)
- `IFeatureDefinitionContext` — passed to each provider's `Define`; exposes `AddGroup(name, displayName)`, `GetGroupOrDefault(name)`, and `RemoveGroup(name)`. Groups are created by name — there is no instance-taking `AddGroup(FeatureGroupDefinition)` overload
- `FeatureValue` — record returned by `GetAsync`/`GetAllAsync` carrying the resolved string value and the `FeatureValueProvider` that supplied it; bulk reads (`GetAllAsync`, `GetAllForTenantAsync`, `GetAllForEditionAsync`, `GetAllDefaultAsync`) return `IReadOnlyList<FeatureValue>`
- `FeatureValueProviderNames` — constants `Tenant`, `Edition`, `DefaultValue` for targeting built-in providers
- Extension methods on `IFeatureManager`: `IsEnabledAsync`, `GetAsync<T>`, `EnsureEnabledAsync`, `GrantAsync`, `RevokeAsync`
- Scoped extension methods: `GetForTenantAsync`, `SetForTenantAsync`, `GrantToTenantAsync`, `RevokeFromTenantAsync`, `DeleteForTenantAsync` (tenant); equivalent `*ForEditionAsync` / `*ToEditionAsync` set (edition); `GetDefaultAsync`, `GetAllDefaultAsync` (default provider)
- `RequiresFeatureAttribute` — gates a controller class or action on one or more features; `IsAnd` property controls AND vs. OR policy (default: OR)
- `DisableFeatureCheckAttribute` — bypasses a class-level `[RequiresFeature]` gate on individual action methods

## Installation

```bash
dotnet add package Headless.Features.Abstractions
```

## Quick Start

```csharp
public sealed class BillingService(IFeatureManager features)
{
    public async Task ProcessAsync(string tenantId, CancellationToken ct)
    {
        // Check a boolean flag (defaults to false when value is absent)
        if (await features.IsEnabledAsync("EnableReports", ct))
        {
            // report logic
        }

        // Read a typed value for a specific tenant
        var maxUsers = await features.GetAsync<int>(
            "MaxUsers",
            providerName: FeatureValueProviderNames.Tenant,
            providerKey: tenantId,
            fallback: true,
            cancellationToken: ct
        );
    }
}

// Shorter form using scoped extension members
public sealed class TenantOnboardingService(IFeatureManager features)
{
    public Task GrantPremiumAsync(string tenantId) => features.GrantToTenantAsync("EnableReports", tenantId);

    public Task RevokeAsync(string tenantId) => features.RevokeFromTenantAsync("EnableReports", tenantId);
}
```

### Defining Features

```csharp
public sealed class MyFeatureDefinitionProvider : IFeatureDefinitionProvider
{
    public void Define(IFeatureDefinitionContext context)
    {
        var group = context.AddGroup("App.Features");

        group.AddChild(new FeatureDefinitionCreateOptions { Name = "MaxUsers", DefaultValue = "10" });
        group.AddChild(new FeatureDefinitionCreateOptions { Name = "EnableReports", DefaultValue = "false" });

        // Nested child features
        var billingFeature = group.AddChild(
            new FeatureDefinitionCreateOptions { Name = "Billing", DefaultValue = "false" }
        );
        billingFeature.AddChild(
            new FeatureDefinitionCreateOptions { Name = "Billing.Invoices", DefaultValue = "false" }
        );
    }
}
```

## Configuration

None. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.
