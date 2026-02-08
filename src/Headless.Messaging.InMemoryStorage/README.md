# Headless.Messaging.InMemoryStorage

In-memory outbox storage for testing and development.

## Problem Solved

Provides ephemeral message storage without database dependencies for local development, integration tests, and prototyping.

## Key Features

- **Zero Dependencies**: No database required
- **Fast**: In-memory operations
- **Testing**: Deterministic behavior for tests
- **Full API**: Complete outbox storage implementation
- **Monitoring**: In-memory dashboard data

## Installation

```bash
dotnet add package Headless.Messaging.InMemoryStorage
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UseInMemoryStorage();
    options.UseRabbitMQ(config);

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

No configuration required. Just call `UseInMemoryStorage()`.

## Scheduling Storage

`UseInMemoryStorage()` automatically registers `InMemoryScheduledJobStorage` for job scheduling. Useful for testing scheduled jobs without database dependencies:

```csharp
builder.Services.AddMessages(options =>
{
    options.UseInMemoryStorage(); // Registers both outbox and scheduling storage
    options.ScanConsumers(typeof(Program).Assembly);
});
```

All scheduled job executions are stored in-memory and reset on restart.

## Dependencies

- `Headless.Messaging.Core`

## Side Effects

None. All messages are stored in memory and lost on restart. Not suitable for production.
