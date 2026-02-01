# Framework.Ticker Test Design

## Package Overview

Framework.Ticker provides a comprehensive job scheduling system with support for both time-based (one-time) and cron-based (recurring) tickers. The system consists of 7 packages:

| Package | Purpose | Test Priority |
|---------|---------|---------------|
| Framework.Ticker.Abstractions | Core interfaces, entities, managers | High |
| Framework.Ticker.Core | Scheduler, dispatcher, execution handler | High |
| Framework.Ticker.EntityFramework | EF Core persistence provider | High |
| Framework.Ticker.Caching.Redis | Redis-based caching and heartbeat | Medium |
| Framework.Ticker.Dashboard | REST API and SignalR hub for monitoring | Medium |
| Framework.Ticker.OpenTelemetry | Tracing and metrics instrumentation | Low |
| Framework.Ticker.SourceGenerator | Compile-time function discovery | Low |

## Architecture Summary

```
┌──────────────────────────────────────────────────────────────────┐
│                    Framework.Ticker Architecture                  │
├──────────────────────────────────────────────────────────────────┤
│  Dashboard (REST API + SignalR)                                   │
│    ├─ DashboardEndpoints (CRUD operations)                       │
│    ├─ TickerQNotificationHub (real-time updates)                 │
│    └─ Authentication (Basic, Bearer, Host, Custom)               │
├──────────────────────────────────────────────────────────────────┤
│  Core Scheduler                                                   │
│    ├─ TickerQSchedulerBackgroundService (main loop)              │
│    ├─ TickerQFallbackBackgroundService (timed-out tasks)         │
│    ├─ TickerExecutionTaskHandler (task execution + retry)        │
│    └─ TickerQTaskScheduler (custom thread pool)                  │
├──────────────────────────────────────────────────────────────────┤
│  Managers                                                         │
│    ├─ TickerManager<TTime,TCron> (CRUD facade)                   │
│    ├─ InternalTickerManager (acquisition, updates)               │
│    └─ FluentChainTickerBuilder (parent-child chains)             │
├──────────────────────────────────────────────────────────────────┤
│  Persistence Layer                                                │
│    ├─ ITickerPersistenceProvider<TTime,TCron>                    │
│    ├─ TickerInMemoryPersistenceProvider (in-memory)              │
│    └─ TickerEfCorePersistenceProvider (EF Core)                  │
├──────────────────────────────────────────────────────────────────┤
│  Entities                                                         │
│    ├─ TimeTickerEntity (one-time jobs)                           │
│    ├─ CronTickerEntity (recurring definitions)                   │
│    └─ CronTickerOccurrenceEntity (individual executions)         │
└──────────────────────────────────────────────────────────────────┘
```

## Existing Test Coverage

**Location:** `tests/Framework.Ticker.Tests.Unit/`

| Test File | Coverage Area | Test Count |
|-----------|--------------|------------|
| TickerOptionsBuilderTests.cs | Builder configuration | 8 |
| InternalFunctionContextTests.cs | Property tracking | 8 |
| CronScheduleCacheTests.cs | Cron parsing + caching | 2 |
| RetryBehaviorTests.cs | Retry intervals + execution | 3 |
| TickerFunctionContextTests.cs | Context properties | ~5 |

**Total existing:** ~26 tests

---

## Test Design by Package

### 1. Framework.Ticker.Abstractions

#### 1.1 CronScheduleCache (Unit)
**File:** `CronScheduleCache.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_parse_valid_6_part_cron_expression | Valid expression returns CrontabSchedule | P1 |
| should_return_null_for_invalid_expression | Invalid syntax returns null | P1 |
| should_normalize_whitespace_between_parts | Multiple spaces normalized to single | P1 |
| should_cache_parsed_expressions | Same expression returns cached instance | P1 |
| should_calculate_next_occurrence_in_utc | Time conversion to UTC | P1 |
| should_invalidate_removes_from_cache | Invalidate returns true and removes | P2 |
| should_handle_timezone_conversion | Local time converts correctly | P2 |
| should_throw_for_null_expression | ArgumentNullException on null | P2 |

**Existing:** 2 tests cover basic parsing and normalization

#### 1.2 TickerExecutionContext (Unit)
**File:** `TickerExecutionContext.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_set_functions_with_volatile_write | Thread-safe assignment | P1 |
| should_get_functions_with_volatile_read | Thread-safe read | P1 |
| should_set_next_planned_occurrence | DateTime assignment | P1 |
| should_notify_action_when_set | Callback invoked on change | P2 |
| should_track_last_host_exception | Exception message stored | P2 |

