# Test Case Design: Framework.Features (All Packages)

**Packages:**
- `src/Framework.Features.Abstractions`
- `src/Framework.Features.Core`
- `src/Framework.Features.Storage.EntityFramework`

**Test Projects:** `Framework.Features.Tests.Integration` (existing)
**Generated:** 2026-01-25

## Package Analysis

### Framework.Features.Abstractions

| File | Purpose | Testable |
|------|---------|----------|
| `Definitions/IFeatureDefinitionManager.cs` | Feature definition manager interface | Low (interface) |
| `Definitions/IFeatureDefinitionProvider.cs` | Definition provider interface | Low (interface) |
| `Models/FeatureDefinition.cs` | Feature definition model with parent/child support | High |
| `Models/FeatureGroupDefinition.cs` | Feature group definition | Medium |
| `Models/FeatureValue.cs` | Feature value record | Low |
| `Models/ICanCreateChildFeature.cs` | Child feature interface | Low (interface) |
| `Models/IFeatureDefinitionContext.cs` | Definition context interface | Low (interface) |
| `Values/IFeatureManager.cs` | Feature value manager interface | Low (interface) |
| `Values/FeatureManagerExtensions.cs` | Convenience extension methods | Medium |
| `Values/DefaultValueFeatureManagerExtensions.cs` | Default value extensions | Medium |
| `Values/EditionFeatureManagerExtensions.cs` | Edition feature extensions | Medium |
| `Values/TenantFeatureManagerExtensions.cs` | Tenant feature extensions | Medium |
| `Filters/RequiresFeatureAttribute.cs` | MVC filter attribute | Medium |
| `Filters/DisableFeatureCheckAttribute.cs` | Disable check attribute | Low |

### Framework.Features.Core

| File | Purpose | Testable |
|------|---------|----------|
| `Definitions/FeatureDefinitionManager.cs` | IFeatureDefinitionManager impl combining static + dynamic | High |
| `Definitions/StaticFeatureDefinitionStore.cs` | In-memory static store | High |
| `Definitions/DynamicFeatureDefinitionStore.cs` | Database-backed dynamic store | High (integration) |
| `Definitions/FeatureDefinitionSerializer.cs` | Serialize definitions to/from records | High |
| `Models/FeatureDefinitionContext.cs` | Definition context impl | Medium |
| `Values/FeatureManager.cs` | IFeatureManager impl with provider chain | High |
| `Values/FeatureValueStore.cs` | Feature value persistence | High (integration) |
| `Values/FeatureValueProviderManager.cs` | Provider registration/ordering | High |
| `Values/FeatureValueCacheItem.cs` | Cache item for feature values | Low |
| `Values/FeatureValueCacheItemInvalidator.cs` | Cache invalidation | Medium |
| `ValueProviders/IFeatureValueProvider.cs` | Value provider interface | Low (interface) |
| `ValueProviders/DefaultValueFeatureValueProvider.cs` | Default value provider | High |
| `ValueProviders/EditionFeatureValueProvider.cs` | Edition-based provider | High |
| `ValueProviders/TenantFeatureValueProvider.cs` | Tenant-based provider | High |
| `ValueProviders/StoreFeatureValueProvider.cs` | Database store provider | High (integration) |
| `Seeders/FeaturesInitializationBackgroundService.cs` | Background seeding | Medium |

### Framework.Features.Storage.EntityFramework

| File | Purpose | Testable |
|------|---------|----------|
| `IFeaturesDbContext.cs` | DbContext interface | Low (interface) |
| `FeaturesDbContext.cs` | EF DbContext | Medium |
| `EfFeatureDefinitionRecordRepository.cs` | EF repository for definitions | High (integration) |
| `EfFeatureValueRecordRecordRepository.cs` | EF repository for values | High (integration) |
| `FeaturesModelBuilderExtensions.cs` | EF model configuration | Low |
| `Setup.cs` | DI registration | Low |

## Current Test Coverage

**Existing Integration Tests:** ~8 tests
- `FeatureDefinitionManagerTests.cs` - Definition management
- `DynamicFeatureDefinitionStoreTests.cs` - Dynamic store
- `FeatureManagerTests.cs` - Feature manager (6 detailed tests)

---

## Missing: FeatureDefinition Unit Tests

