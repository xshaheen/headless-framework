# Headless.Identity Test Design

## Overview

The Headless.Identity.Storage.EntityFramework package provides a specialized ASP.NET Core Identity DbContext that integrates with the Headless Framework's entity processing, transaction management, and message publishing capabilities.

### Packages
1. **Headless.Identity.Storage.EntityFramework** - HeadlessIdentityDbContext base class

### Key Components
- **HeadlessIdentityDbContext** - Abstract generic base class extending IdentityDbContext
- Integrates IHeadlessEntityModelProcessor for auditing and events
- Navigation modified tracking via HeadlessEntityFrameworkNavigationModifiedTracker
- Transaction execution helpers with retry strategies
- Local and distributed message publishing within transactions

### Existing Tests
**No existing tests found**

---

## 1. Headless.Identity.Storage.EntityFramework

### 1.1 HeadlessIdentityDbContext - SaveChanges Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 1 | should_process_entity_entries_on_save | Integration | IHeadlessEntityModelProcessor called |
| 2 | should_skip_transaction_when_no_emitters | Integration | Direct save without transaction |
| 3 | should_use_existing_transaction_when_available | Integration | Reuses Database.CurrentTransaction |
| 4 | should_create_new_transaction_when_needed | Integration | BeginTransactionAsync called |
| 5 | should_use_read_committed_isolation | Integration | IsolationLevel.ReadCommitted |
| 6 | should_publish_local_messages_before_save | Integration | LocalEmitters published first |
| 7 | should_publish_distributed_messages_after_save | Integration | DistributedEmitters published last |
| 8 | should_commit_transaction_on_success | Integration | Transaction committed |
| 9 | should_clear_emitter_messages_after_save | Integration | Report.ClearEmitterMessages called |
| 10 | should_clear_navigation_tracker_after_save | Integration | RemoveModifiedEntityEntries called |
| 11 | should_use_execution_strategy | Integration | CreateExecutionStrategy().Execute |

### 1.2 HeadlessIdentityDbContext - SaveChanges Sync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 12 | should_process_entries_sync | Integration | Sync CoreSaveChanges |
| 13 | should_skip_transaction_sync_when_no_emitters | Integration | Direct save without transaction |
| 14 | should_use_existing_transaction_sync | Integration | Reuses CurrentTransaction |
| 15 | should_create_new_transaction_sync | Integration | BeginTransaction called |
| 16 | should_commit_transaction_sync | Integration | transaction.Commit() |

### 1.3 HeadlessIdentityDbContext - SaveChangesAsync Overloads Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 17 | should_call_core_save_from_parameterless | Unit | SaveChangesAsync() delegates |
| 18 | should_call_core_save_with_accept_changes | Unit | SaveChangesAsync(bool) delegates |
| 19 | should_pass_cancellation_token | Unit | CancellationToken forwarded |

### 1.4 HeadlessIdentityDbContext - ExecuteTransactionAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 20 | should_execute_operation_in_transaction | Integration | Operation executed within transaction |
| 21 | should_commit_when_operation_returns_true | Integration | Commit on success |
| 22 | should_rollback_when_operation_returns_false | Integration | Rollback on false |
| 23 | should_rollback_on_exception | Integration | Rollback on throw |
| 24 | should_rethrow_exception_after_rollback | Integration | Exception propagated |
| 25 | should_use_specified_isolation_level | Integration | Custom IsolationLevel |
| 26 | should_use_execution_strategy_for_retries | Integration | CreateExecutionStrategy |

### 1.5 HeadlessIdentityDbContext - ExecuteTransactionAsync with Arg Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 27 | should_pass_argument_to_operation | Integration | TArg forwarded |
| 28 | should_commit_with_arg_when_true | Integration | Commit on success |
| 29 | should_rollback_with_arg_when_false | Integration | Rollback on false |

