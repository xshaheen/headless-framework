# Framework.Sql Test Design

## Overview

The Framework.Sql packages provide database connection abstractions and implementations for PostgreSQL, SQL Server, and SQLite. The packages follow a simple factory pattern for creating database connections with support for connection pooling via a current connection abstraction.

### Packages
1. **Framework.Sql.Abstractions** - Interfaces for connection factories, current connection, and connection string checking
2. **Framework.Sql.PostgreSql** - Npgsql-based PostgreSQL implementation
3. **Framework.Sql.SqlServer** - Microsoft.Data.SqlClient-based SQL Server implementation
4. **Framework.Sql.Sqlite** - Microsoft.Data.Sqlite-based SQLite implementation

### Key Components
- **ISqlConnectionFactory** - Creates new open database connections
- **ISqlCurrentConnection** - Manages a single reusable connection per scope with thread-safe access
- **IConnectionStringChecker** - Validates connection strings and checks database existence
- **DefaultSqlCurrentConnection** - Thread-safe current connection implementation using AsyncLock

### Existing Tests
Located in:
- `tests/Framework.Sql.PostgreSql.Tests.Integration/` - ~6 tests
- `tests/Framework.Sql.SqlServer.Tests.Integration/` - ~5 tests
- `tests/Framework.Sql.Sqlite.Tests.Integration/` - ~5 tests
- `tests/Framework.Sql.Tests.Harness/` - Shared test base class

**Total existing: ~16 integration tests**

---

## 1. Framework.Sql.Abstractions

### 1.1 DefaultSqlCurrentConnection Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 1 | should_return_open_connection | Unit | GetOpenConnectionAsync returns Open state |
| 2 | should_reuse_same_connection_on_multiple_calls | Unit | Same instance returned when still open |
| 3 | should_create_new_connection_when_previous_closed | Unit | Creates new if existing is closed |
| 4 | should_dispose_existing_before_creating_new | Unit | Properly disposes closed connections |
| 5 | should_handle_concurrent_access_with_async_lock | Unit | Thread-safe access via AsyncLock |
| 6 | should_close_connection_on_dispose | Unit | DisposeAsync closes open connection |
| 7 | should_set_connection_to_null_on_dispose | Unit | Cleanup state on dispose |
| 8 | should_handle_dispose_when_no_connection | Unit | Safe dispose when never used |
| 9 | should_handle_dispose_when_already_closed | Unit | Safe dispose when already closed |
| 10 | should_not_throw_on_multiple_disposes | Unit | Idempotent dispose |

### 1.2 ISqlConnectionFactory Interface Compliance Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 11 | should_return_connection_string | Unit | GetConnectionString returns stored value |
| 12 | should_create_connection_async | Unit | CreateNewConnectionAsync returns DbConnection |

### 1.3 IConnectionStringChecker Interface Compliance Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 13 | should_return_tuple_with_connected_status | Unit | (Connected, DatabaseExists) tuple structure |
| 14 | should_check_async_with_cancellation | Unit | CancellationToken support (implicit) |

---

## 2. Framework.Sql.PostgreSql

### 2.1 NpgsqlConnectionFactory Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 15 | should_store_connection_string | Unit | Constructor stores connection string |
| 16 | should_return_connection_string | Unit | GetConnectionString returns stored value |
| 17 | should_create_npgsql_connection | Integration | CreateNewConnectionAsync returns NpgsqlConnection |
| 18 | should_open_connection_automatically | Integration | Connection is opened before return |
| 19 | should_implement_interface_explicitly | Unit | ISqlConnectionFactory.CreateNewConnectionAsync delegation |
| 20 | should_pass_cancellation_token | Integration | OpenAsync respects cancellation |
| 21 | should_throw_on_invalid_connection_string | Integration | Invalid string throws on open |
| 22 | should_throw_on_unreachable_server | Integration | Timeout on unreachable host |
| 23 | should_execute_query_on_created_connection | Integration | Connection is usable for queries |

