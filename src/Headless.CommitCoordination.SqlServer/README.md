# Headless.CommitCoordination.SqlServer

## Problem Solved

Correlates SQL Server commit or rollback signals to attached commit scopes.

## Key Features

- `SqlServerCommitSignalSource`.
- Provider-key registry for detected commit and rollback signals.
- DI extension `AddSqlServerCommitCoordination()`.
- `SqlConnection.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call coordinated transaction for raw ADO (opens the connection if closed; no execution-strategy retry).

## Design Notes

Detected signals remove the scope from the registry before signaling. The returned scope still owns the ambient pop; the signal source owns an async service scope for the out-of-band drain and releases it after the terminal signal completes.

The SqlClient diagnostic is a low-latency commit signal, not the durability mechanism. It depends on `Microsoft.Data.SqlClient` diagnostic event names and payload shapes, which can drift across driver upgrades, so a missed, delayed, or disabled diagnostic must not lose work: the consumer commits a durable row inside the transaction and recovers it by polling, and a faulted drain is left for that recovery path. Prefer `Headless.CommitCoordination.EntityFramework` where EF owns the commit edge — its interceptor signal has no such fragility.

On startup the hosted diagnostic service runs a bounded self-probe when enabled. The probe opens a configured SQL Server connection, commits a throwaway transaction, and verifies that SqlClient emitted a commit diagnostic payload with the expected connection correlation shape. The result is recorded in `SqlServerCommitDiagnosticProbeState`: default `Warn` mode marks the state `Degraded` and logs a warning when the probe cannot run or fails, `Strict` mode fails hosted-service startup, and `Disabled` skips the probe.

## Installation

```bash
dotnet add package Headless.CommitCoordination.SqlServer
```

## Quick Start

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

## Configuration

```csharp
services.AddSqlServerCommitCoordination(options =>
{
    options.DiagnosticProbeMode = CommitProbeMode.Strict;
    options.DiagnosticProbeTimeout = TimeSpan.FromSeconds(5);
    options.DiagnosticProbeConnectionFactory = ct => ValueTask.FromResult(new SqlConnection(connectionString));
});
```

Default mode is `Warn`. Without a `DiagnosticProbeConnectionFactory`, startup continues but the probe state is marked `Degraded` so operators can see that diagnostic compatibility was not proven. Use `Strict` in environments where out-of-band SQL Server detection must be verified at startup.

## Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.Data.SqlClient`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions` — required by the `IHostedService` diagnostic subscription
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Options`

## Side Effects

Registers core commit coordination services, `SqlServerCommitSignalSource`, `ICommitSignalSource`, the SqlClient diagnostic observer/listener, and an `IHostedService` that owns the diagnostic subscription lifetime.
