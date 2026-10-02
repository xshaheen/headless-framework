# Headless.Messaging.Core.Tests.Harness

Shared test harness for messaging providers. Provides base test classes with reusable test scenarios for transport, consumer, and storage implementations.

## Overview

This harness enables consistent integration testing across all messaging providers (RabbitMQ, Kafka, NATS, AWS SQS, etc.) by defining standard test scenarios that each provider can run against its real infrastructure.

## Base Test Classes

| Class | Purpose |
|-------|---------|
| `TransportTestsBase` | Tests for `ITransport` implementations (sending messages) |
| `ConsumerClientTestsBase` | Tests for `IConsumerClient` implementations (receiving messages) |
| `TransportConsumerConformanceTestsBase` | Broker-observed round trip and real settlement invariants |
| `TransportProviderConformanceDriver` | Provider-only broker operations consumed by shared routing, ownership, isolation, startup, poison, and migration assertions |
| `BrokerFaultTestsBase` | Optional, manifest-driven recovery and fault invariants |
| `DataStorageTestsBase` | Tests for `IDataStorage` implementations (message persistence) |
| `MessagingIntegrationTestsBase` | Full pub-sub cycle tests with DI setup |

## Capability Flags

Each base class uses explicit capability flags to skip tests that don't apply to a specific provider. All messaging capability flags default to `false`; provider leaves must opt in so new tests cannot silently advertise unproven coverage.

### TransportCapabilities

```csharp
protected override TransportCapabilities Capabilities => new()
{
    SupportsOrdering = true,      // Messages maintain order (e.g., RabbitMQ, Kafka)
    SupportsDeadLetter = true,    // Dead letter queue support
    SupportsPriority = false,     // Message priority levels
    SupportsDelayedDelivery = false, // Scheduled/delayed messages
    SupportsBusTransport = true,  // Publish/fanout intent
    SupportsQueueTransport = true, // Queue/competing-consumer intent
    SupportsHeaders = true,       // Producer accepts custom message headers
};
```

### ConsumerClientCapabilities

```csharp
protected override ConsumerClientCapabilities Capabilities => new()
{
    SupportsFetchTopics = true,        // Can fetch topic metadata
    SupportsConcurrentProcessing = true, // Concurrent message handling
    SupportsReject = true,             // Exposes reject/nack wiring
    SupportsGracefulShutdown = true,   // Clean shutdown support
};
```

These flags describe producer or client API behavior only. Broker-observed routing, ownership, isolation, settlement, migration, and fault behavior are tracked separately by `TransportConformanceManifest`. Every profile also declares the provider-native terminal invariant, maximum delivery count, and restart-inclusive observation window used to rule out delayed malformed-envelope redelivery. Its three scenario states are:

- `Supported`: an executable broker-backed assertion exists;
- `Unsupported`: the gap has a rationale and linked issue;
- `NotApplicable`: the provider topology or protocol makes the scenario inapplicable, with a rationale.

An enabled real-broker leaf must support every mandatory baseline cell. Fabricated callback values and producer-side message inspection are not settlement or broker-delivery evidence.

## Broker Conformance Sessions

Provider leaves create an isolated `TransportConsumerConformanceSession` with a unique destination, real producer, real consumer, bounded no-redelivery window, and provider cleanup callback. The shared base:

- waits for `IConsumerClient.WaitUntilReadyAsync` before publishing;
- captures the broker-delivered body, headers, and opaque settlement value;
- passes that exact value to `CommitAsync` or `RejectAsync`;
- observes redelivery or its absence beyond the provider's configured settlement boundary;
- bounds startup, receive, shutdown, and negative-observation windows;
- includes consumer diagnostics when a delivery times out.

NATS is a reference implementation. Its test leaf uses lane-qualified memory-backed JetStream streams and durables with a one-second `AckWait`, making ACK/NAK behavior deterministic without changing production defaults. Its provider leaf proves group fan-out, replica competition, queue ownership, same-name lane isolation, terminal malformed-envelope acknowledgement across restart, and a drained legacy stream followed by roll-forward-only publication. Consumer pause/recovery is tracked separately from broker interruption so the suite does not overstate what was fault-injected.

