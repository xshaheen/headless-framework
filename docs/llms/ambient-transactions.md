---
domain: Ambient Transactions
packages: Headless.AmbientTransactions.Abstractions, Headless.AmbientTransactions.EntityFramework, Headless.AmbientTransactions.InMemory, Headless.AmbientTransactions.PostgreSql, Headless.AmbientTransactions.SqlServer
---

# Ambient Transactions

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.AmbientTransactions.Abstractions](#headlessambienttransactionsabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.AmbientTransactions.EntityFramework](#headlessambienttransactionsentityframework)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.AmbientTransactions.InMemory](#headlessambienttransactionsinmemory)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.AmbientTransactions.PostgreSql](#headlessambienttransactionspostgresql)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.AmbientTransactions.SqlServer](#headlessambienttransactionssqlserver)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Design Notes](#design-notes-3)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)

> Coordinates provider-owned database transactions with deferred work that must run only after commit.

## Quick Orientation

Use `IAmbientTransaction` when framework code must attach work to the caller's database transaction and run that work only after commit. The substrate is messaging-agnostic: messaging buffers `MediumMessage`, jobs can buffer job rows, and audit log can resolve the active provider transaction through `IAmbientDbTransactionResolver`. Pick one provider package for the underlying transaction shape and add `Headless.AmbientTransactions.EntityFramework` only when the caller begins transactions through EF Core `DatabaseFacade` or `IDbContextTransaction`.

## Agent Instructions

- Depend on `IAmbientTransaction` from `Headless.AmbientTransactions.Abstractions`; do not revive `IOutboxTransaction` or messaging transaction types.
- Register deferred work through `RegisterCommitWork(Func<CancellationToken, ValueTask>)`; do not add public object bags for arbitrary state.
- Use `ICurrentAmbientTransaction` to discover the current transaction; do not introduce a second AsyncLocal transaction accessor in another domain.
- SQL Server ambient transactions intentionally do not drain commit work inside `Commit` / `CommitAsync`; post-commit integrations call `CompleteExternally()`.
- PostgreSQL and in-memory providers drain commit work inline after commit.
- Use `IAmbientDbTransactionResolver` when a raw storage package needs to opportunistically enlist in an existing EF transaction.

## Core Concepts

### Ambient Transaction

An ambient transaction is a provider-owned database transaction plus a current-transaction accessor. Setting `IAmbientTransaction.DbTransaction` to a non-null transaction makes it current; setting it back to `null` clears the current transaction for that async flow.

### Commit Work

Commit work is a typed callback registered on `IAmbientTransaction` with `RegisterCommitWork`. Providers drain it only on commit or external completion, and discard it on rollback.

### External Completion

`CompleteExternally()` represents "the underlying provider transaction has already committed elsewhere; drain now." SQL Server messaging uses this after SqlClient's post-commit diagnostic because draining inline during `Commit` would double-dispatch.

## Choosing a Provider

Pick the provider that owns the underlying transaction object.

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.AmbientTransactions.InMemory` | Tests, local development, or single-instance flows with no durable DB transaction | You need cross-process or database durability | Simple callback coordination only |
| `Headless.AmbientTransactions.PostgreSql` | `IDbTransaction` / `DbTransaction` comes from PostgreSQL | You need SQL Server diagnostic-driven post-commit drain | Commit work drains inline after commit |
| `Headless.AmbientTransactions.SqlServer` | `IDbTransaction` / `DbTransaction` comes from SQL Server | You expect commit work to drain inline from `Commit` | Requires an external post-commit integration for drains |
| `Headless.AmbientTransactions.EntityFramework` | Transactions are started through EF Core Relational | You only use raw `IDbConnection` transactions | Adapter package, not a database provider by itself |

---

## Headless.AmbientTransactions.Abstractions

Defines the provider-agnostic ambient transaction contracts.

### Problem Solved

Gives domains a shared way to coordinate a current database transaction and deferred commit work without depending on messaging, jobs, EF Core, or a specific database provider.

### Key Features

- `IAmbientTransaction` with `DbTransaction`, `AutoCommit`, commit, rollback, and external completion.
- `ICurrentAmbientTransaction` backed by `AsyncLocalCurrentAmbientTransaction`.
- `IAmbientWorkBuffer<TWork>` for domain-specific buffered work.
- `IAmbientDbTransactionResolver` for storage packages that can enlist in a current transaction.

### Design Notes

- The contract exposes typed commit work, not an `Items` bag. Consumers keep domain state in their own buffer and register only the drain callback.
- `AsyncLocalCurrentAmbientTransaction` preserves holder mutation semantics so `DbTransaction = null` clears the current transaction from the same async flow.

### Installation

```bash
dotnet add package Headless.AmbientTransactions.Abstractions
```

### Quick Start

```csharp
services.AddSingleton<ICurrentAmbientTransaction, AsyncLocalCurrentAmbientTransaction>();
```

### Configuration

None.

### Dependencies

None.

### Side Effects

None.

---

## Headless.AmbientTransactions.EntityFramework

Adapts EF Core Relational transactions to `IAmbientTransaction`.

### Problem Solved

Lets callers begin or wrap EF Core `IDbContextTransaction` instances while still using the generic ambient transaction contract.

### Key Features

- `DatabaseFacade.BeginAmbientTransaction(...)` and async overloads.
- `IDbContextTransaction.AsAmbientTransaction(...)`.
- DI setup for EF ambient transactions.

### Design Notes

- This package depends on EF Core Relational only; it does not reference `Headless.Orm.EntityFramework`.

### Installation

```bash
dotnet add package Headless.AmbientTransactions.EntityFramework
```

### Quick Start

```csharp
services.AddEntityFrameworkAmbientTransactions();

var ambient = serviceProvider.GetRequiredService<IAmbientTransaction>();
await dbContext.Database.BeginAmbientTransactionAsync(ambient, autoCommit: false, cancellationToken);
```

### Configuration

None.

### Dependencies

- `Headless.AmbientTransactions.Abstractions`
- Microsoft.EntityFrameworkCore.Relational

### Side Effects

- Registers `ICurrentAmbientTransaction` as a singleton if missing.
- Registers `IAmbientTransaction` as a transient EF ambient transaction.

---

## Headless.AmbientTransactions.InMemory

In-process ambient transaction provider.

### Problem Solved

Provides callback coordination for tests, local development, and single-instance flows that do not have a durable database transaction handle.

### Key Features

- In-memory `IAmbientTransaction` implementation.
- Inline drain after commit.
- Rollback discards registered commit work.

### Installation

```bash
dotnet add package Headless.AmbientTransactions.InMemory
```

### Quick Start

```csharp
services.AddInMemoryAmbientTransactions();
```

### Configuration

None.

### Dependencies

- `Headless.AmbientTransactions.Abstractions`

### Side Effects

- Registers `ICurrentAmbientTransaction` as a singleton if missing.
- Registers `IAmbientTransaction` as a transient in-memory transaction.

---

## Headless.AmbientTransactions.PostgreSql

PostgreSQL ambient transaction provider.

### Problem Solved

Coordinates PostgreSQL `IDbTransaction` / `DbTransaction` commits with deferred work drains.

### Key Features

- PostgreSQL `IAmbientTransaction` implementation.
- `IDbConnection.BeginAmbientTransaction(...)` support through the abstractions package.
- Inline drain after provider commit.

### Design Notes

- Commit drains run inline after PostgreSQL commit. Use this when the caller owns the commit boundary and wants post-commit work to run before the call returns.

### Installation

```bash
dotnet add package Headless.AmbientTransactions.PostgreSql
```

### Quick Start

```csharp
services.AddPostgreSqlAmbientTransactions();
```

### Configuration

None.

### Dependencies

- `Headless.AmbientTransactions.Abstractions`

### Side Effects

- Registers `ICurrentAmbientTransaction` as a singleton if missing.
- Registers `IAmbientTransaction` as a transient PostgreSQL transaction.

---

## Headless.AmbientTransactions.SqlServer

SQL Server ambient transaction provider.

### Problem Solved

Coordinates SQL Server `IDbTransaction` / `DbTransaction` commits with deferred work that may need to drain from an external post-commit signal.

### Key Features

- SQL Server `IAmbientTransaction` implementation.
- `IDbConnection.BeginAmbientTransaction(...)` support through the abstractions package.
- `CompleteExternally()` path for diagnostic-driven drains.

### Design Notes

- `Commit` and `CommitAsync` commit the SQL Server transaction but do not drain commit work inline. Integrations that observe the real provider commit must call `CompleteExternally()` once.

### Installation

```bash
dotnet add package Headless.AmbientTransactions.SqlServer
```

### Quick Start

```csharp
services.AddSqlServerAmbientTransactions();
```

### Configuration

None.

### Dependencies

- `Headless.AmbientTransactions.Abstractions`

### Side Effects

- Registers `ICurrentAmbientTransaction` as a singleton if missing.
- Registers `IAmbientTransaction` as a transient SQL Server transaction.
