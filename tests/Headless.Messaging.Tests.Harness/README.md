# Headless.Messaging.Tests.Harness

Shared test harness for messaging providers. Provides base test classes with reusable test scenarios for transport, consumer, and storage implementations.

## Overview

This harness enables consistent integration testing across all messaging providers (RabbitMQ, Kafka, NATS, AWS SQS, etc.) by defining standard test scenarios that each provider can run against its real infrastructure.

## Which tier proves what

A messaging change is confirmed in seconds by the fast tier, and the slower tiers prove what only a real broker, a real database, or several hosts can show.

| Tier | Projects | Runs | What it proves |
|---|---|---|---|
| Fast | `Headless.Messaging.Tests.Unit`, `Headless.Messaging.InMemory.Tests.Unit`, `Headless.Messaging.Storage.InMemory.Tests.Unit` | `make test-affected`, `make verify-affected`, CI | The real pipeline against the simulated broker and fault-injecting store (`Simulation/`), on a fake clock: retry, settlement order, shutdown drain, circuit pause and resume, request/reply, and every contract suite the simulator supports |
| Real broker | `Headless.Messaging.<Broker>.Tests.Integration` | `make test-affected-integration` before a PR; every suite on a pull request or merge that reaches one, and weekly | Protocol behavior the simulator only models: settlement, redelivery, restart and network-fault recovery, ordering, size limits |
| Real database | `Headless.Messaging.Storage.<Db>.Tests.Integration` | as above | Store-clock leases, clock skew, racing claimants, lost commit replies, fault classification |
| End to end | `Headless.Messaging.<Broker><Store>.Tests.Integration` | as above | Outbox to broker to consumer to inbox through the real host |
| Scheduled | `Headless.Messaging.Tests.Chaos` | `make test-messaging-chaos`; weekly (full sizes) or on dispatch | Several hosts under broker, storage, and network faults, checked by a message ledger; never selected by an affected run |

Fast-tier projects may not start a container or open a socket, and `Policy/FastTierPolicyTests` counts real waits (`Task.Delay`, `Thread.Sleep`, `SpinWait.SpinUntil`, `TimeProvider.System.Delay`) in the two core unit projects and this harness against `Policy/real-wait-baseline.txt`. A new wait fails the test; converting one lowers its line. Waits that back off, reconnect, or time out I/O read the keyed back-off clock (`MessagingOptions.BackoffClockServiceKey`), which `SimulationHost` replaces with the simulated clock.

## Guarantees and quarantine

`Guarantees/MessagingGuarantee.cs` catalogues the critical messaging guarantees, each with a stable `MSG-…` ID. A guarantee is in one of three states:

- **Bound:** at least one test carries `[Guarantee("MSG-…")]` and runs. A guarantee that needs a real system must be bound to a real-broker or real-store test, not only the simulator.
- **No test yet:** a catalogue entry that names an open issue tracking the missing test.
- **Unsupported:** its test exists but fails on a product defect, so it skips through `MessagingGuarantees.RequireSupport` with the defect and an open issue. A transport scenario is quarantined the same way in `TransportConformanceManifest`.

`Policy/GuaranteeCatalogueTests` fails when a guarantee has no binding and no issue, or when an attribute names an unknown ID. Each provider's `ProviderConformanceEvidenceTests` fails when a guarantee it inherits does not run there. Never delete or `Skip` a failing test: quarantine it against an issue, and flip it back to `Supported` in the change that fixes the defect. The weekly quarantine check fails while a closed issue still quarantines a cell.

## Base Test Classes

