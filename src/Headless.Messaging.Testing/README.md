# Headless.Messaging.Testing

In-process test harness for asserting on published, consumed, and faulted messages without external infrastructure.

## Problem Solved

Integration-testing a messaging pipeline typically requires a running broker and timing-sensitive polling. This package eliminates both: it wires the full pipeline in memory and exposes awaitable assertions that block until the expected message arrives (or the timeout elapses).

## Key Features

- **Zero Infrastructure**: No broker, no Docker — runs entirely in-process
- **Awaitable Assertions**: `WaitForPublished`, `WaitForConsumed`, `WaitForFaulted` block until observed or timed out
- **Full Pipeline Coverage**: Decorates the real transport and consume pipeline, so middleware, serialization, and consumer logic all execute
- **Isolated Per Test**: Each `MessagingTestHarness` instance owns its own DI container and observation store
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

## Isolation

Each `MessagingTestHarness` instance creates its own `ServiceProvider` and `MessageObservationStore`. Tests running in parallel with separate harness instances do not share state. Always dispose the harness after each test:

```csharp
await using var harness = await MessagingTestHarness.CreateAsync(...);
```

## Dependencies

- `Headless.Messaging.Core`
- `Headless.Messaging.InMemoryQueue`
- `Headless.Messaging.InMemoryStorage`
