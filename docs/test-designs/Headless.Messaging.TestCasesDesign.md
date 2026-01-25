# Headless.Messaging Test Cases Design

This document provides comprehensive test case designs for all 16 Headless.Messaging.* projects.

## Test Coverage Overview

| Project | Unit Tests | Integration Tests | Status |
|---------|:----------:|:-----------------:|:------:|
| Headless.Messaging.Abstractions | ⚪ Design | - | **Missing** |
| Headless.Messaging.Core | ✅ Exists | ⚪ Design | Partial |
| Headless.Messaging.AwsSqs | ✅ Exists | ✅ Exists | Complete |
| Headless.Messaging.AzureServiceBus | ✅ Exists | ⚪ Design | Partial |
| Headless.Messaging.Kafka | ⚪ Design | ⚪ Design | **Missing** |
| Headless.Messaging.RabbitMq | ✅ Exists | ⚪ Design | Partial |
| Headless.Messaging.Nats | ⚪ Design | ⚪ Design | **Missing** |
| Headless.Messaging.Pulsar | ⚪ Design | ⚪ Design | **Missing** |
| Headless.Messaging.RedisStreams | ⚪ Design | ⚪ Design | **Missing** |
| Headless.Messaging.InMemoryQueue | ⚪ Design | - | **Missing** |
| Headless.Messaging.PostgreSql | ✅ Exists | ✅ Exists | Complete |
| Headless.Messaging.SqlServer | ✅ Exists | ✅ Exists | Complete |
| Headless.Messaging.InMemoryStorage | ⚪ Design | - | **Missing** |
| Headless.Messaging.Dashboard | ⚪ Design | ⚪ Design | **Missing** |
| Headless.Messaging.Dashboard.K8s | ⚪ Design | ⚪ Design | **Missing** |
| Headless.Messaging.OpenTelemetry | ⚪ Design | - | **Missing** |

Legend: ✅ Exists | ⚪ Design (proposed) | - Not applicable

---

## Testing Conventions

Based on existing test patterns in the codebase:

### Structure
- **Unit Tests**: `Headless.Messaging.{Project}.Tests.Unit`
- **Integration Tests**: `Headless.Messaging.{Project}.Tests.Integration`
- **Test Harness**: `Headless.Messaging.{Project}.Tests.Harness` (shared fixtures)

### Frameworks
- xUnit v3 with Microsoft Testing Platform (MTP)
- AwesomeAssertions for fluent assertions
- NSubstitute for mocking
- Testcontainers for integration tests
- Bogus for test data generation

### Naming Convention
```
should_{action}_{expected_behavior}_when_{condition}
```

### Base Class
All test classes should inherit from `Framework.Testing.Tests.TestBase` which provides:
- `Logger` - ILogger instance
- `Faker` - Bogus faker for test data
- `AbortToken` - CancellationToken for test timeout

---

## 1. Headless.Messaging.Abstractions

**Project Type**: Core abstractions (interfaces, base classes, contracts)

### Unit Tests Design

#### 1.1 ConsumeContext Tests
```
ConsumeContextTests
├── should_set_message_property_correctly
├── should_set_message_id_property_correctly
├── should_set_correlation_id_property_correctly
├── should_set_headers_property_correctly
├── should_set_timestamp_property_correctly
├── should_set_topic_property_correctly
├── should_allow_null_correlation_id
└── should_preserve_header_case_sensitivity
```

#### 1.2 MessageHeader Tests
```
MessageHeaderTests
├── should_create_with_dictionary
├── should_get_header_value_by_key
├── should_return_null_for_missing_key
├── should_support_case_sensitive_keys
├── should_enumerate_all_headers
└── should_handle_empty_dictionary
```

#### 1.3 ConsumerMetadata Tests
```
ConsumerMetadataTests
├── should_store_consumer_type_correctly
├── should_store_message_type_correctly
├── should_store_topic_correctly
├── should_store_group_correctly
└── should_be_immutable
```

#### 1.4 MessagingConventions Tests
```
MessagingConventionsTests
├── should_use_default_topic_naming_convention
├── should_use_default_group_naming_convention
├── should_allow_custom_topic_naming_convention
├── should_allow_custom_group_naming_convention
└── should_apply_version_suffix_to_group
```