RabbitMQ uses lane-qualified exchanges, routing keys, and owned queues; ACK absence is observed beyond the provider window, ordinary reject proves broker requeue, and malformed-envelope construction is terminally rejected without requeue. Its real-broker leaf also proves consumer-identity fan-out, replica competition, Queue ownership, same-name lane isolation, and a drained legacy exchange followed by roll-forward-only publication. AWS queue conformance is explicitly LocalStack-backed: commit proves SQS deletion, reject proves visibility-timeout redelivery with a fresh receipt context, and SNS empty-body dispatch is `NotApplicable` with its protocol rationale. AWS pause recovery and broker-restart behavior remain explicit linked gaps rather than inferred coverage.

Kafka is queue/consumer-group only in the current provider contract. Pulsar's Testcontainers leaf proves lane-qualified bus group fan-out, replica competition, queue ownership, same-name isolation, terminal malformed acknowledgement across restart, and legacy drain followed by roll-forward-only publication; its negative-ack redelivery delay is shortened only in the test fixture, and broker-restart recovery remains a linked gap. Azure Service Bus runs against a dedicated real namespace using `HEADLESS_TEST_AZURE_SERVICE_BUS_CONNECTION_STRING`. The credential must grant entity-management rights because the fixture creates and deletes only its uniquely named queues, topics, and subscriptions. Missing credentials produce precise local skips; the protected `Azure Service Bus Conformance` workflow fails preflight unless the secret exists and verifies that real tests—not only the credential marker—executed.

## Transport Conformance Matrix

`S` means the manifest cell is `Supported` and names an executable broker-backed assertion. `U` means `Unsupported` with the manifest's [tracked gap](https://github.com/xshaheen/headless-framework/issues/359). `N/A` means `NotApplicable` with a topology or protocol rationale. `S†` is executable real-service evidence that still requires the protected Azure credential; a local skip-only run is not completion evidence.

| Manifest scenario | NATS | RabbitMQ | AWS/LocalStack | Kafka | Pulsar | Azure Service Bus | InMemory | Redis |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `RoutingAffinityMappingOrRejection` | S | S | S | S | S | S† | S | S |
| `QueueRoundTrip` | S | S | S | S | S | S† | S | S |
| `BusRoundTrip` | S | S | S | N/A | S | S† | S | S |
| `HeaderRoundTrip` | S | S | S | S | S | S† | S | S |
| `EmptyBodyDispatch` | S | S | N/A | U | U | U | S | S |
| `CommitSettlement` | S | S | S | S | S | S† | S | S |
| `RejectRedelivery` | S | S | S | S | S | S† | S | S |
| `ConsumerPauseRecovery` | S | S | U | S | S | S† | U | U |
| `BrokerInterruptionRecovery` | U | U | U | U | U | U | U | U |
| `StaleSettlement` | U | U | U | U | U | U | U | U |
| `HandlerFailureRedelivery` | U | U | U | U | U | U | U | U |
| `BoundedGracefulShutdown` | S | S | S | S | S | S† | S | S |
| `BusConsumerIdentityFanOut` | S | S | S | N/A | S | S† | S | S |
| `BusReplicaCompetition` | S | S | S | N/A | S | S† | S | S |
| `QueueOwnership` | S | S | S | S | S | S† | S | S |
| `SameNameLaneIsolation` | S | S | S | N/A | S | S† | S | S |
| `StartupRejectionBeforeSideEffects` | U | U | U | S | U | U | U | U |
| `MalformedEnvelopeTerminalSettlement` | S | S | S | S | S | U | N/A | S |
| `RequestReplyRoundTrip` | S | S | N/A | N/A | U | U | S | S |
| `RequestReplyCallerIsolation` | S | S | N/A | N/A | U | U | S | S |
| `RequestReplyForeignAddressRefusal` | S | S | N/A | N/A | U | U | S | S |
| `RequestReplyCallerCleanup` | S | S | N/A | N/A | U | U | S | S |
| `RequestReplyStartupRejection` | N/A | N/A | U | U | U | U | N/A | N/A |

