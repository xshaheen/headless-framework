---
domain: Commit Coordination
packages: CommitCoordination.Abstractions, CommitCoordination.Core, CommitCoordination.DurableWork, CommitCoordination.EntityFramework, EntityFramework.CommitCoordination, CommitCoordination.InMemory, CommitCoordination.PostgreSql, CommitCoordination.SqlServer
---

# Commit Coordination

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Coordinator](#coordinator)
    - [Signal Source](#signal-source)
    - [Work Buffer](#work-buffer)
    - [Capability](#capability)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.CommitCoordination.Abstractions](#headlesscommitcoordinationabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.CommitCoordination.Core](#headlesscommitcoordinationcore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.CommitCoordination.DurableWork](#headlesscommitcoordinationdurablework)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.CommitCoordination.EntityFramework](#headlesscommitcoordinationentityframework)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Design Notes](#design-notes-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.EntityFramework.CommitCoordination](#headlessentityframeworkcommitcoordination)
- [Headless.CommitCoordination.InMemory](#headlesscommitcoordinationinmemory)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.CommitCoordination.PostgreSql](#headlesscommitcoordinationpostgresql)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)
- [Headless.CommitCoordination.SqlServer](#headlesscommitcoordinationsqlserver)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-6)
    - [Design Notes](#design-notes-4)
    - [Installation](#installation-6)
    - [Quick Start](#quick-start-6)
    - [Configuration](#configuration-6)
    - [Dependencies](#dependencies-6)
    - [Side Effects](#side-effects-6)

> Commit Coordination runs registered work only after the unit of work it belongs to reaches a committed or rolled-back terminal outcome.

## Quick Orientation

Use Commit Coordination when a framework subsystem must defer work until the data it belongs to has durably committed. Messaging uses it to store outbox rows inside the relational transaction and dispatch only after commit. Jobs can use `DurableWorkBuffer<TRow>` to fail closed unless a relational capability is available.

The coordinator guarantees exactly-once callback invocation per coordinator instance. It does not guarantee exactly-once business effects for brokers, external services, or processes that crash after commit.

**Commit detection is an acceleration hook, not a correctness mechanism.** A detected signal (SQL Server SqlClient diagnostic, EF interceptor) only dispatches deferred work *sooner*; correctness must not depend on it firing. The consumer commits a durable row inside the transaction and recovers it through an independent polling sweep, so if the signal is missed, delayed, or disabled, the work is still found and executed. In-memory accelerator buffers (`InMemoryWorkBuffer<T>`) therefore require the consumer to own that durable store plus recovery (messaging: outbox rows + retry sweep); `DurableWorkBuffer<TRow>` writes rows in-transaction and does not depend on detection at all.

## Agent Instructions

- Consumer packages should depend on `Headless.CommitCoordination.Abstractions`; provider packages depend on `Headless.CommitCoordination.Core`.
- `Headless.CommitCoordination.EntityFramework` owns generic EF commit detection. `Headless.EntityFramework.CommitCoordination` is the separate adapter that makes the Headless save pipeline select it.
- Register work with `OnCommit` or `OnRollback`; do not expose commit or rollback control to consumers.
- Use `GetOrAdd<TBuffer>` only for scope-local deferred work buffers. Do not use it as a service locator.
- Use `TryGetCapability<IRelationalCommitContext>` when work must write durable rows inside the active relational transaction.
- Never make correctness depend on a detected commit signal. Detection (SQL Server diagnostic, EF interceptor) only dispatches sooner; back every in-memory accelerator buffer with a durable row committed in-transaction plus an independent polling/recovery sweep, so a missed, delayed, or disabled signal still executes the work.
- Prefer the single-call `ExecuteCoordinatedTransactionAsync(...)` helper over hand-rolling `Begin` + `EnlistCommitCoordination`; it welds the enlist into the transaction so it cannot be forgotten. A `HeadlessDbContext` self-sources its request scope (no `IServiceProvider` argument); a plain `DbContext`, `SqlConnection`, or `NpgsqlConnection` cannot, so those overloads require the scope passed explicitly. Pass the **request-scoped** provider (e.g. `HttpContext.RequestServices` or an injected scoped `IServiceProvider`), never the root container — the post-commit drain resolves scoped services, and the root provider would resolve the wrong (or no) scope.
- Durable jobs should keep `DurableWorkProviderMismatchPolicy.Throw`; fallback modes are for at-least-once accelerators that already have recovery.

## Core Concepts

### Coordinator

`ICommitCoordinator` is the register-only contract. It accepts outcome-keyed callbacks and typed work buffers while `State == Active`; enlistment after terminal transition throws.

### Signal Source

`ICommitSignalSource` adapts a provider's native commit or rollback edge into a coordinator signal. In-memory sources are driven directly; SQL Server can correlate detected signals by provider transaction key. A detected signal is a latency accelerator: it dispatches deferred work as soon as the commit edge is observed, but the consumer's durable store and polling recovery — not the signal — are the source of truth.

### Work Buffer

`ICommitWorkBuffer` marks state that belongs to one commit scope. `InMemoryWorkBuffer<T>` is a thread-safe queue for post-commit accelerators. `DurableWorkBuffer<TRow>` writes rows while the relational transaction is still open.

### Capability

`ICommitCapability` is a read-only provider escape hatch populated by the scope owner. `IRelationalCommitContext` exposes BCL `DbConnection` and `DbTransaction` handles without putting database concepts on the root coordinator.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.CommitCoordination.InMemory` | The owner explicitly signals commit or rollback in-process. | Work needs relational transaction handles. | No provider capability. |
| `Headless.CommitCoordination.EntityFramework` | EF Core owns the unit-of-work edge. | Raw ADO.NET owns commit detection. | Depends only on EF Core relational APIs. |
| `Headless.CommitCoordination.PostgreSql` | PostgreSQL flows commit through framework-owned transaction APIs. | The app bypasses framework transaction helpers. | Inline signal source; no out-of-band detection. |
| `Headless.CommitCoordination.SqlServer` | SQL Server commit is detected by provider transaction key. | Diagnostic correlation cannot be established. | Correlation registry must remove scopes on every terminal path. |

## Headless.CommitCoordination.Abstractions

### Problem Solved

Defines the public commit coordination contracts without provider dependencies.

### Key Features

- `ICommitCoordinator`, `ICurrentCommitCoordinator`, `ICommitScope`, and `ICommitSignalSource`.
- Outcome callbacks for commit and rollback.
- Typed scope-local work buffers.
- Capability lookup through `ICommitCapability`.

### Design Notes

The root contract is not a transaction. Consumers can register work but cannot decide the terminal outcome.

### Installation

```bash
dotnet add package Headless.CommitCoordination.Abstractions
```

### Quick Start

```csharp
var coordinator = currentCommitCoordinator.Current;
coordinator?.OnCommit((context, ct) => ValueTask.CompletedTask);
```

### Configuration

None.

### Dependencies

None.

### Side Effects

None.

## Headless.CommitCoordination.Core

### Problem Solved

Implements the in-process coordinator, ambient stack, scope factory, and relational capability implementation.

### Key Features

- Thread-safe callback registration and typed buffers.
- Ambient current coordinator through `ICurrentCommitCoordinator` (backed by an internal AsyncLocal stack).
- Nested scopes join the root by default.
- Terminal drain runs callbacks with `CancellationToken.None` and aggregates failures.

### Design Notes

`Dispose` schedules an un-signalled rollback drain in the background so sync callers are not blocked on async callbacks. `DisposeAsync` restores the ambient parent synchronously before any rollback drain so `await using` does not strand `AsyncLocal` state.

**Savepoints are invisible to the coordinator.** Enlisted work binds to the OUTERMOST commit edge only: a `RollbackToSavepoint` discards the database writes made after the savepoint but does NOT discard commit work buffered during that window — on final commit, all buffered work drains, including work registered inside the rolled-back region. If an operation publishes or enqueues inside a partial-rollback region, that mismatch is the consumer's to manage: enlist work only after the last possible partial rollback, or register/dispose the callback manually. Nested *scopes* (child coordinators joining the root) are supported and conformance-tested; nested *savepoint tracking* is deliberately out of scope.

### Installation

```bash
dotnet add package Headless.CommitCoordination.Core
```

### Quick Start

```csharp
services.AddCommitCoordination();
```

### Configuration

None.

### Dependencies

- `Headless.CommitCoordination.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

Registers `ICurrentCommitCoordinator` and `ICommitScopeFactory` (the scope-opening seam for custom `ICommitSignalSource` implementations); the backing stack and factory types are internal.

## Headless.CommitCoordination.DurableWork

### Problem Solved

Provides a base for durable work buffers that must write rows inside the active relational transaction.

### Key Features

- `DurableWorkBuffer<TRow>` base class.
- `DurableWorkProviderMismatchPolicy.Throw` default.
- Explicit `Warn` fallback for consumers that already have recovery.

### Design Notes

Durable work fails closed by default because running a job before its triggering data commits is a correctness bug. Rows are written inside the active relational transaction at enlist time, so a durable buffer does not depend on commit detection at all: the row commits atomically with the business data and is recovered by the consumer's relay regardless of whether any signal fires.

### Installation

```bash
dotnet add package Headless.CommitCoordination.DurableWork
```

### Quick Start

```csharp
public sealed record JobRow(string Name);

public sealed class JobWorkBuffer(ICommitCoordinator coordinator) : DurableWorkBuffer<JobRow>(coordinator)
{
    protected override ValueTask WriteRowAsync(JobRow row, IRelationalCommitContext context, CancellationToken ct)
    {
        // write row using context.Connection/context.Transaction
        return ValueTask.CompletedTask;
    }
}
```

### Configuration

Choose `DurableWorkProviderMismatchPolicy.Throw` or `Warn` per buffer.

### Dependencies

- `Headless.CommitCoordination.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

None.

## Headless.CommitCoordination.EntityFramework

### Problem Solved

Bridges EF Core's transaction commit/rollback edges to commit coordination, so work buffered inside a transaction — outbox dispatch, durable jobs — drains atomically on commit and is discarded on rollback. It also closes the interceptor-attach footgun (EF Core does not auto-discover DI-registered interceptors) and surfaces a mis-wire loudly at startup.

### Key Features

- `EntityFrameworkCommitSignalSource`.
- DI extension `AddEntityFrameworkCommitCoordination()`.
- `DbContext.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call resilient coordinated transaction (plain `DbContext`; pass the request scope). `HeadlessDbContext` and `HeadlessIdentityDbContext` (any `IHeadlessDbContext`) have a scope-free overload in `Headless.EntityFramework`.
- Auto-attach of DI-registered interceptors to a consumer's own `DbContext` options via `IDbContextOptionsConfiguration<TContext>` (EF Core 9+). The public helper `services.AddDiRegisteredInterceptorsConfiguration<TContext>()` (in `Headless.EntityFramework`, namespace `Headless.EntityFramework`) registers a configuration that runs against every `DbContext<TContext>` options build — including a plain `AddDbContext<TContext>` with no `AddInterceptors(...)`. `options.AddDiRegisteredInterceptors(sp)` remains the explicit per-options-action form.
- Startup gate `CommitInterceptorStartupGate<TContext>` with `CommitProbeMode` (`Disabled` / `Warn` / `Strict`, default `Warn`) configured through `CommitInterceptorProbeOptions`.

### Design Notes

**Interceptor attachment is the wiring footgun, and the framework now closes it two ways.** EF Core does not auto-discover `IInterceptor` registrations from the application container, so an interceptor registered "in DI only" never observes the commit edge and coordinated work silently drains as rollback. The fix is `IDbContextOptionsConfiguration<TContext>`: a DI-registered configuration that EF Core applies during every `DbContext<TContext>` options build, including a plain `AddDbContext<TContext>`. The messaging EF storage path and `AddHeadlessDbContext`/`AddHeadlessIdentityDbContext` register it for you; a plain `AddDbContext` consumer wiring its own options action calls `services.AddDiRegisteredInterceptorsConfiguration<TContext>()` once, or `options.AddDiRegisteredInterceptors(sp)` inside the action.

**The startup gate turns the silent mis-wire into a boot-time signal.** When the outbox/coordination is enabled but the interceptor is not actually attached, the old failure mode was a transaction that *looks* transactional but isn't — publishes drain as rollback and vanish with no error. `CommitInterceptorStartupGate<TContext>` runs before any hosted service: it opens a transaction on the consumer's `DbContext`, commits an **empty** transaction (no data mutated), and asserts the commit interceptor fired. On a mis-wire it logs a loud warning (`Warn`, the default) or throws at startup (`Strict`, opt-in via `services.Configure<CommitInterceptorProbeOptions>(o => o.Mode = CommitProbeMode.Strict)`). This is the EF sibling of the SqlServer diagnostic self-probe (D11) — same Warn-default / Strict-opt-in posture, but the EF probe asserts interceptor attachment rather than SqlClient diagnostic compatibility. The on-by-default messaging wiring (see [messaging.md](messaging.md) → Core Concepts → Transactional outbox) enables this gate automatically on the EF storage path; the raw-ADO storage paths attach no interceptor and use the SqlServer/PostgreSql sources instead.

### Installation

```bash
dotnet add package Headless.CommitCoordination.EntityFramework
```

### Quick Start

`ExecuteCoordinatedTransactionAsync` is **the recommended path** — it welds open + enlist + commit into one call so the enlist cannot be forgotten; raw `EnlistCommitCoordination` is the advanced seam (the EF interceptor signals the commit edge, so no manual signal is needed, unlike PostgreSQL).

The EF execution strategy may replay failures that occur before commit starts. Once `CommitAsync` begins, the helper surfaces any exception without replay because the server may already have committed; callers should reconcile by a client-generated key or another durable idempotency key before deciding to retry the business operation.

```csharp
services.AddEntityFrameworkCommitCoordination();

// A plain AddDbContext must attach the commit interceptor to its options — EF Core does NOT auto-discover
// IInterceptor registrations, so without it the commit edge is never observed and coordinated work silently
// drains as rollback. Two equivalent ways: the inline AddDiRegisteredInterceptors(sp) shown here, or a one-time
// services.AddDiRegisteredInterceptorsConfiguration<MyDbContext>() (both from Headless.EntityFramework).
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

### Configuration

None.

### Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.EntityFrameworkCore.Relational`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions` — required by the startup gate (`CommitInterceptorStartupGate<TContext>`)
- `Microsoft.Extensions.Logging.Abstractions` — required by the startup gate
- `Microsoft.Extensions.Options` — required by `CommitInterceptorProbeOptions`

### Side Effects

Registers core commit coordination services, `EntityFrameworkCommitSignalSource`, `ICommitSignalSource`, and the EF transaction interceptor **in DI only** — the interceptor still has to reach the context options. `Headless.EntityFramework.CommitCoordination` and the messaging EF adapter packages wire it automatically for their contexts; a plain `AddDbContext` consumer calls `options.AddDiRegisteredInterceptors(sp)` or registers `AddDiRegisteredInterceptorsConfiguration<TContext>()`. When the startup gate is enabled it also registers `CommitInterceptorStartupGate<TContext>`.

## Headless.EntityFramework.CommitCoordination

This opt-in adapter connects `Headless.EntityFramework`'s internal save-pipeline transaction seam to `Headless.CommitCoordination.EntityFramework`. Install it and chain `.AddCommitCoordination()` from `AddHeadlessDbContextServices(...)` when buffered work must enlist in the transaction opened by the Headless save pipeline. The core `Headless.EntityFramework` package otherwise keeps a no-op coordinator and carries no commit-coordination package reference.

## Headless.CommitCoordination.InMemory

### Problem Solved

Provides an explicit in-process signal source for tests and owner-driven flows.

### Key Features

- `InMemoryCommitSignalSource`.
- DI extension `AddInMemoryCommitCoordination()`.

### Installation

```bash
dotnet add package Headless.CommitCoordination.InMemory
```

### Quick Start

```csharp
services.AddInMemoryCommitCoordination();
```

### Configuration

None.

### Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

### Side Effects

Registers core commit coordination services, `InMemoryCommitSignalSource`, and `ICommitSignalSource`.

## Headless.CommitCoordination.PostgreSql

### Problem Solved

Provides PostgreSQL commit coordination registration points for inline framework-owned transaction flows.

### Key Features

- `PostgreSqlCommitSignalSource`.
- DI extension `AddPostgreSqlCommitCoordination()`.
- `NpgsqlConnection.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call coordinated transaction for raw ADO (opens the connection if closed; no execution-strategy retry).

### Installation

```bash
dotnet add package Headless.CommitCoordination.PostgreSql
```

### Quick Start

`ExecuteCoordinatedTransactionAsync` is **the recommended path** — it welds open + enlist + commit + signal into one call so nothing can be forgotten:

```csharp
services.AddPostgreSqlCommitCoordination();

// Open + enlist + commit in one call; the enlist cannot be forgotten.
await connection.ExecuteCoordinatedTransactionAsync(
    async (conn, ct) => {
        // raw-ADO work on conn, plus publishes that enlist on the ambient coordinator
    },
    services: requestServiceProvider
);
```

### Advanced: raw enlistment

> **WARNING — PostgreSQL is an inline (caller-driven) signal provider.** Npgsql exposes no commit
> diagnostic, so nothing signals for you. If you hand-roll `EnlistCommitCoordination`, you MUST call
> `scope.SignalAsync(CommitOutcome.Committed)` immediately after `transaction.CommitAsync(...)`.
> An un-signalled scope dispose drains as **rollback** and silently discards every enlisted publish on
> a transaction that actually committed — durable outbox rows survive (the relay sweep recovers them),
> but accelerator-only work is lost. Prefer the helper above; it signals for you.

```csharp
await using var tx = await connection.BeginTransactionAsync(ct);
await using var scope = connection.EnlistCommitCoordination(tx, requestServiceProvider);

// ... raw-ADO work + publishes ...
await tx.CommitAsync(ct);
await scope.SignalAsync(CommitOutcome.Committed); // REQUIRED — see warning above
```

### Configuration

None.

### Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Npgsql`

### Side Effects

Registers core commit coordination services, `PostgreSqlCommitSignalSource`, and `ICommitSignalSource`.

## Headless.CommitCoordination.SqlServer

### Problem Solved

Correlates SQL Server commit or rollback signals to attached commit scopes.

### Key Features

- `SqlServerCommitSignalSource`.
- Provider-key registry for detected commit and rollback signals.
- DI extension `AddSqlServerCommitCoordination()`.
- `SqlConnection.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call coordinated transaction for raw ADO (opens the connection if closed; no execution-strategy retry).

### Design Notes

Detected signals remove the scope from the registry before signaling. The returned scope still owns the ambient pop; the signal source owns an async service scope for the out-of-band drain and releases it after the terminal signal completes.

The SqlClient diagnostic is a low-latency commit signal, not the durability mechanism. It depends on `Microsoft.Data.SqlClient` diagnostic event names and payload shapes, which can drift across driver upgrades, so a missed, delayed, or disabled diagnostic must not lose work: the consumer commits a durable row inside the transaction and recovers it by polling, and a faulted drain is left for that recovery path. Prefer `Headless.CommitCoordination.EntityFramework` where EF owns the commit edge — its interceptor signal has no such fragility.

On startup the hosted diagnostic service runs a bounded self-probe when enabled. The probe opens a configured SQL Server connection, commits a throwaway transaction, and verifies that SqlClient emitted a commit diagnostic payload with the expected connection correlation shape. The result is recorded in `SqlServerCommitDiagnosticProbeState`: default `Warn` mode marks the state `Degraded` and logs a warning when the probe cannot run or fails, `Strict` mode fails hosted-service startup, and `Disabled` skips the probe.

### Installation

```bash
dotnet add package Headless.CommitCoordination.SqlServer
```

### Quick Start

`ExecuteCoordinatedTransactionAsync` is **the recommended path** — it welds open + enlist + commit into one call so the enlist cannot be forgotten; raw `EnlistCommitCoordination` is the advanced seam (commit detection is out-of-band here, so no manual signal is needed, unlike PostgreSQL).

```csharp
services.AddSqlServerCommitCoordination();

// Open + enlist + commit in one call; the enlist cannot be forgotten.
await connection.ExecuteCoordinatedTransactionAsync(
    async (conn, ct) => {
        // raw-ADO work on conn, plus publishes that enlist on the ambient coordinator
    },
    services: requestServiceProvider
);
```

### Configuration

```csharp
services.AddSqlServerCommitCoordination(options =>
{
    options.DiagnosticProbeMode = CommitProbeMode.Strict;
    options.DiagnosticProbeTimeout = TimeSpan.FromSeconds(5);
    options.DiagnosticProbeConnectionFactory = ct => ValueTask.FromResult(new SqlConnection(connectionString));
});
```

Default mode is `Warn`. Without a `DiagnosticProbeConnectionFactory`, startup continues but the probe state is marked `Degraded` so operators can see that diagnostic compatibility was not proven. Use `Strict` in environments where out-of-band SQL Server detection must be verified at startup.

### Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.Data.SqlClient`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions` — required by the `IHostedService` diagnostic subscription
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Options`

### Side Effects

Registers core commit coordination services, `SqlServerCommitSignalSource`, `ICommitSignalSource`, the SqlClient diagnostic observer/listener, and an `IHostedService` that owns the diagnostic subscription lifetime.