#### 1.5 ConsumeFilter Tests
```
ConsumeFilterTests
├── should_have_default_order_zero
├── should_invoke_before_consume
├── should_invoke_after_consume
├── should_invoke_on_exception
├── should_respect_filter_order
└── should_chain_multiple_filters
```

---

## 2. Headless.Messaging.Core

**Project Type**: Main infrastructure and runtime
**Status**: Has existing unit tests, needs expansion

### Unit Tests Design (Additional)

#### 2.1 MessagingOptions Validation Tests
```
MessagingOptionsTests
├── should_have_default_failed_retry_count_of_50
├── should_have_default_failed_retry_interval_of_60_seconds
├── should_have_default_succeed_message_expired_after_24_hours
├── should_have_default_failed_message_expired_after_15_days
├── should_validate_failed_retry_count_positive
├── should_validate_failed_retry_interval_positive
├── should_validate_consumer_thread_count_positive
└── should_use_exponential_backoff_by_default
```

#### 2.2 ExponentialBackoffStrategy Tests
```
ExponentialBackoffStrategyTests
├── should_return_initial_delay_for_first_retry
├── should_double_delay_for_each_retry
├── should_cap_delay_at_max_delay
├── should_add_jitter_within_bounds
├── should_handle_zero_retry_count
└── should_handle_large_retry_counts_without_overflow
```

#### 2.3 FixedIntervalBackoffStrategy Tests
```
FixedIntervalBackoffStrategyTests
├── should_return_configured_interval_for_all_retries
├── should_handle_zero_retry_count
└── should_handle_large_retry_counts
```

#### 2.4 ConsumerExecutor Tests
```
ConsumerExecutorTests
├── should_execute_consumer_successfully
├── should_handle_consumer_exception
├── should_apply_filters_in_order
├── should_invoke_lifecycle_hooks
├── should_respect_cancellation_token
└── should_dispose_scoped_services
```

#### 2.5 MessageProcessor Tests
```
MessageProcessorTests
├── should_process_received_message
├── should_update_message_status_on_success
├── should_update_message_status_on_failure
├── should_retry_failed_message
├── should_move_to_dead_letter_after_max_retries
├── should_handle_deserialization_errors
└── should_skip_expired_messages
```

#### 2.6 RetryProcessor Tests
```
RetryProcessorTests
├── should_poll_for_failed_messages
├── should_requeue_failed_messages
├── should_respect_retry_interval
├── should_skip_messages_past_max_retries
├── should_handle_database_connection_errors
└── should_respect_cancellation_token
```

#### 2.7 BootstrapperService Tests
```
BootstrapperServiceTests
├── should_start_consumers_on_start
├── should_stop_consumers_on_stop
├── should_initialize_storage_on_start
├── should_handle_startup_errors_gracefully
├── should_respect_cancellation_token
└── should_log_startup_information
```

### Integration Tests Design

#### 2.8 End-to-End Consumer Tests (with InMemory)
```
EndToEndConsumerIntegrationTests
├── should_process_single_message_end_to_end
├── should_process_multiple_messages_in_order
├── should_handle_consumer_exception_and_retry
├── should_support_multiple_consumers_same_topic
├── should_support_consumer_groups
├── should_apply_version_isolation
└── should_handle_graceful_shutdown
```

---

## 3. Headless.Messaging.AwsSqs

**Project Type**: AWS SQS/SNS transport provider
**Status**: Has unit and integration tests (complete)

### Additional Unit Tests Design

#### 3.1 AmazonSqsOptions Validation Tests
```
AmazonSqsOptionsValidationTests
├── should_require_region_endpoint
├── should_allow_optional_credentials
├── should_allow_optional_sqs_service_url
├── should_allow_optional_sns_service_url
└── should_validate_service_urls_format
```

#### 3.2 SqsConsumerClientFactory Tests
```
SqsConsumerClientFactoryTests
├── should_create_consumer_client
├── should_configure_client_with_options
├── should_reuse_sqs_client_connection
└── should_handle_creation_errors
```

---

## 4. Headless.Messaging.AzureServiceBus

