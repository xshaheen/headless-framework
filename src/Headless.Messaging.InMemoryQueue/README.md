# Headless.Messaging.InMemoryQueue

In-memory message queue transport for testing and development.

## Problem Solved

Provides a lightweight, no-infrastructure message queue for local development, testing, and single-instance applications without external dependencies.

## Key Features

- **Zero Dependencies**: No external broker required
- **Fast**: In-process message delivery
- **Testing Friendly**: Deterministic, synchronous behavior
- **Same API**: Identical interface to production transports
- **Thread-Safe**: Concurrent producer/consumer support

## Installation

```bash
dotnet add package Headless.Messaging.InMemoryQueue
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UseInMemoryStorage();
    options.UseInMemoryQueue();

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

No configuration required. Just call `UseInMemoryQueue()`.

## Dependencies

- `Headless.Messaging.Core`

## Side Effects

None. Messages are stored in memory only and lost on restart.
