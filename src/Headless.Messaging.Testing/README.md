# Headless.Messaging.Testing

In-process test harness for asserting on published, consumed, and faulted messages without external infrastructure.

## Problem Solved

Integration-testing a messaging pipeline typically requires a running broker and timing-sensitive polling. This package eliminates both: it wires the full pipeline in memory and exposes awaitable assertions that block until the expected message arrives (or the timeout elapses).

## Key Features

- **Zero Infrastructure**: No broker, no Docker — runs entirely in-process
- **Awaitable Assertions**: `WaitForPublished`, `WaitForConsumed`, `WaitForFaulted` block until observed or timed out
- **Full Pipeline Coverage**: Decorates the real transport and consume pipeline, so middleware, serialization, and consumer logic all execute
- **Isolated Per Test**: Each `MessagingTestHarness` instance owns its own observation store
- **Host Integration**: `AddMessagingTestHarness()` extension decorates an existing DI container for use with `WebApplicationFactory`, `IHost`, or `WebApplication`
- **Predicate Overloads**: Wait for a specific message matching a condition, not just any message of a type

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
        options.UseInMemoryMessageQueue();
        options.UseInMemoryStorage();
        options.Subscribe<OrderCreatedConsumer>("orders.created").Group("order-svc");
    });
});

await harness.Publisher.PublishAsync(new OrderCreated { OrderId = "ORD-1" });

var msg = await harness.WaitForConsumed<OrderCreated>(TimeSpan.FromSeconds(5));
msg.Message.Should().BeOfType<OrderCreated>();
```

## Observable Collections

The harness records every message in three collections, available as snapshots at any time:

| Property | Contents |
|---|---|
| `harness.Published` | All messages sent to the transport |
| `harness.Consumed` | All messages consumed successfully |
| `harness.Faulted` | All messages whose consumer threw an unhandled exception |

```csharp
harness.Published.Should().ContainSingle(m => m.MessageType == typeof(OrderCreated));
harness.Faulted.Should().BeEmpty();
```

Each entry is a `RecordedMessage` with `MessageType`, `Message`, `MessageId`, `CorrelationId`, `Headers`, `Topic`, `Timestamp`, and (for faulted) `Exception`.

## WaitFor* Methods

All `WaitFor*` methods return a `RecordedMessage` or throw `MessageObservationTimeoutException` with a diagnostic listing what was observed during the wait.

```csharp
// Wait for any message of type T
var recorded = await harness.WaitForConsumed<OrderCreated>(TimeSpan.FromSeconds(5));

// Wait for a specific message matching a predicate
var recorded = await harness.WaitForConsumed<OrderCreated>(
    predicate: m => m.OrderId == "ORD-1",
    timeout: TimeSpan.FromSeconds(5));

// Same API for published and faulted
await harness.WaitForPublished<OrderCreated>(TimeSpan.FromSeconds(5));
await harness.WaitForFaulted<BadMessage>(TimeSpan.FromSeconds(5));
```

## TestConsumer\<T\>

Use `TestConsumer<T>` as a lightweight consumer double when you only need to capture messages without custom handling logic.

```csharp
await using var harness = await MessagingTestHarness.CreateAsync(services =>
{
    services.AddSingleton<TestConsumer<OrderCreated>>();

    services.AddHeadlessMessaging(options =>
    {
        options.UseInMemoryMessageQueue();
        options.UseInMemoryStorage();
        options.Subscribe<TestConsumer<OrderCreated>>("orders.created");
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

## xUnit Integration

The harness is standalone — it creates its own `ServiceProvider` per instance — so it works with any test runner. Below are recommended patterns for xUnit v3.

### Per-Test Harness (Recommended)

Create a fresh harness in each test for full isolation. Extend `TestBase` to get `AbortToken` and logging:

```csharp
public sealed class OrderMessagingTests : TestBase
{
    [Fact]
    public async Task Should_consume_order_created_event()
    {
        await using var harness = await MessagingTestHarness.CreateAsync(services =>
        {
            services.AddHeadlessMessaging(options =>
            {
                options.UseInMemoryMessageQueue();
                options.UseInMemoryStorage();
                options.Subscribe<OrderCreatedConsumer>("orders.created");
            });
        });

        await harness.Publisher.PublishAsync(new OrderCreated("ORD-1"), AbortToken);
        var recorded = await harness.WaitForConsumed<OrderCreated>(TimeSpan.FromSeconds(5), AbortToken);

        recorded.Message.Should().BeOfType<OrderCreated>()
            .Which.OrderId.Should().Be("ORD-1");
    }
}
```

### Shared Fixture (When Harness Setup Is Expensive)

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
                options.UseInMemoryMessageQueue();
                options.UseInMemoryStorage();
                options.Subscribe<TestConsumer<OrderCreated>>("orders.created");
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        await Harness.DisposeAsync();
    }
}

public sealed class OrderMessagingTests(OrderHarnessFixture fixture)
    : TestBase, IClassFixture<OrderHarnessFixture>
{
    [Fact]
    public async Task Should_consume_order_created_event()
    {
        var consumer = fixture.Harness.GetTestConsumer<OrderCreated>();
        consumer.Clear(); // Reset between tests

        await fixture.Harness.Publisher.PublishAsync(new OrderCreated("ORD-1"), AbortToken);
        await fixture.Harness.WaitForConsumed<OrderCreated>(TimeSpan.FromSeconds(5), AbortToken);

        consumer.ReceivedMessages.Should().ContainSingle(m => m.OrderId == "ORD-1");
    }
}
```

> **Note:** When sharing a harness across tests, use `TestConsumer<T>.Clear()` and check `WaitFor*` with predicates to avoid cross-test interference.

### Host Integration (WebApplicationFactory / IHost)

Use `AddMessagingTestHarness()` to inject the recording infrastructure into the application's own DI container. The harness shares the same transport as the app — when an API endpoint publishes a message, the harness observes it end-to-end.

#### With WebApplicationFactory

```csharp
public sealed class OrderApiTests : TestBase
{
    [Fact]
    public async Task Post_order_should_publish_and_consume_event()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
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
        recorded.Message.Should().BeOfType<OrderCreated>()
            .Which.OrderId.Should().Be("ORD-1");
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
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Test" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddHeadlessMessaging(options =>
        {
            options.UseInMemoryMessageQueue();
            options.UseInMemoryStorage();
            options.Subscribe<OrderCreatedConsumer>("orders.created");
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
        recorded.Message.Should().BeOfType<OrderCreated>()
            .Which.OrderId.Should().Be("ORD-1");
    }
}
```

> **Note:** `AddMessagingTestHarness()` must be called **after** `AddHeadlessMessaging()` so the transport and pipeline registrations exist to be decorated. The host manages bootstrapping and disposal — the harness does not dispose the container.

## Isolation

Each `MessagingTestHarness` instance owns its own `MessageObservationStore`. Tests running in parallel with separate harness instances do not share state.

- **Standalone** (`CreateAsync`): owns its own `ServiceProvider` — always dispose after each test via `await using`.
- **Hosted** (`AddMessagingTestHarness()`): the host owns the `ServiceProvider` — the harness does not dispose the container.

## Dependencies

- `Headless.Messaging.Core`
- `Headless.Messaging.InMemoryQueue`
- `Headless.Messaging.InMemoryStorage`
