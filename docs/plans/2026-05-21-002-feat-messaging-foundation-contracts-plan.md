---
date: 2026-05-21
type: feat
status: active
depth: deep
origin: docs/plans/2026-05-21-001-feat-messaging-bus-queue-split-plan.md
part_of: messaging-bus-queue-split
sequence: 1
---

# feat: Messaging Foundation Contracts

## Summary

Create the bus/queue contract foundation for the messaging split. This slice owns the new intent abstraction packages, the public publisher/transport interfaces, the core publisher implementations, consumer registration helpers, and bootstrap-time capability validation. It deliberately stops before storage schema changes and provider migrations.

Parent design map: [2026-05-21-001-feat-messaging-bus-queue-split-plan.md](2026-05-21-001-feat-messaging-bus-queue-split-plan.md).

## Scope

In scope:

- Parent units: U1, U2, U3, plus the startup-validation design from U4.
- Requirements: R1, R2, R3, R4, R5, R6, R18.
- Acceptance examples: AE1, AE2, AE4, and the bootstrap-validation portion of AE6.
- Current WIP reconciliation: this worktree already contains initial `Bus.Abstractions` / `Queue.Abstractions` projects, `IntentType`, and `MessagePublishOptionsBase`; treat them as scaffold to audit and complete.

Out of scope:

- SQL/InMemory storage schema changes.
- Drainer row dispatch implementation beyond the validator contract.
- Provider migrations.
- OpenTelemetry/dashboard/docs rewrite, except comments/XML docs needed for new public APIs.

## Key Decisions

- Intent is type-level: `IBus` / `IOutboxBus` for broadcast, `IQueue` / `IOutboxQueue` for point-to-point.
- Durability is a second type-level axis: direct interfaces do not persist; outbox interfaces persist.
- `PublishOptions` and `EnqueueOptions` inherit shared `MessagePublishOptionsBase`; `Delay` is declared on derived options and honored only by outbox publishers.
- `ConsumeContext<T>.IntentType` is required and registration-derived. Do not rely on enum zero as a default.
- Provider capability is not inferred from transitive `Headless.Messaging.Core` references. Capability requires provider-owned direct references plus setup-registered `I*Transport`.
- Missing transport support fails in the existing `Bootstrapper` / `IBootstrapper.BootstrapAsync` path before dispatch starts. Do not invent `MessagingBuilder.Build()`.

## Files

- Audit/modify: `src/Headless.Messaging.Bus.Abstractions/Headless.Messaging.Bus.Abstractions.csproj`
- Audit/modify: `src/Headless.Messaging.Bus.Abstractions/IBus.cs`
- Audit/modify: `src/Headless.Messaging.Bus.Abstractions/IOutboxBus.cs`
- Audit/modify: `src/Headless.Messaging.Bus.Abstractions/IBusTransport.cs`
- Audit/modify: `src/Headless.Messaging.Bus.Abstractions/PublishOptions.cs`
- Create/modify: `src/Headless.Messaging.Bus.Abstractions/ServiceCollectionExtensions.cs`
- Audit/modify: `src/Headless.Messaging.Queue.Abstractions/Headless.Messaging.Queue.Abstractions.csproj`
- Audit/modify: `src/Headless.Messaging.Queue.Abstractions/IQueue.cs`
- Audit/modify: `src/Headless.Messaging.Queue.Abstractions/IOutboxQueue.cs`
- Audit/modify: `src/Headless.Messaging.Queue.Abstractions/IQueueTransport.cs`
- Audit/modify: `src/Headless.Messaging.Queue.Abstractions/EnqueueOptions.cs`
- Create/modify: `src/Headless.Messaging.Queue.Abstractions/ServiceCollectionExtensions.cs`
- Modify: `src/Headless.Messaging.Abstractions/ConsumeContext.cs`
- Audit/modify: `src/Headless.Messaging.Abstractions/IntentType.cs`
- Audit/modify: `src/Headless.Messaging.Abstractions/MessagePublishOptionsBase.cs`
- Delete: `src/Headless.Messaging.Abstractions/IDirectPublisher.cs`
- Delete: `src/Headless.Messaging.Abstractions/IOutboxPublisher.cs`
- Delete: `src/Headless.Messaging.Abstractions/IScheduledPublisher.cs`
- Delete: `src/Headless.Messaging.Abstractions/IMessagePublisher.cs`
- Delete: `src/Headless.Messaging.Abstractions/MessagePublisherExtensions.cs`
- Move/rename or retain: `src/Headless.Messaging.Abstractions/PublisherSentFailedException.cs`
- Move/split: `src/Headless.Messaging.Core/Transport/ITransport.cs`
- Modify: `src/Headless.Messaging.Abstractions/PublishOptions.cs`
- Create: `src/Headless.Messaging.Core/Internal/Bus.cs`
- Create: `src/Headless.Messaging.Core/Internal/OutboxBus.cs`
- Create: `src/Headless.Messaging.Core/Internal/Queue.cs`
- Create: `src/Headless.Messaging.Core/Internal/OutboxQueue.cs`
- Delete: `src/Headless.Messaging.Core/Internal/DirectPublisher.cs`
- Delete: `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs`
- Modify: `src/Headless.Messaging.Core/Setup.cs`
- Modify: `src/Headless.Messaging.Core/ServiceCollectionExtensions.cs`
- Modify: `src/Headless.Messaging.Core/Internal/PublishMiddlewarePipeline.cs`
- Modify: `src/Headless.Messaging.Core/Configuration/MessagingBuilder.cs`
- Modify: `src/Headless.Messaging.Core/Processor/Dispatcher.cs`
- Modify: `src/Headless.Messaging.Core/ConsumerMetadata.cs`
- Modify: `src/Headless.Messaging.Core/Internal/ConsumerExecutorDescriptor.cs`
- Modify: `src/Headless.Messaging.Core/Transport/IConsumerClientFactory.cs`
- Modify: `src/Headless.Messaging.Core/Internal/IRuntimeSubscriber.cs`
- Modify: `src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs`
- Modify: `headless-framework.slnx`

