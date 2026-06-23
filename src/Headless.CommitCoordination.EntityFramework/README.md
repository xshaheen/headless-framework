# Headless.CommitCoordination.EntityFramework

## Problem Solved

Bridges EF Core's transaction commit/rollback edges to commit coordination, so work buffered inside a transaction — outbox dispatch, durable jobs — drains atomically on commit and is discarded on rollback. It also closes the interceptor-attach footgun (EF Core does not auto-discover DI-registered interceptors) and surfaces a mis-wire loudly at startup.

## Key Features

- `EntityFrameworkCommitSignalSource`.
- DI extension `AddEntityFrameworkCommitCoordination()`.
- `DbContext.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call resilient coordinated transaction (plain `DbContext`; pass the request scope). `HeadlessDbContext` and `HeadlessIdentityDbContext` (any `IHeadlessDbContext`) have a scope-free overload in `Headless.Orm.EntityFramework`.
- Auto-attach of DI-registered interceptors to a consumer's own `DbContext` options via `IDbContextOptionsConfiguration<TContext>` (EF Core 9+). The public helper `services.AddDiRegisteredInterceptorsConfiguration<TContext>()` (in `Headless.Orm.EntityFramework`, namespace `Headless.EntityFramework`) runs against every `DbContext<TContext>` options build, including a plain `AddDbContext<TContext>` with no `AddInterceptors(...)`. `options.AddDiRegisteredInterceptors(sp)` remains the explicit per-options-action form.
- Startup gate `CommitInterceptorStartupGate<TContext>` with `CommitInterceptorProbeMode` (`Disabled` / `Warn` / `Strict`, default `Warn`) configured through `CommitInterceptorProbeOptions`.

## Design Notes

**Interceptor attachment is the wiring footgun, and the framework closes it two ways.** EF Core does not auto-discover `IInterceptor` registrations from the application container, so an interceptor registered "in DI only" never observes the commit edge and coordinated work silently drains as rollback. `IDbContextOptionsConfiguration<TContext>` is a DI-registered configuration that EF Core applies during every `DbContext<TContext>` options build, including a plain `AddDbContext<TContext>`. `AddHeadlessDbContext`/`AddHeadlessIdentityDbContext` and the on-by-default messaging EF storage path register it for you; a plain `AddDbContext` consumer wiring its own options action calls `services.AddDiRegisteredInterceptorsConfiguration<TContext>()` once, or `options.AddDiRegisteredInterceptors(sp)` inside the action.

**The startup gate turns the silent mis-wire into a boot-time signal.** When coordination is enabled but the interceptor is not actually attached, a transaction *looks* transactional but isn't — publishes drain as rollback and vanish with no error. `CommitInterceptorStartupGate<TContext>` runs before any hosted service: it commits an empty transaction (no data mutated) on the consumer's `DbContext` and asserts the commit interceptor fired. On a mis-wire it logs a loud warning (`Warn`, the default) or throws at startup (`Strict`, opt-in via `services.Configure<CommitInterceptorProbeOptions>(o => o.Mode = CommitInterceptorProbeMode.Strict)`). The on-by-default `Headless.Messaging.Core` EF storage path enables this gate automatically; raw-ADO storage paths attach no interceptor and use the SqlServer/PostgreSql signal sources instead.

The probe opens a real (empty) transaction against the database on every host start. Set `Mode = CommitInterceptorProbeMode.Disabled` to skip that round-trip — the escape-hatch for a cold-start latency budget or a boot environment where the database is not yet reachable. The cost is losing early mis-wire detection; durability is unaffected because the outbox row and relay sweep recover the work either way.

## Installation

```bash
dotnet add package Headless.CommitCoordination.EntityFramework
```

## Quick Start

`ExecuteCoordinatedTransactionAsync` is **the recommended path** — it welds open + enlist + commit into one call so the enlist cannot be forgotten; raw `EnlistCommitCoordination` is the advanced seam (the EF interceptor signals the commit edge, so no manual signal is needed, unlike PostgreSQL).

```csharp
services.AddEntityFrameworkCommitCoordination();

// A plain AddDbContext must attach the commit interceptor to its options — EF Core does NOT auto-discover
// IInterceptor registrations, so without it the commit edge is never observed and coordinated work silently
// drains as rollback. Two equivalent ways: the inline AddDiRegisteredInterceptors(sp) shown here, or a one-time
// services.AddDiRegisteredInterceptorsConfiguration<MyDbContext>() (both from Headless.Orm.EntityFramework).
// AddHeadlessDbContext / AddHeadlessIdentityDbContext and the messaging EF storage path do this for you.
services.AddDbContext<MyDbContext>(
    (sp, options) => options.UseNpgsql(connectionString).AddDiRegisteredInterceptors(sp)
);

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

None.

## Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.EntityFrameworkCore.Relational`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions` — required by the startup gate (`CommitInterceptorStartupGate<TContext>`)
- `Microsoft.Extensions.Logging.Abstractions` — required by the startup gate
- `Microsoft.Extensions.Options` — required by `CommitInterceptorProbeOptions`

## Side Effects

Registers core commit coordination services, `EntityFrameworkCommitSignalSource`, `ICommitSignalSource`, and the EF transaction interceptor **in DI only** — the interceptor still has to reach the context options. `AddHeadlessDbContext`/`AddHeadlessIdentityDbContext` and the on-by-default messaging EF storage path wire it automatically (via `IDbContextOptionsConfiguration<TContext>`, registered by `AddDiRegisteredInterceptorsConfiguration<TContext>()`); a plain `AddDbContext` consumer wiring its own options action calls `options.AddDiRegisteredInterceptors(sp)` (or `options.AddInterceptors(sp.GetServices<IInterceptor>())`) inside the action, or registers `AddDiRegisteredInterceptorsConfiguration<TContext>()` once — otherwise the commit edge is never observed. When the startup gate is enabled it also registers `CommitInterceptorStartupGate<TContext>`, which runs an empty-commit probe before hosted services start (`CommitInterceptorProbeMode` Warn default / Strict opt-in).
