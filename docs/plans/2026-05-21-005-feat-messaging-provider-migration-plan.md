---
date: 2026-05-21
type: feat
status: completed
depth: deep
origin: docs/plans/2026-05-21-001-feat-messaging-bus-queue-split-plan.md
part_of: messaging-bus-queue-split
sequence: 4
depends_on:
  - docs/plans/2026-05-21-002-feat-messaging-foundation-contracts-plan.md
  - docs/plans/2026-05-21-003-feat-messaging-intent-persistence-drainer-plan.md
  - docs/plans/2026-05-21-004-feat-messaging-inmemory-testing-vertical-plan.md
---

# feat: Messaging Provider Migration

## Summary

Migrate the external messaging providers to the new bus/queue transport contracts. This slice covers dual-intent providers, queue-only providers, the AWS rename/split, and the new RedisPubSub bus-only package. It depends on the InMemory vertical slice and testing harness so provider semantics can be checked consistently.

Parent design map: [2026-05-21-001-feat-messaging-bus-queue-split-plan.md](2026-05-21-001-feat-messaging-bus-queue-split-plan.md).

## Scope

In scope:

- Parent units: U7, U8, U9.
- Requirements: R13, R14, R15, R17.
- Acceptance examples: AE6, AE7, AE8.
- Providers: RabbitMQ, NATS Core, Azure Service Bus, AWS SNS/SQS, Kafka, RedisStreams, Pulsar, RedisPubSub.

Out of scope:

- InMemory provider and testing harness migration.
- Storage/drainer contract changes.
- OTel/dashboard/docs final rewrite, except provider README capability notes needed by this slice.
- NATS JetStream-backed broker replay/durable consumer retention; that remains follow-up under #233.

## Capability Matrix

| Package | Bus | Queue |
|---|:---:|:---:|
| `Headless.Messaging.RabbitMq` | yes | yes |
| `Headless.Messaging.Nats` | yes | yes |
| `Headless.Messaging.AzureServiceBus` | yes | yes |
| `Headless.Messaging.Aws` | yes | yes |
| `Headless.Messaging.Pulsar` | yes | yes |
| `Headless.Messaging.Kafka` | no | yes |
| `Headless.Messaging.RedisStreams` | no | yes |
| `Headless.Messaging.RedisPubSub` | yes | no |

Capability requires direct provider references to the intent abstraction package plus setup registration of the matching `I*Transport`. Transitive `Core` references do not count.

## Files

- Modify: `src/Headless.Messaging.RabbitMq/RabbitMqTransport.cs`
- Create/modify: `src/Headless.Messaging.RabbitMq/RabbitMqBusTransport.cs`
- Create/modify: `src/Headless.Messaging.RabbitMq/RabbitMqQueueTransport.cs`
- Modify: `src/Headless.Messaging.RabbitMq/Setup.cs`
- Modify: `src/Headless.Messaging.RabbitMq/Headless.Messaging.RabbitMq.csproj`
- Modify: `src/Headless.Messaging.Nats/NatsTransport.cs`
- Create/modify: `src/Headless.Messaging.Nats/NatsBusTransport.cs`
- Create/modify: `src/Headless.Messaging.Nats/NatsQueueTransport.cs`
- Modify: `src/Headless.Messaging.Nats/Setup.cs`
- Modify: `src/Headless.Messaging.Nats/Headless.Messaging.Nats.csproj`
- Modify: `src/Headless.Messaging.AzureServiceBus/AzureServiceBusTransport.cs`
- Create/modify: `src/Headless.Messaging.AzureServiceBus/AzureServiceBusBusTransport.cs`
- Create/modify: `src/Headless.Messaging.AzureServiceBus/AzureServiceBusQueueTransport.cs`
- Modify: `src/Headless.Messaging.AzureServiceBus/Setup.cs`
- Modify: `src/Headless.Messaging.AzureServiceBus/Headless.Messaging.AzureServiceBus.csproj`
- Rename directory: `src/Headless.Messaging.AwsSqs/` -> `src/Headless.Messaging.Aws/`
- Create/modify: `src/Headless.Messaging.Aws/AmazonSnsBusTransport.cs`
- Create/modify: `src/Headless.Messaging.Aws/AmazonSqsQueueTransport.cs`
- Keep/modify: `src/Headless.Messaging.Aws/AmazonSqsConsumerClient.cs`
- Modify: `src/Headless.Messaging.Aws/Setup.cs`
- Modify: `src/Headless.Messaging.Aws/Headless.Messaging.Aws.csproj`
- Rename: `tests/Headless.Messaging.AwsSqs.Tests.Integration/` -> `tests/Headless.Messaging.Aws.Tests.Integration/`
- Rename: `demo/Headless.Messaging.AwsSqs.InMemory.Demo/` -> `demo/Headless.Messaging.Aws.InMemory.Demo/`
- Modify: `src/Headless.Messaging.Kafka/KafkaTransport.cs`
- Modify: `src/Headless.Messaging.Kafka/Setup.cs`
- Modify: `src/Headless.Messaging.Kafka/Headless.Messaging.Kafka.csproj`
- Modify: `src/Headless.Messaging.RedisStreams/RedisTransport.cs`
- Modify: `src/Headless.Messaging.RedisStreams/Setup.cs`
- Modify: `src/Headless.Messaging.RedisStreams/Headless.Messaging.RedisStreams.csproj`
- Modify: `src/Headless.Messaging.Pulsar/PulsarTransport.cs`
- Create/modify: `src/Headless.Messaging.Pulsar/PulsarBusTransport.cs`
- Create/modify: `src/Headless.Messaging.Pulsar/PulsarQueueTransport.cs`
- Modify: `src/Headless.Messaging.Pulsar/Setup.cs`
- Modify: `src/Headless.Messaging.Pulsar/Headless.Messaging.Pulsar.csproj`
- Create: `src/Headless.Messaging.RedisPubSub/Headless.Messaging.RedisPubSub.csproj`
- Create: `src/Headless.Messaging.RedisPubSub/RedisPubSubBusTransport.cs`
- Create: `src/Headless.Messaging.RedisPubSub/RedisPubSubOptions.cs`
- Create: `src/Headless.Messaging.RedisPubSub/Setup.cs`
- Create: `src/Headless.Messaging.RedisPubSub/README.md`
- Modify: `headless-framework.slnx`
- Modify provider unit/integration test projects under `tests/Headless.Messaging.*`

