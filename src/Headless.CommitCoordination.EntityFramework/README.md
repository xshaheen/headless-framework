# Headless.CommitCoordination.EntityFramework

## Problem Solved

Bridges EF Core's transaction commit/rollback edges to commit coordination, so work buffered inside a transaction — outbox dispatch, durable jobs — drains atomically on commit and is discarded on rollback. It also closes the interceptor-attach footgun (EF Core does not auto-discover DI-registered interceptors) and surfaces a mis-wire loudly at startup.

## Key Features

- Internal `EntityFrameworkCommitSignalSource` registered as `ICommitSignalSource`.
- `AddEntityFrameworkCommitCoordination<TContext>()` wires the registered context, commit interceptor, and startup probe. The nongeneric overload registers services only for advanced integrations.
- `DbContext.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call resilient coordinated transaction (plain `DbContext`; pass the request scope). `HeadlessDbContext` and `HeadlessIdentityDbContext` (any `IHeadlessDbContext`) have a scope-free overload in `Headless.EntityFramework`.
- The generic helper auto-attaches only the commit-coordination interceptor through `IDbContextOptionsConfiguration<TContext>`, including plain `AddDbContext<TContext>` registrations. Repeated calls are idempotent.
- Startup gate `CommitInterceptorStartupGate<TContext>` with `CommitProbeMode` (`Disabled` / `Warn` / `Strict`, default `Warn`) configured through `CommitInterceptorProbeOptions`.

## Design Notes

EF Core does not auto-discover `IInterceptor` registrations from the application container. Use `AddEntityFrameworkCommitCoordination<TContext>()` after registering a plain application context: it attaches the commit interceptor to every options build and registers the startup probe. The nongeneric overload is a service-only seam for integrations that already own attachment and probing. The Jobs application-context provider convenience methods and the messaging EF storage path wire the generic stack automatically.

**The startup gate turns the silent mis-wire into a boot-time signal.** When coordination is enabled but the interceptor is not actually attached, a transaction *looks* transactional but isn't — publishes drain as rollback and vanish with no error. `CommitInterceptorStartupGate<TContext>` runs before any hosted service: it commits an empty transaction (no data mutated) on the consumer's `DbContext` and asserts the commit interceptor fired. On a mis-wire it logs a loud warning (`Warn`, the default) or throws at startup (`Strict`, opt-in via `services.Configure<CommitInterceptorProbeOptions>(o => o.Mode = CommitProbeMode.Strict)`). The on-by-default `Headless.Messaging.Core` EF storage path enables this gate automatically; raw-ADO storage paths attach no interceptor and use the SqlServer/PostgreSql signal sources instead.

The probe opens a real (empty) transaction against the database on every host start. Set `Mode = CommitProbeMode.Disabled` to skip that round-trip — the escape-hatch for a cold-start latency budget or a boot environment where the database is not yet reachable. The cost is losing early mis-wire detection; durability is unaffected because the outbox row and relay sweep recover the work either way.

## Installation

```bash
dotnet add package Headless.CommitCoordination.EntityFramework
```

## Quick Start

`ExecuteCoordinatedTransactionAsync` is **the recommended path** — it welds open + enlist + commit into one call so the enlist cannot be forgotten; raw `EnlistCommitCoordination` is the advanced seam (the EF interceptor signals the commit edge, so no manual signal is needed, unlike PostgreSQL).

The EF execution strategy may replay failures that occur before commit starts. Once `CommitAsync` begins, the helper surfaces any exception without replay because the server may already have committed; callers should reconcile by a client-generated key or another durable idempotency key before deciding to retry the business operation.

```csharp
using Headless.CommitCoordination;
using Microsoft.EntityFrameworkCore;

services.AddDbContext<MyDbContext>(options => options.UseNpgsql(connectionString));
services.AddEntityFrameworkCommitCoordination<MyDbContext>();

// Open + enlist + commit in one call; publishes inside the operation drain atomically on commit.
await db.ExecuteCoordinatedTransactionAsync(
    async (context, ct) =>
    {
        await context.SaveChangesAsync(ct);
        await bus.PublishAsync(new OrderPlaced(orderId), ct);
    },
    services: requestServiceProvider
);
```

## Configuration

Configure `CommitInterceptorProbeOptions.Mode`: `Warn` by default, `Strict` to fail startup on a miswired interceptor, or `Disabled` to skip the startup database probe.

## Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.EntityFrameworkCore.Relational`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions` — required by the startup gate (`CommitInterceptorStartupGate<TContext>`)
- `Microsoft.Extensions.Logging.Abstractions` — required by the startup gate
- `Microsoft.Extensions.Options` — required by `CommitInterceptorProbeOptions`

## Side Effects

Both overloads register core commit coordination, the EF commit signal source, and its transaction interceptor. The generic overload additionally attaches that interceptor to the selected context and registers an empty-transaction startup probe. It creates no schema and does not automatically start or enlist application transactions; use `ExecuteCoordinatedTransactionAsync` for the operation boundary.