**Project Type**: Azure Service Bus transport provider
**Status**: Has unit tests, needs integration tests

### Additional Unit Tests Design

#### 4.1 AzureServiceBusOptions Validation Tests
```
AzureServiceBusOptionsValidationTests
├── should_require_connection_string_or_namespace
├── should_validate_connection_string_format
├── should_have_default_topic_path_messaging
├── should_have_default_max_concurrent_calls_1
├── should_allow_enable_sessions
└── should_allow_token_credential
```

#### 4.2 ServiceBusConsumerClient Tests
```
ServiceBusConsumerClientTests
├── should_have_correct_broker_address
├── should_connect_to_service_bus
├── should_subscribe_to_topics
├── should_handle_session_enabled_subscriptions
├── should_acknowledge_messages
├── should_reject_messages
└── should_handle_connection_errors
```

### Integration Tests Design

#### 4.3 Azure Service Bus Integration (with Emulator or Real)
```
AzureServiceBusIntegrationTests : IClassFixture<AzureServiceBusTestFixture>
├── should_send_and_receive_message
├── should_handle_multiple_subscribers
├── should_support_message_sessions
├── should_handle_dead_letter_queue
├── should_respect_message_ttl
└── should_handle_reconnection
```

**Test Fixture**:
```csharp
public sealed class AzureServiceBusTestFixture : IAsyncLifetime
{
    // Use Azure Service Bus Emulator or connection string from env
}
```

---

## 5. Headless.Messaging.Kafka

**Project Type**: Apache Kafka transport provider
**Status**: Missing all tests

### Unit Tests Design

#### 5.1 MessagingKafkaOptions Tests
```
MessagingKafkaOptionsTests
├── should_require_servers
├── should_have_default_connection_pool_size_10
├── should_allow_custom_main_config
├── should_allow_topic_options
└── should_validate_servers_format
```

#### 5.2 KafkaConsumerClient Tests
```
KafkaConsumerClientTests
├── should_have_correct_broker_address
├── should_create_consumer_on_connect
├── should_subscribe_to_topics
├── should_commit_offsets
├── should_handle_deserialization_errors
├── should_handle_broker_disconnection
├── should_handle_rebalance
└── should_dispose_consumer_properly
```

#### 5.3 KafkaTransport Tests
```
KafkaTransportTests
├── should_send_message_to_topic
├── should_use_connection_pool
├── should_handle_producer_errors
├── should_support_message_headers
└── should_handle_serialization
```

#### 5.4 KafkaConsumerClientFactory Tests
```
KafkaConsumerClientFactoryTests
├── should_create_consumer_client
├── should_configure_with_options
└── should_handle_creation_errors
```

### Integration Tests Design

#### 5.5 Kafka Integration (with Testcontainers)
```
KafkaIntegrationTests : IClassFixture<KafkaTestFixture>
├── should_produce_and_consume_message
├── should_handle_multiple_partitions
├── should_handle_consumer_groups
├── should_handle_offset_reset
├── should_handle_broker_failure
└── should_respect_topic_configuration
```

**Test Fixture**:
```csharp
[CollectionDefinition]
public sealed class KafkaTestFixture(IMessageSink messageSink)
    : ContainerFixture<KafkaBuilder, KafkaContainer>(messageSink),
      ICollectionFixture<KafkaTestFixture>
{
    protected override KafkaBuilder Configure() =>
        base.Configure().WithImage("confluentinc/cp-kafka:latest");
}
```

---

## 6. Headless.Messaging.RabbitMq

**Project Type**: RabbitMQ transport provider
**Status**: Has unit tests, needs integration tests

### Additional Unit Tests Design

#### 6.1 RabbitMqOptions Tests
```
RabbitMqOptionsTests
├── should_have_default_hostname_localhost
├── should_have_default_virtual_host_slash
├── should_have_default_exchange_name
├── should_allow_multiple_hostnames
├── should_validate_port_range
└── should_support_publish_confirms
```

### Integration Tests Design

