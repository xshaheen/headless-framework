# Headless.CommitCoordination.EntityFramework

## Problem Solved

Provides EF Core commit coordination registration points.

## Key Features

- `EntityFrameworkCommitSignalSource`.
- DI extension `AddEntityFrameworkCommitCoordination()`.
- `DbContext.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call resilient coordinated transaction (plain `DbContext`; pass the request scope). `HeadlessDbContext` has a scope-free overload in `Headless.Orm.EntityFramework`.

## Installation

```bash
dotnet add package Headless.CommitCoordination.EntityFramework
```

## Quick Start

```csharp
services.AddEntityFrameworkCommitCoordination();

// Open + enlist + commit in one call; publishes inside the operation drain atomically on commit.
await db.ExecuteCoordinatedTransactionAsync(
    async (context, ct) =>
    {
        await context.SaveChangesAsync(ct);
        await bus.PublishAsync(new OrderPlaced(orderId), ct);
    },
    services: requestServiceProvider);
```

## Configuration

None.

## Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.EntityFrameworkCore.Relational`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

## Side Effects

Registers core commit coordination services, `EntityFrameworkCommitSignalSource`, `ICommitSignalSource`, and the EF transaction interceptor.
