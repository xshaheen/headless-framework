# Test Case Design: Framework.Settings (All Packages)

**Packages:**
- `src/Framework.Settings.Abstractions`
- `src/Framework.Settings.Core`
- `src/Framework.Settings.Storage.EntityFramework`

**Test Projects:**
- `tests/Framework.Settings.Tests.Integration` (existing)
- `tests/Framework.Settings.Tests.Unit` (new project needed)

**Generated:** 2026-01-25

---

## Package Analysis

### Framework.Settings.Abstractions

| File | Purpose | Testable |
|------|---------|----------|
| `Definitions/ISettingDefinitionManager.cs` | Definition manager interface | Low (interface) |
| `Definitions/ISettingDefinitionProvider.cs` | Definition provider interface | Low (interface) |
| `Models/ISettingDefinitionContext.cs` | Definition context interface | Low (interface) |
| `Models/SettingDefinition.cs` | Setting definition class | High |
| `Models/SettingValue.cs` | Setting value class | Medium |
| `Values/ISettingManager.cs` | Setting manager interface | Low (interface) |
| `Values/SettingManagerExtensions.cs` | Manager extension methods | High |
| `Values/DefaultSettingManagerExtensions.cs` | Default provider extensions | High |
| `Values/GlobalSettingManagerExtensions.cs` | Global provider extensions | High |
| `Values/TenantSettingManagerExtensions.cs` | Tenant provider extensions | High |
| `Values/UserSettingManagerExtensions.cs` | User provider extensions | High |
| `Values/ConfigurationValueSettingManagerExtensions.cs` | Config provider extensions | High |
| `Values/SettingValueProviderNames.cs` | Provider name constants | Low (constants) |

### Framework.Settings.Core

| File | Purpose | Testable |
|------|---------|----------|
| `Definitions/SettingDefinitionManager.cs` | Definition manager impl | High |
| `Definitions/StaticSettingDefinitionStore.cs` | Static definition store | High |
| `Definitions/DynamicSettingDefinitionStore.cs` | Dynamic definition store | High (integration) |
| `Definitions/SettingDefinitionSerializer.cs` | Definition serializer | High |
| `Values/SettingManager.cs` | Setting manager impl | High |
| `Values/SettingValueStore.cs` | Value storage | High |
| `Values/SettingValueCacheItem.cs` | Cache item | Medium |
| `Values/SettingValueCacheItemInvalidator.cs` | Cache invalidation | High |
| `ValueProviders/DefaultValueSettingValueProvider.cs` | Default value provider | High |
| `ValueProviders/ConfigurationSettingValueProvider.cs` | Config value provider | High |
| `ValueProviders/GlobalSettingValueProvider.cs` | Global value provider | High |
| `ValueProviders/TenantSettingValueProvider.cs` | Tenant value provider | High |
| `ValueProviders/UserSettingValueProvider.cs` | User value provider | High |
| `ValueProviders/StoreSettingValueProvider.cs` | Store value provider | High |
| `ValueProviders/ISettingValueProvider.cs` | Value provider interface | Low (interface) |
| `ValueProviders/ISettingValueProviderManager.cs` | Provider manager interface | Low (interface) |
| `Helpers/ISettingEncryptionService.cs` | Encryption service interface | Low (interface) |
| `Entities/SettingDefinitionRecord.cs` | Definition entity | Medium |
| `Entities/SettingValueRecord.cs` | Value entity | Medium |
| `Entities/SettingDefinitionRecordConstants.cs` | Entity constants | Low (constants) |
| `Entities/SettingValueRecordConstants.cs` | Entity constants | Low (constants) |
| `Models/SettingDefinitionContext.cs` | Definition context | Medium |
| `Models/SettingManagementOptions.cs` | Management options | Medium |
| `Models/SettingManagementProvidersOptions.cs` | Provider options | Medium |
| `Repositories/ISettingDefinitionRecordRepository.cs` | Definition repo interface | Low (interface) |
| `Repositories/ISettingValueRecordRepository.cs` | Value repo interface | Low (interface) |
| `Seeders/SettingsInitializationBackgroundService.cs` | Background initializer | Medium |
| `Setup.cs` | DI registration | Low |

### Framework.Settings.Storage.EntityFramework

| File | Purpose | Testable |
|------|---------|----------|
| `EfSettingDefinitionRecordRepository.cs` | EF definition repository | High (integration) |
| `EfSettingValueRecordRepository.cs` | EF value repository | High (integration) |
| `ISettingsDbContext.cs` | DB context interface | Low (interface) |
| `SettingsDbContext.cs` | DB context | Medium |
| `SettingsModelBuilderExtensions.cs` | Model builder extensions | Medium |
| `Setup.cs` | DI registration | Low |

---

## Existing Test Coverage

### Framework.Settings.Tests.Integration