#### 6.2 RabbitMQ Integration (with Testcontainers)
```
RabbitMqIntegrationTests : IClassFixture<RabbitMqTestFixture>
├── should_send_and_receive_message
├── should_handle_exchange_binding
├── should_handle_queue_declaration
├── should_handle_message_acknowledgement
├── should_handle_message_rejection
├── should_handle_dead_letter_exchange
├── should_handle_connection_recovery
├── should_handle_channel_recovery
└── should_support_publish_confirms
```

**Test Fixture**:
```csharp
[CollectionDefinition]
public sealed class RabbitMqTestFixture(IMessageSink messageSink)
    : ContainerFixture<RabbitMqBuilder, RabbitMqContainer>(messageSink),
      ICollectionFixture<RabbitMqTestFixture>
{
    protected override RabbitMqBuilder Configure() =>
        base.Configure().WithImage("rabbitmq:3-management");
}
```

---

## 7. Headless.Messaging.Nats

**Project Type**: NATS JetStream transport provider
**Status**: Missing all tests

### Unit Tests Design

#### 7.1 MessagingNatsOptions Tests
```
MessagingNatsOptionsTests
├── should_have_default_servers_localhost_4222
├── should_have_default_connection_pool_size_10
├── should_enable_stream_creation_by_default
├── should_allow_custom_nats_options
└── should_validate_servers_format
```

#### 7.2 NatsConsumerClient Tests
```
NatsConsumerClientTests
├── should_have_correct_broker_address
├── should_connect_to_nats
├── should_subscribe_to_jetstream_consumer
├── should_acknowledge_messages
├── should_nak_messages
├── should_handle_connection_errors
├── should_create_stream_when_enabled
└── should_dispose_properly
```

#### 7.3 NatsTransport Tests
```
NatsTransportTests
├── should_publish_to_jetstream
├── should_use_connection_pool
├── should_handle_publish_errors
├── should_support_message_headers
└── should_handle_serialization
```

### Integration Tests Design

#### 7.4 NATS Integration (with Testcontainers)
```
NatsIntegrationTests : IClassFixture<NatsTestFixture>
├── should_publish_and_consume_message
├── should_handle_jetstream_streams
├── should_handle_consumer_groups
├── should_handle_message_replay
├── should_handle_server_reconnection
└── should_handle_stream_limits
```

**Test Fixture**:
```csharp
[CollectionDefinition]
public sealed class NatsTestFixture(IMessageSink messageSink)
    : ContainerFixture<NatsBuilder, NatsContainer>(messageSink),
      ICollectionFixture<NatsTestFixture>
{
    protected override NatsBuilder Configure() =>
        base.Configure().WithImage("nats:latest").WithCommand("--jetstream");
}
```

---

## 8. Headless.Messaging.Pulsar

**Project Type**: Apache Pulsar transport provider
**Status**: Missing all tests

### Unit Tests Design

#### 8.1 MessagingPulsarOptions Tests
```
MessagingPulsarOptionsTests
├── should_require_service_url
├── should_have_default_client_log_disabled
├── should_allow_tls_options
├── should_validate_service_url_format
└── should_support_multiple_service_urls
```

#### 8.2 PulsarConsumerClient Tests
```
PulsarConsumerClientTests
├── should_have_correct_broker_address
├── should_connect_to_pulsar
├── should_subscribe_to_topics
├── should_acknowledge_messages
├── should_negative_acknowledge_messages
├── should_handle_connection_errors
└── should_dispose_properly
```

#### 8.3 PulsarTransport Tests
```
PulsarTransportTests
├── should_send_message_to_topic
├── should_handle_producer_errors
├── should_support_message_properties
└── should_handle_serialization
```

### Integration Tests Design

#### 8.4 Pulsar Integration (with Testcontainers)
```
PulsarIntegrationTests : IClassFixture<PulsarTestFixture>
├── should_produce_and_consume_message
├── should_handle_subscriptions
├── should_handle_message_acknowledgement
├── should_handle_dead_letter_topic
├── should_handle_message_retry
└── should_handle_broker_reconnection
```

**Test Fixture**:
```csharp
[CollectionDefinition]
public sealed class PulsarTestFixture(IMessageSink messageSink)
    : ContainerFixture<PulsarBuilder, PulsarContainer>(messageSink),
      ICollectionFixture<PulsarTestFixture>
{
    protected override PulsarBuilder Configure() =>
        base.Configure().WithImage("apachepulsar/pulsar:latest");
}
```

