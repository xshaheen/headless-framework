# Test Case Design: Headless.Messaging.Pulsar

**Package:** `src/Headless.Messaging.Pulsar`
**Test Projects:** None ❌ (needs creation)
**Generated:** 2026-01-25

## Package Analysis

Apache Pulsar transport.

| File | Type | Priority |
|------|------|----------|
| `PulsarTransport.cs` | Transport impl | P1 |
| `PulsarConsumerClient.cs` | Consumer client | P1 |
| `PulsarConsumerClientFactory.cs` | Factory | P2 |
| `IConnectionFactory.cs` | Connection factory | P2 |
| `MessagingPulsarOptions.cs` | Options | P2 |
| `PulsarHeaders.cs` | Header encoding | P3 |
| `Setup.cs` | DI registration | P3 |

## Test Recommendation

**Create: `Headless.Messaging.Pulsar.Tests.Unit`**

### Unit Tests Needed

#### PulsarTransport Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_send_message_to_topic` | P1 | Basic send |
| `should_include_message_properties` | P1 | Header encoding |
| `should_handle_send_failure` | P1 | Error handling |
| `should_use_connection_pool` | P2 | Pool usage |

#### PulsarConsumerClient Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_subscribe_to_topic` | P1 | Subscription |
| `should_invoke_callback_on_message` | P1 | Message receipt |
| `should_ack_on_commit` | P1 | Acknowledgment |
| `should_nack_on_reject` | P1 | Rejection |
| `should_support_subscription_types` | P2 | Exclusive/Shared/Failover |
| `should_stop_on_dispose` | P2 | Cleanup |

#### MessagingPulsarOptions Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_require_service_url` | P1 | Required config |
| `should_have_default_subscription_type` | P2 | Default |
| `should_support_auth_config` | P2 | Authentication |

#### IConnectionFactory Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_client_with_service_url` | P1 | Client creation |
| `should_configure_authentication` | P2 | Auth setup |

### Integration Tests Needed

**Create: `Headless.Messaging.Pulsar.Tests.Integration`**

| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_publish_and_consume_message` | P1 | End-to-end |
| `should_handle_topic_creation` | P2 | Auto-provision |
| `should_support_dead_letter_topic` | P2 | DLT |

## Test Infrastructure

```csharp
// Use Testcontainers for Pulsar
public class PulsarFixture : IAsyncLifetime
{
    private readonly PulsarContainer _container = new PulsarBuilder()
        .WithImage("apachepulsar/pulsar:3.0.0")
        .Build();

    public string ServiceUrl => _container.GetBrokerAddress();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 7 |
| **Required Unit Tests** | **~15 cases** (new project) |
| **Required Integration Tests** | **~5 cases** (new project) |
| Priority | P3 - Less common transport |
