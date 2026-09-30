# Removal candidates: Headless.Messaging.Core.Tests.Unit

Tests in `tests/Headless.Messaging.Core.Tests.Unit` whose subject is the old Messaging registration API. Delete them in
the same change that deletes the API. Every other old-API use in this project was converted to attribute-declared
consumers (`Helpers/ConsumerModule.cs`, `setup.AddConsumer<T>()`), `Message<T>(name, version)` contracts, and `Tune`.

Delete this file in the removal change.

## Whole files

- `ForMessageRegistrationTests.cs` — all 45 tests exercise `Bus/Queue.ForMessage`, consumer builders, `HandlerId`, `Group`, and assembly scanning. Surviving guarantees and where they are covered now:
  - explicit name reaches a publish before the consumer drain → `Registration/MessageContractTests.should_resolve_the_contract_name_for_a_publish_before_the_consumer_drain` (new)
  - cross-type name collision at startup, case-insensitive → `Registration/MessageContractTests.should_reject_two_message_types_declaring_one_contract_name_at_startup` (new)
  - identity length limit → `Registration/ConsumerDeclarationTests.should_reject_an_identity_longer_than_durable_storage_holds`
  - contract version validation → `Registration/MessageContractTests.should_reject_an_invalid_name_or_version`
  - duplicate registration merge / conflict → `Registration/ConsumerHostControlTests` identity and Queue conflict tests, `Registration/GeneratedModuleRegistrationTests.should_register_a_module_once_when_contributions_and_the_host_add_it_in_any_order`
  - several consumers for one message → `IntegrationTests/BusIntegrationTests.should_deliver_message_to_each_group_for_same_message_type`, `ConsumerServiceSelectorTests.should_handle_multiple_consumers_for_same_topic`
  - **no new-shape equivalent** (decide before deleting): per-consumer inbox retention (`should_capture_valid_per_consumer_inbox_retention_and_reject_invalid_durations`), per-consumer circuit-breaker override (`should_reject_duplicate_same_consumer_registration_with_conflicting_circuit_breaker`, `should_apply_scanned_consumer_circuit_breaker_override*`), `Concurrency(0)` rejection through `Tune` (`should_throw_when_concurrency_is_zero`; configuration-bound values are covered by `ConsumerHostControlTests.should_fail_startup_when_configuration_tuning_is_invalid`).
- `Registration/ProviderConfigBagTests.cs` — all tests drive `BusMessageBuilder<T>` and `ForMessage` provider-config overlays. Consumer-scope provider config through `Tune` is covered by the new `Registration/ConsumerTuningProviderConfigTests.cs`. Message-scope provider config (`IMessageProviderConfigBuilder<T>`, used by the Kafka/NATS/Azure Service Bus `ForMessage` extensions) has no `Message<T>` equivalent.
- `Registration/MessagingRegistrationApiSurfaceTests.cs` — reflection checks on the `Bus`/`Queue` roots, `IBusMessageBuilder<>`/`IQueueMessageBuilder<>`, `IScannedConsumerBuilder`, and `ForConsumersFromAssembly*`. `message_registration_owns_its_lane` survives only if `MessageRegistration` keeps `Lane`; `message_scoped_middleware_registration_requires_an_explicit_lane` needs `AddConsumeMiddlewareFor` dropped from its filter, keeping `AddPublishMiddlewareFor`.
- `IntegrationTests/IntegrationTestBase.cs` — no subclass uses it; its only dependency is `AddTestSetup`.
- `Helpers/TestServiceCollectionExtensions.cs` — `AddTestSetup` scans the assembly with `ForConsumersFromAssembly` and sets `DefaultGroupName`; only `IntegrationTestBase` calls it.

## Individual tests