Evidence anchors:

- Queue/body/header, commit, reject, isolation, and shutdown: `TransportConsumerConformanceTestsBase` provider overrides.
- Empty-body broker dispatch: `should_dispatch_empty_message_body` in the NATS and RabbitMQ consumer leaves.
- Pause/resume: `BrokerFaultTestsBase.should_resume_delivery_once_after_consumer_pause` provider overrides.
- NATS, RabbitMQ, and AWS Bus fan-out: their `TransportConsumerConformanceTestsBase` provider leaves; Pulsar bus/queue intent: `PulsarTransportTests`; Azure topic/subscription fan-out: `AzureServiceBusTransportTests`.
- AWS consumer-identity fan-out, replica competition, queue ownership, same-name lane isolation, and terminal malformed-envelope deletion: `AmazonSqsConsumerClientConformanceTests` and `MalformedMessageTests` against LocalStack.
- Redis Streams Bus/Queue routing, group fan-out, replica competition, lane isolation, settlement, shutdown, and poison handling: `RedisConsumerConformanceTests` against Testcontainers Redis.
- InMemory Bus/Queue group fan-out, replica competition, ownership, and lane isolation: `InMemoryProviderConformanceTests` using the shared provider driver.
- Azure Service Bus group fan-out, replica competition, Queue ownership, and lane isolation: `AzureServiceBusConsumerClientHarnessTests` on the credential-gated managed-service tier.
- Kafka Queue ownership and terminal poison-offset handling: `KafkaConsumerClientConformanceTests`; Bus startup rejection before storage initialization: `SetupTests`.
- NATS lane isolation, consumer-identity/replica semantics, terminal malformed acknowledgement, and legacy drain/roll-forward proof: `NatsConsumerClientTests` against Testcontainers NATS JetStream.
- Pulsar lane isolation, consumer-identity/replica semantics, terminal malformed acknowledgement, and legacy drain/roll-forward proof: `PulsarConsumerClientHarnessTests` against Testcontainers Pulsar.
- RabbitMQ lane isolation, consumer-identity/replica semantics, terminal malformed rejection, and legacy drain/roll-forward proof: `RabbitMqConsumerClientConformanceTests` against Testcontainers RabbitMQ.
- InMemory request/reply round trip, caller isolation and restart, foreign reply-address refusal, and caller cleanup: `InMemoryProviderConformanceTests` running `TransportRequestReplyConformance`. RabbitMQ runs the same suite against Testcontainers RabbitMQ in `RabbitMqRequestReplyConformanceTests`, probing reply queues by passive declare; `RabbitMqReplyTransportTests` covers the exclusive reply queue's lifetime, sends to a deleted reply queue, refusal of `amq.gen-` and application queues, and re-declaration under a new address. NATS runs the suite in `NatsRequestReplyConformanceTests`, and again with stream provisioning disabled in `NatsRequestReplyWithoutStreamProvisioningTests`, probing reply subscriptions through the server's `connz` monitoring endpoint; `NatsReplyTransportTests` covers replies staying out of every JetStream stream, the reply subject surviving a server restart under the same address, immediate failure of a request no stream captures, and refusal of lane and inbox subjects. Redis runs the suite in `RedisRequestReplyConformanceTests`, probing reply channels with `PUBSUB NUMSUB` and checking by `SCAN` that no key is named after a reply address; `RedisReplyTransportTests` covers a round trip that leaves no key under the reply namespace, the reply channel surviving a dropped subscription connection under the same address, a listener opened while the server is down handing out its address once the server comes up, and refusal of lane stream keys and unrelated channels with no write. Kafka and AWS reject request/reply by design (`N/A`); Azure Service Bus and Pulsar reject it until their reply channels exist (`U`).
- AWS evidence is LocalStack-backed, not managed AWS. Azure evidence is a real isolated namespace tier, not an emulator.

