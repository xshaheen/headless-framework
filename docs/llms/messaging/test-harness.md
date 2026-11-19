---
domain: Messaging
packages: Messaging.Testing
---

# Messaging test harness

> Part of [Messaging](../messaging.md). Assert published, consumed, and failed messages in application tests, with no broker.

## Headless.Messaging.Testing

`MessagingTestHarness` records messages at the Bus and Queue transport layer over the in-memory transport. Use it in application tests that assert what was published or consumed. For the shared test base classes and Testcontainers fixtures, see [testing.md](../testing.md).

### Setup

```bash
dotnet add package Headless.Messaging.Testing
```

Choose an entry point:

- **`services.AddMessagingTestHarness()`** decorates an existing host's messaging registrations (a `WebApplicationFactory`, an `IHost`). Call it after `AddHeadlessMessaging(...)`.
- **`MessagingTestHarness.CreateAsync(...)`** builds and owns a standalone test `ServiceProvider`. Dispose the harness after each test.
- **`services.ReplaceMessagingTransportWithInMemory()`** swaps the broker transport an application registered (RabbitMQ, Kafka, Amazon SQS, Azure Service Bus, NATS, Redis, or a custom `IMessagesOptionsExtension`) for the in-memory transport, so the application's own registration runs without a broker. Each [named transport](transports.md#named-transports) becomes a named in-memory transport of its own, so routing stays as in production, and `AddMessagingTestHarness()` records publishes through every transport. Call it after `AddHeadlessMessaging` and before `AddMessagingTestHarness()`.

Consumers under test register as in production: attribute-declared classes and `AddModule<…MessagingModule>()`.

```csharp
using AwesomeAssertions;

services.AddMessagingTestHarness();

var harness = provider.GetRequiredService<MessagingTestHarness>();
await harness.WaitForPublishedAsync<OrderPlaced>(TimeSpan.FromSeconds(5));

// Shared harness: wait for in-flight store-first work, then clear observations and storage.
await harness.ResetAsync();

// Enlisted publish: completing the unit of work dispatches the captured message;
// a throwing delegate rolls it back and nothing is recorded.
await harness.RunInUnitOfWorkAsync(async (sp, unit) =>
{
    await unit.Outbox.PublishAsync(new OrderPlaced(Guid.NewGuid()));
});

var recorded = await harness.WaitForPublishedAsync<OrderPlaced>(TimeSpan.FromSeconds(5));
recorded.ResolvedDeliveryMode.Should().Be(DeliveryMode.Durable);
recorded.IsCoordinated.Should().BeTrue();
```

To test an application whose `Program` registers a broker, replace its transport in the test host. With `WebApplicationFactory<Program>`, register both calls inside `ConfigureTestServices`, which runs after the application's registrations:

```csharp
// Inside builder.ConfigureTestServices(services => { ... }) of a WebApplicationFactory<Program>:
services.ReplaceMessagingTransportWithInMemory().AddMessagingTestHarness();

// After the factory has started the host:
await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(cancellationToken);

var harness = provider.GetRequiredService<MessagingTestHarness>();
await harness.WaitForConsumedAsync<OrderPlaced>(TimeSpan.FromSeconds(5), cancellationToken);
```

**Verify:** a `WaitForPublishedAsync<T>` or `WaitForConsumedAsync<T>` call returns a `RecordedMessage`. When nothing matches in time, it throws `MessageObservationTimeoutException`.

### API

- **Waits.** `WaitForPublishedAsync<T>(...)`, `WaitForConsumedAsync<T>(...)`, `WaitForFaultedAsync<T>(...)`, and `WaitForTerminalAsync<T>(...)` return once a match arrives, or throw `MessageObservationTimeoutException` when the timeout passes. Overloads take a payload predicate. `WaitForPublishedAsync<T>(MessageLane.Bus)` or `MessageLane.Queue` tells apart identical payloads sent on the two lanes. The `timeout` parameter defaults to `MessagingTestHarness.DefaultTimeout` (5 seconds).
- **`RecordedMessage`.** `RequestedDeliveryMode` and `ResolvedDeliveryMode` report what was asked for and what ran. `IsCoordinated` (`bool?`) reports whether the row was written inside a unit of work.
- **`RunInUnitOfWorkAsync(...)`** creates a service scope, begins a resource-less unit of work on it, and passes the delegate both that scope's `IServiceProvider` and the `IUnitOfWork` (`Func<IServiceProvider, IUnitOfWork, Task>`, plus a `Task<TResult>` overload). Publish through the unit's `Outbox` inside the delegate to test an enlisted publish against a real commit or rollback.
- **`ResetAsync()`** drains in-flight publish and consume work, then clears a shared harness.
- **`IServiceProvider.ResetMessagingHarnessAsync()`** resolves the registered harness and calls `ResetAsync()`, and does nothing when no harness is registered. A shared test host resets through it, for example `await server.Services.ResetMessagingHarnessAsync(cancellationToken)`.
- **`TestConsumer<T>`** captures consume contexts with no handler logic. It is generic and carries no consumer attribute, so the source generator cannot register it. Attach it as a runtime subscription: `subscriber.SubscribeAsync<OrderPlaced>((context, _, ct) => testConsumer.ConsumeAsync(context, ct), new RuntimeSubscriptionOptions { HandlerId = "tests.order-placed" })`.