## Approach

1. Reconcile the WIP scaffold first: attach new projects to `headless-framework.slnx`, audit XML docs, package references, `[PublicAPI]`, SDK usage, and source headers.
2. Complete the public contract surface and remove the old publisher interfaces and extension types.
3. Split the unified transport contract into `IBusTransport` and `IQueueTransport`; keep shared transport primitives such as `BrokerAddress` in Core.
4. Implement `Bus`, `OutboxBus`, `Queue`, and `OutboxQueue` as separate `internal sealed` classes.
5. Wire all four publishers through existing setup and publish middleware. Direct publishers do not touch storage; outbox publishers persist a `MediumMessage` carrying the correct `IntentType`.
6. Extend the existing `ConsumerMetadata` / registry path for `AddBusConsumer<T,THandler>()` and `AddQueueConsumer<T,THandler>()`. Do not add a parallel consumer-registration model.
7. Add bootstrap-time validation in `IBootstrapper.BootstrapAsync` so missing `IBusTransport` / `IQueueTransport` support fails before dispatch begins.

## Test Suite Design

- New unit tests: `tests/Headless.Messaging.Bus.Abstractions.Tests.Unit/`
- New unit tests: `tests/Headless.Messaging.Queue.Abstractions.Tests.Unit/`
- Existing shared tests: `tests/Headless.Messaging.Abstractions.Tests.Unit/`
- Core unit tests: `tests/Headless.Messaging.Core.Tests.Unit/`
- Compile-time probe: `tests/Headless.Messaging.PackageReference.Tests.Probe/`

## Test Scenarios

- Queue-only abstraction probe cannot resolve `IBus`.
- `Bus.Abstractions` does not expose queue symbols; `Queue.Abstractions` does not expose bus symbols.
- `IntentType` values remain storage-stable: `Bus = 0`, `Queue = 1`.
- `ConsumeContext<T>` requires explicit `IntentType`; test helpers do not default to `Bus`.
- `OutboxQueue.EnqueueAsync` persists a queue-intent message with `DelayTime` and `ExpiresAt` when delayed.
- `OutboxBus.PublishAsync` persists a bus-intent message with immediate-dispatch status when not delayed.
- Direct `Bus.PublishAsync` invokes only `IBusTransport`; direct `Queue.EnqueueAsync` invokes only `IQueueTransport`.
- `AddBusConsumer<T,THandler>()` and `AddQueueConsumer<T,THandler>()` create existing metadata entries distinguished by `IntentType`.
- Dual registration of the same handler for both intents produces two resolvable metadata/descriptor entries.
- Bootstrap validation throws a clear `InvalidOperationException` when bus intent is registered without `IBusTransport`; symmetric for queue intent.

## Verification

- `dotnet build headless-framework.slnx --no-incremental` is green.
- Relevant unit/probe tests pass.
- `rg "IDirectPublisher|IOutboxPublisher|IScheduledPublisher|IMessagePublisher" src tests` returns zero hits after the deletion lands.
- `rg "DirectPublisher|OutboxPublisher" src tests` returns zero hits except intentional rename-history text in plan docs.
- `headless-framework.slnx` includes both new abstraction projects and their tests.

## Handoff Criteria

This plan is complete when the new contracts and core publisher/consumer registration surface compile, the old publisher API is gone, and bootstrap validation can reject unsupported intent use before any provider migration starts.