## Approach

1. Migrate dual-intent providers to separate bus and queue transport classes with shared connection helpers.
2. Keep NATS on NATS Core for this slice; do not add a JetStream startup requirement.
3. Rename AWS from `AwsSqs` to `Aws`; implement SNS as bus and SQS as queue while keeping `AmazonSqsConsumerClient` as the shared consume path.
4. Narrow Kafka and RedisStreams to queue-only contracts and registrations.
5. Split Pulsar by subscription semantics: independent subscriptions for bus, shared/key-shared subscription for queue.
6. Add RedisPubSub as a new bus-only provider with prominent volatile-delivery semantics.
7. Update setup registrations and csproj direct references so provider capability matches the matrix.
8. Use the intent-aware harness from the InMemory slice to apply consistent transport tests.

## Test Suite Design

- RabbitMQ and NATS use existing integration projects.
- AWS uses renamed LocalStack integration tests.
- Azure Service Bus remains unit-only unless implementation deliberately adds new emulator/live infrastructure.
- Kafka, RedisStreams, and Pulsar update existing unit tests.
- RedisPubSub should use a Redis Testcontainers integration suite if feasible; otherwise document and test through a deterministic fake/subscriber seam.

## Test Scenarios

- RabbitMQ/NATS/Azure bus broadcast delivers to all subscribers.
- RabbitMQ/NATS/Azure queue sends to exactly one competing consumer.
- AWS bus publish uses SNS and subscribed SQS queues each receive one copy.
- AWS queue enqueue sends directly to SQS and queue consumers receive point-to-point.
- AWS LocalStack provisioning covers SNS topic, SQS queue subscription, and direct SQS queue.
- Kafka and RedisStreams packages do not directly reference `Bus.Abstractions`.
- Kafka bus-intent app fails during `IBootstrapper.BootstrapAsync()` with missing `IBusTransport`.
- Kafka/RedisStreams queue paths work end-to-end through outbox, drainer, transport, consumer, and received storage.
- RedisPubSub package directly references only `Bus.Abstractions` and exposes no `IQueueTransport`.
- RedisPubSub broadcast delivers to connected subscribers.
- RedisPubSub offline subscriber does not receive messages after reconnect; this verifies volatile delivery.
- Pulsar bus publishes to independent subscriptions and each logical subscriber receives a copy.
- Pulsar queue publishes to shared/key-shared subscription and exactly one worker receives.
- Provider README capability tables match csproj references and setup registrations.

## Verification

- Provider unit/integration suites pass according to each provider's existing test infrastructure.
- `dotnet build headless-framework.slnx --no-incremental` is green.
- `make pack` produces package artifacts including `Headless.Messaging.RedisPubSub`.
- `rg "AwsSqs" src tests demo` returns zero hits except intentional rename-history text in plan docs or changelogs.
- `rg "ITransport" src/Headless.Messaging.* tests/Headless.Messaging.*` shows no provider implementations or tests using the deleted unified transport contract, except intentional historical plan text.

## Handoff Criteria

This plan is complete when every provider's runtime registration, csproj references, tests, and README capability statement agree with the bus/queue capability matrix.
