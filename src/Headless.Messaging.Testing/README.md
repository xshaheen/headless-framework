# Headless.Messaging.Testing

In-process test harness for asserting on published, consumed, faulted, and exhausted messages without external infrastructure.

## Problem Solved

Integration-testing a messaging pipeline typically requires a running broker and timing-sensitive polling. This package eliminates both: it wires the full pipeline in memory and exposes awaitable assertions that block until the expected message arrives (or the timeout elapses).

## Key Features

- **Zero Infrastructure**: No broker, no Docker — runs entirely in-process
- **Awaitable Assertions**: `WaitForPublished`, `WaitForConsumed`, `WaitForFaulted`, and `WaitForExhausted` block until observed or timed out
- **Lane-Aware Observations**: registrations use `setup.Bus` / `setup.Queue`, while observations use `WaitForPublished<T>(MessageLane.Bus)` / `MessageLane.Queue`; identical payloads on the two lanes remain distinct
- **Full Pipeline Coverage**: Decorates the real bus/queue transports and consume pipeline, so middleware, serialization, and consumer logic all execute
- **Store-First By Default**: keeps the production `DeliveryMode.Durable` default, so a plain publish is stored first and dispatched from storage exactly as in the application; `RecordedMessage.RequestedDeliveryMode` / `ResolvedDeliveryMode` report what was asked for and what ran
- **Required Enlistment**: `RunInUnitOfWorkAsync(...)` creates a service scope, begins a resource-less unit of work on it, and runs the delegate with that scope's provider, so tests can publish types registered `WithEnlistment(TransactionEnlistment.Required)` and observe completion versus rollback
- **Isolated Per Test**: Each `MessagingTestHarness` instance owns its own observation store; `ResetAsync()` drains in-flight work before clearing a shared one
- **Host Integration**: `AddMessagingTestHarness()` extension decorates an existing DI container for use with `WebApplicationFactory`, `IHost`, or `WebApplication`
- **Predicate Overloads**: Wait for a specific message matching a condition, not just any message of a type

## Design Notes

Use the testing package for application tests that need to assert published messages or consumed messages. Provider conformance still belongs in provider-specific or shared harness tests.

The harness does not weaken delivery: `MessagingOptions.DefaultDeliveryMode` stays `Durable`, so `PublishAsync` returns once the row is in in-memory storage and the transport send, the `Published` observation, and consumption follow on dispatcher threads. Assert through `WaitFor*` rather than reading the collections right after a publish. `ResetAsync()` waits until no published row is `Scheduled`/`Queued` and no received row is `Scheduled` (those states bracket every send and consumer execution), drops transport messages no consumer picked up, and only then clears observations and storage. A publish that is not yet due is clock-parked and is not awaited: it is stored as `Queued` when due within a minute and as `Delayed` beyond that, the dispatcher holds it either way, and it publishes when due on the host `TimeProvider` (a `Queued` row counts as in flight only once it is due on that clock), so a shared harness should not carry pending delays across tests, or should advance a `FakeTimeProvider` past them before resetting. `RunInUnitOfWorkAsync` begins the unit of work with `IUnitOfWorkManager.BeginAsync()` (resource-less); in-memory storage is the one storage that can join a resource-less unit, so publishes made inside the delegate through that scope's `IBus`/`IQueue` enlist on it. The unit completes on success and is abandoned (rolled back) on an exception from the delegate. `TransactionEnlistment` only decides whether a write must enlist — `ResolvedDeliveryMode` is still `Durable` either way, since `DeliveryMode` no longer has a distinct value for enlisted delivery.

Two consequences of running on in-memory storage. First, the in-memory transport hands a message to its consumer inside the send, before the sending thread records `Published`; the harness's consume decorator therefore waits for the message's `Published` record before running the consumer, so for any one message `Published` is always observable before `Consumed` or `Faulted`, and `harness.Published` is safe to read after `WaitForConsumed`. Second, `harness.Publisher` and `harness.Queue` resolve from a harness-owned scope that carries no active unit of work, so their publishes are always autonomous durable writes — a type registered `WithEnlistment(TransactionEnlistment.Required)` throws when published through them directly, exactly like production with no active unit of work. Exercise a `Required` type by resolving `IBus`/`IQueue` inside `harness.RunInUnitOfWorkAsync(...)` instead, from the delegate's own scope provider, so the resolved facade sees the unit of work the harness began.

## Installation

```bash
dotnet add package Headless.Messaging.Testing
```

## Quick Start