---

## 9. Headless.Messaging.RedisStreams

**Project Type**: Redis Streams transport provider
**Status**: Missing all tests

### Unit Tests Design

#### 9.1 MessagingRedisOptions Tests
```
MessagingRedisOptionsTests
├── should_have_default_configuration_localhost
├── should_have_default_stream_entries_count_10
├── should_have_default_connection_pool_size_10
├── should_allow_custom_configuration
├── should_allow_on_consume_error_callback
└── should_validate_configuration
```

#### 9.2 RedisConsumerClient Tests
```
RedisConsumerClientTests
├── should_have_correct_broker_address
├── should_connect_to_redis
├── should_create_consumer_group
├── should_read_from_stream
├── should_acknowledge_messages
├── should_handle_pending_messages
├── should_handle_connection_errors
└── should_dispose_properly
```

#### 9.3 RedisTransport Tests
```
RedisTransportTests
├── should_add_message_to_stream
├── should_use_connection_pool
├── should_handle_add_errors
└── should_support_message_fields
```

### Integration Tests Design

#### 9.4 Redis Streams Integration (with Testcontainers)
```
RedisStreamsIntegrationTests : IClassFixture<RedisTestFixture>
├── should_produce_and_consume_message
├── should_handle_consumer_groups
├── should_handle_pending_messages
├── should_handle_stream_trimming
├── should_handle_connection_recovery
└── should_handle_cluster_mode
```

**Test Fixture**:
```csharp
[CollectionDefinition]
public sealed class RedisTestFixture(IMessageSink messageSink)
    : ContainerFixture<RedisBuilder, RedisContainer>(messageSink),
      ICollectionFixture<RedisTestFixture>
{
    protected override RedisBuilder Configure() =>
        base.Configure().WithImage("redis:7-alpine");
}
```

---

## 10. Headless.Messaging.InMemoryQueue

**Project Type**: In-memory queue (for testing/dev)
**Status**: Missing all tests

### Unit Tests Design

#### 10.1 InMemoryConsumerClient Tests
```
InMemoryConsumerClientTests
├── should_have_correct_broker_address
├── should_connect_without_error
├── should_subscribe_to_topics
├── should_receive_messages
├── should_acknowledge_messages
├── should_reject_messages
├── should_handle_multiple_subscribers
└── should_dispose_properly
```

#### 10.2 InMemoryTransport Tests
```
InMemoryTransportTests
├── should_send_message_to_queue
├── should_deliver_to_all_subscribers
├── should_handle_no_subscribers
├── should_preserve_message_order
├── should_support_message_headers
└── should_handle_concurrent_sends
```

#### 10.3 InMemoryQueue Behavior Tests
```
InMemoryQueueBehaviorTests
├── should_support_single_consumer
├── should_support_competing_consumers
├── should_support_fanout_pattern
├── should_clear_queue_on_dispose
└── should_handle_backpressure
```

---

## 11. Headless.Messaging.PostgreSql

**Project Type**: PostgreSQL message storage
**Status**: Has unit and integration tests (complete)

### Additional Unit Tests Design

#### 11.1 PostgreSqlOptions Tests
```
PostgreSqlOptionsTests
├── should_allow_connection_string
├── should_allow_npgsql_data_source
├── should_require_either_connection_string_or_data_source
└── should_validate_connection_string_format
```

#### 11.2 PostgreSqlStorageInitializer Tests
```
PostgreSqlStorageInitializerTests
├── should_create_schema_if_not_exists
├── should_create_published_table
├── should_create_received_table
├── should_create_indexes
├── should_be_idempotent
└── should_handle_concurrent_initialization
```

---

## 12. Headless.Messaging.SqlServer

**Project Type**: SQL Server message storage
**Status**: Has unit and integration tests (complete)

### Additional Unit Tests Design

#### 12.1 SqlServerOptions Tests
```
SqlServerOptionsTests
├── should_require_connection_string
├── should_validate_connection_string_format
└── should_allow_schema_name
```

