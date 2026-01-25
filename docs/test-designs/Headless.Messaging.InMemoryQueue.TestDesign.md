# Test Case Design: Headless.Messaging.InMemoryQueue

**Package:** `src/Headless.Messaging.InMemoryQueue`
**Test Projects:** None ❌ (needs creation)
**Generated:** 2026-01-25

## Package Analysis

In-memory transport for testing and development. Critical for test infrastructure.

| File | Type | Priority |
|------|------|----------|
| `InMemoryConsumerClient.cs` | Consumer client | P1 |
| `InMemoryConsumerClientFactory.cs` | Factory | P2 |
| `InMemoryQueueTransport.cs` | Transport impl | P1 |
| `MemoryQueue.cs` | Queue data structure | P1 |
| `Setup.cs` | DI registration | P2 |

## Test Recommendation

**Create: `Headless.Messaging.InMemoryQueue.Tests.Unit`**

### MemoryQueue Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_enqueue_and_dequeue_messages` | P1 | Basic FIFO |
| `should_support_multiple_topics` | P1 | Topic isolation |
| `should_return_null_when_empty` | P1 | Empty queue |
| `should_be_thread_safe` | P1 | Concurrent access |
| `should_support_consumer_groups` | P2 | Group partitioning |
| `should_clear_queue_on_dispose` | P2 | Cleanup |

### InMemoryConsumerClient Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_receive_messages_from_queue` | P1 | Basic consumption |
| `should_invoke_callback_on_message` | P1 | Callback pattern |
| `should_commit_consumed_messages` | P1 | Commit semantics |
| `should_reject_and_requeue` | P1 | Reject behavior |
| `should_stop_listening_on_dispose` | P2 | Cleanup |

### InMemoryQueueTransport Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_send_message_to_queue` | P1 | Basic send |
| `should_return_success_result` | P1 | Result type |
| `should_support_message_headers` | P2 | Header passthrough |

### Setup Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_register_transport_services` | P2 | DI registration |
| `should_register_consumer_factory` | P2 | Factory registration |

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 5 |
| **Required Unit Tests** | **~15 cases** |
| Integration Tests | 0 (tested via Core integration) |
| Priority | P2 - Test infrastructure |

**Note:** This package is primarily used in tests for other packages. Its correctness is critical for test reliability.
