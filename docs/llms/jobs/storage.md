---
domain: Jobs (Background Jobs)
packages: Jobs.EntityFramework, Jobs.EntityFramework.PostgreSql, Jobs.EntityFramework.SqlServer
---

# Store jobs durably

> Part of [Jobs](../jobs.md). Choose and wire the Entity Framework Core store, its PostgreSQL and SQL Server claim packages, the schema, and keyed-scheduling storage.

## Choose a store

Without a store package, `AddHeadlessJobs` keeps jobs in memory: one process, lost on restart, and cron rows reseeded only by `AddDeployStepsOnStartup`. Use the EF store for anything durable or multi-node.

| Store | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| In-memory (default) | Tests, local development, one process that may lose jobs | Jobs must survive a restart or run on several nodes | No setup, nothing durable |
| `UsePostgreSql<TContext>` | The application database is PostgreSQL 14 or later | The database is not PostgreSQL | `FOR UPDATE SKIP LOCKED` claims let nodes take disjoint due rows in one round trip |
| `UseSqlServer<TContext>` | The application database is SQL Server 2022 or later, or Azure SQL | Page-lock contention or escalation dominates and you cannot address it operationally | `READPAST` skips row locks, but page locks can still block |
| EF optimistic CAS (`UseEntityFramework` with no claim package) | PostgreSQL, SQL Server, or SQLite with low contention, or claim SQL must stay portable | Many workers race for the same due rows, or any job sets `ClusterMaxConcurrency` | No extra package, but losing claimers waste a compare-and-swap round trip |

The PostgreSQL and SQL Server packages extend the EF store. They replace how rows are claimed and fix the row-id ordering for their database. They do not change the scheduler, leases, retries, or the persistence contract.

### Backend support

- **Generic EF claims** support PostgreSQL, SQL Server, and SQLite. Any other EF provider fails at startup with `NotSupportedException`.
- **Runtime cron creation** (`ICronJobManager.AddAsync`, `AddBatchAsync`, and `ScheduleRecurringAsync`) needs the database's current-statement clock, which only PostgreSQL (`clock_timestamp()`) and SQL Server (`SYSUTCDATETIME()`) provide. On any other backend it throws `NotSupportedException`. Time jobs, attribute-seeded cron definitions, and `IJobPersistenceProvider.InsertCronJobsAsync(jobs, ct)` still work there.
- **Run-now and delayed due times** on another backend come from the host's `TimeProvider` instead of the database. On an embedded database such as SQLite that is the store's clock anyway. On a server database the stamp carries the node's skew.
- **Native AOT.** The EF store is not AOT-compatible.

---

## Headless.Jobs.EntityFramework

The durable, multi-node Jobs store on Entity Framework Core. It persists time jobs, cron definitions, occurrences, attempt history, and idempotency reservations, and judges leases with the database clock.

### Setup

```bash
dotnet add package Headless.Jobs.EntityFramework
```

