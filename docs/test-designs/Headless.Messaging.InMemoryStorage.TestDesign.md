# Test Case Design: Headless.Messaging.InMemoryStorage

**Package:** `src/Headless.Messaging.InMemoryStorage`
**Test Projects:** None ❌ (needs creation)
**Generated:** 2026-01-25

## Package Analysis

In-memory storage for testing and development. Implements IDataStorage for outbox persistence.

| File | Type | Priority |
|------|------|----------|
| `InMemoryDataStorage.cs` | Storage impl | P1 |
| `InMemoryMonitoringApi.cs` | Monitoring impl | P2 |
| `InMemoryOutboxTransaction.cs` | Transaction impl | P1 |
| `InMemoryStorageInitializer.cs` | Initializer | P3 |
| `MemoryMessage.cs` | Message model | P2 |
| `Setup.cs` | DI registration | P3 |

## Test Recommendation

**Create: `Headless.Messaging.InMemoryStorage.Tests.Unit`**

### InMemoryDataStorage Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_store_published_message` | P1 | Basic store |
| `should_store_received_message` | P1 | Basic store |
| `should_retrieve_message_by_id` | P1 | Retrieval |
| `should_change_message_status` | P1 | Status transition |
| `should_acquire_distributed_lock` | P1 | Locking |
| `should_release_distributed_lock` | P1 | Lock release |
| `should_renew_lock_ttl` | P2 | TTL extension |
| `should_get_messages_for_retry` | P1 | Retry query |
| `should_get_delayed_messages` | P1 | Scheduled messages |
| `should_delete_expired_messages` | P2 | Expiry cleanup |
| `should_be_thread_safe` | P1 | Concurrent access |

### InMemoryOutboxTransaction Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_commit_pending_messages` | P1 | Commit |
| `should_rollback_pending_messages` | P1 | Rollback |
| `should_auto_commit_on_dispose` | P2 | Auto-commit |
| `should_track_transaction_state` | P2 | State management |

### InMemoryMonitoringApi Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_return_message_count_by_status` | P2 | Statistics |
| `should_query_published_messages` | P2 | Published query |
| `should_query_received_messages` | P2 | Received query |
| `should_support_pagination` | P2 | Paging |
| `should_support_content_search` | P3 | Search |

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 6 |
| **Required Unit Tests** | **~20 cases** |
| Integration Tests | 0 (tested via Core integration) |
| Priority | P2 - Test infrastructure |

**Note:** Critical for integration test reliability. Must match behavior of real storage implementations.
