# Headless.Messaging.InMemory

In-memory message queue transport for testing and development.

## Problem Solved

Provides a lightweight, no-infrastructure message queue for local development, testing, and single-instance applications without external dependencies.

## Key Features

- **Zero Dependencies**: No external broker required
- **Fast**: In-process message delivery
- **Testing Friendly**: Deterministic, synchronous behavior
- **Same API**: Identical interface to production transports
- **Thread-Safe**: Concurrent producer/consumer support

## Design Notes

- Publish and consume happen in process only. Headers and payload never leave memory.
- Delay stays in the core pipeline. There is no broker-native scheduling layer.
- Manual callers should await `IBootstrapper.BootstrapAsync(...)` before publishing or attaching runtime subscriptions.
- Commit is a no-op.
- Reject is a no-op. There is no durable redelivery or dead-letter queue.
- `SubscribeAsync(...)` only registers in-memory message-name bindings for the current process.
- Publishing to an unbound message name still fails fast. `InMemory` is intentionally strict so tests and local development do not silently hide invalid publish targets or missing consumer registration.
- Single-threaded consumption preserves queue order. Higher `ConsumerThreadCount` can reorder concurrent handlers.
- Payload size is limited by process memory.
- Each consumer group buffers at most 65,536 pending messages. When the buffer is full (for example, a paused or stalled consumer), further deliveries to that group are dropped and reported through the transport log callback instead of growing memory without bound.

## Installation

```bash
dotnet add package Headless.Messaging.InMemory
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.ForMessagesFromAssemblyContaining<Program>();
    options.UseInMemoryStorage();
    options.UseInMemory();
});
```

## Configuration

No configuration required. Just call `UseInMemory()`.

## Dependencies

- `Headless.Messaging.Core`

## Side Effects

None. Messages are stored in memory only and lost on restart.