| Class | Purpose |
|-------|---------|
| `TransportTestsBase` | Tests for `ITransport` implementations (sending messages) |
| `ConsumerClientTestsBase` | Tests for `IConsumerClient` implementations (receiving messages) |
| `TransportConsumerConformanceTestsBase` | Broker-observed round trip and real settlement invariants |
| `TransportProviderConformanceDriver` | Provider-only broker operations consumed by shared routing, ownership, isolation, startup, poison, and migration assertions |
| `BrokerFaultTestsBase` | Optional, manifest-driven recovery and fault invariants |
| `NetworkFaultTestsBase` | Cuts the link between client and broker (`INetworkLink`) while the broker keeps running, and checks the client reconnects and keeps consuming; a Toxiproxy route is the link for a real broker |
| `TransportConnectivityTestsBase` | Stops and restarts an isolated broker under a running host and checks the host's broker-reachability state follows |
| `DataStorageTestsBase` | Tests for `IDataStorage` implementations (message persistence) |
| `MessagingIntegrationTestsBase` | Full pub-sub cycle through a real broker and a real store, run by one broker-and-store pair project per broker |
| `EntityFrameworkOutboxRelayTestsBase` | The EF Core outbox bridge relaying a committed integration event through a real broker |
| `MultiHostGuaranteeSuiteBase` | Multi-host coordination guarantees, declared here for the guarantee catalogue and implemented per database by the scheduled chaos project, `Headless.Messaging.Tests.Chaos` (`make test-messaging-chaos`) |

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

The receive timeouts, no-redelivery windows, and publish retry pauses of a session, and the outage and reconnect windows of `NetworkFaultTestsBase`, run on the session's `TimeProvider`. It is the system clock unless the leaf passes one, so broker leaves keep real waits, while the simulator leaf passes a `SimulationWaitClock` that moves simulated time only while the test waits and the simulated broker has settled.

### Settlement and envelope scenarios

These scenarios carry `[Fact]` on the base method, so every leaf runs them and skips the cells its manifest profile does not mark `Supported`. A leaf that supports one overrides the member that scenario needs:

| Scenario | Base test | Leaf member |
|---|---|---|
| `HeaderBuilderFailureRedelivery` | `should_redeliver_then_give_up_when_the_header_builder_fails_transiently` | `CreateSessionAsync(TransportConformanceSessionOptions, …)` installs the provider's custom headers builder around `HeaderBuilder` and maps `DeliveryLimit` onto the broker's delivery budget; set `HasDeadLetterDestination` when the broker keeps a copy |
| `DeadLetterSingleCopy` | `should_keep_one_dead_letter_copy_and_settle_the_original_once` | `DeadLettering`, `HasDeadLetterDestination`, and `TakeDeadLetteredMessageIdsAsync`, which drains the dead-letter destination |
| `StandardHeaderRoundTrip` | `should_round_trip_every_standard_header_unchanged` | none; the default session |
| `HandlerConcurrencyCap` | `should_never_hand_over_more_deliveries_at_once_than_the_requested_concurrency` | `Concurrency` |
| `PerKeyOrdering` | `should_deliver_the_messages_of_one_key_in_publish_order` | `RoutingAffinity`: a destination that orders by key (several Kafka partitions, a Pulsar `Key_Shared` subscription, an SQS FIFO queue) |
| `BodySizeLimit` | `should_round_trip_a_body_at_the_size_limit_and_refuse_one_byte_more` | `MaximumBodySize`: the broker's documented limit less what the conformance envelope's headers take of it |
| `MessageExpiry` | `should_not_deliver_a_message_that_expired_before_it_was_consumed` | `TimeToLive`: the destination's time-to-live |

The header-builder scenario throws a transient `TimeoutException` from the builder. It expects the message to be redelivered, and a message whose builder never succeeds to stop being redelivered within the delivery budget. `RequireSupport` skips a cell whose provider loses the message or retries it forever, against #1199. The body-size scenario also sends one message after the refused one, so a publish that reports failure while the broker keeps the message, or kills the consumer, still fails the cell.

NATS is a reference implementation. Its test leaf uses lane-qualified memory-backed JetStream streams and durables with a one-second `AckWait`, making ACK/NAK behavior deterministic without changing production defaults. Its provider leaf proves group fan-out, replica competition, queue ownership, same-name lane isolation, terminal malformed-envelope acknowledgement across restart, and a drained legacy stream followed by roll-forward-only publication. Consumer pause/recovery is tracked separately from broker interruption so the suite does not overstate what was fault-injected.

