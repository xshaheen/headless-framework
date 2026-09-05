# Keyed Jobs storage

Initialize the application's Jobs database from the current EF model before starting writers or workers. The [contract storage guide](jobs-versioned-contracts.md) describes executable name/version/payload identity. Keyed scheduling is supported by the in-memory provider and the PostgreSQL/SQL Server relational providers.

## Provider obligations

`IJobPersistenceProvider`, `ITimeJobManager`, and `IJobScheduler` expose keyed create, observe, replace, and generation-fenced cancellation. A persistence implementation must enforce atomic key arbitration, generation fencing, and retention across every mutation path. A read-then-insert adapter is insufficient. Unsupported implementations reject before effects.

## Time-job metadata

| Nullable column | Meaning |
| --- | --- |
| `BusinessKey` | Ordinal caller key, at most 200 UTF-16 code units |
| `IntentFingerprint` | SHA-256 of canonical durable intent, 64 lowercase hexadecimal characters |
| `FingerprintAlgorithm` | Recorded algorithm; current writer uses `v1`, maximum length 16 |
| `Generation` | Positive generation within tenant/system scope, function, and business key |
| `IsCurrentGeneration` | Current-key marker independent of execution status |

Ordinary jobs have all five values null. Only the keyed scheduling API establishes a business key; a run ID, description, correlation ID, or payload field never implicitly becomes one.

Install an all-or-none check: either all five columns are null, or all are nonnull with nonempty key/fingerprint/algorithm strings and a positive generation. Keyed rows must have no parent or continuation run condition. Explicit `IS NOT NULL` tests matter: SQL check constraints accept an unknown result, so a comparison such as `Generation > 0` alone does not reject partial metadata.

Install four unique filtered/partial indexes:

| Scope | Indexed columns | Filter |
| --- | --- | --- |
| System generation history | `Function, BusinessKey, Generation` | Key present, tenant null |
| Tenant generation history | `TenantId, Function, BusinessKey, Generation` | Key present, tenant nonnull |
| System current generation | `Function, BusinessKey` | Key present, tenant null, current true |
| Tenant current generation | `TenantId, Function, BusinessKey` | Key present, tenant nonnull, current true |

Create the schema using the application's actual EF model, including its table, schema, column, and index names. The separate system indexes make null tenant scope explicit on both PostgreSQL and SQL Server. Scope comparisons must match the runtime's ordinal identity: PostgreSQL `C` collation and SQL Server `Latin1_General_100_BIN2`, with runtime validation rejecting padded or malformed values. Preserve the logical UTF-16 bounds even though PostgreSQL `varchar(n)` counts Unicode characters differently.

When using `UseApplicationDbContext<TContext>(ConfigurationType.IgnoreModelCustomizer)`, configure those collations explicitly in the consumer model. For example, in `OnModelCreating`:

```csharp
var collation = Database.ProviderName switch
{
    "Npgsql.EntityFrameworkCore.PostgreSQL" => "C",
    "Microsoft.EntityFrameworkCore.SqlServer" => "Latin1_General_100_BIN2",
    _ => throw new NotSupportedException("This store does not support keyed Jobs."),
};
modelBuilder.ApplyConfiguration(new TimeJobConfigurations<TimeJobEntity>("jobs", collation));
modelBuilder.ApplyConfiguration(new CronJobConfigurations<CronJobEntity>("jobs", collation));
modelBuilder.ApplyConfiguration(new CronJobOccurrenceConfigurations<CronJobEntity>("jobs", collation));

modelBuilder.Entity<TimeJobEntity>().ToTable("scheduled_jobs", "application");
modelBuilder.Entity<TimeJobEntity>().Property(job => job.BusinessKey).HasColumnName("business_key");
modelBuilder.FinalizeJobsModel<TimeJobEntity>(this);
```

Call `FinalizeJobsModel<TTimeJob>(this)` at the end of `OnModelCreating`, after all consumer table and column overrides. It builds the four keyed indexes and metadata check constraint from the final mapped names with the provider's identifier quoting and Boolean literal. The built-in Jobs model customizer finalizes automatically. An unfinalized consumer model rejects keyed scheduling and cancellation with a diagnostic; ordinary unkeyed operations remain available. Finalization configures the EF model only and does not create or alter the database.

A matching explicit model-default collation is also supported. Keyed scheduling and cancellation validate the finalized model's function, tenant, and business-key column collations before accessing a key. Missing or different collations reject with a diagnostic; the provider does not alter consumer mappings or infer the database default. This validates model configuration; the database must be initialized from that same model. Ordinary unkeyed operations remain available.

The indexes and conditional writes enforce storage ownership; process-local locks alone are insufficient. Raw SQL or custom persistence writers must honor the entire keyed protocol, including validation, exact payload bytes, UTC microsecond due-time normalization, and the recorded fingerprint algorithm. Do not populate metadata by hand or recompute stored fingerprints with a newer serializer.

## Retention and verification

Current and historical keyed rows remain indefinitely after success, failure, cancellation, or replacement. Ordinary update/reset/delete APIs reject keyed records; a mixed deletion containing any keyed row rejects before deleting any member. Ordinary add/update operations, including coordinated writes and detached entities populated through consumer EF APIs, also reject attachment to a retained keyed parent before batch effects. Keyed rows remain standalone. Include these rows in operational storage sizing and backups. There is no key expiration, forget-key operation, terminal rearm, or compact replacement ledger.

The shared provider conformance suite initializes the actual EF mappings, checks all-or-none metadata and chain rejection through direct database writes, and verifies current/history uniqueness in tenant and system scopes. It separately exercises concurrent matching/conflicting schedules, replacement, stale cancellation, ordinary mutation rejection, and restart observation.

Reuse the same absolute due instant when retrying a scheduling intent. Equal semantic JSON is not equal durable byte intent. Unknown fingerprint algorithms fail explicitly; existing fingerprints are never recomputed using a new serializer.