```csharp
await using var harness = await MessagingTestHarness.CreateAsync(services =>
{
    services.AddHeadlessMessaging(options =>
    {
        options.Bus.ForMessage<OrderCreated>(message =>
            message.Contract("orders.created").Consumer<OrderCreatedConsumer>(consumer =>
                consumer
                    .ConsumerIdentity("orders.created-handler")

                    .Group("order-svc")
            )
        );
        options.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
        options.UseInMemory();
        options.UseInMemoryStorage();
    });
});

await harness.Publisher.PublishAsync(new OrderCreated { OrderId = "ORD-1" });

var msg = await harness.WaitForConsumed<OrderCreated>(TimeSpan.FromSeconds(5));
msg.Message.Should().BeOfType<OrderCreated>();
```

### Observable Collections

The harness records every message in four collections, available as snapshots at any time:

| Property | Contents |
|---|---|
| `harness.Published` | All messages sent to the transport |
| `harness.Consumed` | All messages consumed successfully |
| `harness.Faulted` | All messages whose consumer threw an unhandled exception |
| `harness.Exhausted` | All messages whose retry budget was exhausted |

```csharp
harness.Published.Should().ContainSingle(m => m.MessageType == typeof(OrderCreated));
harness.Faulted.Should().BeEmpty();
```

Each entry is a `RecordedMessage` with `MessageType`, `Message`, `MessageId`, `CorrelationId`, `Headers`, `MessageName`, `Lane`, `RequestedDeliveryMode`, `ResolvedDeliveryMode`, `Timestamp`, and (for faulted or exhausted observations) `Exception`. A default or `Required`-enlisted publish records `Durable` for both `RequestedDeliveryMode` and `ResolvedDeliveryMode` — `TransactionEnlistment` does not change the recorded delivery mode; only an explicit `Direct` publish records `Direct`.

### WaitFor* Methods

All `WaitFor*` methods return a `RecordedMessage` or throw `MessageObservationTimeoutException` with a diagnostic listing what was observed during the wait.

```csharp
// Wait for any message of type T
var recorded = await harness.WaitForConsumed<OrderCreated>(TimeSpan.FromSeconds(5));

// Wait for a specific message matching a predicate
var recorded = await harness.WaitForConsumed<OrderCreated>(
    predicate: m => m.OrderId == "ORD-1",
    timeout: TimeSpan.FromSeconds(5)
);

// Same API for published and faulted
await harness.WaitForPublished<OrderCreated>(TimeSpan.FromSeconds(5));
await harness.WaitForPublished<OrderCreated>(MessageLane.Bus, TimeSpan.FromSeconds(5));
await harness.WaitForConsumed<OrderCreated>(MessageLane.Queue, TimeSpan.FromSeconds(5));
await harness.WaitForFaulted<BadMessage>(TimeSpan.FromSeconds(5));
await harness.WaitForExhausted<BadMessage>(TimeSpan.FromSeconds(5));
```

### Required Enlistment

`RunInUnitOfWorkAsync` creates a service scope, begins a resource-less unit of work on it (`IUnitOfWorkManager.BeginAsync()`), and runs the delegate with that scope's provider: completing the unit dispatches everything captured on it, and an exception from the delegate abandons (rolls back) the unit. A type registered `WithEnlistment(TransactionEnlistment.Required)` publishes only while a compatible unit of work is active; outside one — including through `harness.Publisher`/`harness.Queue`, which carry no unit of work — the publish throws `InvalidOperationException`. Resolve `IBus`/`IQueue` from the delegate's own scope provider so the resolved facade sees the unit `RunInUnitOfWorkAsync` began.

```csharp
await using var harness = await MessagingTestHarness.CreateAsync(services =>
{
    services.AddHeadlessMessaging(options =>
    {
        options.Bus.ForMessage<OrderCreated>(message =>
            message.Contract("orders.created").WithEnlistment(TransactionEnlistment.Required)
        );
        options.UseInMemory();
        options.UseInMemoryStorage();
    });
});

// Completion: the captured row is stored and dispatched.
await harness.RunInUnitOfWorkAsync(async sp =>
{
    var bus = sp.GetRequiredService<IBus>();
    await bus.PublishAsync(new OrderCreated("ORD-1"));
});
var recorded = await harness.WaitForPublished<OrderCreated>(TimeSpan.FromSeconds(5));
recorded.ResolvedDeliveryMode.Should().Be(DeliveryMode.Durable);

// Rollback: the delegate throws, the unit is abandoned, nothing is recorded.
var act = () => harness.RunInUnitOfWorkAsync(async sp =>
{
    var bus = sp.GetRequiredService<IBus>();
    await bus.PublishAsync(new OrderCreated("ORD-2"));
    throw new InvalidOperationException("business rule failed");
});
await act.Should().ThrowAsync<InvalidOperationException>();
```

### TestConsumer\<T\>