### 2.2 NpgsqlConnectionStringChecker Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 24 | should_return_connected_true_when_server_reachable | Integration | Connected = true for valid server |
| 25 | should_return_database_exists_true_when_db_exists | Integration | DatabaseExists = true for existing DB |
| 26 | should_return_connected_false_when_server_unreachable | Integration | Connected = false on timeout |
| 27 | should_return_database_exists_false_when_db_missing | Integration | DatabaseExists = false for nonexistent |
| 28 | should_connect_to_postgres_database_first | Unit | Uses 'postgres' DB for initial connection |
| 29 | should_set_timeout_to_1_second | Unit | Timeout = 1 in connection builder |
| 30 | should_change_to_target_database | Integration | ChangeDatabaseAsync to target |
| 31 | should_log_warning_on_exception | Unit | Logger.LogWarning on failure |
| 32 | should_close_connection_after_check | Integration | Connection properly closed |
| 33 | should_handle_malformed_connection_string | Integration | Returns (false, false) gracefully |

---

## 3. Framework.Sql.SqlServer

### 3.1 SqlServerConnectionFactory Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 34 | should_store_connection_string | Unit | Constructor stores connection string |
| 35 | should_return_connection_string | Unit | GetConnectionString returns stored value |
| 36 | should_create_sql_connection | Integration | CreateNewConnectionAsync returns SqlConnection |
| 37 | should_open_connection_automatically | Integration | Connection is opened before return |
| 38 | should_implement_interface_explicitly | Unit | ISqlConnectionFactory.CreateNewConnectionAsync delegation |
| 39 | should_pass_cancellation_token | Integration | OpenAsync respects cancellation |
| 40 | should_throw_on_invalid_connection_string | Integration | Invalid string throws on open |
| 41 | should_throw_on_unreachable_server | Integration | Timeout on unreachable host |
| 42 | should_execute_query_on_created_connection | Integration | Connection is usable for queries |

### 3.2 SqlServerConnectionStringChecker Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 43 | should_return_connected_true_when_server_reachable | Integration | Connected = true for valid server |
| 44 | should_return_database_exists_true_when_db_exists | Integration | DatabaseExists = true for existing DB |
| 45 | should_return_connected_false_when_server_unreachable | Integration | Connected = false on timeout |
| 46 | should_return_database_exists_false_when_db_missing | Integration | DatabaseExists = false for nonexistent |
| 47 | should_connect_to_master_database_first | Unit | Uses 'master' DB for initial connection |
| 48 | should_set_connect_timeout_to_1_second | Unit | ConnectTimeout = 1 in connection builder |
| 49 | should_change_to_target_database | Integration | ChangeDatabaseAsync to InitialCatalog |
| 50 | should_log_warning_on_exception | Unit | Logger.LogWarning on failure |
| 51 | should_close_connection_after_check | Integration | Connection properly closed |
| 52 | should_handle_malformed_connection_string | Integration | Returns (false, false) gracefully |

---

## 4. Framework.Sql.Sqlite

### 4.1 SqliteConnectionFactory Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 53 | should_store_connection_string | Unit | Constructor stores connection string |
| 54 | should_return_connection_string | Unit | GetConnectionString returns stored value |
| 55 | should_create_sqlite_connection | Integration | CreateNewConnectionAsync returns SqliteConnection |
| 56 | should_open_connection_automatically | Integration | Connection is opened before return |
| 57 | should_implement_interface_explicitly | Unit | ISqlConnectionFactory.CreateNewConnectionAsync delegation |
| 58 | should_pass_cancellation_token | Integration | OpenAsync respects cancellation |
| 59 | should_create_in_memory_database | Integration | Data Source=:memory: works |
| 60 | should_create_file_based_database | Integration | File-based DB creation |
| 61 | should_execute_query_on_created_connection | Integration | Connection is usable for queries |