RabbitMQ uses lane-qualified exchanges, routing keys, and owned queues; ACK absence is observed beyond the provider window, ordinary reject proves broker requeue, and malformed-envelope construction is terminally rejected without requeue. Its real-broker leaf also proves consumer-identity fan-out, replica competition, Queue ownership, same-name lane isolation, and a drained legacy exchange followed by roll-forward-only publication. AWS queue conformance is explicitly LocalStack-backed: commit proves SQS deletion, reject proves visibility-timeout redelivery with a fresh receipt context, and SNS empty-body dispatch is `NotApplicable` with its protocol rationale. AWS pause recovery remains an explicit linked gap. Its broker-restart evidence restarts LocalStack and recreates the queues LocalStack loses, as an SQS outage would leave them, so it proves the client resumes, not that SQS kept anything.

Kafka is queue/consumer-group only in the current provider contract. Pulsar's Testcontainers leaf proves lane-qualified bus group fan-out, replica competition, queue ownership, same-name isolation, terminal malformed acknowledgement across restart, and legacy drain followed by roll-forward-only publication; its negative-ack redelivery delay is shortened only in the test fixture, and broker-restart recovery remains a linked gap because stopping the consumer after a restart can overrun its shutdown timeout. Azure Service Bus runs against a dedicated real namespace using `HEADLESS_TEST_AZURE_SERVICE_BUS_CONNECTION_STRING`. The credential must grant entity-management rights because the fixture creates and deletes only its uniquely named queues, topics, and subscriptions. Missing credentials produce precise local skips; the protected `Azure Service Bus Conformance` workflow fails preflight unless the secret exists and verifies that real tests—not only the credential marker—executed.

Azure Service Bus has two manifest profiles. The real namespace (`Azure Service Bus`) is authoritative for provisioning, the administration API, and every-instance subscriptions. The emulator (`Azure Service Bus Emulator`) covers fixed-entity scenarios: `AzureServiceBusEmulatorFixture` starts `mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1`, pinned by digest, with the SQL Server it stores entities in (Azure SQL Edge on arm64 hosts), and runs the provider with `AutoProvision = false` on the queues, topic, and subscriptions that `Emulator/Config.json` declares. Each session takes a queue of its own from that file, and the broker-restart leaf restarts an emulator of its own on a fixed host port, which recreates the `Config.json` entities empty. A scenario whose evidence creates entities at run time is `N/A` on the emulator and stays the real namespace's to prove. The emulator and SQL Server run only under their license terms, and the tests never accept them on your behalf: set `HEADLESS_ACCEPT_SERVICEBUS_EMULATOR_EULA=Y` to accept the [emulator license](https://github.com/Azure/azure-service-bus-emulator-installer/blob/main/EMULATOR_EULA.txt) and the SQL Server license and run the emulator tests. Without it, every emulator test skips with that reason and no container starts. The integration workflow's `azure-service-bus-emulator` leg sets it, because the repository owner accepted the emulator license for test runs; every other machine opts in on its own. The emulator profile is not a real broker leaf: its leaves skip on any machine that has not opted in, and it has no routing-affinity evidence, so no simulator cell rests on it and the drift report does not wait for it.

## Transport Conformance Matrix

`S` means the manifest cell is `Supported` and names an executable broker-backed assertion. `U` means `Unsupported` with the issue the manifest links for that cell. `N/A` means `NotApplicable` with a topology or protocol rationale. `S†` is executable real-service evidence that still requires the protected Azure credential; a local skip-only run is not completion evidence. `S‡` is executable emulator evidence that runs only with `HEADLESS_ACCEPT_SERVICEBUS_EMULATOR_EULA=Y`; a skip-only run is not completion evidence either.

The `Simulator` column is the in-process simulated broker (`Simulation/SimulatedBroker`), run by the fast tier in `Headless.Messaging.Tests.Unit`. It is not a real broker, so `TransportConformanceManifestTests` fails when one of its `S` cells is `S` on no real broker leaf: a simulator pass must always have real-broker evidence behind it.

