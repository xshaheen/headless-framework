# Migrating Jobs to versioned contracts

The consuming application owns the EF migration and its deployment. Updating Headless packages does not migrate an existing Jobs database. Stop all Jobs workers, schedulers, seeders, dashboard writers, and cron-definition writers before migration; keep them stopped until every deployed binary uses the new schema. Mixed old/new binaries are unsupported.

This migration captures the execution contract on each row. A materialized cron occurrence owns its function name, contract version, and exact serialized request bytes, so later parent edits cannot change its execution intent. Versions describe payload schemas; they are independent of retries, lease ownership, attempts, and business-key generations.

## Schema changes to generate and review

Generate the migration from the consuming application's actual Jobs `DbContext`, provider, custom entity types, table names, and schema. Review its migration, model snapshot, and deployment SQL before applying it. Do not substitute `EnsureCreated` or a framework startup hook for an upgrade.

| Table | Required changes |
| --- | --- |
| `TimeJobs` | Bound `Function`; add required bounded `ContractVersion`; add nullable `CorrelationId` and `CausationId`. Preserve existing tenant and request columns. |
| `CronJobs` | Same contract and lineage changes. Definitions remain system scope. |
| `CronJobOccurrences` | Add required bounded `Function` and `ContractVersion`, nullable binary `Request`, nullable `CorrelationId` and `CausationId`, and nullable `TenantId` using the existing tenant bound of 200. Occurrences remain system scope (`TenantId = NULL`). |

`Function` allows 1–200 UTF-16 code units and `ContractVersion` allows 1–100. Both compare with ordinal, case-sensitive equality. No Unicode normalization, case folding, trimming, truncation, leading/trailing whitespace, control characters, or invalid surrogate sequences are permitted. An embedded ordinary space is permitted. `Invoice.Send` and `invoice.send`, and versions `V1` and `v1`, are different identities.

Use PostgreSQL `varchar(200)` / `varchar(100)` with `COLLATE "C"`, or SQL Server `nvarchar(200)` / `nvarchar(100)` with `COLLATE Latin1_General_100_BIN2`. PostgreSQL's physical length limit counts Unicode code points; SQL Server's `nvarchar` capacity counts UTF-16 code units. Those physical bounds are not equivalent for supplementary characters. Apply the same .NET validation in preflight and every application writer. For example, 100 rocket characters consume 200 UTF-16 units and fit a name; 101 must fail on both providers even though PostgreSQL `varchar(200)` alone would accept them. SQL Server comparisons pad trailing spaces even under binary collation; rejecting edge whitespace prevents such aliases in valid identities.

The EF converters validate normal new writes. Raw SQL and external writers must run the same validation or provide equivalent reviewed constraints. `NOT NULL` alone does not reject an empty or whitespace-only version. The representative fixture adds nonblank checks to its migration, but those checks do not implement the complete Unicode validation contract. Do not claim arbitrary raw SQL writers have the same guarantees merely because the column is bounded.

## Upgrade sequence

1. Quiesce all readers/writers listed above, take a recoverable backup, and capture counts and identifiers needed to reconcile the migration. A serializable migration transaction does not replace stopping other binaries.
2. Before narrowing any column or changing data, scan every legacy function name, including historical/terminal rows. Validate with the installed contract rules. A public validation entry point is constructing `JobFunctionDescriptor(name, null, string.Empty, JobPriority.Normal, 0, JobContract.LegacyVersion)`; the descriptor validates both identity strings without registering or executing a function. Report offending row identifiers and lengths without logging request bytes. Abort on invalid, oversized, or orphaned rows and resolve them explicitly; do not silently truncate, normalize, delete, or rename them.
3. Add the new columns as nullable inside the consumer's reviewed transactional migration. Preserve the existing binary representation (`bytea` / `varbinary(max)`) for `Request`.
4. Backfill all legacy time jobs and cron definitions to contract version `"1"`. Copy each occurrence's `Function` and exact `Request` bytes from its available parent and set its version to `"1"`. Include completed and failed occurrences, not only pending work. Keep requestless payloads `NULL`; never deserialize and reserialize a backfill.
5. Leave legacy business lineage `NULL` because the database has no evidence for the original cause. Keep existing time-job tenant values and set occurrence tenants to `NULL`. Do not invent lineage from tracing IDs, owner IDs, retries, or timestamps.
6. Verify all occurrence joins resolved, version values are nonblank, row counts match, request lengths/bytes match the captured parent data, and all values pass the logical contract. Then enforce required columns, provider-specific bounds/collations, and reviewed nonblank constraints. Generated SQL may need to drop and recreate affected indexes around SQL Server column/collation changes; preserve the application's index definitions.
7. Remove any temporary backfill default before committing. Prefer explicit `UPDATE` backfill without a default at all. The CLR default `"1"` is an API default, not a database default that conceals an old writer. A raw insert that omits `ContractVersion` must fail after migration.
8. Start only the new binaries, verify registered name/version pairs, and exercise representative scheduling, pickup, retry/recovery, and cron-parent edits against the upgraded database before restoring normal traffic.

**Historical limitation:** legacy occurrences did not own their request bytes. If a parent was edited before this migration, the original payload and identity cannot be reconstructed from the current Jobs tables. The backfill preserves the parent data available during the quiesced migration; it does not recover original historical intent. Resolve affected pending work operationally before resuming it when that distinction matters.

## Downgrade and recovery

Operational recovery is roll-forward. A downgrade preflight scans `TimeJobs`, `CronJobs`, and `CronJobOccurrences`, including terminal rows, for any version other than exact `"1"` (or a missing version). Any such row blocks downgrade. Later business-key migrations also require refusing downgrade whenever keyed metadata exists; this contract migration does not create business keys or assign keys to legacy rows.

Passing the version scan is a necessary compatibility check, not permission to drop snapshot columns: even a v1 occurrence can differ from its later-edited parent, and an old binary would read the parent again. Preserve the new schema and execution tuples while deploying a forward fix. Do not reset a stored version to `"1"` or delete rows merely to make a downgrade check pass. Restore a backup only through the consuming application's recovery procedure, reconciling any effects already performed after that backup.

## Representative migration evidence

The shared [migration conformance fixture](../../tests/Headless.Jobs.EntityFramework.Tests.Harness/JobsContractMigrationConformanceTests.cs) executes native legacy DDL, preflight, `ALTER TABLE`, byte-preserving backfill, and constraints against each provider. Its connection-local temporary tables represent the affected columns of the three legacy tables; unrelated scheduling columns, application indexes, custom entities, EF migration history, and deployment orchestration are deliberately outside that fixture. It is a tested example to adapt, not an application migration to deploy unchanged.

The [PostgreSQL wrapper](../../tests/Headless.Jobs.EntityFramework.PostgreSql.Tests.Integration/PostgreSqlContractMigrationTests.cs) and [SQL Server wrapper](../../tests/Headless.Jobs.EntityFramework.SqlServer.Tests.Integration/SqlServerContractMigrationTests.cs) cover case distinctions, exact bytes and null payloads, supplementary-character boundaries, invalid-data refusal before partial migration, required/nonblank versions without a database default, and non-v1 downgrade refusal in all three tables. These tests complement the full provider runtime suites and the consuming application's own upgrade rehearsal. On ARM, the shared SQL Server fixture uses Azure SQL Edge 1.0.7; that result is not SQL Server 2022 certification.
