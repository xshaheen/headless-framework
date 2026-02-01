# Test Case Design: Headless.Messaging (All Packages)

**Packages:**
- `src/Headless.Messaging.Abstractions`
- `src/Headless.Messaging.Core`
- `src/Headless.Messaging.AwsSqs`
- `src/Headless.Messaging.AzureServiceBus`
- `src/Headless.Messaging.RabbitMq`
- `src/Headless.Messaging.Kafka`
- `src/Headless.Messaging.Nats`
- `src/Headless.Messaging.Pulsar`
- `src/Headless.Messaging.RedisStreams`
- `src/Headless.Messaging.PostgreSql`
- `src/Headless.Messaging.SqlServer`
- `src/Headless.Messaging.InMemoryQueue`
- `src/Headless.Messaging.InMemoryStorage`
- `src/Headless.Messaging.OpenTelemetry`
- `src/Headless.Messaging.Dashboard`
- `src/Headless.Messaging.Dashboard.K8s`

**Existing Test Projects:**
- `tests/Headless.Messaging.Core.Tests.Unit` (~2982 lines)
- `tests/Headless.Messaging.Core.Tests.Harness`
- `tests/Headless.Messaging.AwsSqs.Tests.Unit`
- `tests/Headless.Messaging.AwsSqs.Tests.Integration`
- `tests/Headless.Messaging.AzureServiceBus.Tests.Unit`
- `tests/Headless.Messaging.PostgreSql.Tests.Unit`
- `tests/Headless.Messaging.PostgreSql.Tests.Integration`
- `tests/Headless.Messaging.SqlServer.Tests.Unit`
- `tests/Headless.Messaging.SqlServer.Tests.Integration`
- `tests/Headless.Messaging.RabbitMq.Tests.Unit`

**Generated:** 2026-01-25

---

## Package Analysis

### Headless.Messaging.Abstractions

| File | Purpose | Testable |
|------|---------|----------|
| `IConsume.cs` | Message consumer interface | Low (interface) |
| `IOutboxPublisher.cs` | Outbox publisher interface | Low (interface) |
| `IConsumerRegistry.cs` | Consumer registry interface | Low (interface) |
| `IConsumerBuilder.cs` | Consumer builder interface | Low (interface) |
| `IMessagingBuilder.cs` | Messaging builder interface | Low (interface) |
| `IOutboxTransaction.cs` | Transaction interface | Low (interface) |
| `IConsumerLifecycle.cs` | Consumer lifecycle interface | Low (interface) |
| `IConsumeFilter.cs` | Consume filter interface | Low (interface) |
| `ConsumeContext.cs` | Message consumption context | High |
| `ConsumerMetadata.cs` | Consumer metadata record | Medium |
| `MessagingConventions.cs` | Topic naming conventions | High |
| `MessagingConventionsExtensions.cs` | Convention extensions | High |
| `ServiceCollectionConsumerBuilder.cs` | DI consumer builder | Medium |
| `ServiceCollectionExtensions.cs` | DI extensions | Medium |
| `Messages/Message.cs` | Message class with extensions | High |
| `Messages/MediumMessage.cs` | Persisted message class | Medium |
| `Messages/ConsumerContext.cs` | Consumer context | Medium |
| `Messages/ConsumerExecutorDescriptor.cs` | Executor descriptor | Medium |
| `Messages/MessageType.cs` | Message type enum | Low (enum) |
| `Messages/Headers.cs` | Header constants | Low (constants) |
| `Messages/MessageHeader.cs` | Header dictionary | Medium |
| `Retry/IRetryBackoffStrategy.cs` | Backoff strategy interface | Low (interface) |

### Headless.Messaging.Core