- `ConsumerServiceSelectorTests.should_use_explicit_group_without_appending_convention_suffixes` — asserts an explicit `Group()` survives `UseApplicationId`.
- `ConsumerServiceSelectorTests.should_use_default_group_when_not_specified` — asserts the convention group from `UseApplicationId` and `GetDefaultHandlerId`. A declared Bus consumer's group is its identity, covered by `ConsumerHostControlTests.should_register_one_bus_identity_covering_two_messages_under_one_subscription`.
- `MessagingBuilderTests.should_register_consumer_in_di_as_scoped` — asserts `ForMessage` registers `IConsume<T>` in DI; declared consumers are built per delivery, not resolved from DI.
- `MessagingBuilderTests.should_use_explicit_default_group_name_when_configured` — `MessagingOptions.DefaultGroupName`.
- `MessagingBuilderTests.should_apply_group_name_prefix_to_generated_groups` — `GroupNamePrefix` with `UseApplicationId`.
- `MessagingBuilderTests.with_circuit_breaker_keys_the_override_by_consumer_identity` — `WithCircuitBreaker` on the consumer builder; `Tune` has no circuit-breaker setting. Circuit keying by identity at runtime stays covered by `ConsumerRegisterTests.startup_keys_circuits_by_consumer_identity_and_pauses_its_clients_on_open`.
- `CircuitBreaker/CircuitBreakerOptionsTests` (`ConsumerCircuitBreakerRegistryTests`) — `with_circuit_breaker_on_consumer_builder_registers_per_group` and the two section comments name the `ForMessage`/`WithCircuitBreaker` path; the registry tests themselves stay if `ConsumerCircuitBreakerRegistry` stays.
- `IntegrationTests/BusIntegrationTests.should_dispatch_directly_to_consumer_without_transport` — resolves the consumer through `IMessageDispatcher` from DI (reflection/compiled dispatch).
- `IntegrationTests/IConsumeIntegrationTests.should_invoke_handler_through_dispatcher` — `IMessageDispatcher` path.
- `IntegrationTests/IConsumeIntegrationTests.should_dispatch_to_correct_handler_based_on_message_type` — `IMessageDispatcher` path.
- `IntegrationTests/IConsumeIntegrationTests.should_resolve_consumers_from_di_container` — `ForMessage` DI registration of `IConsume<T>`.
- `IntegrationTests/IConsumeIntegrationTests.should_use_topic_mapping_in_discovery` — `ForConsumersFromAssembly`.
- `IntegrationTests/IConsumeIntegrationTests.should_handle_multi_message_consumer_registration` — `ForConsumersFromAssembly`; one identity covering two messages is covered by `ConsumerHostControlTests.should_register_one_bus_identity_covering_two_messages_under_one_subscription`. Delete `MultiEventConsumer` with it.
- `Configuration/DefaultDeliveryModeTests.should_inherit_global_mode_for_an_assembly_scan_registration` — assembly scan.
- `Configuration/DefaultDeliveryModeTests.should_apply_explicit_type_policy_alongside_assembly_scan_registrations_for_the_same_type` — assembly scan plus `ForMessage`; also delete `_ConfigureScannedConsumer`, `ScannedMessage`, `FirstScannedConsumer`, `SecondScannedConsumer`, and the `Action<MessagingSetupBuilder>` `_CreateProvider` overload.
- `Configuration/MessagingCapabilityModelTests.should_reject_missing_durable_identity_before_storage_or_processors_start` — a `ForMessage` consumer without an identity; a declared consumer cannot omit one.
- `Configuration/MessagingCapabilityModelTests.should_validate_only_effective_lane_mapping_when_lane_override_replaces_global_fallback` — a lane-owned `ForMessage` name overriding the type-global mapping; `Message<T>` has one name for both lanes.
- `Configuration/MessagingCapabilityModelTests.should_check_message_name_collisions_using_effective_lane_mapping` — same lane-override concept.
- `Registration/ContributionOrderTests.should_still_reject_a_message_type_registered_twice_on_one_lane_across_setup_calls` — duplicate `ForMessage`; delete `_ForBusMessage` with it.
- `Registration/MessageContractTests.should_reject_a_contract_for_a_message_already_declared_through_for_message` — `Message<T>` vs `ForMessage` conflict.
- `Registration/MessageContractTests.should_reject_for_message_for_a_message_already_declared_by_a_contract` — same conflict, other order.
- `Configuration/MessagingBuilderMiddlewareTests.should_record_typed_consume_middleware_group_and_message_type` — group-keyed `AddConsumeMiddlewareFor`.
- `Configuration/MessagingBuilderMiddlewareTests.should_apply_configured_group_prefix_to_typed_consume_middleware_group` — `GroupNamePrefix` with `AddConsumeMiddlewareFor`.
- `Configuration/MiddlewareRegistryReceiveTests.should_match_typed_receive_descriptor_on_exact_type_group_and_lane` — group-keyed `AddReceiveMiddlewareFor`.
- `Configuration/MiddlewareRegistryReceiveTests.should_keep_only_global_receive_middleware_for_a_queue_lane_lookup` — registers a group-keyed receive middleware to prove the lane filter.
- `Configuration/MiddlewareRegistryReceiveTests.should_order_global_before_typed_then_priority_then_registration_order` — ordering includes group-keyed receive middleware; the global-only ordering needs a replacement if it must survive.
- `Configuration/MiddlewareRegistryReceiveTests.should_apply_configured_group_prefix_to_typed_receive_middleware_group` — `GroupNamePrefix` with `AddReceiveMiddlewareFor`.
- `Configuration/MiddlewareRegistryReceiveTests.should_register_receive_middleware_as_scoped_ireceivemiddleware` — registers through `AddReceiveMiddlewareFor`; switch to `AddReceiveMiddleware<T>()` if the scoped-lifetime check must survive.
- `Configuration/MiddlewareRegistryReceiveTests.should_record_receive_descriptor_identity` — `AddReceiveMiddlewareFor` descriptor identity.
- `Internal/ConsumeMiddlewarePipelineMigratedTests.should_invoke_bus_middleware_before_typed_consume_middleware`, `should_keep_direct_bus_consume_middleware_when_typed_descriptor_group_does_not_match`, `should_skip_typed_consume_middleware_for_other_group`, `should_skip_typed_consume_middleware_for_other_message_type` — group-keyed `AddConsumeMiddlewareFor`. Per-consumer middleware through `Tune` is covered by `ConsumerHostControlTests.should_run_tuned_middleware_only_for_the_tuned_consumer`.

