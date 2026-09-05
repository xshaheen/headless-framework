# Headless.Jobs.EntityFramework.SqlServer

## Problem Solved

Replaces the portable EF select-and-compare-and-swap pickup path with SQL Server-native atomic claim-and-output operations under scheduler contention.

This package composes `Headless.Jobs.EntityFramework` with SQL Server claims and an application DbContext setup path. EF continues to own job storage, mapping definitions, recovery, the public persistence contract, and transaction-lifecycle primitives; this package owns SQL Server-specific claim execution, including SQL, parameters, and locking behavior.

## Key Features

- `UseSqlServer<TContext>(configureCoordination)` reuses the registered application database and wires Jobs models, native claims, cluster membership, and EF commit coordination.

- **Ordinal contract storage**: Jobs function/version columns use `Latin1_General_100_BIN2` and `nvarchar(200)`/`nvarchar(100)`, matching the shared UTF-16 bounds without case normalization. Runtime validation reject surrounding whitespace so SQL padding does not introduce alternate identities. Native creation snapshots name, version, and request together under the definition lock; existing claims hydrate the stored occurrence tuple.
- Selects claim candidates with `UPDLOCK`, `READPAST`, and `ROWLOCK`, then returns winners from the same update through `OUTPUT inserted...`.
- Bounds set-based root and fallback-occurrence selection to 100 winners per transaction to limit lock footprint and escalation risk; skipped or excess work remains eligible for the next scheduler pass.
- Adds `READCOMMITTEDLOCK` when `READ_COMMITTED_SNAPSHOT` is enabled, as required for `READPAST` under read-committed snapshot isolation.
- Creates cron occurrences atomically against the unique execution-time and cron-job key, deduplicating against every occurrence that **accounts for** the instant under the shared occupied-instant rule — live, terminal, or a status this binary does not recognize. The only row that does not account is one a startup definition reconciliation retired without a replacement (`CronOccurrenceDisposition.ReplacementOwed`), whose fire is still owed. The predicate and its literals are derived from `CronOccurrenceAccounting`, so this SQL cannot drift from the PostgreSQL sibling or the portable EF path.
- Derives and delimits schema, table, and column identifiers from the EF model while parameterizing runtime values.
- Claims the root and two supported descendant levels in one transaction and returns work only after commit.
- Declares the SQL Server comb as the GUID ordering for every SQL Server-backed Jobs row, so `UseSqlServerClaims()` fixes row-id ordering for the whole EF store rather than for the claim strategy alone.

## Design Notes

SQL Server compares `uniqueidentifier` from its **last** bytes first, while UUIDv7 puts its timestamp in the **first** bytes. The framework's unkeyed Version 7 default is therefore effectively random under this backend's ordering and fragments the clustered primary keys on insert; the comb generator puts its sequential component where SQL Server looks first. `UseSqlServerClaims()` declares that ordering once, and both the claim strategy (keyed injection) and the shared occurrence-materialization path (through the option builder) resolve it — materialization is where most occurrence rows are created, so leaving it on the unkeyed default silently defeats the clustering this package exists to protect.

`READPAST` skips row locks, not page locks. Page locking or lock escalation can therefore block competing claimers even with `ROWLOCK`, which is a preference rather than a guarantee. The package does not change `LOCK_ESCALATION`; operators should measure contention, lock memory, and workload behavior before applying database-level changes. SQL Server 2019 or later and Azure SQL are the supported targets.

## Installation

```bash
dotnet add package Headless.Jobs.EntityFramework.SqlServer
```

## Quick Start

```csharp
using Headless.Jobs;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddHeadlessJobs(jobs =>
{
    jobs.UseSqlServer<AppDbContext>(coordination => coordination.ClusterName = "orders");
    jobs.ConfigureJob<OrderReminder>(new JobOptions { RequireAtomicEnlistment = true });
});
```

## Configuration

Register `AppDbContext` first, with a public constructor accepting only `DbContextOptions<AppDbContext>`. The convenience method derives the connection string in a temporary DI scope and rejects an EF context configured for another backend. It adds Jobs mappings in the `jobs` schema while retaining application `OnModelCreating` configuration. It does not create application/Jobs tables: create the fresh application schema from that combined model before starting workers.

This convenience API targets the standard `TimeJobEntity` / `CronJobEntity` store and one fixed application database. Per-request or per-tenant database selection is not supported: singleton cluster membership captures the configured connection once. Provider authentication callbacks and data-source customizations are not copied from EF options.

Cluster identity is explicit. This call selects one SQL Server coordination provider with its default storage options, including coordination-table initialization at startup. Do not also register `AddHeadlessCoordination`: duplicate provider configuration fails. For a separately configured coordination store, custom provider options/data source/authentication callbacks, custom Jobs entities, dedicated Jobs context, schema/pool settings, or a custom model customizer, use the existing `UseEntityFramework(ef => ...)` path and configure those integrations explicitly. The optional `modelConfiguration: ConfigurationType.IgnoreModelCustomizer` argument retains an application-owned model customizer; the application must then add the Jobs mappings itself.

Inside `db.ExecuteCoordinatedTransactionAsync(operation, requestServiceProvider, cancellationToken: ct)`, application writes, same-database durable Messaging publishes, and job schedules share the transaction. Configure Messaging transport/storage separately. `RequireAtomicEnlistment` rejects scheduling outside a compatible transaction; it does not start one. External message delivery and job execution happen after durable acceptance and remain at-least-once.

`UseSqlServerClaims()` has no provider-specific options. Configure the `DbContext`, schema, and pool size through the existing Jobs EF builder. Register exactly one native claim provider. Omitting this call keeps the portable EF optimistic-CAS fallback. The strategy detects `READ_COMMITTED_SNAPSHOT` and adjusts its locking hints.

## Dependencies

- `Headless.Jobs.EntityFramework`
- `Headless.CommitCoordination.EntityFramework`
- `Headless.Coordination.SqlServer`
- `Microsoft.EntityFrameworkCore.SqlServer`
- `Polly.Core`

## Side Effects

- The application-context convenience method attaches the commit interceptor and its startup empty-transaction probe (Warn by default), registers cluster membership and its initializer, and applies Jobs model configuration.

- Replaces the default Jobs EF claim strategy with the SQL Server atomic strategy.
- Executes provider-native, parameterized SQL against the mapped Jobs tables during pickup.
- Does not change lock-escalation settings, scheduler cadence, leases, retry policy, or the public persistence contract.