| File | Purpose | Testable |
|------|---------|----------|
| `ConsumerRegistry.cs` | Central consumer registry | High (covered) |
| `ConsumerBuilder.cs` | Consumer builder impl | High |
| `Retry/ExponentialBackoffStrategy.cs` | Exponential backoff with jitter | High |
| `Retry/FixedIntervalBackoffStrategy.cs` | Fixed interval backoff | High |
| `Serialization/JsonUtf8Serializer.cs` | JSON serializer | High |
| `Serialization/ISerializer.cs` | Serializer interface | Low (interface) |
| `Configuration/MessagingBuilder.cs` | Messaging builder impl | High |
| `Configuration/MessagingOptions.cs` | Messaging options | Medium |
| `Configuration/IMessagesOptionsExtension.cs` | Options extension interface | Low (interface) |
| `Messages/OperateResult.cs` | Operation result | Medium |
| `Messages/TransportMessage.cs` | Transport message | Medium |
| `Messages/FailedInfo.cs` | Failure information | Medium |
| `Transport/IConsumerClient.cs` | Consumer client interface | Low (interface) |
| `Transport/IConsumerClientFactory.cs` | Client factory interface | Low (interface) |
| `Transport/IDispatcher.cs` | Message dispatcher interface | Low (interface) |
| `Transport/ITransport.cs` | Transport interface | Low (interface) |
| `Transport/BrokerAddress.cs` | Broker address record | Medium |
| `Transport/MqLogType.cs` | Log type enum | Low (enum) |
| `Processor/IProcessor.cs` | Processor interface | Low (interface) |
| `Processor/IDispatcher.Default.cs` | Default dispatcher | High |
| `Processor/IProcessor.NeedRetry.cs` | Retry processor | High |
| `Processor/IProcessor.Delayed.cs` | Delayed processor | High |
| `Processor/IProcessor.Collector.cs` | Message collector | High |
| `Processor/IProcessor.InfiniteRetry.cs` | Infinite retry processor | High |
| `Processor/IProcessor.TransportCheck.cs` | Transport health check | High |
| `Processor/IProcessingServer.Message.cs` | Message processing server | High |
| `Processor/ProcessingContext.cs` | Processing context | Medium |
| `Internal/MethodMatcherCache.cs` | Method matcher cache | High |
| `Internal/IMessageSender.Default.cs` | Default message sender | High |
| `IBootstrapper.cs` | Bootstrapper interface | Low (interface) |

### Headless.Messaging.AwsSqs

| File | Purpose | Testable |
|------|---------|----------|
| `AmazonSqsConsumerClient.cs` | SQS consumer client | High (integration) |
| `AmazonSqsConsumerClientFactory.cs` | SQS client factory | Medium |
| `AmazonSqsTransport.cs` | SQS transport | High (integration) |
| `AmazonSqsOptions.cs` | SQS options | Medium |
| `AmazonPolicyExtensions.cs` | Policy extensions | High (covered) |
| `AwsClientFactory.cs` | AWS client factory | Medium |
| `TopicNormalizer.cs` | Topic name normalizer | High (covered) |
| `SqsReceivedMessage.cs` | SQS received message | Medium |
| `Setup.cs` | DI registration | Low |

### Headless.Messaging.PostgreSql

| File | Purpose | Testable |
|------|---------|----------|
| `PostgreSqlDataStorage.cs` | PostgreSQL outbox storage | High (integration) |
| `PostgreSqlMonitoringApi.cs` | Monitoring API | High (integration) |
| `PostgreSqlStorageInitializer.cs` | Storage initializer | High (integration) |
| `PostgreSqlOutboxTransaction.cs` | Outbox transaction | High (integration) |
| `PostgreSqlEntityFrameworkDbTransaction.cs` | EF transaction | High (integration) |
| `PostgreSqlEntityFrameworkMessagingOptions.cs` | EF options | Medium |
| `PostgreSqlOptions.cs` | PostgreSQL options | Medium |
| `PostgreSqlOptionsValidator.cs` | Options validator | High |
| `EntityFrameworkTransactionExtensions.cs` | EF transaction extensions | Medium |
| `DbConnectionExtensions.cs` | Connection extensions | High |
| `Setup.cs` | DI registration | Low |

### Headless.Messaging.SqlServer