## Edits, not deletions

- `Configuration/MessagingOptionsCopyToTests._SetNonDefaultValues` — drop the `DefaultGroupName` and `GroupNamePrefix` assignments when the properties go; the copy test reflects over every public property.
- `Configuration/MessagingOptionsValidatorMiddlewareTests.should_reject_typed_consume_middleware_registered_at_bus_scope` — the expected message names `AddConsumeMiddlewareFor`; update it with the validator's text.
- `EveryInstance/EveryInstanceContractTests` and `Internal/ProviderHeaderContributionTests` — construct `MessageConsumerRegistration` with `IsAssemblyScan: false`; drop the argument when the parameter goes.
- `Internal/ConsumeMiddlewarePipelineTests` and `Internal/ConsumeMiddlewarePipelineMigratedTests.should_resolve_separate_middleware_instances_per_consume_for_singleton_pipeline` — stub `IMessageDispatcher` behind descriptors without a generated dispatch. If the dispatcher fallback goes, give those descriptors a `Dispatch` delegate instead.
- `Helpers/StableConsumerTestContracts.cs` — `StableContract` and `ConfigureKnownScannedConsumer` serve only the tests above; keep `UseProcessLocalInMemoryStorage`.

## Converted tests whose premise narrowed

- `ConsumerRegisterTests.startup_keys_circuits_by_consumer_identity_and_pauses_its_clients_on_open` and `receive_keys_the_circuit_by_consumer_identity_not_subscription` used `Group("ready-group")` to make the subscription name differ from the identity. A declared Bus consumer subscribes under its identity, so the two no longer differ; the negative `"ready-group"` assertions were removed and the tests now check keying by identity only.
- `RuntimeSubscriberTests.runtime_registry_uses_the_bus_lane_for_names_descriptors_and_invoker_identity` declared different Bus and Queue names; `Message<T>` declares one name, so the test now proves lane selection through descriptors and invoker lookup only.