#### 1.3 TickerManager (Unit)
**File:** `Managers/TickerManager.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_add_time_ticker_with_validation | Valid ticker persisted | P1 |
| should_reject_time_ticker_without_function | TickerValidatorException | P1 |
| should_reject_time_ticker_without_execution_time | Validation failure | P1 |
| should_update_time_ticker_and_restart_scheduler | Scheduler restarted | P1 |
| should_delete_time_ticker_by_id | Removal and count returned | P1 |
| should_delete_batch_of_time_tickers | Batch deletion | P2 |
| should_add_cron_ticker_with_expression | Valid cron persisted | P1 |
| should_reject_cron_ticker_with_invalid_expression | Validation failure | P1 |
| should_update_cron_ticker_and_invalidate_cache | Cache invalidated | P1 |
| should_delete_cron_ticker_by_id | Removal | P1 |
| should_get_time_ticker_by_id | Single ticker retrieval | P2 |
| should_get_time_tickers_paginated | Pagination works | P2 |
| should_get_cron_tickers_paginated | Pagination works | P2 |

#### 1.4 InternalFunctionContext (Unit)
**File:** `Models/InternalFunctionContext.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_track_updated_properties_via_set_property | Property names in set | P1 |
| should_reset_tracked_properties | Clear without value change | P1 |
| should_allow_multiple_updates_same_property | Only one entry in set | P1 |
| should_throw_for_non_property_expression | ArgumentException | P1 |
| should_initialize_children_as_empty_list | Default non-null | P2 |
| should_assign_cached_delegate_and_priority | Fields assigned | P2 |

**Existing:** 8 tests provide good coverage

#### 1.5 TickerOptionsBuilder (Unit)
**File:** `TickerOptionsBuilder.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_configure_request_json_options | Custom serializer settings | P1 |
| should_enable_gzip_compression | Flag set to true | P1 |
| should_ignore_seed_defined_cron_tickers | Seeding disabled | P1 |
| should_set_exception_handler_type | Type registered | P1 |
| should_configure_scheduler_options | MaxConcurrency, NodeId | P1 |
| should_disable_background_services | Flag set to false | P2 |
| should_set_time_seeder_action | Delegate stored | P2 |
| should_set_cron_seeder_action | Delegate stored | P2 |

**Existing:** 8 tests provide good coverage

#### 1.6 Entity Classes (Unit)

| Test Case | Description | Priority |
|-----------|-------------|----------|
| TimeTickerEntity_should_have_parent_child_relationship | Navigation works | P2 |
| TimeTickerEntity_should_track_status_transitions | Status enum values | P2 |
| CronTickerEntity_should_store_expression_and_request | Properties set | P2 |
| CronTickerOccurrenceEntity_should_link_to_parent | Foreign key works | P2 |
| BaseTickerEntity_should_have_audit_fields | CreatedAt, UpdatedAt | P3 |

#### 1.7 TickerFunctionProvider (Unit)
**File:** `TickerFunctionProvider.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_discover_functions_from_attribute | Reflection finds functions | P1 |
| should_return_function_metadata | Name, type, priority | P1 |
| should_freeze_on_first_read | No modifications after | P2 |