**File:** `tests/Framework.Features.Tests.Unit/Models/FeatureDefinitionTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_require_name` | Name validation |
| `should_default_display_name_to_name` | DisplayName fallback |
| `should_allow_setting_display_name` | DisplayName setter |
| `should_default_is_visible_to_clients_true` | IsVisibleToClients default |
| `should_default_is_available_to_host_true` | IsAvailableToHost default |
| `should_initialize_properties_dictionary` | Properties not null |
| `should_initialize_providers_list` | Providers not null |
| `should_add_child_feature` | AddChild method |
| `should_set_parent_on_child` | Parent property |
| `should_remove_child_feature` | RemoveChild method |
| `should_clear_parent_on_remove` | Parent null after remove |
| `should_throw_when_removing_nonexistent_child` | InvalidOperationException |
| `should_support_indexer_for_properties` | this[name] getter/setter |
| `should_format_toString` | ToString format |

---

## Missing: FeatureGroupDefinition Unit Tests

**File:** `tests/Framework.Features.Tests.Unit/Models/FeatureGroupDefinitionTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_require_name` | Name validation |
| `should_add_child_feature` | AddChild method |
| `should_return_child_from_add` | Return value |
| `should_access_features` | Features property |

---

## Missing: FeatureDefinitionManager Unit Tests

**File:** `tests/Framework.Features.Tests.Unit/Definitions/FeatureDefinitionManagerTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_find_from_static_store` | Static store priority |
| `should_find_from_dynamic_store` | Dynamic fallback |
| `should_return_null_when_not_found` | Missing feature |
| `should_get_features_from_both_stores` | Combined features |
| `should_prefer_static_over_dynamic` | Static priority |
| `should_get_groups_from_both_stores` | Combined groups |
| `should_prefer_static_groups_over_dynamic` | Static group priority |

---

## Missing: StaticFeatureDefinitionStore Unit Tests

**File:** `tests/Framework.Features.Tests.Unit/Definitions/StaticFeatureDefinitionStoreTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_build_from_providers` | Provider aggregation |
| `should_get_feature_by_name` | GetOrDefaultAsync |
| `should_return_null_for_unknown_feature` | Missing feature |
| `should_get_all_features` | GetFeaturesAsync |
| `should_get_all_groups` | GetGroupsAsync |

---

## Missing: FeatureManager Unit Tests

**File:** `tests/Framework.Features.Tests.Unit/Values/FeatureManagerTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_throw_when_feature_not_defined` | ConflictException |
| `should_return_null_value_when_no_provider_has_value` | Empty result |
| `should_get_value_from_first_provider` | Provider ordering |
| `should_skip_providers_until_named_provider` | providerName filter |
| `should_fallback_to_next_provider` | Fallback behavior |
| `should_not_fallback_when_fallback_false` | No fallback |
| `should_require_provider_name_when_fallback_false` | Argument validation |
| `should_get_all_for_provider` | GetAllAsync |
| `should_set_value` | SetAsync |
| `should_clear_value_when_matches_fallback` | Optimization |
| `should_force_set_value` | forceToSet parameter |
| `should_throw_when_provider_readonly` | IFeatureValueProvider check |
| `should_delete_all_values_for_provider` | DeleteAsync |

---

## Missing: DefaultValueFeatureValueProvider Unit Tests

**File:** `tests/Framework.Features.Tests.Unit/ValueProviders/DefaultValueFeatureValueProviderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_return_default_value` | DefaultValue from definition |
| `should_return_null_when_no_default` | Missing default |
| `should_ignore_provider_key` | Key not used |

---

## Missing: EditionFeatureValueProvider Unit Tests

**File:** `tests/Framework.Features.Tests.Unit/ValueProviders/EditionFeatureValueProviderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_get_value_for_edition` | Edition-scoped value |
| `should_return_null_when_edition_not_found` | Missing edition |
| `should_set_value_for_edition` | SetAsync |
| `should_clear_value_for_edition` | ClearAsync |

---

## Missing: TenantFeatureValueProvider Unit Tests

**File:** `tests/Framework.Features.Tests.Unit/ValueProviders/TenantFeatureValueProviderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_get_value_for_tenant` | Tenant-scoped value |
| `should_return_null_when_tenant_not_found` | Missing tenant |
| `should_set_value_for_tenant` | SetAsync |
| `should_clear_value_for_tenant` | ClearAsync |
| `should_use_current_tenant_when_key_null` | ICurrentTenant integration |

---

## Missing: FeatureValueProviderManager Unit Tests

**File:** `tests/Framework.Features.Tests.Unit/Values/FeatureValueProviderManagerTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_register_providers_in_order` | Ordering |
| `should_expose_providers_as_readonly` | ValueProviders property |

---

## Missing: FeatureDefinitionSerializer Unit Tests