That policy holds only while the real brokers keep passing, so `.github/workflows/messaging-integration.yml` runs the broker, broker-plus-database, and relational storage suites against real containers (`make test-messaging-integration`), with no retries. `scripts/messaging-integration-scope.py` decides each run and holds the one list of suites: a pull request or a push to `main` whose change reaches any of them through the transitive project graph runs every suite, since drift is judged only once every real broker behind a cell has run; the weekly run adds chaos and the quarantine check. The pull-request run stays outside `CI status`, so it is evidence, never a gate. With `HEADLESS_CONFORMANCE_EXPORT_DIR` set, each provider's binding check (`TransportConformanceTestBindings.GetValidationErrors`) also writes the manifest snapshot and its scenario-to-test bindings there (`ConformanceEvidenceExport`). `make messaging-drift-report` joins those with the run's TRX files: a simulator `S` cell that every real broker backing it fails is reported as simulator drift, naming the cell and its catalogued guarantee, and any other failure is a provider failure. `make messaging-quarantine-check` asks GitHub, read-only, whether each issue an `Unsupported` cell or an unbound guarantee links is still open, and fails naming every cell whose issue is closed. To prove the drift report still labels drift, dispatch the workflow with `drift_drill` set to a scenario name (`HEADLESS_CONFORMANCE_DRIFT_DRILL`): every real-broker leaf that requires that scenario then fails on purpose (`ConformanceDriftDrill`), while the simulator's leaves keep passing. No pull request, push, or scheduled run sets it.

| Manifest scenario | NATS | RabbitMQ | AWS/LocalStack | Kafka | Pulsar | Azure Service Bus | Azure Service Bus Emulator | InMemory | Redis | Simulator |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `RoutingAffinityMappingOrRejection` | S | S | S | S | S | S† | N/A | S | S | S |
| `QueueRoundTrip` | S | S | S | S | S | S† | S‡ | S | S | S |
| `BusRoundTrip` | S | S | S | N/A | S | S† | S‡ | S | S | S |
| `HeaderRoundTrip` | S | S | S | S | S | S† | S‡ | S | S | S |
| `EmptyBodyDispatch` | S | S | N/A | U | U | U | U | S | S | S |
| `CommitSettlement` | S | S | S | S | S | S† | S‡ | S | S | S |
| `RejectRedelivery` | S | S | S | S | S | S† | S‡ | S | S | S |
| `ConsumerPauseRecovery` | S | S | S | S | S | S† | S‡ | U | S | S |
| `BrokerInterruptionRecovery` | S | S | S | S | U | U | S‡ | U | S | S |
| `ConnectionDropRecovery` | S | S | S | S | S | U | U | U | S | S |
| `PublishDuringOutage` | S | S | S | S | S | U | U | U | S | S |
| `StaleSettlement` | S | S | S | S | S | U | S‡ | U | S | S |
| `HandlerFailureRedelivery` | S | S | S | S | U | U | S‡ | U | S | S |
| `BoundedGracefulShutdown` | S | S | S | S | S | S† | U | S | S | S |
| `BusConsumerIdentityFanOut` | S | S | S | N/A | S | S† | N/A | S | S | S |
| `BusReplicaCompetition` | S | S | S | N/A | S | S† | N/A | S | S | S |
| `QueueOwnership` | S | S | S | S | S | S† | N/A | S | S | S |
| `SameNameLaneIsolation` | S | S | S | N/A | S | S† | N/A | S | S | S |
| `StartupRejectionBeforeSideEffects` | U | U | U | S | U | U | U | U | U | N/A |
| `MalformedEnvelopeTerminalSettlement` | S | S | S | S | S | U | U | N/A | S | N/A |
| `RequestReplyRoundTrip` | S | S | N/A | N/A | U | U | U | S | S | S |
| `RequestReplyCallerIsolation` | S | S | N/A | N/A | U | U | U | S | S | S |
| `RequestReplyForeignAddressRefusal` | S | S | N/A | N/A | U | U | U | S | S | S |
| `RequestReplyCallerCleanup` | S | S | N/A | N/A | U | U | U | S | S | S |
| `RequestReplyStartupRejection` | N/A | N/A | S | S | S | S† | U | N/A | N/A | N/A |
| `RawPayloadConsumption` | S | S | S | S | S | S† | U | N/A | S | N/A |
| `HeaderBuilderFailureRedelivery` | S | S | S | U | U | U | U | N/A | U | N/A |
| `DeadLetterSingleCopy` | N/A | S | N/A | N/A | N/A | U | U | U | N/A | S |
| `StandardHeaderRoundTrip` | S | S | S | S | S | U | U | U | S | S |
| `HandlerConcurrencyCap` | S | S | S | S | S | U | U | U | S | S |
| `PerKeyOrdering` | N/A | N/A | S | S | S | U | U | N/A | N/A | N/A |
| `BodySizeLimit` | S | S | S | S | S | U | U | N/A | N/A | N/A |
| `MessageExpiry` | S | S | N/A | N/A | N/A | U | U | N/A | N/A | N/A |

