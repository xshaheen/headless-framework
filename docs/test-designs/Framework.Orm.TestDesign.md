# Framework.Orm Test Design

## Package Overview

Framework.Orm provides ORM utilities and abstractions for Entity Framework Core and Couchbase. The system consists of 2 packages:

| Package | Purpose | Test Priority |
|---------|---------|---------------|
| Framework.Orm.EntityFramework | EF Core base context with auditing, multi-tenancy, messaging | High |
| Framework.Orm.Couchbase | Couchbase bucket context and document sets | Medium |

## Architecture Summary

```
┌──────────────────────────────────────────────────────────────────┐
│                   Framework.Orm Architecture                      │
├──────────────────────────────────────────────────────────────────┤
│  HeadlessDbContext (abstract base)                               │
│    ├─ Automatic auditing (Create, Update, Delete, Suspend)       │
│    ├─ Multi-tenancy filtering (IMultiTenant)                     │
│    ├─ Concurrency stamps (IHasConcurrencyStamp)                  │
│    ├─ Local/Distributed message publishing                       │
│    └─ Transaction management                                      │
├──────────────────────────────────────────────────────────────────┤
│  HeadlessEntityModelProcessor                                     │
│    ├─ Query filter configuration (tenant, soft-delete, suspend)  │
│    ├─ Value converter setup (DateTime normalization)             │
│    ├─ Audit field population                                     │
│    └─ Entity event publishing (Created/Updated/Deleted/Changed)  │
├──────────────────────────────────────────────────────────────────┤
│  Navigation Modified Tracker                                      │
│    ├─ Tracks navigation property changes                         │
│    └─ Detects many-to-many modifications                         │
├──────────────────────────────────────────────────────────────────┤
│  Value Converters                                                 │
│    ├─ MoneyValueConverter                                        │
│    ├─ MonthValueConverter                                        │
│    ├─ LocaleValueConverter                                       │
│    ├─ CurrencyConfiguration                                      │
│    ├─ PhoneNumberConfiguration                                   │
│    ├─ AccountIdValueConverter                                    │
│    ├─ UserIdValueConverter                                       │
│    └─ NormalizeDateTimeValueConverter                            │
├──────────────────────────────────────────────────────────────────┤
│  DataGrid Extensions                                              │
│    ├─ ToDataGridAsync (ordering + pagination)                    │
│    ├─ OrderBy extensions                                         │
│    └─ IndexPage result type                                      │
├──────────────────────────────────────────────────────────────────┤
│  Couchbase (separate package)                                     │
│    ├─ CouchbaseBucketContext                                     │
│    ├─ DocumentSet extensions                                     │
│    └─ Cluster/Transaction providers                              │
└──────────────────────────────────────────────────────────────────┘
```

## Existing Test Coverage

**Location:** `tests/Headless.Orm.EntityFramework.Tests.Integration/`

| Test File | Coverage Area | Test Count |
|-----------|--------------|------------|
| HeadlessDbContextTests.cs | Audit, global filters, transactions | 8 |
| EntityFrameworkNavigationModifiedTrackerTests.cs | Navigation tracking | 5 |

**Total existing:** ~13 tests

---

## Test Design by Package

### 1. Framework.Orm.EntityFramework