| File | Purpose | Testable |
|------|---------|----------|
| `SqlServerDataStorage.cs` | SQL Server outbox storage | High (integration) |
| `SqlServerMonitoringApi.cs` | Monitoring API | High (integration) |
| `SqlServerStorageInitializer.cs` | Storage initializer | High (integration) |
| `SqlServerOutboxTransaction.cs` | Outbox transaction | High (integration) |
| `SqlServerEntityFrameworkDbTransaction.cs` | EF transaction | High (integration) |
| `SqlServerEntityFrameworkMessagingOptions.cs` | EF options | Medium (1 test exists) |
| `SqlServerOptions.cs` | SQL Server options | Medium |
| `SqlServerOptionsValidator.cs` | Options validator | High |
| `Setup.cs` | DI registration | Low |

---

## Existing Test Coverage

### Headless.Messaging.Core.Tests.Unit

| Test File | Coverage |
|-----------|----------|
| `ConsumerRegistryTests.cs` | Register, freeze, update, concurrent access |
| `ConsumerServiceSelectorTests.cs` | Service selection |
| `ConsumerLifecycleTests.cs` | Lifecycle management |
| `DispatcherTests.cs` | Message dispatching |
| `MessageExtensionTest.cs` | Message extensions |
| `MessageTest.cs` | Message class |
| `MessagingBuilderTests.cs` | Builder configuration |
| `SubscribeInvokerTests.cs` | Subscribe invocation |
| `TypeSafePublishApiTests.cs` | Type-safe publish API |
| `IntegrationTests/IConsumeIntegrationTests.cs` | IConsume integration |
| `IntegrationTests/MessageOrderingTests.cs` | Message ordering |

### Headless.Messaging.AwsSqs.Tests.Unit

| Test File | Coverage |
|-----------|----------|
| `AmazonSqsConsumerClientTests.cs` | Consumer client |
| `AmazonPolicyExtensionsTests.cs` | Policy extensions |
| `TopicNormalizerTests.cs` | Topic normalization |

---

## Missing: ConsumeContext Tests

**File:** `tests/Headless.Messaging.Abstractions.Tests.Unit/ConsumeContextTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_store_message_property` | Message property |
| `should_store_message_id_property` | MessageId required |
| `should_throw_when_message_id_is_null` | Null validation |
| `should_throw_when_message_id_is_whitespace` | Whitespace validation |
| `should_store_correlation_id_property` | CorrelationId nullable |
| `should_throw_when_correlation_id_is_empty_string` | Empty string validation |
| `should_allow_null_correlation_id` | Null allowed |
| `should_store_headers_property` | Headers property |
| `should_store_timestamp_property` | Timestamp property |
| `should_store_topic_property` | Topic property |

---

## Missing: Message Extensions Tests

**File:** `tests/Headless.Messaging.Core.Tests.Unit/Messages/MessageExtensionsTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_get_id_from_headers` | GetId() |
| `should_throw_when_id_not_in_headers` | GetId() KeyNotFoundException |
| `should_get_name_from_headers` | GetName() |
| `should_throw_when_name_not_in_headers` | GetName() KeyNotFoundException |
| `should_get_callback_name_from_headers` | GetCallbackName() |
| `should_return_null_when_callback_name_not_set` | GetCallbackName() null |
| `should_get_group_from_headers` | GetGroup() |
| `should_return_null_when_group_not_set` | GetGroup() null |
| `should_get_correlation_sequence` | GetCorrelationSequence() |
| `should_return_zero_when_correlation_sequence_not_set` | Default 0 |
| `should_get_execution_instance_id` | GetExecutionInstanceId() |
| `should_return_null_when_execution_instance_id_not_set` | GetExecutionInstanceId() null |
| `should_detect_exception_in_headers` | HasException() true |
| `should_return_false_when_no_exception` | HasException() false |
| `should_add_exception_to_headers` | AddOrUpdateException() |
| `should_update_existing_exception` | AddOrUpdateException() update |
| `should_remove_exception_from_headers` | RemoveException() |

---

## Missing: ExponentialBackoffStrategy Tests

