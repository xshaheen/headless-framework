# Test Case Design: Headless.Messaging.Core

**Package:** `src/Headless.Messaging.Core`
**Test Projects:** `Headless.Messaging.Core.Tests.Unit` ✅, `Headless.Messaging.Core.Tests.Harness` ✅
**Generated:** 2026-01-25

## Package Analysis

This is the main implementation package containing the outbox publisher, consumer registration, message dispatch, retry logic, and serialization.

### Critical Components

| File | Type | Priority |
|------|------|----------|
| `Internal/OutboxPublisher.cs` | Publisher impl | P1 |
| `Internal/IConsumerRegister.Default.cs` | Consumer lifecycle | P1 |
| `Internal/ISubscribeInvoker.Default.cs` | Message dispatch | P1 |
| `Internal/CompiledMessageDispatcher.cs` | Compiled dispatch | P1 |
| `Internal/IConsumerServiceSelector.Default.cs` | Consumer discovery | P1 |
| `Processor/IDispatcher.Default.cs` | Channel dispatcher | P1 |
| `Retry/ExponentialBackoffStrategy.cs` | Retry delay calc | P1 |
| `Retry/FixedIntervalBackoffStrategy.cs` | Retry delay calc | P2 |
| `Serialization/JsonUtf8Serializer.cs` | JSON serialization | P1 |
| `Configuration/MessagingOptions.cs` | Configuration | P2 |
| `ConsumerRegistry.cs` | Registration store | P2 |
| `ConsumerBuilder.cs` | Fluent builder | P2 |

## Existing Tests (Gap Analysis)

Current test coverage in `Headless.Messaging.Core.Tests.Unit`:
- ✅ ConsumerLifecycleTests
- ✅ ConsumerRegistryTests
- ✅ ConsumerServiceSelectorTests
- ✅ DispatcherTests
- ✅ SubscribeInvokerTests
- ✅ MessagingBuilderTests
- ✅ TypeSafePublishApiTests
- ✅ MessageTest, MessageExtensionTest
- ✅ IConsumeIntegrationTests
- ✅ MessageOrderingTests

### Missing Test Coverage

#### ExponentialBackoffStrategy Tests (CRITICAL - Thread Safety Bug)
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_calculate_exponential_delay` | P1 | Basic delay calculation |
| `should_apply_jitter` | P1 | Jitter range validation |
| `should_cap_at_max_delay` | P1 | Upper bound enforcement |
| `should_be_thread_safe` | P1 | **BUG: Current impl is NOT thread-safe** |
| `should_respect_retryable_exceptions` | P2 | Exception filtering |
| `should_return_false_for_non_retryable` | P2 | Non-retryable detection |

#### FixedIntervalBackoffStrategy Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_return_fixed_interval` | P2 | Constant delay |
| `should_respect_max_retries` | P2 | Max retry count |

#### OutboxPublisher Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_store_message_in_database` | P1 | Basic publish |
| `should_use_topic_mapping_when_configured` | P1 | Type-safe publish |
| `should_throw_when_no_topic_mapping_exists` | P1 | Missing mapping error |
| `should_include_custom_headers` | P1 | Header passthrough |
| `should_schedule_delayed_messages` | P1 | Delay scheduling |
| `should_participate_in_transaction` | P1 | Transactional publish |
| `should_not_send_if_transaction_rolls_back` | P1 | Rollback behavior |
| `should_generate_message_id` | P2 | Auto ID generation |
| `should_set_correlation_id` | P2 | Correlation propagation |

#### JsonUtf8Serializer Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_serialize_message_to_utf8` | P1 | Basic serialization |
| `should_deserialize_utf8_to_message` | P1 | Basic deserialization |
| `should_handle_null_message` | P1 | Null handling |
| `should_preserve_message_headers` | P1 | Header round-trip |
| `should_use_camel_case_by_default` | P2 | Naming policy |
| `should_handle_polymorphic_types` | P2 | Inheritance |

#### MessagingOptions Validation Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_validate_topic_name_length` | P1 | Max 255 chars |
| `should_validate_topic_name_characters` | P1 | Allowed chars only |
| `should_reject_leading_dots` | P1 | No leading dots |
| `should_reject_consecutive_dots` | P1 | No .. patterns |
| `should_set_default_retry_count` | P2 | Default value |
| `should_set_default_parallel_settings` | P2 | Default values |

#### IConsumerRegister.Default Tests (Partial Coverage)
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_start_consumer_clients` | P1 | Startup |
| `should_restart_on_broker_failure` | P1 | Resilience |
| `should_track_health_status` | P1 | Health reporting |
| `should_stop_gracefully` | P1 | Shutdown |
| `should_use_volatile_for_isHealthy` | P1 | **BUG: Missing volatile** |

#### Dispatcher Channel Tests (Partial Coverage)
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_respect_bounded_capacity` | P2 | Backpressure |
| `should_process_messages_in_order` | P2 | FIFO guarantee |
| `should_complete_channel_on_dispose` | P2 | Cleanup |

## Integration Tests Needed

Create `Headless.Messaging.Core.Tests.Integration`:

| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_publish_and_consume_message_end_to_end` | P1 | Full pipeline |
| `should_retry_failed_messages` | P1 | Retry behavior |
| `should_dead_letter_after_max_retries` | P1 | DLQ behavior |
| `should_process_delayed_messages_at_scheduled_time` | P1 | Delay scheduler |
| `should_handle_concurrent_consumers` | P1 | Parallelism |
| `should_respect_consumer_groups` | P2 | Group isolation |

## Test Infrastructure

```csharp
// Use InMemoryQueue + InMemoryStorage for integration tests
services.AddMessaging(o => o
    .UseInMemoryQueue()
    .UseInMemoryStorage()
    .AddConsumer<TestMessage, TestConsumer>());
```

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 50+ |
| Existing Unit Tests | ~15 test files |
| **Missing Unit Tests** | **~40 cases** |
| **Missing Integration Tests** | **~10 cases** |
| Priority | P1 - Core functionality |

**Note:** The Harness project provides shared fixtures. Consider adding more builders for test message generation.