### 1.6 HeadlessIdentityDbContext - ExecuteTransactionAsync with Result Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 30 | should_return_result_on_commit | Integration | TResult returned |
| 31 | should_return_null_on_rollback | Integration | Null on false |
| 32 | should_commit_and_return_result | Integration | Both commit and return |

### 1.7 HeadlessIdentityDbContext - ExecuteTransactionAsync with Arg and Result Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 33 | should_pass_arg_and_return_result | Integration | TArg → TResult flow |
| 34 | should_commit_with_result_when_true | Integration | Commit and return |
| 35 | should_rollback_with_result_when_false | Integration | Rollback, return null |

### 1.8 HeadlessIdentityDbContext - ConfigureConventions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 36 | should_add_building_blocks_converters | Integration | AddBuildingBlocksPrimitivesConvertersMappings |

### 1.9 HeadlessIdentityDbContext - OnModelCreating Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 37 | should_set_default_schema | Integration | HasDefaultSchema when not empty |
| 38 | should_skip_schema_when_empty | Integration | No schema when null/whitespace |
| 39 | should_call_base_model_creating | Integration | Base identity configuration |
| 40 | should_process_model_via_processor | Integration | ProcessModelCreating called |

### 1.10 HeadlessIdentityDbContext - Navigation Tracking Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 41 | should_track_navigation_changes | Integration | ChangeTracker events subscribed |
| 42 | should_detect_navigation_added | Integration | Tracked event handling |
| 43 | should_detect_navigation_removed | Integration | StateChanged event handling |
| 44 | should_clear_tracker_after_save | Integration | RemoveModifiedEntityEntries |

### 1.11 Setup Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 45 | should_register_required_services | Unit | DI registration |

---

## Summary

| Category | Unit Tests | Integration Tests | Total |
|----------|------------|-------------------|-------|
| SaveChanges Async | 0 | 11 | 11 |
| SaveChanges Sync | 0 | 5 | 5 |
| SaveChanges Overloads | 3 | 0 | 3 |
| ExecuteTransaction | 0 | 7 | 7 |
| ExecuteTransaction with Arg | 0 | 3 | 3 |
| ExecuteTransaction with Result | 0 | 3 | 3 |
| ExecuteTransaction Arg+Result | 0 | 3 | 3 |
| ConfigureConventions | 0 | 1 | 1 |
| OnModelCreating | 0 | 4 | 4 |
| Navigation Tracking | 0 | 4 | 4 |
| Setup | 1 | 0 | 1 |
| **Total** | **4** | **41** | **45** |

### Test Distribution
- **Unit tests**: 4 (mock-based)
- **Integration tests**: 41 (requires database)
- **Existing tests**: 0
- **Missing tests**: 45 (all tests new)

### Test Project Structure
```
tests/
└── Headless.Identity.Tests.Integration/        (NEW - 45 tests)
    ├── TestSetup/
    │   ├── TestUser.cs
    │   ├── TestRole.cs
    │   ├── TestIdentityDbContext.cs
    │   └── IdentityTestFixture.cs
    ├── SaveChanges/
    │   ├── SaveChangesAsyncTests.cs
    │   └── SaveChangesSyncTests.cs
    ├── Transactions/
    │   └── ExecuteTransactionTests.cs
    └── ModelCreating/
        └── OnModelCreatingTests.cs
```

### Key Testing Considerations

1. **Abstract Base Class**: HeadlessIdentityDbContext is abstract with abstract message publishing methods - tests need a concrete implementation.

2. **Generic Type Constraints**: Tests need concrete User, Role, and Key types that satisfy the Identity constraints.

3. **Transaction Management**: Tests must verify the complex transaction flow with execution strategies, especially around retry behavior.

4. **Message Publishing Order**: Local messages publish before SaveChanges, distributed messages after - this order is critical for consistency.

5. **Integration with HeadlessDbContext**: Much of the behavior mirrors Headless.Orm.EntityFramework - tests should focus on Identity-specific aspects.
