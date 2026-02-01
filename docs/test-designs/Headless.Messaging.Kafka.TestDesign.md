# Test Case Design: Headless.Messaging.Kafka

**Package:** `src/Headless.Messaging.Kafka`
**Test Projects:** None ❌ (needs creation)
**Generated:** 2026-01-25

## Package Analysis

Kafka transport with connection pooling.

| File | Type | Priority |
|------|------|----------|
| `KafkaTransport.cs` | Transport impl | P1 |
| `KafkaConsumerClient.cs` | Consumer client | P1 |
| `KafkaConsumerClientFactory.cs` | Factory | P2 |
| `IKafkaConnectionPool.cs` | Pool interface | P2 |
| `MessagingKafkaOptions.cs` | Options | P2 |
| `KafkaHeaders.cs` | Header constants | P3 |
| `Setup.cs` | DI registration | P3 |

## Known Issues

1. **Incorrectly includes NATS.Client dependency** (todo #016)
2. **Missing options validator** - Unlike RabbitMQ, no validation

## Test Recommendation

**Create: `Headless.Messaging.Kafka.Tests.Unit`**

### Unit Tests Needed

#### MessagingKafkaOptions Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_require_bootstrap_servers` | P1 | Required config |
| `should_have_sensible_defaults` | P2 | Default values |
| `should_validate_group_id` | P2 | Consumer group |

#### IKafkaConnectionPool Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_producer_on_first_rent` | P1 | Lazy init |
| `should_reuse_producers` | P1 | Pooling behavior |
| `should_dispose_producers_on_cleanup` | P2 | Resource cleanup |

#### KafkaTransport Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_produce_message_to_topic` | P1 | Basic produce |
| `should_include_message_headers` | P1 | Header encoding |
| `should_use_message_id_as_key` | P1 | Partitioning |
| `should_use_custom_key_when_provided` | P2 | KafkaKey header |
| `should_return_producer_to_pool` | P1 | Pool usage |
| `should_handle_PersistenceStatus_correctly` | P1 | Ack handling |

#### KafkaConsumerClient Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_subscribe_to_topic` | P1 | Subscription |
| `should_invoke_callback_on_message` | P1 | Message receipt |
| `should_decode_headers_correctly` | P1 | Header decoding |
| `should_commit_offsets_on_commit` | P1 | Offset management |
| `should_not_commit_on_reject` | P1 | Rejection handling |
| `should_handle_rebalance` | P2 | Consumer group |
| `should_use_static_lock` | P2 | **Issue: Static lock** |

### Integration Tests Needed

**Create: `Headless.Messaging.Kafka.Tests.Integration`**

| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_produce_and_consume_message` | P1 | End-to-end |
| `should_handle_broker_failover` | P1 | Resilience |
| `should_respect_consumer_groups` | P1 | Group semantics |
| `should_handle_partition_assignment` | P2 | Partitioning |

## Test Infrastructure

```csharp
// Use Testcontainers for Kafka
public class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _container = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:7.5.0")
        .Build();

    public string BootstrapServers => _container.GetBootstrapAddress();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 7 |
| **Required Unit Tests** | **~20 cases** (new project) |
| **Required Integration Tests** | **~5 cases** (new project) |
| Priority | P1 - Production transport |

**Note:** Add options validator similar to RabbitMQ to ensure proper configuration.