| Test File | Coverage |
|-----------|----------|
| `SettingDefinitionManagerTests.cs` | Definition manager integration |
| `SettingDefinitionRecordRepositoryTests.cs` | Repository integration |
| `SettingManagerTests.cs` | Manager integration |
| `DynamicSettingDefinitionStoreTests.cs` | Dynamic store integration |

---

## Missing: SettingDefinition Tests

**File:** `tests/Framework.Settings.Tests.Unit/Models/SettingDefinitionTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_create_with_required_name` | Name required |
| `should_store_default_value` | DefaultValue property |
| `should_store_display_name` | DisplayName property |
| `should_store_description` | Description property |
| `should_store_is_encrypted_flag` | IsEncrypted property |
| `should_store_is_inherited_flag` | IsInherited property |
| `should_store_providers_list` | Providers collection |
| `should_store_custom_properties` | CustomProperties dictionary |
| `should_default_is_encrypted_to_false` | Default false |
| `should_default_is_inherited_to_true` | Default true |

---

## Missing: SettingValue Tests

**File:** `tests/Framework.Settings.Tests.Unit/Models/SettingValueTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_create_with_name_and_value` | Constructor |
| `should_allow_null_value` | Nullable value |
| `should_store_name_property` | Name property |
| `should_store_value_property` | Value property |

---

## Missing: SettingDefinitionManager Tests

**File:** `tests/Framework.Settings.Tests.Unit/Definitions/SettingDefinitionManagerTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_find_in_static_store_first` | Static priority |
| `should_fallback_to_dynamic_store` | Dynamic fallback |
| `should_return_null_when_not_found` | Not found |
| `should_throw_when_name_is_null` | Argument validation |
| `should_get_all_from_both_stores` | GetAllAsync |
| `should_prefer_static_over_dynamic_duplicates` | Deduplication |
| `should_return_unique_definitions` | No duplicates |

---

## Missing: StaticSettingDefinitionStore Tests

**File:** `tests/Framework.Settings.Tests.Unit/Definitions/StaticSettingDefinitionStoreTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_get_all_static_definitions` | GetAllAsync |
| `should_get_definition_by_name` | GetOrDefaultAsync |
| `should_return_null_when_not_found` | Not found |
| `should_collect_from_all_providers` | Multiple providers |

---

## Missing: SettingManager Tests

**File:** `tests/Framework.Settings.Tests.Unit/Values/SettingManagerTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_find_value_from_providers` | FindAsync |
| `should_throw_when_setting_not_defined` | ConflictException |
| `should_skip_to_specified_provider` | providerName param |
| `should_use_provider_key_when_specified` | providerKey param |
| `should_fallback_through_providers` | fallback = true |
| `should_not_fallback_when_disabled` | fallback = false |
| `should_decrypt_encrypted_settings` | IsEncrypted |
| `should_get_all_for_setting_names` | GetAllAsync(names) |
| `should_get_all_for_provider` | GetAllAsync(provider) |
| `should_set_value_for_provider` | SetAsync |
| `should_encrypt_before_storing` | IsEncrypted |
| `should_clear_if_same_as_fallback` | Inheritance |
| `should_throw_when_provider_readonly` | Readonly provider |
| `should_delete_all_provider_values` | DeleteAsync |
| `should_throw_when_setting_name_null` | Argument validation |
| `should_throw_when_provider_name_null` | Argument validation |

---

## Missing: DefaultValueSettingValueProvider Tests

**File:** `tests/Framework.Settings.Tests.Unit/ValueProviders/DefaultValueSettingValueProviderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_return_default_value` | GetOrDefaultAsync |
| `should_return_null_when_no_default` | No default value |
| `should_return_provider_name` | Name property |
| `should_get_all_default_values` | GetAllAsync |

---

## Missing: ConfigurationSettingValueProvider Tests

**File:** `tests/Framework.Settings.Tests.Unit/ValueProviders/ConfigurationSettingValueProviderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_read_from_configuration` | IConfiguration |
| `should_use_setting_name_as_key` | Key mapping |
| `should_return_null_when_not_in_config` | Not found |
| `should_return_provider_name` | Name property |
| `should_get_all_from_configuration` | GetAllAsync |

---

## Missing: GlobalSettingValueProvider Tests

**File:** `tests/Framework.Settings.Tests.Unit/ValueProviders/GlobalSettingValueProviderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_read_from_store_with_null_key` | Global scope |
| `should_write_to_store_with_null_key` | SetAsync |
| `should_clear_from_store` | ClearAsync |
| `should_return_provider_name` | Name property |

---

## Missing: TenantSettingValueProvider Tests

