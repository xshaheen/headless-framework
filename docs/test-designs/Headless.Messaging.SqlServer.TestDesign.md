# Test Case Design: Headless.Messaging.SqlServer

**Package:** `src/Headless.Messaging.SqlServer`
**Test Projects:** `Headless.Messaging.SqlServer.Tests.Unit` ✅, `Headless.Messaging.SqlServer.Tests.Integration` ✅
**Generated:** 2026-01-25

## Package Analysis

SQL Server storage implementation with schema management and monitoring API.

| File | Type | Priority |
|------|------|----------|
| `SqlServerDataStorage.cs` | Storage impl | P1 |
| `SqlServerMonitoringApi.cs` | Monitoring impl | P2 |
| `SqlServerStorageInitializer.cs` | Schema DDL | P1 |
| `SqlServerEntityFrameworkMessagingOptions.cs` | Options | P1 (Security) |
| `Diagnostics/DiagnosticObserver.cs` | Transaction tracking | P2 |
| `Diagnostics/DiagnosticProcessorObserver.cs` | Observer setup | P3 |

## Known Issues to Test

1. **Missing schema name validation regex** (todo #006)
2. **ID interpolation in DELETE** (todo #013)
3. **Missing row locks in retry processor** (todo #012)

## Test Recommendation

### Additional Unit Tests Needed

#### SqlServerEntityFrameworkMessagingOptions Tests (CRITICAL)
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `Schema_should_validate_with_regex` | P1 | **BUG: Missing validation** |
| `Schema_should_reject_sql_injection_patterns` | P1 | Reject `dbo]; DROP TABLE--` |
| `Schema_should_reject_special_characters` | P1 | Security |
| `Schema_should_enforce_max_length` | P2 | 128 chars max |
| `Schema_should_default_to_headless` | P2 | Default value |

#### SqlServerDataStorage Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `DeletePublishedAsync_should_use_parameterized_query` | P1 | **BUG: ID interpolation** |
| `DeleteReceivedAsync_should_use_parameterized_query` | P1 | **BUG: ID interpolation** |
| `GetRetryMessages_should_use_UPDLOCK_READPAST` | P1 | **BUG: Missing row lock** |
| `AcquireLockAsync_should_return_false_when_lock_held` | P1 | Lock contention |
| `AcquireLockAsync_should_succeed_after_ttl_expires` | P1 | TTL behavior |
| `RenewLockAsync_should_extend_ttl` | P2 | Lock renewal |
| `ChangePublishStateAsync_should_update_status` | P1 | Status change |
| `ChangeReceiveStateAsync_should_update_status` | P1 | Status change |

#### SqlServerMonitoringApi Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `GetMessageByIdAsync_should_use_parameterized_query` | P1 | **BUG: ID interpolation** |
| `GetPublishedMessagesAsync_should_support_pagination` | P2 | Pagination |
| `GetStatisticsAsync_should_return_counts_by_status` | P2 | Statistics |

#### SqlServerStorageInitializer Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_schema_if_not_exists` | P1 | Schema creation |
| `should_escape_schema_name_in_DDL` | P1 | SQL injection prevention |
| `should_create_tables_with_correct_structure` | P1 | Table DDL |
| `should_create_indexes` | P2 | Index creation |

### Integration Tests Needed

| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_store_and_retrieve_message_end_to_end` | P1 | Full round-trip |
| `should_handle_concurrent_lock_acquisition` | P1 | Lock contention |
| `should_process_retry_messages_without_duplicates` | P1 | READPAST |
| `should_work_with_EF_Core_transactions` | P1 | EF integration |

## Test Infrastructure

```csharp
// Use Testcontainers for SQL Server
public class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
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
| **Additional Unit Tests Needed** | **~20 cases** |
| **Additional Integration Tests Needed** | **~5 cases** |
| Priority | P1 - Production storage |

**Security Focus:** Schema name validation is CRITICAL - unlike PostgreSQL, SQL Server implementation lacks regex validation, allowing potential SQL injection through schema names.