Evidence anchors:

- Queue/body/header, commit, reject, isolation, and shutdown: `TransportConsumerConformanceTestsBase` provider overrides.
- Empty-body broker dispatch: `should_dispatch_empty_message_body` in the NATS and RabbitMQ consumer leaves.
- Pause/resume: `BrokerFaultTestsBase.should_resume_delivery_once_after_consumer_pause` provider overrides.
- Broker restart: `BrokerFaultTestsBase.should_resume_delivery_once_after_broker_restart` provider overrides, each on a broker of its own on fixed host ports (`CreateInterruptibleSessionAsync`) so the restart disturbs no other test. NATS uses a file-backed stream there, and the AWS leaf recreates LocalStack's in-memory queues after the restart.
- Connection drop and publish during an outage: `NetworkFaultTestsBase` provider overrides, each through a Toxiproxy route the test cuts while the broker keeps running. A client that learns broker addresses from the broker can route around a proxy, so the Kafka, Pulsar, and AWS leaves also run `AssertTrafficStopsWhileTheLinkIsCutAsync`: a message published past the route must not reach the cut-off consumer until the route is back, and a publish over the cut route must not succeed. The Kafka leaf runs its own broker (`ProxiedKafkaContainer`) that advertises the route instead of its own port. Clear Toxiproxy faults with `ToxiproxyServer.ResetAsync` (`POST /reset`), because deleting a single toxic can hang the Toxiproxy 2.12 API.
- Stale settlement and callback failure: `should_not_settle_another_delivery_when_a_settled_value_is_used_again` and `should_keep_the_delivery_and_the_listener_when_the_callback_throws` in each provider's `TransportConsumerConformanceTestsBase` leaf.
- NATS, RabbitMQ, and AWS Bus fan-out: their `TransportConsumerConformanceTestsBase` provider leaves; Pulsar bus/queue intent: `PulsarTransportTests`; Azure topic/subscription fan-out: `AzureServiceBusTransportTests`.
- AWS consumer-identity fan-out, replica competition, queue ownership, same-name lane isolation, and terminal malformed-envelope deletion: `AmazonSqsConsumerClientConformanceTests` and `MalformedMessageTests` against LocalStack.
- Redis Streams Bus/Queue routing, group fan-out, replica competition, lane isolation, settlement, shutdown, and poison handling: `RedisConsumerConformanceTests` against Testcontainers Redis.
- InMemory Bus/Queue group fan-out, replica competition, ownership, and lane isolation: `InMemoryProviderConformanceTests` using the shared provider driver.
- Azure Service Bus group fan-out, replica competition, Queue ownership, and lane isolation: `AzureServiceBusConsumerClientHarnessTests` on the credential-gated managed-service tier.
- Azure Service Bus emulator round trip, settlement, stale settlement, and callback failure: `AzureServiceBusEmulatorConsumerClientHarnessTests`, whose bounded-shutdown cell is `U` against #1282 because stopping a consumer waits for an active handler; topic fan-out to the two `Config.json` subscriptions: `AzureServiceBusEmulatorTransportTests`; pause and broker restart: `AzureServiceBusEmulatorBrokerFaultTests`.
- Kafka Queue ownership and terminal poison-offset handling: `KafkaConsumerClientConformanceTests`; Bus startup rejection before storage initialization: `SetupTests`.
- NATS lane isolation, consumer-identity/replica semantics, terminal malformed acknowledgement, and legacy drain/roll-forward proof: `NatsConsumerClientTests` against Testcontainers NATS JetStream.
- Pulsar lane isolation, consumer-identity/replica semantics, terminal malformed acknowledgement, and legacy drain/roll-forward proof: `PulsarConsumerClientHarnessTests` against Testcontainers Pulsar.
- RabbitMQ lane isolation, consumer-identity/replica semantics, terminal malformed rejection, and legacy drain/roll-forward proof: `RabbitMqConsumerClientConformanceTests` against Testcontainers RabbitMQ.
- InMemory request/reply round trip, caller isolation and restart, foreign reply-address refusal, and caller cleanup: `InMemoryProviderConformanceTests` running `TransportRequestReplyConformance`. RabbitMQ runs the same suite against Testcontainers RabbitMQ in `RabbitMqRequestReplyConformanceTests`, probing reply queues by passive declare; `RabbitMqReplyTransportTests` covers the exclusive reply queue's lifetime, sends to a deleted reply queue, refusal of `amq.gen-` and application queues, and re-declaration under a new address. NATS runs the suite in `NatsRequestReplyConformanceTests`, and again with stream provisioning disabled in `NatsRequestReplyWithoutStreamProvisioningTests`, probing reply subscriptions through the server's `connz` monitoring endpoint; `NatsReplyTransportTests` covers replies staying out of every JetStream stream, the reply subject surviving a server restart under the same address, immediate failure of a request no stream captures, and refusal of lane and inbox subjects. Redis runs the suite in `RedisRequestReplyConformanceTests`, probing reply channels with `PUBSUB NUMSUB` and checking by `SCAN` that no key is named after a reply address; `RedisReplyTransportTests` covers a round trip that leaves no key under the reply namespace, the reply channel surviving a dropped subscription connection under the same address, a listener opened while the server is down handing out its address once the server comes up, and refusal of lane stream keys and unrelated channels with no write. Kafka and AWS reject request/reply by design (`N/A`); Azure Service Bus and Pulsar reject it until their reply channels exist (`U`). All four prove the rejection itself: their `ProviderConformanceEvidenceTests` run `AssertRejectedAtStartupAsync` against the real transport wiring, so `RequestReplyStartupRejection` is `S` for them.
- Raw payloads (a message published with the broker's native client, no Headless headers, to a `RawPayload()` contract): each provider's `*RawPayloadTests` leaf proves consumption with a broker-derived message id, one handler run across a second delivery of the same broker message, and poison handling of a malformed body. RabbitMQ also proves that a raw delivery without `message-id` is rejected. The Azure Service Bus leaf runs in the protected Azure workflow with the conformance evidence.
- AWS evidence is LocalStack-backed, not managed AWS. The `Azure Service Bus` column is a real isolated namespace tier; the `Azure Service Bus Emulator` column is the local emulator on fixed entities, and neither stands in for the other.
- Settlement and envelope scenarios (header-builder failure, dead-letter copy, standard headers, concurrency cap, per-key ordering, body size, expiry): the base `[Fact]` methods of `TransportConsumerConformanceTestsBase`, run by each provider's consumer conformance leaf. The header-builder cell is `S` where the broker redelivers and then stops within the delivery budget: RabbitMQ on a quorum queue with `DeliveryLimit` and `EnableDeadLettering`, and NATS through `MaxDeliver`. AWS, Redis, and Pulsar lose the message and Azure Service Bus dead-letters it at once, against #1199; Kafka retries it without end, against #1288, because it has no delivery budget. Only RabbitMQ and Azure Service Bus dead-letter a delivery to a destination. NATS terminates the delivery, and the other providers settle `DeadLetterAsync` as a commit does (SQS and Pulsar feed their dead-letter queues only through a redelivery budget), so the cell is `N/A` for them. Per-key ordering is `S` where the broker orders by key: Kafka with three partitions, a Pulsar `Key_Shared` subscription, and an SQS FIFO queue. RabbitMQ, NATS, Redis, and InMemory reject a routing affinity key at publish, so the cell is `N/A` until #1256. Each body-size leaf states its limit less the envelope's header overhead. RabbitMQ's limit is the client's inbound body limit (`ConnectionFactory.MaxInboundMessageBodySize`, 64 MiB by default), below the broker's 128 MiB, and the transport refuses a larger body at publish. The expiry cell is `S` where the provider exposes a time-to-live: RabbitMQ's queue `MessageTTL` and the NATS stream's maximum age.
- Simulator: the same suites run unchanged in `Headless.Messaging.Tests.Unit/Contracts` (`SimulatorConsumerConformanceTests`, `SimulatorBrokerFaultTests`, `SimulatorNetworkFaultTests`, `SimulatorRequestReplyConformanceTests`), bound in its `ProviderConformanceEvidenceTests`. Its network link and broker restart drop the simulated connection and hold it down until the client has seen a receive fail; its reply listeners register at the broker. The request/reply hosts run on the system clock, so that suite still waits real time for its timeout scenarios.

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
| `SupportsRequestReply` | The provider declares request/reply | `true` runs the suite; `false` runs `AssertRejectedAtStartupAsync` instead | S |
| `ConfigureRequestReplyTransport(MessagingSetupBuilder)` | Always, supported or not | Selects the provider for one host; called once per host, and every host must reach the same broker |
| `ConfigureRequestReplyServices(IServiceCollection)` | Wiring must win over a provider default | Runs after `AddHeadlessMessaging` for each host |
| `HasReplyObjectsAsync(string replyAddress, CancellationToken)` | The provider declares request/reply | Whether the broker still holds the queue, subscription, or channel behind a reply address; must be `true` while the caller runs |

Derive the leaf from `TransportRequestReplyConformanceTestsBase` and implement `CreateDriverAsync`; the base runs each scenario as its own `[Fact]` with `AbortToken`: round trip, two callers, responder failure, no responder, timeout without a running responder, late reply, foreign reply address, restarted caller, tenant flow, and stopped-caller cleanup (`should_leave_no_reply_objects_after_the_caller_stops`). A provider that does not declare request/reply calls `AssertRejectedAtStartupAsync` from its own `[Fact]` instead. The timeout scenario starts and stops a responder host before calling, so a broker that refuses sends to unprovisioned destinations still reaches a timeout. Each scenario names its contracts after a fresh run id, so leaves may share one broker container.

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
    <ProjectReference Include="..\Headless.Messaging.Tests.Harness\Headless.Messaging.Tests.Harness.csproj" />
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

    protected override IMessageSerializer GetSerializer()
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

### 2. Opt in to the real-database scenarios

Some storage scenarios carry `[Fact]` on the base method, so every leaf runs them and skips the ones its store cannot show:

- Override `DatabaseHostPort` and `CreateStorageThrough(host, port)` to let a test put a Toxiproxy route (`ToxiproxyDatabaseRoute`) between the storage and a running database and cut it. `TransactionalInboxRetryConformanceTests` has the same pair, with `ConfigureContextThrough`, for its lost-commit-reply scenario.
- Override `CoordinationProvider` on a `DeadOwnerReclaimConformanceTests` leaf to run the killed-node scenario on the provider's real membership store instead of `ControlledNodeMembership`.
- An EF Core storage variant derives the ADO.NET leaf, reads its connection settings from a context registration, and returns `true` from `RegisteredFromEntityFrameworkContext`, so it runs the same suite.

## Full Integration Tests

`MessagingIntegrationTestsBase` runs the whole path, outbox to broker to consumer to inbox, through the real setup
builder. Its leaves live in pair projects, `tests/Headless.Messaging.<Broker><Store>.Tests.Integration`, not in the
provider integration projects: each broker pairs with PostgreSQL, and SQL Server pairs with RabbitMQ. A change to a
storage package then selects only the pair projects, and a change to a broker selects its own pair and provider
projects. Scenarios that carry `[Fact]` on the base, such as the unit-of-work, retry-to-terminal, duplicate-delivery,
broker-outage, and host-restart ones, run on every leaf without an override; the older scenarios are opted into one by
one. Each pair project's `ProviderConformanceEvidenceTests` checks that every guarantee it inherits runs there.

```csharp
[Collection("RabbitMqPostgreSql")]
public sealed class RabbitMqPostgreSqlMessagingIntegrationTests(RabbitMqPostgreSqlFixture fixture)
    : MessagingIntegrationTestsBase
{
    protected override void ConfigureTransport(MessagingSetupBuilder setup) =>
        setup.UseRabbitMq(r => { r.HostName = fixture.RabbitMqHostName; r.Port = fixture.RabbitMqPort; });

    protected override void ConfigureStorage(MessagingSetupBuilder setup) =>
        setup.UsePostgreSql(fixture.StorageConnectionString);

    // The unit-of-work scenarios begin a unit on the messaging database.
    protected override DbConnection CreateStorageConnection() => new NpgsqlConnection(fixture.StorageConnectionString);

    protected override ValueTask<IUnitOfWork> BeginUnitOfWorkAsync(
        IUnitOfWorkFactory factory, DbConnection connection, CancellationToken cancellationToken
    ) => factory.BeginAsync((NpgsqlConnection)connection, cancellationToken);

    // The outage scenario makes the broker unreachable without losing its state: a paused container.
    protected override Task InterruptBrokerAsync(CancellationToken cancellationToken) =>
        fixture.PauseBrokerAsync(cancellationToken);

    protected override Task RestoreBrokerAsync(CancellationToken cancellationToken) =>
        fixture.UnpauseBrokerAsync(cancellationToken);

    [Fact]
    public override Task should_publish_and_consume_message_end_to_end()
        => base.should_publish_and_consume_message_end_to_end();

    // ... opt into the remaining base scenarios that have no [Fact] of their own
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
# One class in one project: seconds for the fast tier
make test-class CLASS='*SimulatorBrokerFaultTests' TEST_PROJECT=tests/Headless.Messaging.Tests.Unit/Headless.Messaging.Tests.Unit.csproj

# Chase a flaky fault test
make test-class CLASS='*RabbitMqNetworkFaultTests' REPEAT=5 TEST_PROJECT=tests/Headless.Messaging.RabbitMq.Tests.Integration/Headless.Messaging.RabbitMq.Tests.Integration.csproj

# Before a PR that touches a provider or storage package (needs Docker)
make test-affected-integration

# The scheduled tiers
make test-messaging-chaos          # smoke sizes; HEADLESS_CHAOS_PROFILE=full for the full sizes
make messaging-quarantine-check    # every quarantine links an open issue
```

## Adding New Test Scenarios

1. Add the scenario to the base class with `[Fact]` on the base method, so every leaf inherits it instead of opting in.
2. Gate it with `RequireSupport(<scenario>)` for a transport scenario, or a capability flag for storage, and add the scenario to the manifest and the matrix above.
3. Tag it with its guarantee ID, adding a catalogue entry when none fits.
4. Run it on every provider. Bind each pass in that provider's `ProviderConformanceEvidenceTests`; quarantine each product-defect failure against an issue.

```csharp
// In TransportConsumerConformanceTestsBase
[Fact]
[Guarantee("MSG-SETTLE-09")]
public virtual async Task should_handle_new_scenario()
{
    RequireSupport(TransportConformanceScenario.NewScenario);

    await using var session = await CreateSessionAsync(AbortToken);
    // Test implementation
}
```
