# Test Case Design: Headless.Messaging.Abstractions

**Package:** `src/Headless.Messaging.Abstractions`
**Test Projects:** None needed (mostly abstractions)
**Generated:** 2026-01-25

## Package Analysis

This package contains primarily abstractions (interfaces) and simple types:

| File | Type | Testable |
|------|------|----------|
| `IConsume.cs` | Interface | No |
| `IOutboxPublisher.cs` | Interface | No |
| `IConsumerBuilder.cs` | Interface | No |
| `IConsumerLifecycle.cs` | Interface | No |
| `IConsumerRegistry.cs` | Interface | No |
| `IMessagingBuilder.cs` | Interface | No |
| `IOutboxTransaction.cs` | Interface | No |
| `IConsumeFilter.cs` | Interface + abstract base | Minimal |
| `ConsumeContext.cs` | Record with validation | Yes |
| `ConsumerMetadata.cs` | Attribute | Minimal |
| `MessagingConventions.cs` | Static config | Yes |
| `ServiceCollectionConsumerBuilder.cs` | Builder impl | Yes |
| `ServiceCollectionExtensions.cs` | Extension methods | Yes |
| `Messages/Message.cs` | Record | Yes |
| `Messages/MediumMessage.cs` | Class | Yes |
| `Messages/MessageHeader.cs` | Dictionary wrapper | Yes |
| `Messages/Headers.cs` | Constants | No |
| `Messages/ConsumerContext.cs` | Record | Minimal |
| `Messages/ConsumerExecutorDescriptor.cs` | Record | No |
| `Messages/MessageType.cs` | Enum | No |
| `Retry/IRetryBackoffStrategy.cs` | Interface | No |

## Test Recommendation

**Create: `Headless.Messaging.Abstractions.Tests.Unit`**

### Unit Tests Required

#### ConsumeContext<T> Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_context_with_valid_properties` | P1 | Valid instantiation |
| `should_throw_when_messageId_is_null` | P1 | Validation enforcement |
| `should_throw_when_messageId_is_whitespace` | P1 | Validation enforcement |
| `should_allow_null_correlationId` | P1 | Null is valid for correlation |
| `should_throw_when_correlationId_is_empty_string` | P1 | Empty string not allowed |
| `should_expose_message_payload` | P1 | Message property works |
| `should_expose_headers` | P1 | Headers accessible |
| `should_expose_timestamp` | P1 | Timestamp accessible |
| `should_expose_topic` | P1 | Topic accessible |

#### Message Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_message_with_required_properties` | P1 | Basic construction |
| `should_generate_unique_id_when_not_provided` | P2 | Auto ID generation |
| `should_use_provided_id` | P2 | Explicit ID |
| `should_have_immutable_headers` | P2 | Headers cannot be modified after creation |

#### MessageHeader Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_support_dictionary_operations` | P1 | Add, get, contains |
| `should_support_case_sensitive_keys` | P2 | Key sensitivity |
| `should_allow_null_values` | P2 | Null header values allowed |

#### MessagingConventions Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_have_default_prefix` | P2 | Default topic prefix |
| `should_allow_custom_prefix` | P2 | Custom prefix setting |
| `should_generate_topic_names` | P2 | Topic name generation |

#### ServiceCollectionConsumerBuilder Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_register_consumer_with_topic` | P1 | Basic consumer registration |
| `should_register_consumer_with_group` | P1 | Consumer group |
| `should_support_fluent_configuration` | P2 | Method chaining |
| `should_validate_topic_name` | P2 | Invalid topic rejected |

#### ConsumeFilter Base Class Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_return_completed_task_for_default_executing` | P2 | Base class behavior |
| `should_return_completed_task_for_default_executed` | P2 | Base class behavior |
| `should_return_completed_task_for_default_exception` | P2 | Base class behavior |

## Test Infrastructure

```csharp
// Sample test messages
public sealed record TestMessage(string Id, string Content);
public sealed record OrderPlaced(Guid OrderId, decimal Amount);

// Test consumer for registration tests
public sealed class TestConsumer : IConsume<TestMessage>
{
    public ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken ct)
        => ValueTask.CompletedTask;
}
```

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 21 |
| Interfaces | 8 |
| Implementations | 6 |
| Records/Classes | 7 |
| **Recommended Unit Tests** | **25-30** |
| Integration Tests | 0 |

**Rationale:** Most interfaces are tested via their implementations in Core. The ConsumeContext validation and ServiceCollectionConsumerBuilder are the key testable components.
