---
title: "One schema runner: per-database lock, history table, verify mode, and deploy scripts"
date: 2026-09-30
last_updated: 2026-09-30
category: architecture-patterns
module: headless-hosting
problem_type: design_pattern
component: database
severity: high
tags:
  - schema-runner
  - storage-initializer
  - ddl
  - schema-history
  - advisory-lock
  - sp-getapplock
  - verify-mode
  - deploy-script
  - postgres
  - sqlserver
related_components:
  - Headless.Hosting
  - Headless.Sql.PostgreSql
  - Headless.Sql.SqlServer
applies_when:
  - Adding or changing a raw PostgreSQL or SQL Server storage provider's tables, indexes, or sequences
  - Changing a feature's DDL after it was released
  - Writing a test fixture that resets a feature's tables
  - Diagnosing a startup failure that names a schema step, a checksum, or headless_schema_history
---

# One schema runner: per-database lock, history table, verify mode, and deploy scripts

## Context

Every raw relational provider used to ship a `*StorageInitializer` that re-implemented one DDL-race protocol: a
per-feature advisory lock or applock, the schema-wide lock, rerun-once on an absorbed race, `CREATE INDEX
CONCURRENTLY` polling, and `TaskCompletionSource` restart semantics. Twenty-two files carried copies of it, and
the per-feature locks were the root of the shared-`CREATE SCHEMA` race documented in
[Storage initializer lifecycle](../best-practices/storage-initializer-lifecycle-correctness.md).

`SchemaRunner` (`Headless.Hosting.Initialization.Schema`) replaces them. A feature contributes ordered, idempotent
`SchemaStep`s through a `SchemaContribution`. The runner applies every contribution that reaches one database in
one pass, under one session lock, and records each step in `headless_schema_history`. Prior art: Flyway's history
table and lock, sqitch's deploy/verify split, and per-dialect build-time scripts in the style of
NServiceBus.Persistence.Sql. The patterns were adapted and no code was copied.

## Guidance

### Grouping and locking

- Contributions group by dialect plus the database identity the dialect derives from an **unopened** connection
  (host, port, and database; server and catalog). Features that each build their own connection to one database
  still share one group, one lock, and one pass. Grouping by the connection-factory delegate was the first attempt,
  and it gave every feature its own pass, which defeats the design.
- The lock is session-scoped and keyed `headless_schema_runner:{identity}`. It is polled
  (`pg_try_advisory_lock`, `sp_getapplock @LockTimeout = 0`) with a 100 ms delay, never taken by blocking. A
  blocking waiter holds a snapshot open that a `CREATE INDEX CONCURRENTLY` in the holder would wait on. The release
  runs in a `finally`.
- Replicas never race each other under the lock. The rerun-once rule remains for creators outside it, such as a
  consumer's EF migration. On PostgreSQL that race is deterministic: a foreign uncommitted `CREATE SCHEMA` makes the
  runner's `CREATE SCHEMA IF NOT EXISTS` wait on `pg_namespace`, and the commit fails it with `23505`. The
  conformance suite forces the race by polling `pg_blocking_pids` before committing.
- The PostgreSQL history DDL still takes `PostgreSqlSchemaInitLock` before `CREATE SCHEMA`, so the runner serializes
  against any code that still creates the shared schema under that lock.

### History

- A step's history row is written after its DDL commits, in its own statement, and the insert is idempotent. A
  history row therefore exists only for committed DDL. A crash between the two leaves objects without a row, and
  the next run re-applies the idempotent step.
- **Warm path.** Before locking, the runner reads the history. If nothing registered is missing, it returns with no
  lock and no DDL. That changed the measured warm cost from slower than the legacy protocol on SQL Server (3.68 ms
  against 3.23 ms for three features) to one query per schema.
- **A recorded step never runs again,** even when its checksum changed. Re-running edited DDL against objects the
  old DDL created is the drift the checksum exists to catch.
- The checksum is SHA-256 of the step SQL with CRLF normalized (`SchemaRunnerChecksum.ForStep`), so a Windows
  checkout does not change it.

### Identity of a feature's history

`SchemaContribution.FeatureId(feature, (configured, default), …)` appends every non-default configurable object
name. Under the bare id, two hosts that name a table differently in one schema record the same `(feature, version)`
with different SQL. The second host reads a checksum change and never creates its own table. Options that change
only column types (AuditLog's `JsonColumnType`) stay out of the id on purpose. Changing them after deployment fails
startup with a checksum mismatch, where the old `CREATE TABLE IF NOT EXISTS` silently ignored the change.

### Startup modes and mismatch kinds

| Kind | Meaning | Apply mode | Verify mode |
|---|---|---|---|
| `Missing` | registered step, no row | applied | fails startup |
| `Checksum` | row's checksum differs from the code | fails startup | fails startup |
| `Unknown` | row for a registered feature, version this host does not know | logged | logged |

`Unknown` must not be fatal. An older replica starting after a newer one applied a later step is the normal rolling
deploy. History rows of features a host does not register are ignored entirely, so hosts with different feature
sets can share a schema.

### Deploy-time scripts

`ExportScript(dialect)` is a public API, not a `dotnet` tool. A tool would have to load and run the application's
own registrations, EF-design-time style, to know which features, options, and names apply. The API receives exactly
the service provider the application builds, and CI calls it from a test or a small entry point. The script is
deterministic (it has no timestamp), guarded per statement, and inserts the same history rows as the runner, so it
can be committed, diffed, run twice, and then accepted by verify mode. SQL Server batches are separated with `GO`.

## Traps

- **The runner trusts its history.** The old initializers re-ran all their DDL on every start, so a table dropped
  by hand came back. Now it does not: delete the feature's history rows, or drop the schema. Test fixtures that
  reset by dropping a feature's tables must drop `headless_schema_history` too. On SQL Server this bites twice,
  because the history table's default constraint also blocks `DROP SCHEMA`. A reused Testcontainers container
  whose previous run died between the two drops starts the next run with history but no tables, and the tests
  that run before the first reset fail with "Invalid object name".
- **Exact object-set assertions** gain `headless_schema_history` in every managed schema.
- **Failures are wrapped.** A connection or step failure is a `SchemaRunnerException` that names the features or
  step, with the driver exception as `InnerException`.

## Examples

| Concern | Where |
|---|---|
| Runner, grouping, warm path, lock polling | `src/Headless.Hosting/Initialization/Schema/SchemaRunner.cs` |
| Contribution and identity rule | `src/Headless.Hosting/Initialization/Schema/SchemaStep.cs` |
| Dialects | `src/Headless.Sql.PostgreSql/PostgreSqlSchemaDialect.cs`, `src/Headless.Sql.SqlServer/SqlServerSchemaDialect.cs` |
| A contribution with a repair step | `src/Headless.Coordination.PostgreSql/PostgreSqlMembershipSchemaContribution.cs` |
| Concurrency, foreign creator, verify, checksum, export conformance | `tests/Headless.SchemaRunner.Tests.Harness/SchemaRunnerConformanceTests.cs` |

## Related

- [Storage initializer lifecycle](../best-practices/storage-initializer-lifecycle-correctness.md): the protocol this
  replaces, and the race analysis the runner still relies on.
- [Provider setup and options](../conventions/provider-setup-and-options.md#schema-contributions-not-initializers):
  the rules a new provider follows.
- Consumer contract: [docs/llms/sql.md](../../llms/sql.md#schema-runner-apply-verify-and-deploy-time-scripts).