**File:** `tests/Framework.Features.Tests.Unit/Definitions/FeatureDefinitionSerializerTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_serialize_definition_to_record` | ToRecord |
| `should_serialize_group_to_record` | GroupToRecord |
| `should_deserialize_record_to_definition` | FromRecord |
| `should_deserialize_group_record` | GroupFromRecord |
| `should_preserve_parent_child_relationships` | Hierarchy |
| `should_preserve_properties` | Properties dictionary |

---

## Missing: RequiresFeatureAttribute Unit Tests

**File:** `tests/Framework.Features.Tests.Unit/Filters/RequiresFeatureAttributeTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_store_feature_names` | FeatureNames property |
| `should_default_require_all_false` | RequireAll default |

---

## Test Infrastructure

### Required Unit Test Project

```xml
<!-- tests/Framework.Features.Tests.Unit/Framework.Features.Tests.Unit.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Framework.Features.Abstractions\Framework.Features.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\Framework.Features.Core\Framework.Features.Core.csproj" />
    <ProjectReference Include="..\Framework.Testing\Framework.Testing.csproj" />
  </ItemGroup>
</Project>
```

### Mock Stores

```csharp
public sealed class FakeStaticFeatureDefinitionStore : IStaticFeatureDefinitionStore
{
    private readonly List<FeatureDefinition> _features = [];
    private readonly List<FeatureGroupDefinition> _groups = [];

    public void AddFeature(FeatureDefinition feature) => _features.Add(feature);
    public void AddGroup(FeatureGroupDefinition group) => _groups.Add(group);

    public Task<FeatureDefinition?> GetOrDefaultAsync(string name, CancellationToken ct = default)
        => Task.FromResult(_features.FirstOrDefault(f => f.Name == name));

    public Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FeatureDefinition>>(_features);

    public Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FeatureGroupDefinition>>(_groups);
}
```

---

## Test Summary

| Component | Existing | New Unit | Total |
|-----------|----------|----------|-------|
| FeatureDefinition | 0 | 14 | 14 |
| FeatureGroupDefinition | 0 | 4 | 4 |
| FeatureDefinitionManager | 0 | 7 | 7 |
| StaticFeatureDefinitionStore | 0 | 5 | 5 |
| FeatureManager | 6 | 13 | 19 |
| DefaultValueFeatureValueProvider | ~2 | 3 | 5 |
| EditionFeatureValueProvider | ~2 | 4 | 6 |
| TenantFeatureValueProvider | 0 | 5 | 5 |
| FeatureValueProviderManager | 0 | 2 | 2 |
| FeatureDefinitionSerializer | 0 | 6 | 6 |
| RequiresFeatureAttribute | 0 | 2 | 2 |
| **Total** | **~10** | **65** | **75** |

---

## Priority Order

1. **FeatureDefinition** - Core model with parent/child hierarchy
2. **FeatureManager** - Central value management (some integration exists)
3. **FeatureDefinitionManager** - Static + Dynamic store combination
4. **Value Providers** - Provider chain implementation
5. **Serializer** - Definition persistence

---

## Notes

1. **Provider ordering** - Providers are checked in registration order
2. **Fallback behavior** - Can optionally fall back through provider chain
3. **Static vs Dynamic** - Static definitions preferred over dynamic
4. **Parent-child features** - Features can have hierarchical relationships
5. **Value caching** - FeatureValueCacheItem for performance
6. **Multi-tenant support** - TenantFeatureValueProvider for per-tenant values
7. **Edition support** - EditionFeatureValueProvider for subscription tiers

---

## Feature System Architecture

```
IFeatureManager
├── GetAsync(name, providerName?, providerKey?, fallback)
├── GetAllAsync(providerName, providerKey?, fallback)
├── SetAsync(name, value, providerName, providerKey, forceToSet)
└── DeleteAsync(providerName, providerKey)

Provider Chain (in order):
├── TenantFeatureValueProvider (ICurrentTenant)
├── EditionFeatureValueProvider (edition key)
├── StoreFeatureValueProvider (database)
└── DefaultValueFeatureValueProvider (definition default)

IFeatureDefinitionManager
├── FindAsync(name) → Static || Dynamic
├── GetFeaturesAsync() → Static ∪ Dynamic (static priority)
└── GetGroupsAsync() → Static ∪ Dynamic (static priority)

Stores:
├── StaticFeatureDefinitionStore (IFeatureDefinitionProvider[])
└── DynamicFeatureDefinitionStore (database)
```

---

## Recommendation

**Medium Priority** - Feature flags are commonly used for feature toggles and subscription management. The existing integration tests cover the main flows, but unit tests would provide faster feedback for:
- FeatureDefinition model behavior
- Provider chain logic
- Store combination logic
