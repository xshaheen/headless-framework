# Headless.Messaging.Storage.InMemory

In-memory outbox storage for testing and development.

## Problem Solved

Provides ephemeral message storage without database dependencies for local development, integration tests, and prototyping.

## Key Features

- **Zero Dependencies**: No database required
- **Fast**: In-memory operations
- **Testing**: Deterministic behavior for tests
- **Full API**: Complete outbox storage implementation
- **Intent-Aware Identity**: Mirrors durable providers by storing bus/queue intent and including it in received-message de-duplication
- **Monitoring**: In-memory dashboard data

InMemoryStorage uses its injected `TimeProvider` for both application-scheduled `NextRetryAt` and authoritative lease ownership. It implements the same duration-based lease SPI and returns the persisted `(LockedUntil, Owner)` identity. Delayed scheduling atomically transitions and leases each per-message winner before returning a deterministic bounded batch. Circuit-open received retries atomically advance `NextRetryAt` and clear only the exact live `(lane, Owner, LockedUntil)` lease generation under the per-row lock. Retry pickup claims due rows in `NextRetryAt` order, as the relational providers do, so an earlier-scheduled row is never starved by a later one once `RetryBatchSize` bounds the batch. Rows sharing an identical `NextRetryAt` fall back to a deterministic per-provider tie-break, which no fairness guarantee depends on.

## Installation

```bash
dotnet add package Headless.Messaging.Storage.InMemory
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.Bus.ForConsumersFromAssemblyContaining<Program>();
    options.UseInMemoryStorage();
    options.UseRabbitMq(config);
});
```

## Configuration

No configuration required. Just call `UseInMemoryStorage()`.

## Dependencies

- `Headless.Messaging.Core`

## Side Effects

None. All messages are stored in memory and lost on restart. Not suitable for production.
