# Test Case Design: Headless.Messaging.PostgreSql

**Package:** `src/Headless.Messaging.PostgreSql`
**Test Projects:** `Headless.Messaging.PostgreSql.Tests.Unit` ✅, `Headless.Messaging.PostgreSql.Tests.Integration` ✅
**Generated:** 2026-01-25

## Package Analysis

PostgreSQL storage implementation with schema management and monitoring API.

| File | Type | Priority |
|------|------|----------|
| `PostgreSqlDataStorage.cs` | Storage impl | P1 |
| `PostgreSqlMonitoringApi.cs` | Monitoring impl | P2 |
| `PostgreSqlStorageInitializer.cs` | Schema DDL | P1 |
| `PostgreSqlEntityFrameworkMessagingOptions.cs` | Options | P2 |
| `Diagnostics/DiagnosticObserver.cs` | Transaction tracking | P2 |
| `Diagnostics/DiagnosticProcessorObserver.cs` | Observer setup | P3 |

## Known Issues to Test

1. **Missing ON CONFLICT for deduplication** (todo #004)
2. **ID interpolation in DELETE** (todo #013)
3. **Missing row locks in retry processor** (todo #012)
4. **Schema validation regex** - Already implemented ✅

## Test Recommendation

### Existing Tests - Gap Analysis

Review existing tests in `PostgreSql.Tests.Unit` and `PostgreSql.Tests.Integration` for coverage.

### Additional Unit Tests Needed

#### PostgreSqlDataStorage Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `StoreMessageAsync_should_use_ON_CONFLICT_for_deduplication` | P1 | **BUG: Missing** |
| `DeletePublishedAsync_should_use_parameterized_query` | P1 | **BUG: ID interpolation** |
| `DeleteReceivedAsync_should_use_parameterized_query` | P1 | **BUG: ID interpolation** |
| `GetRetryMessages_should_use_FOR_UPDATE_SKIP_LOCKED` | P1 | **BUG: Missing row lock** |
| `AcquireLockAsync_should_return_false_when_lock_held` | P1 | Lock contention |
| `AcquireLockAsync_should_succeed_after_ttl_expires` | P1 | TTL behavior |
| `RenewLockAsync_should_extend_ttl` | P2 | Lock renewal |
| `ChangePublishStateAsync_should_update_status` | P1 | Status change |
| `ChangeReceiveStateAsync_should_update_status` | P1 | Status change |

#### PostgreSqlMonitoringApi Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `GetPublishedMessagesAsync_should_support_pagination` | P2 | Pagination |
| `GetPublishedMessagesAsync_should_filter_by_status` | P2 | Status filter |
| `GetPublishedMessagesAsync_should_search_content` | P2 | Content search |
| `GetStatisticsAsync_should_return_counts_by_status` | P2 | Statistics |

#### PostgreSqlStorageInitializer Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_schema_if_not_exists` | P1 | Schema creation |
| `should_create_tables_with_correct_structure` | P1 | Table DDL |
| `should_create_indexes` | P2 | Index creation |
| `should_validate_schema_name_format` | P1 | Regex validation |
| `should_reject_invalid_schema_names` | P1 | Security |

#### PostgreSqlEntityFrameworkMessagingOptions Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `Schema_should_validate_with_regex` | P1 | Format validation |
| `Schema_should_reject_sql_injection` | P1 | Security |
| `Schema_should_default_to_headless` | P2 | Default value |

### Integration Tests Needed

| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_store_and_retrieve_message_end_to_end` | P1 | Full round-trip |
| `should_handle_concurrent_lock_acquisition` | P1 | Lock contention |
| `should_not_duplicate_messages_with_same_id` | P1 | Deduplication |
| `should_process_retry_messages_without_duplicates` | P1 | SKIP LOCKED |
| `should_expire_and_delete_old_messages` | P2 | Cleanup |

## Test Infrastructure

```csharp
// Use Testcontainers for PostgreSQL
public class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 6 |
| Existing Unit Tests | Some ✅ |
| Existing Integration Tests | Some ✅ |
| **Additional Unit Tests Needed** | **~15 cases** |
| **Additional Integration Tests Needed** | **~5 cases** |
| Priority | P1 - Production storage |

**Security Focus:** SQL injection prevention via schema validation and parameterized queries.