#### 1.8 TickerCancellationTokenManager (Unit)
**File:** `TickerCancellationTokenManager.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_add_ticker_cancellation_token | Registration works | P1 |
| should_remove_ticker_cancellation_token | Cleanup works | P1 |
| should_check_if_parent_running_excluding_self | Sibling detection | P1 |
| should_cancel_ticker_by_id | Cancellation triggered | P2 |

---

### 2. Framework.Ticker.Core

#### 2.1 TickerQSchedulerBackgroundService (Unit)
**File:** `BackgroundServices/TickerQSchedulerBackgroundService.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_start_and_mark_running | IsRunning becomes true | P1 |
| should_stop_and_release_resources | Resources released | P1 |
| should_restart_when_requested | Throttled restart | P1 |
| should_restart_if_new_ticker_earlier | 500ms threshold | P1 |
| should_not_restart_if_skip_first_run | Freeze task scheduler | P2 |
| should_handle_cancellation_gracefully | Resources released on shutdown | P1 |
| should_continue_after_exception | Loop continues with delay | P2 |
| should_process_functions_in_priority_order | OrderBy priority | P2 |

#### 2.2 TickerExecutionTaskHandler (Unit)
**File:** `TickerExecutionTaskHandler.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_execute_cron_occurrence_function | Direct execution | P1 |
| should_execute_time_ticker_with_children | Parent + children | P1 |
| should_apply_retry_intervals_correctly | Interval mapping | P1 |
| should_use_last_interval_when_retries_exceed_array | Fallback interval | P1 |
| should_stop_retrying_on_success | Early exit | P1 |
| should_handle_task_cancelled_exception | Status = Cancelled | P1 |
| should_handle_terminate_execution_exception | Status from exception | P1 |
| should_run_in_progress_children_concurrently | Parallel execution | P2 |
| should_skip_children_when_condition_not_met | RunCondition logic | P2 |
| should_bulk_update_skipped_children | Batch update | P2 |
| should_dispose_cancellation_token_source | Cleanup | P2 |
| should_call_exception_handler_on_failure | ITickerExceptionHandler | P2 |

**Existing:** 3 retry tests provide good coverage

#### 2.3 TickerInMemoryPersistenceProvider (Unit)
**File:** `Provider/TickerInMemoryPersistenceProvider.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_queue_time_tickers_with_lock | Status = Queued | P1 |
| should_queue_timed_out_time_tickers | Fallback threshold | P1 |
| should_release_acquired_time_tickers | Lock released | P1 |
| should_get_earliest_time_tickers_in_window | 1-second grouping | P1 |
| should_update_time_ticker_from_context | Property mapping | P1 |
| should_add_time_tickers_with_children | Hierarchy preserved | P1 |
| should_remove_time_tickers_cascade | Children deleted | P1 |
| should_migrate_defined_cron_tickers | Seeding logic | P1 |
| should_queue_cron_occurrences | New occurrences created | P1 |
| should_acquire_immediate_time_tickers | Lock acquired | P2 |
| should_release_dead_node_resources | Skipped status | P2 |
| should_paginate_time_tickers | Page results | P2 |
| should_paginate_cron_tickers | Page results | P2 |

#### 2.4 TickerQTaskScheduler (Unit)
**File:** `TickerQThreadPool/TickerQTaskScheduler.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_queue_work_items_by_priority | Priority ordering | P1 |
| should_freeze_and_resume | State transitions | P1 |
| should_respect_max_concurrency | Semaphore limit | P2 |
| should_handle_idle_worker_timeout | Worker cleanup | P3 |

#### 2.5 RestartThrottleManager (Unit)
**File:** `RestartThrottleManager.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_throttle_rapid_restart_requests | Debounce behavior | P1 |
| should_invoke_restart_callback | Callback triggered | P1 |

#### 2.6 SafeCancellationTokenSource (Unit)
**File:** `SafeCancellationTokenSource.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_create_linked_token_source | Linked to parent | P1 |
| should_cancel_safely | No double-dispose | P2 |

---

### 3. Framework.Ticker.EntityFramework

#### 3.1 TickerEfCorePersistenceProvider (Integration)
**File:** `Infrastructure/TickerEfCorePersistenceProvider.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_get_time_ticker_by_id_with_children | Include navigation | P1 |
| should_get_time_tickers_with_predicate | Where clause | P1 |
| should_paginate_time_tickers | Skip/Take | P1 |
| should_add_time_tickers | SaveChangesAsync | P1 |
| should_update_time_tickers | UpdateRange | P1 |
| should_remove_time_tickers_cascade | Cascade delete | P1 |
| should_get_cron_ticker_by_id | Single retrieval | P1 |
| should_insert_cron_tickers_and_invalidate_cache | Redis cache cleared | P1 |
| should_remove_cron_tickers_execute_delete | Bulk delete | P1 |
| should_get_cron_occurrences_with_ticker | Include CronTicker | P1 |
| should_insert_cron_occurrences | AddRangeAsync | P2 |
| should_acquire_immediate_cron_occurrences | Lock + InProgress | P2 |