#### 1.1 HeadlessDbContext (Integration)
**File:** `Contexts/HeadlessDbContext.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_save_changes_without_emitters_no_messages | Basic save without messaging | P1 |
| should_set_guid_id_on_add | Auto-generate Guid for IEntity<Guid> | P1 |
| should_set_create_audit_date_and_user | DateCreated + CreatedById | P1 |
| should_set_concurrency_stamp_on_add | New ConcurrencyStamp | P1 |
| should_emit_created_and_changed_local_messages | Entity event publishing | P1 |
| should_set_update_audit_on_modified | DateUpdated + UpdatedById | P1 |
| should_update_concurrency_stamp_on_modified | Stamp changes | P1 |
| should_emit_updated_and_changed_local_messages | Update events | P1 |
| should_set_delete_audit_on_soft_delete | DateDeleted + DeletedById | P1 |
| should_set_suspend_audit_on_suspend | DateSuspended + SuspendedById | P1 |
| should_publish_distributed_messages_in_transaction | Transactional messaging | P1 |
| should_commit_transaction_when_operation_returns_true | ExecuteTransactionAsync | P1 |
| should_rollback_transaction_when_operation_returns_false | Rollback path | P1 |
| should_rollback_transaction_on_exception | Exception handling | P1 |
| should_execute_with_isolation_level | ReadCommitted isolation | P2 |
| should_execute_transaction_with_result | Return value variant | P2 |
| should_execute_transaction_with_argument | TArg variant | P2 |
| should_use_existing_transaction_if_present | No nested transaction | P2 |
| should_apply_default_schema | HasDefaultSchema | P2 |

**Existing:** 8 tests provide good coverage of core scenarios

#### 1.2 HeadlessEntityModelProcessor (Unit/Integration)
**File:** `Contexts/HeadlessEntityModelProcessor.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_configure_multi_tenancy_query_filter | TenantId filter | P1 |
| should_configure_not_deleted_query_filter | IsDeleted filter | P1 |
| should_configure_not_suspended_query_filter | IsSuspended filter | P1 |
| should_apply_datetime_value_converter | Normalization | P1 |
| should_set_multi_tenant_id_on_add | Auto-assign TenantId | P1 |
| should_set_guid_id_when_empty | GuidGenerator.Create | P1 |
| should_not_override_existing_guid_id | Keep user-provided ID | P1 |
| should_skip_database_generated_id | Respect DatabaseGenerated attribute | P2 |
| should_set_create_audit_with_user_id | ICreateAudit<UserId> | P1 |
| should_set_create_audit_with_account_id | ICreateAudit<AccountId> | P1 |
| should_not_override_existing_created_by | Keep user-set value | P2 |
| should_set_update_audit_date_and_user | IUpdateAudit population | P1 |
| should_set_delete_audit_when_is_deleted_modified | IDeleteAudit | P1 |
| should_clear_delete_audit_on_undelete | Restore scenario | P2 |
| should_set_suspend_audit_when_is_suspended_modified | ISuspendAudit | P1 |
| should_clear_suspend_audit_on_unsuspend | Reactivate scenario | P2 |
| should_publish_entity_created_event | Local message | P1 |
| should_publish_entity_updated_event | Local message | P1 |
| should_publish_entity_deleted_event | Hard delete | P1 |
| should_publish_entity_changed_event | All state changes | P1 |
| should_cache_event_factories | ConcurrentDictionary reuse | P3 |
| should_detect_domain_modified_properties | Ignore FK changes | P2 |