Prefer the application-context path in [Headless.Jobs.EntityFramework.PostgreSql](#headlessjobsentityframeworkpostgresql) or [Headless.Jobs.EntityFramework.SqlServer](#headlessjobsentityframeworksqlserver). Use `UseEntityFramework` directly for a dedicated Jobs context, a separately configured coordination store, custom entities, or custom pool and model settings. On this path, register coordination first:

```csharp
using Headless.Jobs;
using Microsoft.EntityFrameworkCore;

var conn = builder.Configuration.GetConnectionString("DefaultConnection");

// 1. Register Coordination first: it supplies the node identity and dead-node recovery.
builder.Services.AddHeadlessCoordination(c => c.UseSqlServer(conn));

// 2. Register Jobs with the durable store.
builder.Services.AddHeadlessJobs(options =>
{
    options.AddModule<MyApp.JobsModule>();
    options.UseEntityFramework(ef =>
    {
        ef.UseJobsDbContext<JobsDbContext>(db => db.UseSqlServer(conn));
        ef.UseSqlServerClaims(); // or UsePostgreSqlClaims(); omit both for the portable CAS path
        ef.SetDbContextPoolSize(512); // default: 1024
    });
});
```

Create the Jobs tables from the EF model before you start workers or schedule anything. Jobs never creates or alters the schema at run time.

**Verify:** the `jobs-storage-{DbContext full name}` readiness check is `Healthy` (it runs `Database.CanConnectAsync` and writes nothing). Without a coordination provider, startup throws:

```text
InvalidOperationException: The durable Jobs operational store requires a coordination provider.
Register one with AddHeadlessCoordination(...) before AddHeadlessJobs(... UseEntityFramework(...)).
```

### Choose the context

- **`UseJobsDbContext<TDbContext>(dbOptions)`** registers a dedicated `JobsDbContext` that maps only the Jobs tables. A `JobsDbContext` subclass needs a public constructor that takes its `DbContextOptions`.
- **`UseApplicationDbContext<TDbContext>(ConfigurationType)`** shares an existing application `DbContext`. Jobs creates that context outside any request scope, so it needs a public constructor that takes only `DbContextOptions<TDbContext>`. The call throws `InvalidOperationException` otherwise. A `HeadlessDbContext` or `HeadlessIdentityDbContext` works, registered with `AddHeadlessDbContext` or `AddHeadlessDbContextPool`: the context opens a private DI scope for its save pipeline.
- **Scoped context options** (the default of `AddDbContext` and `AddHeadlessDbContext`) are resolved once and kept for the host's lifetime. Every scoped service the options callback resolves, such as a scoped interceptor, is then shared by the Jobs store rather than created per scope.
- **Audit.** Jobs maps its tables with `ExcludeFromAudit()`, so a context with `AuditLogOptions.AuditByDefault = true` writes no audit rows for scheduler bookkeeping.
- **Context factory.** Jobs registers a pooled singleton `IDbContextFactory<TContext>` with `TryAdd`, so a factory you registered first wins. It must be a singleton. A scoped or transient factory fails startup with `InvalidServiceLifetimeException`.

### Configuration

| Setting | Default | Notes |
| --- | --- | --- |
| `options.ConfigureStorage(storage => storage.Schema = "...")` | `"headless"` | The database schema for every Jobs table, including the idempotency reservation table, on every path. `ConfigureStorage(configuration.GetSection("Headless:Jobs:Storage"))` binds it from configuration. The value is validated at startup. Both forms compose, last call wins. See [sql.md](../sql.md#shared-connection-and-schema-for-storage-features) |
| `ef.SetDbContextPoolSize(n)` | 1024 | The Jobs context pool size |
| `scheduler.DeadNodeReconcileInterval` | `null` (half the coordination `DeadRetentionWindow`) | See [execution.md](execution.md#node-identity-and-recovery) |

- **Object names follow the database.** On PostgreSQL every Jobs object is snake_case: tables `time_jobs`, `cron_jobs`, `cron_job_occurrences`, and `time_job_idempotency_reservations`, with columns, keys, and indexes to match. SQL Server and other providers use PascalCase (`TimeJobs`, `ExecutionTime`). Your own `ToTable` and `HasColumnName` mappings still apply, including in the claim SQL.
- **Cron last-run index.** The `(CronJobId, ExecutionTime DESC, Id DESC)` occurrence index (`ix_cron_job_occurrences_cron_job_id_execution_time_id` on PostgreSQL), which also carries `Status` and `ExecutedAt`, hands the dashboard's last-run-per-cron-job read its rows already in order on PostgreSQL and SQL Server, so that read scans the occurrence history once and never sorts it.
- **Attempt history tables** are `TimeJobAttempts` and `CronJobOccurrenceAttempts` (snake_case on PostgreSQL). A model without them keeps working with no attempt history.
- **Cron-expression cache.** The store reuses the host's `ICache` (`Headless.Caching.InMemory`, `.Redis`, or `.Hybrid`) when one is registered. Do not install a Jobs-specific cache package. Without an `ICache`, cron expressions are read from the database. Cache failures fall back to the database.

### Model you manage yourself

With `UseApplicationDbContext<TContext>(ConfigurationType.IgnoreModelCustomizer)`, or a `JobsDbContext` subclass whose model is built without the customizer (such as from a design-time factory), you add the Jobs mappings yourself and call `modelBuilder.FinalizeJobsModel<TTimeJob>(this)` at the end of `OnModelCreating`, after every Jobs and application mapping. It builds the filtered indexes, the keyed indexes, and the check constraint from the final names. It changes only the EF model, never the database.

```csharp
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var collation = Database.ProviderName switch
        {
            "Npgsql.EntityFrameworkCore.PostgreSQL" => "C",
            "Microsoft.EntityFrameworkCore.SqlServer" => "Latin1_General_100_BIN2",
            _ => throw new NotSupportedException("This store does not support keyed Jobs."),
        };
        // snake_case names on PostgreSQL, PascalCase elsewhere; must match the database the model targets.
        var style = HeadlessStorageNaming.ForProvider(Database.ProviderName);
        modelBuilder.ApplyConfiguration(new TimeJobConfigurations<TimeJobEntity>("jobs", style, collation));
        modelBuilder.ApplyConfiguration(new CronJobConfigurations<CronJobEntity>("jobs", style, collation));
        modelBuilder.ApplyConfiguration(new CronJobOccurrenceConfigurations<CronJobEntity>("jobs", style, collation));

        modelBuilder.Entity<TimeJobEntity>().ToTable("scheduled_jobs", "application");
        modelBuilder.Entity<TimeJobEntity>().Property(job => job.BusinessKey).HasColumnName("business_key");
        modelBuilder.FinalizeJobsModel<TimeJobEntity>(this);
    }
}
```

- Pass `HeadlessStorageNaming.ForProvider(Database.ProviderName)` (namespace `Headless.Hosting`) as the naming style, so names match the database.
- Configure the PostgreSQL `C` or SQL Server `Latin1_General_100_BIN2` collation on time-job `Function`, `TenantId`, and `BusinessKey` for keyed scheduling, through the configurations' `contractCollation` argument or a matching model default.
- `FinalizeJobsModel` also maps the attempt tables, reading the cron occurrence type from the model.
- Keyed operations reject a model without finalization, or with a different collation, before touching a key. Ordinary operations keep working.

### Runtime behavior

- **Database-clock leases.** Lease expiry is judged and stamped with the database's statement clock (`clock_timestamp()` on PostgreSQL, `SYSUTCDATETIME()` on SQL Server), inside the statement that uses it. Cross-node clock skew cannot reclaim a healthy job.
- **Execution strategy.** Every store write that opens its own transaction runs inside the context's execution strategy, so a context configured with `EnableRetryOnFailure` works. Under a retrying strategy, a transient fault before the commit replays the whole transaction on a fresh context. A fault once the commit started surfaces unchanged and is never replayed. A write enlisted in your unit of work never retries on its own: your unit owns replay. The due-work peek also runs under the strategy, so a SQL Server deadlock victim on that read is retried when you configured retries.
- **Key locks.** Keyed and idempotency locks take one database command per acquisition, in sorted order for a batch, with one 30-second contention budget and a 60-second command timeout. Contention throws `TimeoutException` without aborting your transaction. Locks already taken stay owned by that transaction until it ends.
- **Chain deletes.** Deleting a time job deletes its whole descendant subtree in one read-committed transaction, deepest first. A foreign-key violation, deadlock, serialization failure, or transient error retries the whole delete up to three times with jittered backoff. Exhausting those retries leaves the tree intact and returns a failed `JobResult`. A commit failure is never retried. Reissuing the delete is safe and returns zero rows if the first commit succeeded.
- **Cron materialization.** One transaction locks the definition, inserts or recognizes the one `Idle` occurrence for the instant, and advances the schedule, so a crash leaves exactly one claimable occurrence and never a skipped tick.
- **CAS fallback.** Without a claim package, overdue roots are processed at most 100 per sweep, in execution-time and id order.

### Contract storage

Every executable row stores its own `(Function, ContractVersion, Request bytes)`. A cron occurrence copies its definition's tuple when it is created and keeps those exact bytes across retries and restarts.

| Entity | Stored contract and context |
| --- | --- |
| `TimeJobEntity` | Required function and version; nullable request bytes, correlation, causation, and tenant |
| `CronJobEntity` | Required function and version; nullable request bytes and lineage. Always system scope |
| `CronJobOccurrenceEntity` | Its own required function and version; nullable request bytes, correlation, causation, and tenant. Always system scope |

- `Function` holds 1 to 200 UTF-16 code units and `ContractVersion` 1 to 100. Both compare ordinally and case-sensitively. Edge whitespace, control characters, and invalid surrogates are rejected without trimming. Embedded spaces are allowed.
- PostgreSQL maps `varchar(200)` and `varchar(100)` with `COLLATE "C"`. SQL Server maps `nvarchar(200)` and `nvarchar(100)` with `COLLATE Latin1_General_100_BIN2`.
- `CorrelationId` and `CausationId` use your model's column collation, so a case-insensitive collation such as SQL Server's default treats `C-42` and `c-42` alike in the database. The dashboard's correlation read and the runs-list correlation filter still return only runs whose id matches character for character. The `CorrelationId` index serves the lookup.
- `JobContract.InitialVersion` (`"1"`) is a CLR default, not a database default. Every writer must supply a version.
- EF converters validate normal writes. Raw SQL and custom writers must apply the same validation. Constructing a `JobFunctionDescriptor` validates a name and version without registering it.

### Keyed scheduling storage

Keyed scheduling ([scheduling.md](scheduling.md#keyed-deadlines)) uses five nullable time-job columns:

| Column | Meaning |
| --- | --- |
| `BusinessKey` | The ordinal caller key, at most 200 UTF-16 code units |
| `IntentFingerprint` | SHA-256 of the durable intent, 64 lowercase hex characters |
| `FingerprintAlgorithm` | The recorded algorithm. The current writer uses `v1`. At most 16 characters |
| `Generation` | A positive generation within tenant or system scope, function, and business key |
| `IsCurrentGeneration` | Marks the current generation, independent of execution status |

- Ordinary jobs have all five null. Only the keyed API sets a business key. A run id, description, correlation id, or argument field never becomes one.
- A check constraint requires all five null, or all five set with a positive generation. Keyed rows have no parent and no run condition.
- Four unique filtered indexes enforce one current generation and unique generation history per key, separately for system and tenant scope.
- Raw SQL and custom writers must follow the whole keyed protocol. Never fill keyed metadata by hand, and never recompute a stored fingerprint with a newer serializer.
- Keyed rows never expire. Include them in storage sizing and backups.

---

## Headless.Jobs.EntityFramework.PostgreSql

Atomic PostgreSQL claims for the EF Jobs store, plus a one-call setup that reuses the application's PostgreSQL `DbContext`.

### Setup

```bash
dotnet add package Headless.Jobs.EntityFramework.PostgreSql
```

```csharp
using Headless.Jobs;
using Microsoft.EntityFrameworkCore;

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHeadlessJobs(jobs =>
{
    jobs.AddModule<MyApp.JobsModule>();
    jobs.UsePostgreSql<AppDbContext>(coordination => coordination.Configure(o => o.ClusterName = "orders"));
    jobs.ConfigureJob<OrderReminderRequest>(new JobOptions { OnNodeDeath = NodeDeathPolicy.MarkFailed });
});
```

`UsePostgreSql<TContext>` adds the Jobs mappings to your context's model (in the Jobs schema, default `headless`), selects native claims, registers PostgreSQL cluster membership, and registers the EF Core unit-of-work provider, so `unit.Jobs` enlists. Create the combined application and Jobs schema from that model before you start workers.

**Verify:** the `jobs-storage-{AppDbContext full name}` readiness check is `Healthy`. A context configured for another database fails at startup.

### Configuration

- Register `AppDbContext` first, with a public constructor that takes only `DbContextOptions<AppDbContext>`. The call rejects a context configured for another backend.
- **Coordination.** The call selects PostgreSQL coordination with its default storage options, including coordination-table initialization at startup. Do not also call `AddHeadlessCoordination`: duplicate provider configuration fails. The callback receives `HeadlessCoordinationSetupBuilder` for `Configure`, `ConfigureStorage`, and `DisableMembershipHeartbeat`. Do not call a `Use*` provider method on it.
- **Connection.** When the context holds an Npgsql data source (one passed to `UseNpgsql`, or one EF builds for a plugin such as NetTopologySuite or `ConfigureDataSource`), membership shares that data source, with its authentication callbacks. Otherwise membership uses the context's connection string.
- **One fixed database.** The call targets the standard `TimeJobEntity` and `CronJobEntity` store and one database. Per-request or per-tenant database selection is not supported.
- **Advanced composition.** For a separate coordination store, custom provider options, custom Jobs entities, a dedicated Jobs context, schema or pool settings, or a custom model customizer, use `UseEntityFramework(ef => ...)` with `ef.UsePostgreSqlClaims()`. `UsePostgreSqlClaims()` has no options. Configure only one claim package. Omitting it keeps the portable CAS path.
- `modelConfiguration: ConfigurationType.IgnoreModelCustomizer` keeps an application-owned model customizer. You then add the Jobs mappings yourself ([model you manage yourself](#model-you-manage-yourself)).

### Runtime behavior

- Claims time jobs and cron occurrences with `UPDATE ... RETURNING` over a `FOR UPDATE SKIP LOCKED` candidate query, at most 100 winners per transaction. Skipped or excess work stays eligible for the next pass.
- Claims a root and its descendants in one transaction, and returns work only after commit.
- Enforces `ClusterMaxConcurrency` inside each claim with `pg_try_advisory_xact_lock` per limited job.
- Retries a claim that failed with a transient fault before its commit (deadlock `40P01`, serialization failure `40001`, or anything Npgsql reports as transient), in a fresh transaction, up to three attempts, and logs event `JobsClaimTransientRetry` (20102) per retry. A commit fault is never retried.
- Keys every Jobs row with UUIDv7.
- Inside `factory.RunAsync(db, ...)`, application writes, `unit.Outbox` publishes on the same database, and `unit.Jobs` schedules share one transaction ([atomic-enqueue.md](atomic-enqueue.md)). Job execution after the commit stays at-least-once.

---

## Headless.Jobs.EntityFramework.SqlServer

Atomic SQL Server claims for the EF Jobs store, plus a one-call setup that reuses the application's SQL Server `DbContext`.

### Setup

```bash
dotnet add package Headless.Jobs.EntityFramework.SqlServer
```

```csharp
using Headless.Jobs;
using Microsoft.EntityFrameworkCore;

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddHeadlessJobs(jobs =>
{
    jobs.AddModule<MyApp.JobsModule>();
    jobs.UseSqlServer<AppDbContext>(coordination => coordination.Configure(o => o.ClusterName = "orders"));
    jobs.ConfigureJob<OrderReminderRequest>(new JobOptions { OnNodeDeath = NodeDeathPolicy.MarkFailed });
});
```

`UseSqlServer<TContext>` adds the Jobs mappings to your context's model (in the Jobs schema, default `headless`), selects native claims, registers SQL Server cluster membership, and registers the EF Core unit-of-work provider, so `unit.Jobs` enlists. Create the combined application and Jobs schema from that model before you start workers.

**Verify:** the `jobs-storage-{AppDbContext full name}` readiness check is `Healthy`. A connection whose credentials no connection string carries fails at startup with an error naming the cause.

### Configuration

- Register `AppDbContext` first, with a public constructor that takes only `DbContextOptions<AppDbContext>`. The call rejects a context configured for another backend.
- **Coordination.** The call selects SQL Server coordination with its default storage options, including coordination-table initialization at startup. Do not also call `AddHeadlessCoordination`: duplicate provider configuration fails. The callback receives `HeadlessCoordinationSetupBuilder` for `Configure`, `ConfigureStorage`, and `DisableMembershipHeartbeat`. Do not call a `Use*` provider method on it.
- **Connection.** Membership uses the context's connection string, checked by `GetReusableConnectionString` ([sql.md](../sql.md#headlesssqlsqlserver)). A connection that authenticates with credentials no connection string carries (an access token, an access-token callback, a `SqlCredential`, or a password SqlClient removed from an opened connection) fails at startup. A token an interceptor attaches while the connection opens is invisible to that check. For such a host, configure coordination explicitly through `UseEntityFramework(ef => ...)`.
- **One fixed database.** The call targets the standard `TimeJobEntity` and `CronJobEntity` store and one database. Per-request or per-tenant database selection is not supported.
- **Advanced composition.** Use `UseEntityFramework(ef => ...)` with `ef.UseSqlServerClaims()` for the same cases as PostgreSQL. `UseSqlServerClaims()` has no options. Configure only one claim package.
- `modelConfiguration: ConfigurationType.IgnoreModelCustomizer` keeps an application-owned model customizer. You then add the Jobs mappings yourself.

### Runtime behavior

- Selects claim candidates with `UPDLOCK`, `READPAST`, `ROWLOCK`, and `READCOMMITTEDLOCK`, and returns winners through `OUTPUT inserted...`, at most 100 per transaction. Claims behave the same with or without `READ_COMMITTED_SNAPSHOT`.
- `READPAST` skips row locks, not page locks, so page locking or lock escalation can still block claimers. The package does not change `LOCK_ESCALATION`. Measure contention and lock memory before changing database settings.
- Claims a root and its descendants in one transaction, and returns work only after commit.
- Enforces `ClusterMaxConcurrency` inside each claim with a transaction-owned `sp_getapplock` per limited job.
- Retries a claim that failed with a transient fault before its commit (deadlock victim 1205, lock timeout 1222, or a connection fault in EF Core's SQL Server retry set), in a fresh transaction, up to three attempts, and logs event `JobsClaimTransientRetry` (20102) per retry. A commit fault is never retried.
- Keys every Jobs row with a sequential SQL Server comb GUID, so clustered primary keys do not fragment.
- Inside `factory.RunAsync(db, ...)`, application writes, `unit.Outbox` publishes on the same database, and `unit.Jobs` schedules share one transaction. On SQL Server without `READ_COMMITTED_SNAPSHOT`, a non-enlisting call on a row the same unit inserted blocks until it times out ([atomic-enqueue.md](atomic-enqueue.md#what-enlists-and-what-does-not)).