#### 3.2 BasePersistenceProvider (Unit/Integration)
**File:** `Infrastructure/BasePersistenceProvider.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_queue_time_tickers_atomically | Transaction | P1 |
| should_queue_timed_out_tickers | ExecuteUpdateAsync | P1 |
| should_update_ticker_from_function_context | Property mapping | P1 |
| should_get_ticker_request_bytes | Byte array retrieval | P2 |
| should_release_dead_node_resources | Two-phase release | P2 |

#### 3.3 TickerQDbContext (Unit)
**File:** `DbContextFactory/TickerQDbContext.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_configure_time_ticker_entity | Fluent config applied | P2 |
| should_configure_cron_ticker_entity | Fluent config applied | P2 |
| should_configure_cron_occurrence_entity | Fluent config applied | P2 |

#### 3.4 EF Configurations (Unit)
**Files:** `Configurations/*.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| TimeTickerConfigurations_should_set_table_name | Schema validation | P3 |
| CronTickerConfigurations_should_set_indexes | Index verification | P3 |

---

### 4. Framework.Ticker.Caching.Redis

#### 4.1 TickerQRedisContext (Integration)
**File:** `TickerQRedisContext.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_check_redis_connection_availability | HasRedisConnection | P1 |
| should_cache_cron_expressions | Set and get | P1 |
| should_invalidate_cron_cache | RemoveAsync | P2 |

#### 4.2 NodeHeartBeatBackgroundService (Integration)
**File:** `NodeHeartBeatBackgroundService.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_register_node_heartbeat | Redis key set | P1 |
| should_detect_dead_nodes | Expired keys | P1 |
| should_release_dead_node_resources | Cleanup triggered | P2 |

---

### 5. Framework.Ticker.Dashboard

#### 5.1 DashboardEndpoints (Integration)
**File:** `Endpoints/DashboardEndpoints.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_get_auth_info | Mode and enabled | P1 |
| should_validate_auth_credentials | Success/401 | P1 |
| should_get_dashboard_options | Scheduler config | P1 |
| should_get_time_tickers | List retrieval | P1 |
| should_get_time_tickers_paginated | Pagination | P1 |
| should_create_chain_jobs | POST time-ticker | P1 |
| should_update_time_ticker | PUT with timezone | P1 |
| should_delete_time_ticker | DELETE by id | P1 |
| should_delete_time_tickers_batch | DELETE multiple | P2 |
| should_get_cron_tickers | List retrieval | P1 |
| should_get_cron_tickers_paginated | Pagination | P1 |
| should_add_cron_ticker | POST cron | P1 |
| should_update_cron_ticker | PUT cron | P1 |
| should_run_cron_ticker_on_demand | POST run | P2 |
| should_delete_cron_ticker | DELETE cron | P1 |
| should_get_cron_occurrences | By cronTickerId | P1 |
| should_get_cron_occurrences_paginated | Pagination | P2 |
| should_cancel_ticker | POST cancel | P2 |
| should_get_ticker_request | Byte payload | P2 |
| should_get_ticker_functions | Available functions | P2 |
| should_get_next_ticker | Next occurrence | P2 |
| should_stop_ticker_host | POST stop | P2 |
| should_start_ticker_host | POST start | P2 |
| should_restart_ticker_host | POST restart | P2 |
| should_get_ticker_host_status | IsRunning | P2 |
| should_get_last_week_job_status | Statistics | P3 |
| should_get_job_statuses | Overall stats | P3 |
| should_get_machine_jobs | By machine | P3 |
| should_require_auth_for_api_group | Host auth mode | P1 |

