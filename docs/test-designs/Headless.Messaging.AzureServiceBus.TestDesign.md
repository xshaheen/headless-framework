# Test Case Design: Headless.Messaging.AzureServiceBus

**Package:** `src/Headless.Messaging.AzureServiceBus`
**Test Projects:** `Headless.Messaging.AzureServiceBus.Tests.Unit` ✅
**Generated:** 2026-01-25

## Package Analysis

Azure Service Bus transport with topic/subscription support.

| File | Type | Priority |
|------|------|----------|
| `AzureServiceBusTransport.cs` | Transport impl | P1 |
| `AzureServiceBusConsumerClient.cs` | Consumer client | P1 |
| `AzureServiceBusConsumerClientFactory.cs` | Factory | P2 |
| `AzureServiceBusOptions.cs` | Options | P2 |
| `IAzureServiceBusClientFactory.cs` | Client factory | P2 |
| `Producer/ServiceBusProducerDescriptor.cs` | Producer config | P3 |
| `Producer/ServiceBusProducerDescriptorBuilder.cs` | Builder | P3 |
| `Producer/IServiceBusProducerDescriptorFactory.cs` | Factory | P3 |
| `Setup.cs` | DI registration | P3 |

## Test Recommendation

### Additional Unit Tests Needed

#### AzureServiceBusTransport Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_send_message_to_topic` | P1 | Basic send |
| `should_include_message_properties` | P1 | Property encoding |
| `should_set_message_id` | P1 | Message ID |
| `should_set_correlation_id` | P2 | Correlation |
| `should_handle_send_failure` | P1 | Error handling |

#### AzureServiceBusConsumerClient Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_subscription_if_not_exists` | P1 | Auto-provision |
| `should_receive_messages_from_subscription` | P1 | Message receipt |
| `should_complete_message_on_commit` | P1 | Completion |
| `should_abandon_message_on_reject` | P1 | Rejection |
| `should_dead_letter_on_max_retries` | P2 | DLQ |
| `should_handle_session_based_subscriptions` | P2 | Sessions |

#### AzureServiceBusOptions Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_require_connection_string` | P1 | Required config |
| `should_support_managed_identity` | P2 | Azure AD auth |
| `should_have_default_options` | P2 | Defaults |

#### ServiceBusProducerDescriptor Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_descriptor_from_type` | P2 | Type inference |
| `should_use_provided_topic_path` | P2 | Explicit topic |
| `should_support_builder_pattern` | P3 | Fluent API |

### Integration Tests Needed

**Create: `Headless.Messaging.AzureServiceBus.Tests.Integration`**

| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_publish_and_consume_message` | P1 | End-to-end |
| `should_auto_create_subscription` | P1 | Provisioning |
| `should_handle_dead_letter` | P2 | DLQ behavior |

## Test Infrastructure

```csharp
// Use Azure Service Bus Emulator or real connection for integration tests
// Note: No official Testcontainer for Service Bus

public class ServiceBusFixture : IAsyncLifetime
{
    // Option 1: Use connection string from environment
    public string ConnectionString =>
        Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION_STRING")
        ?? throw new SkipException("Service Bus connection not configured");

    // Option 2: Use Azurite (limited Service Bus support)
}
```

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 9 |
| Existing Unit Tests | Some ✅ |
| **Additional Unit Tests Needed** | **~15 cases** |
| **Integration Tests Needed** | **~5 cases** (new project) |
| Priority | P2 - Production transport |

**Note:** Azure Service Bus integration tests are challenging due to lack of local emulator. Consider using real Azure resources in CI/CD with proper cleanup.