**File:** `tests/Headless.Messaging.Core.Tests.Unit/Retry/ExponentialBackoffStrategyTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_use_default_initial_delay` | 1 second default |
| `should_use_default_max_delay` | 5 minutes default |
| `should_use_custom_initial_delay` | Custom initial |
| `should_use_custom_max_delay` | Custom max |
| `should_use_custom_backoff_multiplier` | Custom multiplier |
| `should_calculate_exponential_delay` | 1s, 2s, 4s, 8s... |
| `should_cap_delay_at_max` | Max delay cap |
| `should_add_jitter_to_delay` | ±25% jitter |
| `should_return_null_for_permanent_exceptions` | SubscriberNotFoundException |
| `should_return_null_for_argument_null_exception` | ArgumentNullException |
| `should_return_null_for_argument_exception` | ArgumentException |
| `should_return_null_for_invalid_operation_exception` | InvalidOperationException |
| `should_return_null_for_not_supported_exception` | NotSupportedException |
| `should_retry_for_transient_exceptions` | Other exceptions |
| `should_not_retry_when_exception_is_permanent` | ShouldRetry false |

---

## Missing: FixedIntervalBackoffStrategy Tests

**File:** `tests/Headless.Messaging.Core.Tests.Unit/Retry/FixedIntervalBackoffStrategyTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_use_default_delay` | Default interval |
| `should_use_custom_delay` | Custom interval |
| `should_return_same_delay_for_all_attempts` | Fixed delay |
| `should_add_jitter_when_enabled` | Optional jitter |
| `should_return_null_for_permanent_exceptions` | Permanent failures |
| `should_retry_for_transient_exceptions` | Transient failures |

---

## Missing: JsonUtf8Serializer Tests

**File:** `tests/Headless.Messaging.Core.Tests.Unit/Serialization/JsonUtf8SerializerTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_serialize_message_to_transport_message` | SerializeToTransportMessageAsync |
| `should_handle_null_value_in_message` | Null body |
| `should_serialize_to_utf8_bytes` | UTF8 encoding |
| `should_deserialize_transport_message` | DeserializeAsync |
| `should_handle_empty_body_in_transport_message` | Empty body |
| `should_handle_null_value_type_in_deserialize` | Null type |
| `should_serialize_message_to_string` | Serialize() |
| `should_deserialize_string_to_message` | Deserialize() |
| `should_deserialize_json_element` | Deserialize(object, Type) |
| `should_throw_when_not_json_element` | NotSupportedException |
| `should_detect_json_element_type` | IsJsonType() |
| `should_return_false_for_non_json_element` | IsJsonType() false |
| `should_use_configured_json_options` | JsonSerializerOptions |

---

## Missing: MessagingConventions Tests

**File:** `tests/Headless.Messaging.Abstractions.Tests.Unit/MessagingConventionsTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_return_default_topic_prefix` | Default prefix |
| `should_set_custom_topic_prefix` | Custom prefix |
| `should_format_topic_name` | FormatTopicName |
| `should_format_group_name` | FormatGroupName |
| `should_use_conventions_in_extensions` | Extension methods |

---

## Missing: AmazonSqsConsumerClient Tests (Integration)

**File:** `tests/Headless.Messaging.AwsSqs.Tests.Integration/AmazonSqsConsumerClientTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_fetch_topics_and_create_sns_topics` | FetchTopicsAsync |
| `should_subscribe_to_topics` | SubscribeAsync |
| `should_listen_for_messages` | ListeningAsync |
| `should_commit_message` | CommitAsync |
| `should_reject_message` | RejectAsync |
| `should_handle_semaphore_for_concurrent_processing` | Concurrency |
| `should_dispose_clients_properly` | DisposeAsync |
| `should_handle_invalid_message_structure` | Invalid JSON |
| `should_handle_connection_failure` | Connection error |

---

## Missing: AmazonSqsTransport Tests (Integration)