**File:** `tests/Framework.Settings.Tests.Unit/ValueProviders/TenantSettingValueProviderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_read_from_store_with_tenant_key` | Tenant scope |
| `should_use_current_tenant_id` | ICurrentTenant |
| `should_use_provided_key_over_current` | providerKey override |
| `should_return_null_when_no_tenant` | No tenant context |
| `should_write_to_store_with_tenant_key` | SetAsync |
| `should_clear_from_store` | ClearAsync |
| `should_return_provider_name` | Name property |

---

## Missing: UserSettingValueProvider Tests

**File:** `tests/Framework.Settings.Tests.Unit/ValueProviders/UserSettingValueProviderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_read_from_store_with_user_key` | User scope |
| `should_use_current_user_id` | ICurrentUser |
| `should_use_provided_key_over_current` | providerKey override |
| `should_return_null_when_no_user` | No user context |
| `should_write_to_store_with_user_key` | SetAsync |
| `should_clear_from_store` | ClearAsync |
| `should_return_provider_name` | Name property |

---

## Missing: StoreSettingValueProvider Tests

**File:** `tests/Framework.Settings.Tests.Unit/ValueProviders/StoreSettingValueProviderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_read_from_value_store` | GetOrDefaultAsync |
| `should_write_to_value_store` | SetAsync |
| `should_clear_from_value_store` | ClearAsync |
| `should_get_all_from_store` | GetAllAsync |
| `should_return_provider_name` | Name property |

---

## Missing: SettingValueStore Tests

**File:** `tests/Framework.Settings.Tests.Unit/Values/SettingValueStoreTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_get_value_from_cache` | Cache hit |
| `should_get_value_from_repository` | Cache miss |
| `should_cache_value_after_retrieval` | Cache population |
| `should_delete_value` | DeleteAsync |
| `should_invalidate_cache_on_delete` | Cache invalidation |
| `should_get_all_provider_values` | GetAllProviderValuesAsync |

---

## Missing: SettingValueCacheItemInvalidator Tests

**File:** `tests/Framework.Settings.Tests.Unit/Values/SettingValueCacheItemInvalidatorTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_invalidate_cache_on_event` | Event handling |
| `should_build_correct_cache_key` | Key construction |

---

## Missing: SettingDefinitionSerializer Tests

**File:** `tests/Framework.Settings.Tests.Unit/Definitions/SettingDefinitionSerializerTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_serialize_definition_to_record` | ToRecord |
| `should_deserialize_record_to_definition` | FromRecord |
| `should_preserve_all_properties` | Round-trip |
| `should_handle_null_optional_properties` | Nullable handling |
| `should_serialize_providers_list` | Collection serialization |
| `should_serialize_custom_properties` | Dictionary serialization |

---

## Missing: SettingManagerExtensions Tests

**File:** `tests/Framework.Settings.Tests.Unit/Values/SettingManagerExtensionsTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_get_typed_value` | GetAsync<T> |
| `should_get_string_value` | GetOrEmptyAsync |
| `should_get_bool_value` | GetBoolAsync |
| `should_get_int_value` | GetIntAsync |
| `should_use_default_when_null` | Default value |
| `should_convert_to_specified_type` | Type conversion |

---

## Missing: DefaultSettingManagerExtensions Tests

**File:** `tests/Framework.Settings.Tests.Unit/Values/DefaultSettingManagerExtensionsTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_get_from_default_provider` | GetDefaultAsync |
| `should_get_all_default_values` | GetAllDefaultAsync |

---

## Missing: GlobalSettingManagerExtensions Tests

**File:** `tests/Framework.Settings.Tests.Unit/Values/GlobalSettingManagerExtensionsTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_get_from_global_provider` | GetGlobalAsync |
| `should_get_all_global_values` | GetAllGlobalAsync |
| `should_set_global_value` | SetGlobalAsync |

---

## Missing: TenantSettingManagerExtensions Tests

**File:** `tests/Framework.Settings.Tests.Unit/Values/TenantSettingManagerExtensionsTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_get_from_tenant_provider` | GetTenantAsync |
| `should_get_all_tenant_values` | GetAllTenantAsync |
| `should_set_tenant_value` | SetTenantAsync |
| `should_use_specified_tenant_id` | tenantId parameter |

---

## Missing: UserSettingManagerExtensions Tests

**File:** `tests/Framework.Settings.Tests.Unit/Values/UserSettingManagerExtensionsTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_get_from_user_provider` | GetUserAsync |
| `should_get_all_user_values` | GetAllUserAsync |
| `should_set_user_value` | SetUserAsync |
| `should_use_specified_user_id` | userId parameter |

---

## Test Infrastructure

### Mock Setting Definition Store

