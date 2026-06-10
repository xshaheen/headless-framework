# Headless.CommitCoordination.SqlServer

## Problem Solved

Correlates SQL Server commit or rollback signals to attached commit scopes.

## Key Features

- `SqlServerCommitSignalSource`.
- Provider-key registry for detected commit and rollback signals.
- DI extension `AddSqlServerCommitCoordination()`.

## Design Notes

Detected signals remove the scope from the registry before signaling. The returned scope still owns the ambient pop; the signal source owns an async service scope for the out-of-band drain and releases it after the terminal signal completes.

The SqlClient diagnostic is a low-latency commit signal, not the durability mechanism. It depends on `Microsoft.Data.SqlClient` diagnostic event names and payload shapes, which can drift across driver upgrades, so a missed, delayed, or disabled diagnostic must not lose work: the consumer commits a durable row inside the transaction and recovers it by polling, and a faulted drain is left for that recovery path. Prefer `Headless.CommitCoordination.EntityFramework` where EF owns the commit edge — its interceptor signal has no such fragility.

## Installation

```bash
dotnet add package Headless.CommitCoordination.SqlServer
```

## Quick Start

```csharp
services.AddSqlServerCommitCoordination();
```

## Configuration

None.

## Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.Data.SqlClient`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

Registers core commit coordination services, `SqlServerCommitSignalSource`, `ICommitSignalSource`, the SqlClient diagnostic observer/listener, and an `IHostedService` that owns the diagnostic subscription lifetime.
