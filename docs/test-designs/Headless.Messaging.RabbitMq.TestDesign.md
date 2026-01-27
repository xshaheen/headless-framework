# Test Case Design: Headless.Messaging.RabbitMq

**Package:** `src/Headless.Messaging.RabbitMq`
**Test Projects:** `Headless.Messaging.RabbitMq.Tests.Unit` ✅
**Generated:** 2026-01-25

## Package Analysis

RabbitMQ transport with connection pooling and channel management.

| File | Type | Priority |
|------|------|----------|
| `RabbitMqTransport.cs` | Transport impl | P1 |
| `RabbitMqConsumerClient.cs` | Consumer client | P1 |
| `RabbitMqConsumerClientFactory.cs` | Factory | P2 |
| `IConnectionChannelPool.cs` | Pool interface | P2 |
| `IConnectionChannelPool.Default.cs` | Pool impl | P1 |
| `MessagingRabbitMqOptions.cs` | Options | P2 |
| `RabbitMqOptionsValidator.cs` | Options validator | P1 |
| `RabbitMqValidation.cs` | Topic validation | P2 |
| `QueueArgumentsOptions.cs` | Queue config | P3 |
| `Setup.cs` | DI registration | P3 |

## Test Recommendation

### Existing Tests - Gap Analysis

Review `RabbitMq.Tests.Unit` for coverage of core scenarios.

### Additional Unit Tests Needed

#### RabbitMqOptionsValidator Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_reject_guest_username` | P1 | Security - no default creds |
| `should_reject_guest_password` | P1 | Security - no default creds |
| `should_require_host_name` | P1 | Required config |
| `should_validate_port_range` | P2 | Valid port number |
| `should_accept_valid_credentials` | P2 | Happy path |

#### IConnectionChannelPool Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_connection_on_first_rent` | P1 | Lazy init |
| `should_reuse_channels` | P1 | Pooling behavior |
| `should_respect_pool_size_limit` | P1 | Bounded pool |
| `should_recreate_closed_channels` | P1 | Resilience |
| `should_handle_broker_disconnect` | P1 | Reconnection |
| `should_dispose_all_channels` | P2 | Cleanup |

#### RabbitMqTransport Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_publish_message_to_exchange` | P1 | Basic publish |
| `should_set_persistent_delivery_mode` | P1 | Durability |
| `should_include_message_headers` | P1 | Header passthrough |
| `should_validate_topic_name` | P1 | Topic validation |
| `should_return_channel_to_pool_after_send` | P1 | Pool usage |
| `should_handle_AlreadyClosedException` | P1 | Error handling |

#### RabbitMqConsumerClient Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_declare_queue_on_subscribe` | P1 | Queue creation |
| `should_bind_queue_to_exchange` | P1 | Binding |
| `should_invoke_callback_on_message` | P1 | Message receipt |
| `should_ack_on_commit` | P1 | Acknowledgment |
| `should_nack_on_reject` | P1 | Rejection |
| `should_unsubscribe_on_dispose` | P2 | Cleanup |

#### RabbitMqValidation Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_accept_valid_topic_names` | P2 | Valid names |
| `should_reject_topic_with_invalid_chars` | P2 | Validation |
| `should_reject_empty_topic` | P2 | Required |

### Integration Tests Needed

**Create: `Headless.Messaging.RabbitMq.Tests.Integration`**

| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_publish_and_consume_message` | P1 | End-to-end |
| `should_handle_broker_restart` | P1 | Resilience |
| `should_support_consumer_groups` | P2 | Group isolation |
| `should_dead_letter_rejected_messages` | P2 | DLQ |

## Test Infrastructure

```csharp
// Use Testcontainers for RabbitMQ
public class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 10 |
| Existing Unit Tests | Some ✅ |
| **Additional Unit Tests Needed** | **~20 cases** |
| **Integration Tests Needed** | **~5 cases** (new project) |
| Priority | P1 - Primary transport |

**Note:** RabbitMQ is often the primary production transport. Connection pool resilience is critical.