**File:** `tests/Headless.Messaging.AwsSqs.Tests.Integration/AmazonSqsTransportTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_send_message_to_sns` | SendAsync |
| `should_include_message_attributes` | Attributes |
| `should_handle_send_failure` | Error handling |
| `should_publish_batch_messages` | Batch publish |

---

## Missing: PostgreSqlDataStorage Tests (Integration)

**File:** `tests/Headless.Messaging.PostgreSql.Tests.Integration/PostgreSqlDataStorageTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_acquire_lock` | AcquireLockAsync |
| `should_not_acquire_lock_when_held` | Lock contention |
| `should_release_lock` | ReleaseLockAsync |
| `should_renew_lock` | RenewLockAsync |
| `should_store_message` | StoreMessageAsync |
| `should_store_message_with_transaction` | Transaction support |
| `should_store_received_message` | StoreReceivedMessageAsync |
| `should_store_received_exception_message` | StoreReceivedExceptionMessageAsync |
| `should_change_publish_state` | ChangePublishStateAsync |
| `should_change_receive_state` | ChangeReceiveStateAsync |
| `should_change_publish_state_to_delayed` | ChangePublishStateToDelayedAsync |
| `should_delete_expired_messages` | DeleteExpiresAsync |
| `should_get_published_messages_of_need_retry` | GetPublishedMessagesOfNeedRetry |
| `should_get_received_messages_of_need_retry` | GetReceivedMessagesOfNeedRetry |
| `should_delete_received_message` | DeleteReceivedMessageAsync |
| `should_delete_published_message` | DeletePublishedMessageAsync |
| `should_schedule_delayed_messages` | ScheduleMessagesOfDelayedAsync |
| `should_get_monitoring_api` | GetMonitoringApi |

---

## Missing: SqlServerDataStorage Tests (Integration)

**File:** `tests/Headless.Messaging.SqlServer.Tests.Integration/SqlServerDataStorageTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_acquire_lock` | AcquireLockAsync |
| `should_release_lock` | ReleaseLockAsync |
| `should_renew_lock` | RenewLockAsync |
| `should_store_message` | StoreMessageAsync |
| `should_store_received_message` | StoreReceivedMessageAsync |
| `should_change_publish_state` | ChangePublishStateAsync |
| `should_change_receive_state` | ChangeReceiveStateAsync |
| `should_delete_expired_messages` | DeleteExpiresAsync |
| `should_get_messages_of_need_retry` | GetMessagesOfNeedRetry |
| `should_schedule_delayed_messages` | ScheduleMessagesOfDelayedAsync |

---

## Missing: RabbitMqConsumerClient Tests (Integration)

**File:** `tests/Headless.Messaging.RabbitMq.Tests.Integration/RabbitMqConsumerClientTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_connect_to_rabbitmq` | Connection |
| `should_subscribe_to_queue` | SubscribeAsync |
| `should_listen_for_messages` | ListeningAsync |
| `should_commit_message` | CommitAsync |
| `should_reject_message` | RejectAsync |
| `should_handle_connection_pool` | IConnectionChannelPool |

---

## Missing: RabbitMqTransport Tests (Integration)

**File:** `tests/Headless.Messaging.RabbitMq.Tests.Integration/RabbitMqTransportTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_send_message_to_exchange` | SendAsync |
| `should_publish_with_routing_key` | Routing |
| `should_handle_send_failure` | Error handling |

---

## Missing: KafkaConsumerClient Tests (Integration)

**File:** `tests/Headless.Messaging.Kafka.Tests.Integration/KafkaConsumerClientTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_connect_to_kafka` | Connection |
| `should_subscribe_to_topic` | SubscribeAsync |
| `should_consume_messages` | ListeningAsync |
| `should_commit_offset` | CommitAsync |
| `should_handle_partition_assignment` | Rebalance |

---

## Missing: NatsConsumerClient Tests (Integration)

**File:** `tests/Headless.Messaging.Nats.Tests.Integration/NatsConsumerClientTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_connect_to_nats` | Connection |
| `should_subscribe_to_subject` | SubscribeAsync |
| `should_consume_messages` | ListeningAsync |
| `should_acknowledge_message` | CommitAsync |
| `should_handle_jetstream` | JetStream support |

