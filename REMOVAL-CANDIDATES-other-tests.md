# Removal candidates: tests whose subject is the old registration API

Scope: every test project except `Headless.Messaging.Core.Tests.Unit`, HybridCache, and DistributedLocks. Delete or trim these together with the API they cover.

## Old API is the subject

- `tests/Headless.Messaging.Abstractions.Tests.Unit/MessagingConventionsTests.cs` `should_have_null_default_group`: asserts the default of `MessagingConventions.DefaultGroup`.
- `tests/Headless.Messaging.Abstractions.Tests.Unit/MessagingConventionsTests.cs` `should_allow_custom_default_group`: asserts `MessagingConventions.DefaultGroup` round-trips.
- `tests/Headless.Messaging.Abstractions.Tests.Unit/MessagingConventionsExtensionsTests.cs` `should_mutate_only_the_targeted_properties_when_topic_prefix_suffix_and_default_group`: covers `WithDefaultGroup`; keep the prefix and suffix assertions when the group ones go.
- `tests/Headless.Messaging.Abstractions.Tests.Unit/MessagingConventionsExtensionsTests.cs` `should_support_chaining_without_resetting_prior_values_when_extension_helpers`: chains `WithDefaultGroup`; drop that step and its assertion.
- `tests/Headless.Messaging.RabbitMq.Tests.Unit/RabbitMqMessageBuilderExtensionsTests.cs` `should_store_consumer_prefetch_config`: covers `UseRabbitMq` on the fluent consumer builder; `should_store_consumer_config_when_tuning_a_declared_consumer` already covers the `Tune` path.
- `tests/Headless.Messaging.Kafka.Tests.Unit/KafkaMessageBuilderExtensionsTests.cs` `should_store_consumer_config_without_partition_surface`: covers `UseKafka` on the fluent consumer builder; the `Tune` sibling already covers the replacement.

## Old API is the subject, and the new API has no equivalent yet

`Message<T>(name, version)` has no provider hatch, so message-level provider settings can only be written through `ForMessage`. Deleting these tests without a replacement drops the capability, not just the tests.

- `tests/Headless.Messaging.Nats.Tests.Unit/SetupTests.cs` `should_not_throw_when_sharded_producer_and_sharded_consumer`, `should_throw_with_clear_message_when_sharded_producer_has_unsharded_consumer`, `should_not_throw_when_message_has_no_subject_shard`: shard-symmetry validation between message-level `UseNats(SubjectShard(...))` and consumer-level `UseNats(Sharded())`, which only the `ForMessage` path runs.
- `tests/Headless.Messaging.Nats.Tests.Unit/NatsMessageBuilderExtensionsTests.cs` `should_store_subject_shard_header_contribution`, `should_reject_invalid_subject_shard_tokens`: message-level `SubjectShard` on `BusMessageBuilder`.
- `tests/Headless.Messaging.AzureServiceBus.Tests.Unit/AzureServiceBusMessageBuilderExtensionsTests.cs` `should_store_partition_key_header_contribution`, `should_reject_partition_key_longer_than_service_bus_limit`: message-level `PartitionKey` on `BusMessageBuilder`.
- `tests/Headless.Messaging.Aws.Tests.Unit/AwsMessageBuilderExtensionsTests.cs` `should_store_message_group_id_header_contribution`, `should_reject_message_group_id_longer_than_sqs_limit`: message-level `MessageGroupId` on `BusMessageBuilder`.
- `tests/Headless.Messaging.Kafka.Tests.Unit/KafkaMessageBuilderExtensionsTests.cs` `should_store_partition_key_header_contribution`: message-level `PartitionKey` on `QueueMessageBuilder`.

## Not registration API, but bound to the reflection dispatch being deleted

These build `ConsumerExecutorDescriptor` by hand (`MethodInfo`, `GroupName`) or read group-keyed descriptors. They compile and pass today and need rework when reflection dispatch and group keys go.

- `tests/Headless.EntityFramework.Messaging.Tests.Integration/OutboxBridgeIntegrationTests.cs` and `OutboxBridgeIntegrationTests.Jobs.cs`: invoke consumers through `ISubscribeInvoker` with hand-built descriptors, so the consumers are also registered as scoped services.
- `tests/Headless.Messaging.Core.Tests.Harness/TransactionalInboxScopeConformanceTests.cs`: reads the descriptor through `MethodMatcherCache.GetCandidatesMethodsOfGroupNameGrouped()`.
- `benchmarks/Headless.Messaging.Benchmarks/Scenarios/ConsumeDispatchBenchmarks.cs`: builds a descriptor with `MethodInfo` and `GroupName = "benchmark-group"`.
