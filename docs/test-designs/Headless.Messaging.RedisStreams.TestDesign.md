# Test Case Design: Headless.Messaging.RedisStreams

**Package:** `src/Headless.Messaging.RedisStreams`
**Test Projects:** None ❌ (needs creation)
**Generated:** 2026-01-25

## Package Analysis

Redis Streams transport with consumer groups.

| File | Type | Priority |
|------|------|----------|
| `RedisTransport.cs` | Transport impl | P1 |
| `IConsumerClient.Redis.cs` | Consumer client | P1 |
| `IConsumerClientFactory.Redis.cs` | Factory | P2 |
| `IConnectionPool.cs` | Pool interface | P2 |
| `IConnectionPool.Default.cs` | Pool impl | P1 (CRITICAL BUG) |
| `IConnectionPool.LazyConnection.cs` | Lazy wrapper | P2 |
| `IRedisStreamManager.cs` | Stream operations | P2 |
| `IRedisStreamManagerDefault.cs` | Stream impl | P2 |
| `MessagingRedisOptions.cs` | Options | P2 |
| `TransportMessage.Redis.cs` | Message format | P2 |
| `Setup.cs` | DI registration | P3 |

## Known Issues (CRITICAL)

1. **Sync-over-async in constructor** (todo #003) - Can deadlock

## Test Recommendation

**Create: `Headless.Messaging.RedisStreams.Tests.Unit`**

### Unit Tests Needed

#### IConnectionPool.Default Tests (CRITICAL)
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_not_block_in_constructor` | P1 | **BUG: Sync-over-async** |
| `should_create_connection_lazily` | P1 | Lazy init |
| `should_reuse_connections` | P1 | Pooling |
| `should_handle_connection_failure` | P1 | Resilience |
| `should_dispose_connections` | P2 | Cleanup |

#### RedisTransport Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_add_message_to_stream` | P1 | XADD |
| `should_include_message_fields` | P1 | Field encoding |
| `should_handle_stream_creation` | P2 | Auto-create |
| `should_return_connection_to_pool` | P1 | Pool usage |

#### RedisConsumerClient Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_consumer_group` | P1 | XGROUP CREATE |
| `should_read_from_stream` | P1 | XREADGROUP |
| `should_invoke_callback_on_message` | P1 | Message receipt |
| `should_ack_on_commit` | P1 | XACK |
| `should_claim_pending_messages` | P2 | XCLAIM |
| `should_handle_consumer_group_not_exists` | P2 | Auto-create |

#### IRedisStreamManager Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_stream_if_not_exists` | P2 | Stream creation |
| `should_create_consumer_group` | P2 | Group creation |
| `should_get_pending_messages` | P2 | XPENDING |
| `should_trim_old_messages` | P3 | XTRIM |

#### MessagingRedisOptions Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_require_connection_string` | P1 | Required config |
| `should_support_sentinel_config` | P2 | High availability |
| `should_have_default_stream_options` | P2 | Defaults |

### Integration Tests Needed

**Create: `Headless.Messaging.RedisStreams.Tests.Integration`**

| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_publish_and_consume_message` | P1 | End-to-end |
| `should_handle_consumer_group_rebalance` | P2 | Group semantics |
| `should_process_pending_messages` | P2 | Recovery |

## Critical Bug Test

```csharp
[Fact]
public void constructor_should_not_block()
{
    // This test verifies the sync-over-async fix
    var stopwatch = Stopwatch.StartNew();

    // Act - constructor should return immediately
    var pool = new RedisConnectionPool(options, logger);

    // Assert - should not have blocked for connection
    stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
}
```

## Test Infrastructure

```csharp
// Use Testcontainers for Redis
public class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 11 |
| **Required Unit Tests** | **~25 cases** (new project) |
| **Required Integration Tests** | **~5 cases** (new project) |
| Priority | **P1 CRITICAL** - Deadlock bug |

**CRITICAL:** The sync-over-async in constructor can deadlock the application during startup.