---

## Missing: RedisStreamsConsumerClient Tests (Integration)

**File:** `tests/Headless.Messaging.RedisStreams.Tests.Integration/RedisStreamsConsumerClientTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_connect_to_redis` | Connection |
| `should_create_consumer_group` | Consumer group |
| `should_read_from_stream` | XREAD |
| `should_acknowledge_message` | XACK |
| `should_handle_pending_messages` | XPENDING |

---

## Missing: InMemoryQueue Tests

**File:** `tests/Headless.Messaging.InMemoryQueue.Tests.Unit/InMemoryQueueConsumerClientTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_subscribe_to_queue` | SubscribeAsync |
| `should_receive_published_messages` | ListeningAsync |
| `should_commit_message` | CommitAsync |
| `should_reject_message` | RejectAsync |
| `should_handle_concurrent_consumers` | Concurrency |

---

## Missing: InMemoryStorage Tests

**File:** `tests/Headless.Messaging.InMemoryStorage.Tests.Unit/InMemoryStorageTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_store_message` | StoreMessageAsync |
| `should_retrieve_message` | GetMessage |
| `should_change_message_state` | ChangeStateAsync |
| `should_delete_expired_messages` | DeleteExpiresAsync |
| `should_get_messages_of_need_retry` | GetMessagesOfNeedRetry |

---

## Missing: OpenTelemetry Tests

**File:** `tests/Headless.Messaging.OpenTelemetry.Tests.Unit/OpenTelemetryTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_add_tracing_to_published_messages` | Trace propagation |
| `should_extract_trace_context_from_consumed_messages` | Trace extraction |
| `should_create_spans_for_message_operations` | Span creation |
| `should_record_metrics` | Metrics recording |

---

## Missing: Processor Tests

**File:** `tests/Headless.Messaging.Core.Tests.Unit/Processor/ProcessorTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_process_need_retry_messages` | NeedRetryProcessor |
| `should_process_delayed_messages` | DelayedProcessor |
| `should_collect_messages` | CollectorProcessor |
| `should_handle_infinite_retry` | InfiniteRetryProcessor |
| `should_check_transport_health` | TransportCheckProcessor |
| `should_dispatch_messages_correctly` | DefaultDispatcher |

---

## Test Infrastructure

### Mock Consumer Client

```csharp
public sealed class FakeConsumerClient : IConsumerClient
{
    private readonly Queue<TransportMessage> _messages = new();
    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }
    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }
    public BrokerAddress BrokerAddress => new("fake", "localhost");

    public void EnqueueMessage(TransportMessage message) => _messages.Enqueue(message);

    public ValueTask<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames)
        => new(topicNames.ToList());

    public ValueTask SubscribeAsync(IEnumerable<string> topics) => ValueTask.CompletedTask;

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_messages.TryDequeue(out var message))
            {
                await OnMessageCallback!(message, message.GetId()).AnyContext();
            }
            else
            {
                await Task.Delay(10, cancellationToken).AnyContext();
            }
        }
    }

    public ValueTask CommitAsync(object? sender) => ValueTask.CompletedTask;
    public ValueTask RejectAsync(object? sender) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

### Mock Data Storage

```csharp
public sealed class FakeDataStorage : IDataStorage
{
    private readonly ConcurrentDictionary<string, MediumMessage> _published = new();
    private readonly ConcurrentDictionary<string, MediumMessage> _received = new();
    private readonly ConcurrentDictionary<string, (string Instance, DateTime LastLockTime)> _locks = new();

    public ValueTask<bool> AcquireLockAsync(string key, TimeSpan ttl, string instance, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return new ValueTask<bool>(_locks.TryAdd(key, (instance, now)));
    }