```csharp
public sealed class FakeStaticSettingDefinitionStore : IStaticSettingDefinitionStore
{
    private readonly Dictionary<string, SettingDefinition> _definitions = new(StringComparer.Ordinal);

    public void Add(SettingDefinition definition) => _definitions[definition.Name] = definition;

    public ValueTask<SettingDefinition?> GetOrDefaultAsync(string name, CancellationToken ct = default)
        => new(_definitions.GetValueOrDefault(name));

    public ValueTask<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken ct = default)
        => new(_definitions.Values.ToList());
}
```

### Mock Setting Value Provider

```csharp
public sealed class FakeSettingValueProvider : ISettingValueProvider
{
    private readonly Dictionary<(string Name, string? Key), string?> _values = new();
    public string Name { get; init; } = "Fake";

    public ValueTask<string?> GetOrDefaultAsync(SettingDefinition definition, string? providerKey, CancellationToken ct = default)
        => new(_values.GetValueOrDefault((definition.Name, providerKey)));

    public ValueTask SetAsync(SettingDefinition definition, string value, string? providerKey, CancellationToken ct = default)
    {
        _values[(definition.Name, providerKey)] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(SettingDefinition definition, string? providerKey, CancellationToken ct = default)
    {
        _values.Remove((definition.Name, providerKey));
        return ValueTask.CompletedTask;
    }

    public ValueTask<List<SettingValue>> GetAllAsync(SettingDefinition[] definitions, string? providerKey = null, CancellationToken ct = default)
        => new(definitions.Select(d => new SettingValue(d.Name, _values.GetValueOrDefault((d.Name, providerKey)))).ToList());
}
```

---

## Test Summary

| Component | Existing | New Unit | New Integration | Total |
|-----------|----------|----------|-----------------|-------|
| SettingDefinition | 0 | 10 | 0 | 10 |
| SettingValue | 0 | 4 | 0 | 4 |
| SettingDefinitionManager | ~5 | 7 | 0 | ~12 |
| StaticSettingDefinitionStore | 0 | 4 | 0 | 4 |
| SettingManager | ~10 | 16 | 0 | ~26 |
| DefaultValueSettingValueProvider | 0 | 4 | 0 | 4 |
| ConfigurationSettingValueProvider | 0 | 5 | 0 | 5 |
| GlobalSettingValueProvider | 0 | 4 | 0 | 4 |
| TenantSettingValueProvider | 0 | 7 | 0 | 7 |
| UserSettingValueProvider | 0 | 7 | 0 | 7 |
| StoreSettingValueProvider | 0 | 5 | 0 | 5 |
| SettingValueStore | 0 | 6 | 0 | 6 |
| SettingValueCacheItemInvalidator | 0 | 2 | 0 | 2 |
| SettingDefinitionSerializer | 0 | 6 | 0 | 6 |
| SettingManagerExtensions | 0 | 6 | 0 | 6 |
| Provider Extensions | 0 | 12 | 0 | 12 |
| **Total** | **~15** | **~105** | **0** | **~120** |

---

## Priority Order

1. **SettingManager** - Core setting access logic
2. **SettingDefinitionManager** - Definition resolution
3. **Value Providers** - Provider chain implementation
4. **SettingValueStore** - Cache and persistence
5. **Extension methods** - Convenience APIs

---

## Notes

1. **Provider chain** - Default → Configuration → Global → Tenant → User (reverse order for priority)
2. **IsInherited** - When true, continues checking fallback providers
3. **IsEncrypted** - Values encrypted before storage, decrypted on retrieval
4. **Static vs Dynamic** - Static definitions from code, dynamic from database
5. **forceToSet** - When false, clears value if same as fallback (optimization)
6. **Provider keys** - null for Global, TenantId for Tenant, UserId for User

---

## Settings Architecture

```
ISettingManager
├── FindAsync(name, providerName?, providerKey?, fallback?)
├── GetAllAsync(settingNames)
├── GetAllAsync(providerName, providerKey?, fallback?)
├── SetAsync(name, value, providerName, providerKey, forceToSet?)
└── DeleteAsync(providerName, providerKey)

Provider Chain (in priority order):
1. User (highest priority - per-user settings)
2. Tenant (per-tenant settings)
3. Global (application-wide settings)
4. Configuration (IConfiguration)
5. Default (from SettingDefinition.DefaultValue)

Definition Sources:
├── StaticSettingDefinitionStore (from ISettingDefinitionProvider implementations)
└── DynamicSettingDefinitionStore (from database via repository)

Storage:
├── SettingDefinitionRecord (database table)
└── SettingValueRecord (database table with Name, ProviderName, ProviderKey, Value)

Caching:
└── SettingValueCacheItem (ICache<SettingValueCacheItem>)
```

---

## Recommendation

**Medium Priority** - Settings is commonly used infrastructure. Unit tests should cover:
- Provider chain resolution
- Inheritance logic (IsInherited)
- Encryption/decryption
- Cache behavior
- Extension method type conversion

Integration tests (existing) cover EF repositories and full stack.
