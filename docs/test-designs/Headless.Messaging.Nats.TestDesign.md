# Test Case Design: Headless.Messaging.Nats

**Package:** `src/Headless.Messaging.Nats`
**Test Projects:** None ❌ (needs creation)
**Generated:** 2026-01-25

## Package Analysis

NATS transport with JetStream support.

| File | Type | Priority |
|------|------|----------|
| `NatsTransport.cs` | Transport impl | P1 |
| `NATSConsumerClient.cs` | Consumer client | P1 (CRITICAL BUG) |
| `NATSConsumerClientFactory.cs` | Factory | P2 |
| `INatsConnectionPool.cs` | Pool interface | P2 |
| `MessagingNatsOptions.cs` | Options | P2 |
| `Setup.cs` | DI registration | P3 |

## Known Issues (CRITICAL)

1. **async void message handler** (todo #002) - Can crash the application
2. **Static lock** (todo #008) - Cross-instance contention

## Test Recommendation

**Create: `Headless.Messaging.Nats.Tests.Unit`**

### Unit Tests Needed (High Priority Due to Bug)

#### NATSConsumerClient Tests (CRITICAL)
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `MessageHandler_should_not_use_async_void` | P1 | **BUG: Verify fix** |
| `MessageHandler_should_handle_exceptions_gracefully` | P1 | No crash on error |
| `should_subscribe_to_subject` | P1 | Basic subscription |
| `should_invoke_callback_on_message` | P1 | Message receipt |
| `should_ack_on_commit` | P1 | JetStream ack |
| `should_nak_on_reject` | P1 | JetStream nak |
| `should_use_instance_lock_not_static` | P1 | **BUG: Static lock** |
| `should_unsubscribe_on_dispose` | P2 | Cleanup |

#### INatsConnectionPool Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_connection_on_first_rent` | P1 | Lazy init |
| `should_reuse_connections` | P1 | Pooling |
| `should_handle_disconnect` | P1 | Reconnection |
| `should_dispose_connections` | P2 | Cleanup |

#### NatsTransport Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_publish_message_to_subject` | P1 | Basic publish |
| `should_include_message_headers` | P1 | Header encoding |
| `should_return_connection_to_pool` | P1 | Pool usage |
| `should_handle_publish_failure` | P1 | Error handling |

#### MessagingNatsOptions Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_have_default_server_url` | P2 | Default localhost |
| `should_support_multiple_servers` | P2 | Cluster config |
| `should_parse_credentials_from_url` | P2 | Auth in URL |

### Integration Tests Needed

**Create: `Headless.Messaging.Nats.Tests.Integration`**

| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_publish_and_consume_message` | P1 | End-to-end |
| `should_handle_server_disconnect` | P1 | Resilience |
| `should_support_jetstream` | P2 | JetStream features |
| `should_not_crash_on_handler_exception` | P1 | **BUG verification** |

## Test Infrastructure

```csharp
// Use Testcontainers for NATS
public class NatsFixture : IAsyncLifetime
{
    private readonly NatsContainer _container = new NatsBuilder()
        .WithImage("nats:2.10-alpine")
        .WithCommand("--jetstream")
        .Build();

    public string ServerUrl => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

## Critical Bug Test

```csharp
[Fact]
public async Task handler_exception_should_not_crash_process()
{
    // Arrange - consumer that throws
    var consumer = new ThrowingConsumer();

    // Act - publish message that triggers exception
    await _publisher.PublishAsync("test", new TestMessage());

    // Assert - process should still be alive, exception logged
    // This tests the async void -> async Task fix
}
```

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 6 |
| **Required Unit Tests** | **~20 cases** (new project) |
| **Required Integration Tests** | **~5 cases** (new project) |
| Priority | **P1 CRITICAL** - Application crash bug |

**CRITICAL:** The async void bug can crash the entire application. This package needs immediate test coverage to verify the fix.