    // ... other methods
}
```

---

## Test Summary

| Component | Existing | New Unit | New Integration | Total |
|-----------|----------|----------|-----------------|-------|
| Core (ConsumerRegistry, etc.) | ~50 | 0 | 0 | ~50 |
| ConsumeContext | 0 | 9 | 0 | 9 |
| Message Extensions | 0 | 17 | 0 | 17 |
| ExponentialBackoffStrategy | 0 | 15 | 0 | 15 |
| FixedIntervalBackoffStrategy | 0 | 6 | 0 | 6 |
| JsonUtf8Serializer | 0 | 13 | 0 | 13 |
| MessagingConventions | 0 | 5 | 0 | 5 |
| AmazonSqsConsumerClient | ~3 | 0 | 9 | ~12 |
| AmazonSqsTransport | 0 | 0 | 4 | 4 |
| PostgreSqlDataStorage | 0 | 0 | 18 | 18 |
| SqlServerDataStorage | 0 | 0 | 10 | 10 |
| RabbitMqConsumerClient | 0 | 0 | 6 | 6 |
| RabbitMqTransport | 0 | 0 | 3 | 3 |
| KafkaConsumerClient | 0 | 0 | 5 | 5 |
| NatsConsumerClient | 0 | 0 | 5 | 5 |
| RedisStreamsConsumerClient | 0 | 0 | 5 | 5 |
| InMemoryQueue | 0 | 5 | 0 | 5 |
| InMemoryStorage | 0 | 5 | 0 | 5 |
| OpenTelemetry | 0 | 4 | 0 | 4 |
| Processors | 0 | 6 | 0 | 6 |
| **Total** | **~53** | **~85** | **~65** | **~203** |

---

## Priority Order

1. **ConsumeContext** - Core context validation
2. **Message Extensions** - Heavily used API
3. **ExponentialBackoffStrategy** - Retry logic critical path
4. **JsonUtf8Serializer** - Serialization critical path
5. **PostgreSqlDataStorage** - Primary production storage
6. **SqlServerDataStorage** - Secondary production storage
7. **Transport integrations** - Integration tests

---

## Notes

1. **Outbox pattern** - Messages stored to database before transport, ensures exactly-once delivery
2. **Consumer registry freeze** - Frozen on first GetAll() call, prevents runtime modifications
3. **Exponential backoff** - 1s, 2s, 4s, 8s... capped at 5 minutes with ±25% jitter
4. **Permanent vs transient** - SubscriberNotFoundException, ArgumentException are permanent failures
5. **Transport implementations** - AWS SQS/SNS, RabbitMQ, Kafka, NATS, Redis Streams, Pulsar
6. **Storage implementations** - PostgreSQL, SQL Server, InMemory
7. **Concurrency** - Semaphore-based concurrency control per consumer group
8. **Lock system** - Distributed locks for multi-instance coordination

---

## Messaging Architecture

```
IOutboxPublisher
├── PublishAsync(name, content) → Store to DB
├── PublishDelayAsync(delay, name, content) → Store with ExpiresAt
├── Transaction support for atomic operations
└── Topic mapping for type-safe publishing

Message Flow:
1. Publisher stores message to outbox (PostgreSQL/SQL Server)
2. Processor picks up scheduled messages
3. Transport sends to broker (SQS/RabbitMQ/Kafka/etc.)
4. Consumer client receives from broker
5. Message dispatched to IConsume<T> handler
6. Handler processes, commits or rejects

Storage Tables:
├── Published (Id, Version, Name, Content, Retries, Added, ExpiresAt, StatusName)
├── Received (Id, Version, Name, Group, Content, Retries, Added, ExpiresAt, StatusName)
└── Lock (Key, Instance, LastLockTime)

Status Lifecycle:
Scheduled → Queued → Succeeded/Failed
         ↘ Delayed → Queued → Succeeded/Failed
```

---

## Recommendation

**High Priority** - Messaging is critical infrastructure. Unit tests should cover:
- ConsumeContext validation (required fields)
- Message extension methods
- Backoff strategies (timing, jitter, exception classification)
- Serializer (UTF8, JSON options)

Integration tests are essential for:
- PostgreSQL/SQL Server storage operations
- Transport client operations (connection, subscribe, consume, commit)
- End-to-end message flow