Use `TestConsumer<T>` as a lightweight consumer double when you only need to capture messages without custom handling logic.

```csharp
await using var harness = await MessagingTestHarness.CreateAsync(services =>
{
    services.AddSingleton<TestConsumer<OrderCreated>>();

    services.AddHeadlessMessaging(options =>
    {
        options.Bus.ForMessage<OrderCreated>(message =>
            message.Contract("orders.created").Consumer<TestConsumer<OrderCreated>>(consumer =>
                consumer.ConsumerIdentity("orders.created-test-recorder")
            )
        );
        options.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
        options.UseInMemory();
        options.UseInMemoryStorage();
    });
});

await harness.Publisher.PublishAsync(new OrderCreated { OrderId = "ORD-1" });
await harness.WaitForConsumed<OrderCreated>(TimeSpan.FromSeconds(5));

var consumer = harness.GetTestConsumer<OrderCreated>();
consumer.ReceivedMessages.Should().ContainSingle(m => m.OrderId == "ORD-1");
```

`TestConsumer<T>` exposes:
- `ReceivedContexts` — all `ConsumeContext<T>` instances in order received
- `ReceivedMessages` — projected payloads from `ReceivedContexts`
- `Clear()` — resets captured state (thread-safe)

### xUnit Integration

The harness is standalone — it creates its own `ServiceProvider` per instance — so it works with any test runner. Below are recommended patterns for xUnit v3.

#### Per-Test Harness (Recommended)

Create a fresh harness in each test for full isolation. Extend `TestBase` to get `AbortToken` and logging:

```csharp
public sealed class OrderMessagingTests : TestBase
{
    [Fact]
    public async Task should_consume_order_created_event()
    {
        await using var harness = await MessagingTestHarness.CreateAsync(services =>
        {
            services.AddHeadlessMessaging(options =>
            {
                options.Bus.ForMessage<OrderCreated>(message =>
                    message.Contract("orders.created").Consumer<OrderCreatedConsumer>(consumer =>
                        consumer.ConsumerIdentity("orders.created-handler")
                    )
                );
                options.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
                options.UseInMemory();
                options.UseInMemoryStorage();
            });
        });

        await harness.Publisher.PublishAsync(new OrderCreated("ORD-1"), AbortToken);
        var recorded = await harness.WaitForConsumed<OrderCreated>(TimeSpan.FromSeconds(5), AbortToken);

        recorded.Message.Should().BeOfType<OrderCreated>().Which.OrderId.Should().Be("ORD-1");
    }
}
```

#### Shared Fixture (When Harness Setup Is Expensive)

If many tests share the same consumer topology, use `IClassFixture` with `IAsyncLifetime` to create the harness once per class:

```csharp
public sealed class OrderHarnessFixture : IAsyncLifetime
{
    public MessagingTestHarness Harness { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Harness = await MessagingTestHarness.CreateAsync(services =>
        {
            services.AddSingleton<TestConsumer<OrderCreated>>();
            services.AddHeadlessMessaging(options =>
            {
                options.Bus.ForMessage<OrderCreated>(message =>
                    message.Contract("orders.created").Consumer<TestConsumer<OrderCreated>>(consumer =>
                        consumer.ConsumerIdentity("orders.created-test-recorder")
                    )
                );
                options.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
                options.UseInMemory();
                options.UseInMemoryStorage();
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        await Harness.DisposeAsync();
    }
}

public sealed class OrderMessagingTests(OrderHarnessFixture fixture) : TestBase, IClassFixture<OrderHarnessFixture>
{
    [Fact]
    public async Task Should_consume_order_created_event()
    {
        await fixture.Harness.ResetAsync(cancellationToken: AbortToken); // Drains in-flight work, then clears
        var consumer = fixture.Harness.GetTestConsumer<OrderCreated>();
        consumer.Clear();

        await fixture.Harness.Publisher.PublishAsync(new OrderCreated("ORD-1"), AbortToken);
        await fixture.Harness.WaitForConsumed<OrderCreated>(TimeSpan.FromSeconds(5), AbortToken);

        consumer.ReceivedMessages.Should().ContainSingle(m => m.OrderId == "ORD-1");
    }
}
```

> **Note:** When sharing a harness across tests, call `await harness.ResetAsync()` at each test boundary (it throws `TimeoutException` if in-flight work does not settle within the timeout), clear any `TestConsumer<T>`, and prefer `WaitFor*` predicates to avoid cross-test interference.

#### Host Integration (WebApplicationFactory / IHost)

Use `AddMessagingTestHarness()` to inject the recording infrastructure into the application's own DI container. The harness shares the same transport as the app — when an API endpoint publishes a message, the harness observes it end-to-end.

