# Migrating Jobs storage for keyed scheduling

Keyed scheduling requires a consumer-owned EF migration after the [versioned contract migration](jobs-versioned-contracts.md). Headless maps the columns and constraints; it does not migrate an application's database at startup. Rehearse the generated migration against a disposable copy of the application's schema and representative data before rollout.

Stop workers, scheduler processes, dashboard writers, and application writers for the migration and coordinated package upgrade. Old binaries do not preserve keyed metadata or generation fences and must not run alongside the new binaries. Upgrade all Jobs packages together.

## Custom scheduling and persistence implementations

This release adds required members to `IJobScheduler`, `ITimeJobManager<TTimeJob>`, and `IJobPersistenceProvider<TTimeJob, TCronJob>`. Custom facades and managers must implement keyed scheduling, replacement, and cancellation. Custom providers must implement `ScheduleKeyedTimeJobAsync` and `CancelKeyedTimeJobAsync`, preserving the same atomic create-or-observe, fingerprint comparison, generation fencing, and retention rules across all their ordinary mutation paths. A read-then-insert adapter is not sufficient. Unsupported implementations must reject before effects rather than silently schedule an ordinary job. Rebuild custom implementations against the upgraded abstractions; this is an intentional source and binary compatibility break.

## Storage changes

Add these nullable columns to the application's time-job table without defaults:

| Column | Meaning |
| --- | --- |
| `BusinessKey` | Validated ordinal caller key, at most 200 UTF-16 code units |
| `IntentFingerprint` | SHA-256 digest of the canonical durable intent, 64 lowercase hexadecimal characters |
| `FingerprintAlgorithm` | Recorded interpretation of the fingerprint; current writer uses `v1`, maximum length 16 |
| `Generation` | Positive generation within tenant/system scope, function name, and business key |
| `IsCurrentGeneration` | Current-key marker independent of execution status |

Existing rows must retain null in **all five** columns. Do not infer a business key from a run ID, payload field, description, or legacy correlation ID. Preserve the existing request bytes, execution state, timestamps, and contract version.

Install an all-or-none check: either all five columns are null, or all are nonnull with nonempty key/fingerprint/algorithm strings and a positive generation. Keyed rows must have no parent or continuation run condition. Explicit `IS NOT NULL` tests matter: SQL check constraints accept an unknown result, so a comparison such as `Generation > 0` alone does not reject partial metadata.

Install four unique filtered/partial indexes:

| Scope | Indexed columns | Filter |
| --- | --- | --- |
| System generation history | `Function, BusinessKey, Generation` | Key present, tenant null |
| Tenant generation history | `TenantId, Function, BusinessKey, Generation` | Key present, tenant nonnull |
| System current generation | `Function, BusinessKey` | Key present, tenant null, current true |
| Tenant current generation | `TenantId, Function, BusinessKey` | Key present, tenant nonnull, current true |

Use the actual mapped table, schema, column, and index names from the consumer's generated migration. The separate system indexes make null tenant scope explicit on both PostgreSQL and SQL Server. Scope comparisons must match the runtime's ordinal identity: PostgreSQL `C` collation and SQL Server `Latin1_General_100_BIN2`, with runtime validation rejecting padded or malformed values. Preserve the logical UTF-16 bounds even though PostgreSQL `varchar(n)` counts Unicode characters differently.

The indexes and conditional writes enforce storage ownership; process-local locks alone are insufficient. Raw SQL or custom persistence writers must honor the entire keyed protocol, including validation, exact payload bytes, UTC microsecond due-time normalization, and the recorded fingerprint algorithm. Do not populate metadata by hand or recompute stored fingerprints with a newer serializer.

## Retention and rollout checks

Current and historical keyed rows remain indefinitely after success, failure, cancellation, or replacement. Ordinary update/reset/delete APIs reject keyed records; a mixed deletion containing any keyed row rejects before deleting any member. Include these rows in operational storage sizing and backups. There is no key expiration, forget-key operation, terminal rearm, or compact replacement ledger.

After migration, verify that legacy rows remain wholly unkeyed, each scoped key has one current row, historical generations are unique, and partial metadata cannot be written. Run concurrent identical and conflicting schedules, pending replacement, stale cancellation, and restart observation through the actual application/provider combination. Reuse the same absolute due instant on retries; equal semantic JSON is not equal durable byte intent.

Downgrade after keyed writes is unsupported. Preflight must reject **any** nonnull keyed metadata, including historical rows where `IsCurrentGeneration` is false. Never delete retained generations to make downgrade pass. Preserve the data and roll forward with a repaired application/package version. The versioned-contract downgrade preflight remains necessary too.

## Verification scope

`JobsKeyedMigrationConformanceTests` exercises representative transactional column/index/check installation on connection-local PostgreSQL and SQL Server tables. It checks legacy payload preservation, null metadata, partial/chain rejection, tenant/system and historical/current uniqueness, and downgrade refusal for retained history. Those fixtures deliberately omit unrelated application columns; they do not replace a rehearsal of the consumer's generated migration. Provider scheduling conformance separately verifies the actual EF mappings and runtime operations.