### DataStorageCapabilities

```csharp
protected override DataStorageCapabilities Capabilities => new()
{
    SupportsLocking = true,           // Distributed locking (default: true)
    SupportsExpiration = true,        // Message TTL/expiration (default: true)
    SupportsConcurrentOperations = true, // Concurrent storage ops (default: true)
    SupportsDelayedScheduling = true, // Delayed message scheduling (default: true)
    SupportsMonitoringApi = true,     // Monitoring/stats API (default: true)
};
```

## Request/Reply Conformance

`TransportRequestReplyConformance` (in `RequestReply/`) runs request/reply end to end: each scenario starts a caller host and a responder host against one broker, with in-memory storage, so only the transport varies. A provider leaf wires it through its `TransportProviderConformanceDriver`:

| Driver member | Override when | Contract |
|---|---|---|
| `SupportsRequestReply` | The provider declares request/reply | `true` runs the suite; `false` runs `AssertRejectedAtStartupAsync` instead |
| `ConfigureRequestReplyTransport(MessagingSetupBuilder)` | Always, supported or not | Selects the provider for one host; called once per host, and every host must reach the same broker |
| `ConfigureRequestReplyServices(IServiceCollection)` | Wiring must win over a provider default | Runs after `AddHeadlessMessaging` for each host |
| `HasReplyObjectsAsync(string replyAddress, CancellationToken)` | The provider declares request/reply | Whether the broker still holds the queue, subscription, or channel behind a reply address; must be `true` while the caller runs |

Call each `Assert*Async` method from its own `[Fact]` with `AbortToken`: round trip, two callers, responder failure, no responder, timeout without a running responder, late reply, foreign reply address, restarted caller, tenant flow, and stopped-caller cleanup. The timeout scenario starts and stops a responder host before calling, so a broker that refuses sends to unprovisioned destinations still reaches a timeout. Each scenario names its contracts after a fresh run id, so leaves may share one broker container.

## Creating Transport Provider Tests

### 1. Create the Integration Test Project

Create `tests/Headless.Messaging.<Provider>.Tests.Integration/`:

```xml
<Project Sdk="Headless.NET.Sdk.Test">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Tests</RootNamespace>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Testcontainers.<Provider>" />
    <PackageReference Include="Testcontainers.XunitV3" />
    <PackageReference Include="xunit.v3.mtp-v2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Headless.Messaging.<Provider>\Headless.Messaging.<Provider>.csproj" />
    <ProjectReference Include="..\Headless.Messaging.Core.Tests.Harness\Headless.Messaging.Core.Tests.Harness.csproj" />
  </ItemGroup>
</Project>
```

### 2. Create the Container Fixture

```csharp
using Testcontainers.<Provider>;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class <Provider>Fixture(IMessageSink messageSink)
    : ContainerFixture<<Provider>Builder, <Provider>Container>(messageSink),
        ICollectionFixture<<Provider>Fixture>
{
    public string ConnectionString => Container.GetConnectionString();

    protected override <Provider>Builder Configure()
    {
        return base.Configure().WithImage("<provider>:<version>");
    }
}
```

### 3. Create Transport Tests

```csharp
using Headless.Messaging.Transport;
using Tests.Capabilities;

namespace Tests;

[Collection<<Provider>Fixture>]
public sealed class <Provider>TransportTests(<Provider>Fixture fixture) : TransportTestsBase
{
    protected override TransportCapabilities Capabilities => new()
    {
        SupportsOrdering = true,
        SupportsDeadLetter = true,
        // Set flags based on provider capabilities
    };

    protected override ITransport GetTransport()
    {
        // Create and return configured transport using fixture.ConnectionString
    }

    // Expose base tests as [Fact] methods
    [Fact]
    public override Task should_send_message_successfully() => base.should_send_message_successfully();

    [Fact]
    public override Task should_have_valid_broker_address() => base.should_have_valid_broker_address();

    [Fact]
    public override Task should_accept_message_with_application_headers() =>
        base.should_accept_message_with_application_headers();

    // ... expose all relevant base tests
}
```