### 4.2 SqliteConnectionStringChecker Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 62 | should_return_both_true_for_valid_connection | Integration | Sqlite always returns (true, true) on success |
| 63 | should_return_both_false_on_exception | Integration | Exception returns (false, false) |
| 64 | should_log_warning_on_exception | Unit | Logger.LogWarning on failure |
| 65 | should_close_connection_after_check | Integration | Connection properly closed |
| 66 | should_handle_in_memory_database | Integration | :memory: DB check works |
| 67 | should_handle_nonexistent_file_path | Integration | Missing file handling |

---

## 5. Integration Tests (Cross-Provider)

### 5.1 Connection Factory Common Behavior Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 68 | should_return_open_connection_for_all_providers | Integration | All factories return Open state |
| 69 | should_support_concurrent_connection_creation | Integration | Thread-safe factory usage |
| 70 | should_handle_connection_pool_exhaustion | Integration | Pool limit handling |

### 5.2 Current Connection Common Behavior Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 71 | should_reuse_connection_across_operations | Integration | Same connection for scope |
| 72 | should_handle_parallel_access_correctly | Integration | AsyncLock prevents races |
| 73 | should_reconnect_after_connection_drop | Integration | Recovery from broken connection |

---

## Summary

| Category | Unit Tests | Integration Tests | Total |
|----------|------------|-------------------|-------|
| Abstractions | 14 | 0 | 14 |
| PostgreSql | 4 | 15 | 19 |
| SqlServer | 4 | 15 | 19 |
| Sqlite | 3 | 13 | 16 |
| Cross-Provider | 0 | 6 | 6 |
| **Total** | **25** | **49** | **74** |

### Test Distribution
- **Unit tests**: 25 (mock-based, no database required)
- **Integration tests**: 49 (requires actual database containers)
- **Existing tests**: ~16 integration tests
- **Missing tests**: ~58

### Test Project Structure
```
tests/
├── Framework.Sql.Tests.Unit/                    (NEW - 25 tests)
│   ├── DefaultSqlCurrentConnectionTests.cs
│   ├── PostgreSql/
│   │   └── NpgsqlConnectionStringCheckerTests.cs
│   ├── SqlServer/
│   │   └── SqlServerConnectionStringCheckerTests.cs
│   └── Sqlite/
│       └── SqliteConnectionStringCheckerTests.cs
├── Framework.Sql.Tests.Harness/                 (EXISTING)
│   └── SqlConnectionFactoryTestBase.cs
├── Framework.Sql.PostgreSql.Tests.Integration/  (EXISTING - expand)
│   ├── NpgsqlConnectionFactoryTests.cs          (existing)
│   └── NpgsqlConnectionStringCheckerTests.cs    (new)
├── Framework.Sql.SqlServer.Tests.Integration/   (EXISTING - expand)
│   ├── SqlServerConnectionFactoryTests.cs       (existing)
│   └── SqlServerConnectionStringCheckerTests.cs (new)
└── Framework.Sql.Sqlite.Tests.Integration/      (EXISTING - expand)
    ├── SqliteConnectionFactoryTests.cs          (existing)
    └── SqliteConnectionStringCheckerTests.cs    (new)
```

### Key Testing Considerations

1. **Thread Safety**: The `DefaultSqlCurrentConnection` uses `AsyncLock` from Nito.AsyncEx for thread-safe connection management. Tests should verify concurrent access behavior.

2. **Connection State Management**: Tests must verify that:
   - New connections are always opened before being returned
   - Closed/broken connections trigger new connection creation
   - Proper cleanup occurs during dispose

3. **Provider-Specific Behavior**:
   - PostgreSQL checker connects to 'postgres' system database first
   - SQL Server checker connects to 'master' system database first
   - SQLite checker returns (true, true) on any successful connection

4. **Timeout Configuration**: Connection string checkers set 1-second timeouts to fail fast - tests should verify this prevents hanging.

5. **Error Handling**: All checkers catch exceptions and return (false, false) tuple while logging warnings - tests should verify this graceful degradation.

6. **Integration Test Requirements**:
   - PostgreSQL tests need Testcontainers with PostgreSQL image
   - SQL Server tests need Testcontainers with SQL Server image
   - SQLite tests can use in-memory or temp file databases
