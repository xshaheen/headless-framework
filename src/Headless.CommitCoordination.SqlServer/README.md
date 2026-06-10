# Headless.CommitCoordination.SqlServer

## Problem Solved

Correlates SQL Server commit or rollback signals to attached commit scopes.

## Key Features

- `SqlServerCommitSignalSource`.
- Provider-key registry for detected commit and rollback signals.
- DI extension `AddSqlServerCommitCoordination()`.

## Design Notes

Detected signals remove the scope from the registry before signaling. The returned scope still owns the ambient pop; the signal source owns an async service scope for the out-of-band drain and releases it after the terminal signal completes.

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