#### 12.2 SqlServerStorageInitializer Tests
```
SqlServerStorageInitializerTests
├── should_create_schema_if_not_exists
├── should_create_published_table
├── should_create_received_table
├── should_create_indexes
├── should_be_idempotent
└── should_handle_concurrent_initialization
```

---

## 13. Headless.Messaging.InMemoryStorage

**Project Type**: In-memory message storage (for testing/dev)
**Status**: Missing all tests

### Unit Tests Design

#### 13.1 InMemoryDataStorage Tests
```
InMemoryDataStorageTests
├── should_store_published_message
├── should_store_received_message
├── should_get_published_messages_for_retry
├── should_get_received_messages_for_retry
├── should_update_message_status
├── should_delete_expired_messages
├── should_handle_concurrent_access
├── should_query_by_status
├── should_query_by_date_range
└── should_clear_on_dispose
```

#### 13.2 InMemoryStorageInitializer Tests
```
InMemoryStorageInitializerTests
├── should_initialize_without_error
├── should_be_idempotent
└── should_handle_concurrent_initialization
```

#### 13.3 InMemoryMonitoringApi Tests
```
InMemoryMonitoringApiTests
├── should_get_published_message_counts
├── should_get_received_message_counts
├── should_get_messages_by_status
├── should_get_hourly_success_fail_count
└── should_support_pagination
```

---

## 14. Headless.Messaging.Dashboard

**Project Type**: Web dashboard for monitoring
**Status**: Missing all tests

### Unit Tests Design

#### 14.1 DashboardOptions Tests
```
DashboardOptionsTests
├── should_have_default_path_match_messaging
├── should_have_default_stats_polling_interval_2000
├── should_have_default_allow_anonymous_true
├── should_allow_custom_path_base
├── should_allow_authorization_policy
└── should_validate_polling_interval_positive
```

#### 14.2 DashboardMiddleware Tests
```
DashboardMiddlewareTests
├── should_serve_dashboard_at_path_match
├── should_pass_through_non_dashboard_requests
├── should_serve_static_assets
├── should_apply_path_base
├── should_check_authorization_when_configured
├── should_allow_anonymous_when_enabled
└── should_return_401_when_unauthorized
```

#### 14.3 DashboardApiController Tests
```
DashboardApiControllerTests
├── should_get_published_message_stats
├── should_get_received_message_stats
├── should_get_hourly_metrics
├── should_get_consumers_list
├── should_get_message_details
├── should_requeue_failed_message
└── should_delete_message
```

### Integration Tests Design

#### 14.4 Dashboard Integration Tests
```
DashboardIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
├── should_serve_dashboard_html
├── should_serve_api_stats
├── should_require_auth_when_configured
├── should_apply_cors_policy
└── should_handle_concurrent_requests
```

---

## 15. Headless.Messaging.Dashboard.K8s

**Project Type**: Kubernetes discovery for dashboard
**Status**: Missing all tests

### Unit Tests Design

#### 15.1 K8sDiscoveryOptions Tests
```
K8sDiscoveryOptionsTests
├── should_use_default_k8s_config
├── should_show_only_explicit_visible_nodes_by_default
└── should_allow_custom_k8s_config
```

#### 15.2 K8sNodeDiscovery Tests
```
K8sNodeDiscoveryTests
├── should_discover_pods_with_label
├── should_filter_by_namespace
├── should_extract_service_endpoints
├── should_handle_no_pods_found
├── should_handle_api_errors
└── should_refresh_discovery_periodically
```

#### 15.3 K8sServiceCollectionExtensions Tests
```
K8sServiceCollectionExtensionsTests
├── should_register_standalone_dashboard
├── should_configure_k8s_options
├── should_configure_dashboard_options
└── should_register_discovery_service
```

### Integration Tests Design

#### 15.4 K8s Integration (with Kind or Minikube)
```
K8sIntegrationTests
├── should_discover_messaging_pods
├── should_aggregate_stats_from_multiple_nodes
├── should_handle_pod_scaling
└── should_handle_node_failures
```

---

## 16. Headless.Messaging.OpenTelemetry

**Project Type**: OpenTelemetry tracing/metrics
**Status**: Missing all tests

### Unit Tests Design