#### 5.2 AuthService (Unit)
**File:** `Authentication/AuthService.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_return_auth_info_for_basic_mode | Mode and timeout | P1 |
| should_authenticate_with_valid_basic_credentials | Success result | P1 |
| should_reject_invalid_basic_credentials | Failure result | P1 |
| should_authenticate_with_bearer_token | Token validation | P2 |
| should_authenticate_with_custom_handler | Delegate invoked | P2 |
| should_skip_auth_when_disabled | Anonymous allowed | P2 |

#### 5.3 AuthMiddleware (Unit)
**File:** `Authentication/AuthMiddleware.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_skip_excluded_paths | Auth info endpoint | P1 |
| should_challenge_unauthenticated_requests | 401 response | P1 |
| should_pass_authenticated_requests | Next invoked | P1 |

#### 5.4 TickerQNotificationHub (Integration)
**File:** `Hubs/TickerQNotificationHub.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_broadcast_ticker_created_event | SignalR message | P2 |
| should_broadcast_ticker_updated_event | SignalR message | P2 |
| should_broadcast_ticker_status_change | SignalR message | P2 |

#### 5.5 TickerDashboardRepository (Integration)
**File:** `Infrastructure/Dashboard/TickerDashboardRepository.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_get_time_ticker_graph_data | Aggregated data | P2 |
| should_get_cron_ticker_graph_data | Aggregated data | P2 |
| should_add_on_demand_cron_occurrence | Manual trigger | P2 |
| should_cancel_ticker_by_id | CancellationTokenSource | P2 |
| should_get_ticker_request_by_id | Byte payload | P2 |

---

### 6. Framework.Ticker.OpenTelemetry

#### 6.1 Instrumentation (Unit)
**Files:** `Instrumentation/*.cs`

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_start_job_activity_with_tags | Activity created | P2 |
| should_log_job_enqueued | Structured log | P2 |
| should_log_job_completed | Success/failure | P2 |
| should_log_job_failed_with_exception | Exception details | P2 |
| should_log_job_cancelled | Cancellation logged | P3 |
| should_log_job_skipped | Skip reason | P3 |

---

### 7. Framework.Ticker.SourceGenerator

#### 7.1 Function Discovery (Unit)
**Note:** Source generators are typically tested via compilation verification

| Test Case | Description | Priority |
|-----------|-------------|----------|
| should_generate_function_registration_code | Compilation output | P2 |
| should_discover_attributed_methods | Reflection equivalent | P3 |

---

## Test Summary

| Package | Unit Tests | Integration Tests | Existing | Total New |
|---------|-----------|------------------|----------|-----------|
| Abstractions | 38 | 0 | ~26 | ~12 |
| Core | 28 | 0 | 0 | ~28 |
| EntityFramework | 8 | 14 | 0 | ~22 |
| Caching.Redis | 2 | 4 | 0 | ~6 |
| Dashboard | 14 | 20 | 0 | ~34 |
| OpenTelemetry | 6 | 0 | 0 | ~6 |
| SourceGenerator | 2 | 0 | 0 | ~2 |
| **Total** | **98** | **38** | **~26** | **~110** |

**Grand Total: ~136 test cases** (~110 new, ~26 existing)

---

## Test Infrastructure Requirements

### New Test Projects Needed

1. **Framework.Ticker.Tests.Unit** (exists - extend)
   - Add Core scheduler tests
   - Add persistence provider tests

2. **Framework.Ticker.Tests.Integration** (new)
   - EF Core provider tests with SQL Server/PostgreSQL
   - Redis caching tests
   - Dashboard API tests

### Test Harness Requirements