### Rules and behavior

- **In-memory transport only.** `AddMessagingTestHarness()` and `MessagingTestHarness.CreateAsync(...)` throw `InvalidOperationException` naming the registered transport when it is a broker or a custom transport, so a test never silently needs a reachable broker. Storage is not checked beyond being registered: keep the application's database (for example in a Testcontainers fixture), or use `setup.UseInMemoryStorage()`.
- **Join the bootstrap before the first publish.** In a generic or web host, messaging bootstraps in a background service, so the host's `StartAsync` returns before bootstrap finishes and does not surface its failure. Call `await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(cancellationToken)`, which waits for the running bootstrap and rethrows its failure.
- **Store-first, like production.** `MessagingOptions.DefaultDeliveryMode` stays `Durable`. `PublishAsync` returns once the row is in storage, and the send, the `Published` record, and consumption follow on dispatcher threads. Assert through `WaitFor*`, never by reading the collections right after a publish.
- **Order of records.** For any one message, `Published` is always observable before `Consumed` or `Faulted`, so reading `harness.Published` after `WaitForConsumedAsync` is safe.
- **`ResetAsync()`** waits until no published row is `Scheduled` or `Queued` and no received row is `Scheduled`, drops transport messages no consumer picked up, then clears observations and storage. A publish delayed by more than a minute is not awaited and still fires when due.
- **Enlistment comes only from the unit passed to the delegate.** In-memory storage is the one storage that joins a resource-less unit (see [Delivery Modes](publishing.md#delivery-modes)), so `unit.Outbox` publishes inside `RunInUnitOfWorkAsync` are captured on the unit. Completion stores them and hands them to the dispatcher. An exception from the delegate rolls the unit back, discards the rows, and records nothing. Each call opens its own scope and unit: a nested call is a second unit. `harness.Publisher`, `harness.Queue`, and `IBus`/`IQueue` resolved anywhere are autonomous, so their publishes are standalone durable writes that survive a rollback, even inside the delegate.
- **Unit of work registration.** `AddHeadlessMessaging(...)` registers the unit of work, so any host with messaging resolves a real `IUnitOfWorkFactory`. The harness entry points register nothing extra for it.
- **Deterministic dispatch.** Transport parallelism is off inside the harness.

### Replacing the transport

`ReplaceMessagingTransportWithInMemory()` removes exactly what the transport's `setup.Use…()` call registered: its transports, consumer client factory, connection pools, capability declaration, options with their start-time validators, processing servers (such as the NATS stream warm-up), and health checks (such as `messaging-nats`). It keeps the shared infrastructure, then registers what `setup.UseInMemory()` registers. Consumers, contracts, storage, and the rest of the application's registration are unchanged.

- A host already on the in-memory transport is left as is, so a second call does nothing.
- It throws `InvalidOperationException` when `AddHeadlessMessaging` has not run, when `AddMessagingTestHarness()` already ran, or when a transport was added straight to the service collection instead of through its `setup.Use…()` call.
- The in-memory transport maps no routing affinity. A message that calls `RequireRoutingAffinity()`, as Kafka partition-keyed contracts do, fails startup after the swap with a `MessagingConfigurationException` naming the message, its lane, and the replaced transport. Test such messages against the real broker. A keyed publish without that requirement works, and a consumer tuned `Sequential()` orders by the key as on a broker.

### Step through failure waits

Messaging runs the waits that ride out an unresponsive store or broker on a back-off clock, registered apart from the app clock under `MessagingOptions.BackoffClockServiceKey`. `AddHeadlessMessaging` registers `TimeProvider.System` there, so these waits keep real time when the app registers a `FakeTimeProvider`. They are the in-process inbox admission retries, the in-memory transport's redelivery delay and shutdown drain, the timeout around each lease renewal, `TransportPublishTimeout`, and `RetryPolicy.OnTerminalTimeout`.

To step through them without sleeping, register your own clock under that key, before or after `AddHeadlessMessaging`, and advance it:

```csharp
using Microsoft.Extensions.Time.Testing;

var backoffClock = new FakeTimeProvider();
services.AddKeyedSingleton<TimeProvider>(MessagingOptions.BackoffClockServiceKey, backoffClock);
```

- Use the app clock's `FakeTimeProvider` or a separate one. A separate one steps a failure wait without moving due times, leases, or retention.
- `ResetAsync()` polls on the system clock regardless.
- The default `IConsumerClient.RejectAsync(sender, redeliveryDelay, ...)` hold, which a broker transport without a native delay uses, always waits real time.
- Production hosts leave the key alone.
