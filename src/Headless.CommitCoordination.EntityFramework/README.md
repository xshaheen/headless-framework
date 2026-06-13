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

`ExecuteCoordinatedTransactionAsync` is **the recommended path** — it welds open + enlist + commit into one call so the enlist cannot be forgotten; raw `EnlistCommitCoordination` is the advanced seam (the EF interceptor signals the commit edge, so no manual signal is needed, unlike PostgreSQL).

```csharp
services.AddEntityFrameworkCommitCoordination();

// REQUIRED for plain AddDbContext: EF Core does NOT auto-discover IInterceptor registrations from
// the application container — without them the commit edge is never observed and coordinated work
// silently drains as rollback. Use the AddDiRegisteredInterceptors(sp) helper (from
// Headless.Orm.EntityFramework) to pick up every DI-registered interceptor and skip ones the options
// action already added. (AddHeadlessDbContext / AddHeadlessIdentityDbContext call it automatically.)
services.AddDbContext<MyDbContext>(
    (sp, options) => options.UseNpgsql(connectionString).AddDiRegisteredInterceptors(sp));

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

Registers core commit coordination services, `EntityFrameworkCommitSignalSource`, `ICommitSignalSource`, and the EF transaction interceptor **in DI only** — the interceptor still has to reach the context options. `AddHeadlessDbContext`/`AddHeadlessIdentityDbContext` wire it automatically; plain `AddDbContext` consumers must call `options.AddDiRegisteredInterceptors(sp)` (or `options.AddInterceptors(sp.GetServices<IInterceptor>())`) themselves or the commit edge is never observed.