```csharp
// Fake implementations for unit testing
public class FakeTickerClock : ITickerClock
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
}

public class FakeTickerPersistenceProvider<TTime, TCron>
    : ITickerPersistenceProvider<TTime, TCron>
    where TTime : TimeTickerEntity<TTime>, new()
    where TCron : CronTickerEntity, new()
{
    public List<TTime> TimeTickers { get; } = new();
    public List<TCron> CronTickers { get; } = new();
    // ... stub implementations
}

public class FakeTickerQHostScheduler : ITickerQHostScheduler
{
    public bool IsRunning { get; private set; }
    public int RestartCount { get; private set; }

    public Task StartAsync() { IsRunning = true; return Task.CompletedTask; }
    public Task StopAsync() { IsRunning = false; return Task.CompletedTask; }
    public void Restart() { RestartCount++; }
    public void RestartIfNeeded(DateTime? dateTime) { if (dateTime.HasValue) RestartCount++; }
}
```

### Integration Test Configuration

```csharp
// SQL Server test container
public class TickerSqlServerFixture : IAsyncLifetime
{
    public MsSqlContainer Container { get; private set; }
    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync()
    {
        Container = new MsSqlBuilder().Build();
        await Container.StartAsync();
    }

    public async Task DisposeAsync() => await Container.DisposeAsync();
}

// Redis test container
public class TickerRedisFixture : IAsyncLifetime
{
    public RedisContainer Container { get; private set; }
    public IConnectionMultiplexer Connection { get; private set; }

    public async Task InitializeAsync()
    {
        Container = new RedisBuilder().Build();
        await Container.StartAsync();
        Connection = await ConnectionMultiplexer.ConnectAsync(Container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        Connection?.Dispose();
        await Container.DisposeAsync();
    }
}
```

---

## Key Testing Patterns

### 1. Scheduler Testing Pattern
```csharp
[Fact]
public async Task should_execute_ticker_at_scheduled_time()
{
    // Given
    var clock = new FakeTickerClock { UtcNow = DateTime.UtcNow };
    var executed = false;
    var context = new InternalFunctionContext
    {
        TickerId = Guid.NewGuid(),
        FunctionName = "TestFunc",
        ExecutionTime = clock.UtcNow.AddSeconds(1),
        CachedDelegate = (_, _, _) => { executed = true; return Task.CompletedTask; }
    };

    // When
    await handler.ExecuteTaskAsync(context, isDue: false, AbortToken);

    // Then
    executed.Should().BeTrue();
    context.Status.Should().Be(TickerStatus.Done);
}
```

### 2. Retry Testing Pattern
```csharp
[Theory]
[InlineData(new[] { 1, 2, 3 }, 3, 4)] // 4 attempts (initial + 3 retries)
[InlineData(new[] { 0 }, 2, 3)]       // 3 attempts with 0-second intervals
public async Task should_retry_with_configured_intervals(
    int[] intervals, int maxRetries, int expectedAttempts)
{
    // Given
    var attempts = new List<int>();
    var context = CreateRetryContext(intervals, maxRetries,
        onExecute: (retryCount) => attempts.Add(retryCount));

    // When
    await handler.ExecuteTaskAsync(context, isDue: true, AbortToken);

    // Then
    attempts.Should().HaveCount(expectedAttempts);
}
```

### 3. Concurrency Testing Pattern
```csharp
[Fact]
public async Task should_acquire_lock_atomically()
{
    // Given
    var ticker = CreateTimeTicker();
    await provider.AddTimeTickers([ticker], AbortToken);

    // When - concurrent acquisition attempts
    var tasks = Enumerable.Range(0, 10)
        .Select(_ => provider.AcquireImmediateTimeTickersAsync([ticker.Id], AbortToken))
        .ToArray();
    var results = await Task.WhenAll(tasks);

    // Then - only one should succeed
    var acquired = results.Where(r => r.Length > 0).ToArray();
    acquired.Should().HaveCount(1);
}
```

---

## Notes

1. **Thread Safety**: `TickerExecutionContext` uses `Volatile.Read/Write` for thread-safe access - tests should verify this behavior
2. **Retry Intervals**: Default 30 seconds if not specified; tests use 0-second intervals for speed
3. **Lock Holder**: Uses `Environment.MachineName` by default - configurable via `SchedulerOptionsBuilder.NodeIdentifier`
4. **Cron Cache**: `ConcurrentDictionary` with whitespace normalization - test cache hits and misses
5. **Cascade Delete**: Time tickers with children - EF Core cascade delete vs in-memory manual cascade