#### With WebApplicationFactory

```csharp
public sealed class OrderApiTests : TestBase
{
    [Fact]
    public async Task Post_order_should_publish_and_consume_event()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Decorates the app's existing messaging registrations with recording
                services.AddMessagingTestHarness();
            });
        });

        using var client = factory.CreateClient();
        var harness = factory.Services.GetRequiredService<MessagingTestHarness>();

        // When — call the API endpoint that publishes OrderCreated
        await client.PostAsJsonAsync("/orders", new { Id = "ORD-1" }, AbortToken);

        // Then — the harness observes the message through the app's own pipeline
        var recorded = await harness.WaitForConsumed<OrderCreated>(TimeSpan.FromSeconds(5), AbortToken);
        recorded.Message.Should().BeOfType<OrderCreated>().Which.OrderId.Should().Be("ORD-1");
    }
}
```

#### With WebApplication (no factory)

```csharp
public sealed class OrderApiTests : TestBase
{
    [Fact]
    public async Task Post_order_should_publish_event()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddHeadlessMessaging(options =>
        {
            options.Bus.ForMessage<OrderCreated>(message =>
                message.Contract("orders.created").Consumer<OrderCreatedConsumer>(consumer =>
                    consumer.ConsumerIdentity("orders.created-handler")
                )
            );
            options.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
            options.UseInMemory();
            options.UseInMemoryStorage();
        });

        // Add the test harness AFTER AddHeadlessMessaging
        builder.Services.AddMessagingTestHarness();

        await using var app = builder.Build();
        // ... configure middleware ...
        await app.StartAsync(AbortToken);

        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var harness = app.Services.GetRequiredService<MessagingTestHarness>();

        await client.PostAsJsonAsync("/orders", new { Id = "ORD-1" }, AbortToken);

        var recorded = await harness.WaitForConsumed<OrderCreated>(TimeSpan.FromSeconds(5), AbortToken);
        recorded.Message.Should().BeOfType<OrderCreated>().Which.OrderId.Should().Be("ORD-1");
    }
}
```

> **Note:** `AddMessagingTestHarness()` must be called **after** `AddHeadlessMessaging()` so the transport and pipeline registrations exist to be decorated. The host manages bootstrapping and disposal — the harness does not dispose the container.

### Isolation

Each `MessagingTestHarness` instance owns its own `MessageObservationStore`. Tests running in parallel with separate harness instances do not share state.

- **Standalone** (`CreateAsync`): owns its own `ServiceProvider` — always dispose after each test via `await using`.
- **Hosted** (`AddMessagingTestHarness()`): the host owns the `ServiceProvider` — the harness does not dispose the container.
- **Shared** (fixture or host reused across tests): call `await harness.ResetAsync()` between tests. Because default publishes are store-first, the send and the consumer run after `PublishAsync` returns; the reset waits for that in-flight work (default `MessagingTestHarness.DefaultTimeout`), drops undelivered transport messages, then clears observations and in-memory storage. A delayed publish that is not yet due on the host `TimeProvider` is not awaited; the dispatcher still holds it and publishes it when due, so do not carry pending delays across tests (or advance a `FakeTimeProvider` past them before resetting). Pending `WaitFor*` calls fault when the store is cleared. A message the transport handed to a consumer just before the reset still runs afterwards, but the reset does not wait for it and its `Consumed` or `Faulted` observation is not recorded, so it cannot leak into the next test.

## Configuration

None. `MessagingTestHarness` has no configuration class or options object. The only tuneable is the per-call `timeout` parameter on `WaitFor*` methods and `ResetAsync`; when omitted it defaults to `MessagingTestHarness.DefaultTimeout` (5 seconds). Transport parallelism is intentionally disabled by the harness to guarantee deterministic single-threaded test execution — this is a fixed internal choice and cannot be overridden.

## Dependencies

- `Headless.UnitOfWork` — the harness registers the unit-of-work manager itself (`AddUnitOfWork()`, idempotent) so `RunInUnitOfWorkAsync` can begin one regardless of registration order with the host's `AddHeadlessMessaging(...)` call
- `Headless.Messaging.Core`
- `Headless.Messaging.InMemory`
- `Headless.Messaging.Storage.InMemory`

## Side Effects

- `MessagingTestHarness.CreateAsync(...)` builds and owns an in-process `ServiceProvider` until the harness is disposed.
- `AddMessagingTestHarness()` decorates an existing host's messaging registrations and must run after `AddHeadlessMessaging(...)`.
- Both entry points call `services.AddUnitOfWork()` (idempotent), so the host always resolves a real `IUnitOfWorkManager`.
- Transport parallelism is disabled inside the harness to keep observations deterministic.