## Creating Storage Provider Tests

### 1. Create Storage Tests

```csharp
using Headless.Messaging.Persistence;
using Tests.Capabilities;

namespace Tests;

[Collection<PostgreSqlFixture>]
public sealed class PostgreSqlStorageTests(PostgreSqlFixture fixture) : DataStorageTestsBase
{
    protected override DataStorageCapabilities Capabilities => new()
    {
        SupportsLocking = true,
        SupportsExpiration = true,
        SupportsDelayedScheduling = true,
        SupportsMonitoringApi = true,
    };

    protected override IDataStorage GetStorage()
    {
        // Create configured storage instance
    }

    protected override IStorageTableNames GetTableNames()
    {
        // Return the storage's table-name resolver
    }

    protected override ISerializer GetSerializer()
    {
        // Return the serializer the storage uses
    }

    protected override (IDataStorage Storage, MessagingOptions Options) CreateSchedulingTestStorage(TimeProvider clock)
    {
        // Create a storage whose application clock is the given one
    }

    protected override Task<int> CountReceivedMessagesByIdentityAsync(
        string messageId,
        string consumerIdentity,
        CancellationToken cancellationToken
    )
    {
        // Count received rows for the message and consumer identity
    }

    // Expose base tests
    [Fact]
    public override Task should_initialize_schema() => base.should_initialize_schema();

    [Fact]
    public override Task should_store_published_message() => base.should_store_published_message();

    // ... expose all relevant base tests
}
```

## Full Integration Tests

For complete pub-sub cycle tests with DI:

```csharp
using Headless.Messaging.Configuration;

namespace Tests;

[Collection<RabbitMqFixture>]
public sealed class RabbitMqIntegrationTests(RabbitMqFixture fixture)
    : MessagingIntegrationTestsBase
{
    protected override void ConfigureTransport(MessagingOptions options)
    {
        options.UseRabbitMq(r =>
        {
            r.HostName = fixture.HostName;
            r.Port = fixture.Port;
            r.UserName = fixture.UserName;
            r.Password = fixture.Password;
        });
    }

    protected override void ConfigureStorage(MessagingOptions options)
    {
        options.UseInMemoryStorage(); // Or a real storage provider
    }

    [Fact]
    public override Task should_publish_and_consume_message_end_to_end()
        => base.should_publish_and_consume_message_end_to_end();

    [Fact]
    public override Task should_discover_consumers_from_di()
        => base.should_discover_consumers_from_di();

    // ... expose all relevant base tests
}
```

## Test Helpers

### TestMessage

Simple record for testing message serialization:

```csharp
public sealed record TestMessage
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Payload { get; init; }
}
```

### TestSubscriber

Collects received messages for assertions:

```csharp
var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
await Publisher.PublishAsync(message, new PublishOptions { MessageName = "topic" });
var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10));
received.Should().BeTrue();
subscriber.ReceivedMessages.Should().ContainSingle(m => m.Id == message.Id);
```

## Running Tests

```bash
# Run specific integration tests
dotnet test tests/Headless.Messaging.RabbitMq.Tests.Integration

# Run with filter
dotnet test --filter "FullyQualifiedName~RabbitMqTransportTests"
```

## Adding New Test Scenarios

1. Add virtual test method to appropriate base class
2. Use capability flags for conditional execution
3. Existing provider tests automatically gain new scenarios

```csharp
// In TransportTestsBase
public virtual async Task should_handle_new_scenario()
{
    if (!Capabilities.SupportsNewFeature)
    {
        Assert.Skip("Transport does not support new feature");
    }
    // Test implementation
}
```