#### 16.1 OpenTelemetryExtensions Tests
```
OpenTelemetryExtensionsTests
├── should_add_messaging_instrumentation
├── should_enable_tracing_by_default
├── should_enable_metrics_when_specified
└── should_register_activity_source
```

#### 16.2 MessagingActivitySource Tests
```
MessagingActivitySourceTests
├── should_create_publish_activity
├── should_create_consume_activity
├── should_set_message_attributes
├── should_set_topic_attribute
├── should_set_consumer_group_attribute
├── should_propagate_context
├── should_set_status_on_success
├── should_set_error_status_on_failure
└── should_record_exception
```

#### 16.3 MessagingMetrics Tests
```
MessagingMetricsTests
├── should_count_published_messages
├── should_count_received_messages
├── should_count_failed_messages
├── should_record_processing_duration
├── should_tag_by_topic
├── should_tag_by_consumer_group
└── should_tag_by_status
```

### Integration Tests Design

#### 16.4 OpenTelemetry Integration
```
OpenTelemetryIntegrationTests
├── should_export_traces_to_collector
├── should_export_metrics_to_collector
├── should_correlate_publish_and_consume_spans
├── should_include_all_attributes
└── should_handle_collector_unavailable
```

---

## Priority Order for Implementation

### Phase 1: Critical Missing Tests (High Priority)
1. **Headless.Messaging.InMemoryQueue** - Used heavily in testing
2. **Headless.Messaging.InMemoryStorage** - Used heavily in testing
3. **Headless.Messaging.Abstractions** - Core contracts validation

### Phase 2: Transport Providers (Medium Priority)
4. **Headless.Messaging.Kafka** - Popular message broker
5. **Headless.Messaging.Nats** - Growing adoption
6. **Headless.Messaging.RedisStreams** - Common infrastructure
7. **Headless.Messaging.Pulsar** - Enterprise use cases

### Phase 3: Integration Tests for Existing (Medium Priority)
8. **Headless.Messaging.AzureServiceBus.Tests.Integration**
9. **Headless.Messaging.RabbitMq.Tests.Integration**
10. **Headless.Messaging.Core.Tests.Integration**

### Phase 4: Observability & Dashboard (Lower Priority)
11. **Headless.Messaging.Dashboard** - UI testing
12. **Headless.Messaging.Dashboard.K8s** - K8s-specific
13. **Headless.Messaging.OpenTelemetry** - Observability

---

## Test Data Builders

### Shared Test Fixtures

```csharp
public static class MessageTestData
{
    public static Message CreateTestMessage(string? id = null) => new(
        headers: new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { Headers.MessageId, id ?? Guid.NewGuid().ToString() },
            { Headers.CorrelationId, Guid.NewGuid().ToString() },
        },
        value: new { Email = "test@test.com", Name = "Test User" }
    );

    public static MediumMessage CreateMediumMessage(string id = "1") => new()
    {
        DbId = id,
        Origin = CreateTestMessage(id),
        Content = JsonSerializer.Serialize(CreateTestMessage(id)),
    };
}
```

### Consumer Test Doubles

```csharp
public sealed class TestConsumer<TMessage> : IConsume<TMessage>
{
    private readonly ConcurrentBag<TMessage> _received = [];
    private readonly TaskCompletionSource _tcs = new();

    public IReadOnlyCollection<TMessage> Received => _received;
    public Task WaitForMessage => _tcs.Task;

    public ValueTask Consume(ConsumeContext<TMessage> context, CancellationToken ct)
    {
        _received.Add(context.Message);
        _tcs.TrySetResult();
        return ValueTask.CompletedTask;
    }
}
```

---

## Coverage Targets

| Coverage Type | Target | Minimum |
|---------------|--------|---------|
| Line Coverage | 85% | 80% |
| Branch Coverage | 80% | 70% |
| Mutation Score | 85% | 70% |

---

## Notes

1. All integration tests should use Testcontainers for infrastructure
2. Integration tests must be isolated and not share state
3. Use `[Collection]` attribute to share container fixtures across tests
4. Mark flaky tests with `[Trait("Category", "Flaky")]`
5. Use `TimeProvider` for time-dependent tests
6. All async tests should pass `AbortToken` from `TestBase`