#### 1.3 HeadlessEntityFrameworkNavigationModifiedTracker (Unit)
**File:** `ChangeTrackers/HeadlessEntityFrameworkNavigationModifiedTracker.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_detect_navigation_added | Add to collection | P1 |
| should_detect_navigation_removed | Remove from collection | P1 |
| should_track_modified_entity_entries | GetModifiedEntityEntries | P1 |
| should_clear_tracked_entries | Clear method | P1 |
| should_check_is_entity_entry_modified | Boolean check | P1 |
| should_check_is_navigation_entry_modified | Navigation-specific | P2 |

**Existing:** 5 tests provide good coverage

#### 1.4 Global Filters (Integration)
**File:** `GlobalFilters/*.cs` + `Contexts/IgnoreQueryFiltersExtensions.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_filter_by_tenant_id | Multi-tenancy filter | P1 |
| should_filter_deleted_entities | Soft-delete filter | P1 |
| should_filter_suspended_entities | Suspend filter | P1 |
| should_ignore_multi_tenancy_filter | IgnoreMultiTenancyFilter | P1 |
| should_ignore_not_deleted_filter | IgnoreNotDeletedFilter | P1 |
| should_ignore_not_suspended_filter | IgnoreNotSuspendedFilter | P1 |
| should_combine_multiple_filter_ignores | All filters disabled | P1 |
| should_return_entities_with_null_tenant | Shared entities | P2 |

**Existing:** 1 comprehensive test covers multiple scenarios

#### 1.5 Value Converters (Unit)
**Files:** `Configurations/*.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| MoneyValueConverter_should_convert_to_decimal | Money → decimal | P1 |
| MoneyValueConverter_should_convert_from_decimal | decimal → Money | P1 |
| MonthValueConverter_should_convert_to_int | Month → int | P2 |
| MonthValueConverter_should_convert_from_int | int → Month | P2 |
| LocaleValueConverter_should_convert_to_json | Locales → JSON | P2 |
| LocaleValueConverter_should_convert_from_json | JSON → Locales | P2 |
| CurrencyConfiguration_should_configure_precision | Decimal precision | P2 |
| PhoneNumberConfiguration_should_configure_length | Max length | P2 |
| AccountIdValueConverter_should_convert | AccountId ↔ string | P2 |
| UserIdValueConverter_should_convert | UserId ↔ string | P2 |
| NormalizeDateTimeValueConverter_should_normalize | UTC normalization | P1 |
| NormalizeDateTimeValueConverter_should_handle_nullable | Nullable DateTime | P2 |
| ExtraPropertiesValueConverter_should_serialize_dict | Dictionary ↔ JSON | P2 |
| JsonValueConverter_should_use_serializer_context | STJ source gen | P3 |

#### 1.6 DataGrid Extensions (Unit)
**Files:** `DataGrid/*.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| ToDataGridAsync_should_apply_ordering | OrderBy from request | P1 |
| ToDataGridAsync_should_apply_pagination | ToIndexPageAsync | P1 |
| ToDataGridAsync_should_handle_empty_orders | No ordering applied | P2 |
| ToDataGridAsync_should_throw_on_null_source | ArgumentNullException | P2 |
| ToDataGridAsync_should_throw_on_null_request | ArgumentNullException | P2 |
| OrderBy_should_order_ascending | Direction.Asc | P1 |
| OrderBy_should_order_descending | Direction.Desc | P1 |
| OrderBy_should_chain_multiple_orders | ThenBy | P2 |

#### 1.7 Extension Methods (Unit)
**Files:** `Extensions/*.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| ApplyConfigurationsFromAssembly_should_load_configs | DI-aware loading | P2 |
| ApplyConfigurationsFromAssembly_should_filter_by_predicate | Conditional loading | P3 |
| ConfigureFrameworkConvention_should_apply | EntityTypeBuilder ext | P3 |
| AddBuildingBlocksPrimitivesConvertersMappings_should_register | Convention registration | P3 |
| ToLookupAsync_should_group_by_key | IQueryable extension | P2 |
| WhereInDateRange_DateOnly_should_filter | DateOnly range | P2 |
| WhereInDateRange_DateTimeOffset_should_filter | DateTimeOffset range | P2 |
| FindByIdAsync_should_query_entity | IEntity lookup | P2 |

#### 1.8 Seeders (Integration)
**Files:** `Seeders/*.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| DbMigrationPreSeeder_should_seed_on_startup | IHostedService | P2 |
| AddDbMigrationSeederExtensions_should_register | DI registration | P3 |

---

### 2. Framework.Orm.Couchbase

#### 2.1 CouchbaseBucketContext (Integration)
**File:** `Context/CouchbaseBucketContext.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_initialize_bucket_connection | Bucket access | P1 |
| should_get_document_by_id | Document retrieval | P1 |
| should_upsert_document | Insert/update | P1 |
| should_remove_document | Delete operation | P1 |
| should_execute_n1ql_query | Query execution | P2 |

#### 2.2 DocumentSetExtensions (Unit)
**File:** `Context/DocumentSetExtensions.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_filter_by_type | Type discrimination | P2 |
| should_project_to_type | Document projection | P2 |

#### 2.3 Cluster Providers (Integration)
**Files:** `Clusters/*.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| ClustersProvider_should_return_cluster | ICluster access | P2 |
| TransactionConfigProvider_should_configure | Transaction settings | P3 |
| EventingFunctionsSeeder_should_seed | Eventing setup | P3 |

---

## Test Summary

| Package | Unit Tests | Integration Tests | Existing | Total New |
|---------|-----------|------------------|----------|-----------|
| Orm.EntityFramework | 38 | 32 | ~13 | ~57 |
| Orm.Couchbase | 2 | 8 | 0 | ~10 |
| **Total** | **40** | **40** | **~13** | **~67** |

**Grand Total: ~80 test cases** (~67 new, ~13 existing)

---

## Test Infrastructure Requirements

### Existing Test Project
- `tests/Headless.Orm.EntityFramework.Tests.Integration/` (extend)

### New Test Projects Needed
1. **Framework.Orm.EntityFramework.Tests.Unit** (new)
   - Value converter tests
   - DataGrid extension tests
   - Navigation tracker tests

2. **Framework.Orm.Couchbase.Tests.Integration** (new)
   - Requires Couchbase container

### Test Fixtures

```csharp
// Existing fixture - extend
public sealed class HeadlessDbContextTestFixture : IAsyncLifetime
{
    public static readonly DateTime Now = new(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
    public static readonly UserId UserId = new("test-user-id");

    public IServiceProvider ServiceProvider { get; private set; }
    public ICurrentTenant CurrentTenant { get; private set; }

    // ... SQLite in-memory for fast tests
}

// Couchbase fixture (new)
public sealed class CouchbaseTestFixture : IAsyncLifetime
{
    public CouchbaseContainer Container { get; private set; }
    public ICluster Cluster { get; private set; }
    public IBucket Bucket { get; private set; }

    public async Task InitializeAsync()
    {
        Container = new CouchbaseBuilder()
            .WithBucket(new BucketConfiguration { Name = "test" })
            .Build();
        await Container.StartAsync();
        Cluster = await Couchbase.Cluster.ConnectAsync(
            Container.GetConnectionString(),
            Container.Username,
            Container.Password);
        Bucket = await Cluster.BucketAsync("test");
    }

    public async Task DisposeAsync()
    {
        Cluster?.Dispose();
        await Container.DisposeAsync();
    }
}
```

### Test Entities

```csharp
// Existing test entities
public sealed class TestEntity :
    IEntity<Guid>,
    IMultiTenant,
    ICreateAudit<UserId>,
    IUpdateAudit<UserId>,
    IDeleteAudit<UserId>,
    ISuspendAudit<UserId>,
    IHasConcurrencyStamp,
    ILocalMessageEmitter,
    IDistributedMessageEmitter
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public string? TenantId { get; set; }
    public DateTime DateCreated { get; set; }
    public UserId? CreatedById { get; set; }
    public DateTime? DateUpdated { get; set; }
    public UserId? UpdatedById { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DateDeleted { get; set; }
    public UserId? DeletedById { get; set; }
    public bool IsSuspended { get; set; }
    public DateTime? DateSuspended { get; set; }
    public UserId? SuspendedById { get; set; }
    public string? ConcurrencyStamp { get; set; }
    // ... message emitter implementations
}
```

---

## Key Testing Patterns

### 1. Audit Testing Pattern
```csharp
[Fact]
public async Task should_set_create_audit_on_add()
{
    // Given
    await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
    await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();
    var entity = new TestEntity { Name = "test", TenantId = "T1" };

    // When
    db.Tests.Add(entity);
    await db.SaveChangesAsync(AbortToken);

    // Then
    entity.DateCreated.Should().Be(HeadlessDbContextTestFixture.Now);
    entity.CreatedById.Should().Be(HeadlessDbContextTestFixture.UserId);
    entity.ConcurrencyStamp.Should().NotBeNullOrEmpty();
}
```

### 2. Global Filter Testing Pattern
```csharp
[Fact]
public async Task should_filter_by_tenant()
{
    // Given
    await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
    await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

    var tenant1 = new TestEntity { Name = "a", TenantId = "T1" };
    var tenant2 = new TestEntity { Name = "b", TenantId = "T2" };
    await db.Tests.AddRangeAsync(tenant1, tenant2);
    await db.SaveChangesAsync(AbortToken);

    // When
    using (_fixture.CurrentTenant.Change("T1"))
    {
        var items = await db.Tests.ToListAsync(AbortToken);

        // Then
        items.Should().ContainSingle().Which.Name.Should().Be("a");
    }
}
```

### 3. Value Converter Testing Pattern
```csharp
[Fact]
public void MoneyValueConverter_should_round_trip()
{
    // Given
    var converter = new MoneyValueConverter();
    var money = new Money(123.45m);

    // When
    var stored = converter.ConvertToProvider(money);
    var restored = converter.ConvertFromProvider(stored);

    // Then
    stored.Should().Be(123.45m);
    restored.Should().Be(money);
}
```

### 4. Transaction Testing Pattern
```csharp
[Fact]
public async Task should_rollback_on_exception()
{
    // Given
    await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
    await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

    // When
    var act = async () => await db.ExecuteTransactionAsync(async () =>
    {
        await db.Tests.AddAsync(new TestEntity { Name = "fail", TenantId = "T1" });
        throw new InvalidOperationException("Simulated failure");
    }, cancellationToken: AbortToken);

    // Then
    await act.Should().ThrowAsync<InvalidOperationException>();
    (await db.Tests.CountAsync(AbortToken)).Should().Be(0);
}
```

---

## Notes

1. **Message Publishing Order**: Local messages published before save, distributed messages after save but before commit
2. **Concurrency Stamps**: Auto-generated on add, auto-updated on modify - enables optimistic concurrency
3. **Multi-Tenancy**: Null tenant ID entities are "shared" and returned when tenant filter is null
4. **Soft Delete**: Uses IsDeleted flag with global filter - IgnoreNotDeletedFilter to see deleted
5. **Navigation Tracking**: Custom tracker detects many-to-many relationship changes that EF doesn't mark as modified
6. **Value Converters**: Applied at convention level for primitive types (Money, Month, Locale, etc.)
