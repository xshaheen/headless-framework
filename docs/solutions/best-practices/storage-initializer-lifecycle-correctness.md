---
title: Storage Initializer Lifecycle & Concurrent-Startup Safety
date: 2026-05-25
last_updated: 2026-10-03
module: headless-framework
problem_type: best_practice
component: background_job
severity: high
related_components:
  - database
  - service_class
  - testing_framework
tags: [storage-initializer, hosted-service, idempotent-ddl, startup-race, dispose-order, postgres, sqlserver, advisory-lock]
applies_when:
  - Writing a new I{Feature}StorageInitializer for Postgres or SqlServer, including one whose tables live in the shared headless schema
  - Reviewing concurrent-startup behavior of multiple replicas against one DB
  - Diagnosing startup hangs, duplicate-DDL errors, or DB-unreachable and auth failures during the initializer phase
  - Running CREATE INDEX CONCURRENTLY from an initializer that other replicas wait on
  - Auditing dispose ordering between bootstrapper and repo-held resources
---

# Storage Initializer Lifecycle & Concurrent-Startup Safety

> **Relational DDL no longer uses per-feature initializers.** The raw PostgreSQL and SQL Server providers contribute
> schema steps to one `SchemaRunner`, which owns the per-database lock, the rerun-once rule, and the startup
> lifecycle described in sections 1 and 2 below. See
> [One schema runner](../architecture-patterns/schema-runner-history-lock-and-verify.md). The race analysis in
> section 2 still explains why the runner behaves as it does. Sections 3 to 8 (failure handling, dispose paths,
> log dedup, field limits) still apply as written, and the TCS skeleton lives on in `HostedInitializer`, which the
> runner's initializer and non-relational initializers (Redis scripts, topology) use.

## Context

Each raw provider package in the framework ships a `*StorageInitializer` registered through `AddInitializerHostedService<T>` so the host blocks `Starting` until the schema is ready. These initializers run idempotent DDL and must survive: parallel hosts in a rolling deploy racing the same fresh schema, DB unreachable, auth failure, partial-failure schemas from prior crashed runs, repeat host starts in test rigs, and exceptions on the SaveChanges path that race with `DbContext` disposal.

The contract was hardened iteratively during the storage unification work on branch `xshaheen/refactor-storage-initialization-unification` (PR #354). Two review iterations produced regressions that taught what the lifecycle needs to guarantee; commits `8626a2e78`, `657dfc884`, `3f7572895`, `c439be951`, `b74b602f6`, and `784b48eed` together lock in the patterns below. This doc captures the runtime rules an initializer must honor; see the sibling [Unified Provider Setup Builder Pattern](../architecture-patterns/unified-provider-setup-builder-pattern.md) for the registration shape that puts these in front of the host.

## Guidance

### 1. Initializer skeleton — `IHostedLifecycleService` + `IInitializer` with a TCS promise

Each initializer wraps the DDL run in a `TaskCompletionSource` that callers can await via `WaitForInitializationAsync`. `IsInitialized` is set only after DDL completes successfully.

```csharp
internal sealed class PostgreSqlAuditLogStorageInitializer(...) : IHostedLifecycleService, IInitializer
{
    private TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsInitialized { get; private set; }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        // On a host restart, swap atomically and cancel the previous promise so prior waiters
        // observe OperationCanceledException instead of hanging. On first start, _completion
        // is the field initializer (no prior waiters), so skip the cancel — a fresh TCS is
        // never IsCompleted.
        if (_completion.Task.IsCompleted)
        {
            var previous = Interlocked.Exchange(ref _completion,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            previous.TrySetCanceled(cancellationToken);
        }

        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            IsInitialized = true;
            _completion.TrySetResult();
        }
        catch (Exception ex) { _completion.TrySetException(ex); throw; }
    }

    public async Task WaitForInitializationAsync(CancellationToken ct = default)
        => await _completion.Task.WaitAsync(ct).ConfigureAwait(false);
}
```

The `IsCompleted`-guarded `Interlocked.Exchange` is load-bearing. The earlier (`c439be951`) version unconditionally cancel-then-reassigned, which on first start would cancel the field-initialized TCS that legitimate pre-host waiters might already be awaiting. The `3f7572895` fix replaces it with an atomic swap that only triggers on restart.

### 2. Concurrent-startup race — provider-specific locks + idempotent DDL

**PostgreSQL** (`PostgreSqlAuditLogStorageInitializer._CreateScript`): each statement is `CREATE … IF NOT EXISTS`, and the script takes two transaction-scoped advisory locks before it runs:

1. **The feature lock**, namespaced by feature and keyed on the objects it owns, for example `headless_audit_init:{schema}.{table}` or `headless_fencing_init:{schema}.fencing_leases` on PostgreSQL. It comes first.
2. **The schema-wide lock**, immediately before `CREATE SCHEMA IF NOT EXISTS`. Every PostgreSQL initializer takes it, whatever its feature, and builds it only through `PostgreSqlSchemaInitLock.AcquireStatement(schema)` in `Headless.Sql.PostgreSql`. That method is the single owner of the lock key. Never inline the `hashtextextended(...)` literal: a key that drifts in one feature stops that feature serializing against the others.

```csharp
var lockResource = $"headless_audit_init:{options.Schema}.{options.TableName}";
var acquireLock = $"""SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));""";
var createSchema = $"""
    {PostgreSqlSchemaInitLock.AcquireStatement(options.Schema)}
    CREATE SCHEMA IF NOT EXISTS "{options.Schema}";
    """;
```

PostgreSQL's `IF NOT EXISTS` check is not transactional with the catalog insert. Two concurrent transactions can both pass the check, and the loser fails with `42P06` or `23505`. A per-feature lock serializes replicas of one feature only. Every relational feature now defaults to the one `headless` schema, so Messaging, Fencing, and AuditLog replicas all run `CREATE SCHEMA "headless"` at the same moment, each under a different feature lock.

A failed statement aborts the whole PostgreSQL transaction (`25P02`). Catching `42P06` and rolling back therefore does not absorb the race: it discards every table, index, and sequence the losing feature created in that transaction, the initializer reports success, and the first query fails with `42P01`. The schema-wide lock prevents the collision instead of absorbing it. It is transaction-scoped, so it serializes the features' schema-creating transactions only at boot and releases when each commits or rolls back.

The locks cover Headless initializers only. A schema or object creator outside them still wins the race: a consumer's EF migration (`EnsureSchema` for Jobs or an EF storage variant), a DBA script, or other application code. When it commits first, the initializer's transaction fails with `42P06 / 42P07 / 42710 / 23505` and the rollback discards the feature's DDL, exactly as above.

**Rule: an absorbed race reruns the DDL once in a fresh transaction.** By the time the initializer sees the error, the conflicting creator has committed (the losing insert waits on the catalog unique index until the winner ends), so the rerun's `IF NOT EXISTS` guards pass and it creates what the rollback discarded. Catch only on the first attempt. A second failure is not a race: let it propagate, and never mark the storage initialized without a successful commit. The same rule applies to lazily created objects, such as the DistributedLocks fencing sequence, whose "ensured" flag is set only after the commit.

```csharp
for (var attempt = 1; ; attempt++)
{
    await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

    try
    {
        // run the DDL batch
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return;
    }
    catch (PostgresException ex)
        when (attempt == 1 && ex.SqlState is "42P06" or "42P07" or "42710" or "23505")
    {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        LogSchemaRaceObserved(_logger, ex.SqlState, ex.MessageText);
    }
}
```

The rerun is safe only because every statement in the batch is idempotent: `IF NOT EXISTS` on each `CREATE`, and catalog checks around each `ALTER`. A non-idempotent statement would fail the rerun deterministically and turn the retry into a startup failure.

SQL Server initializers do not need the schema-wide lock. They create the schema in its own guarded block that swallows `2714` and `2759` (how `CREATE SCHEMA` reports the duplicate), and that error does not doom the rest of the batch. Every SQL Server initializer needs that block: another feature can create the shared schema between the `sys.schemas` check and the `CREATE`.

**PostgreSQL with `CREATE INDEX CONCURRENTLY`** (`PostgreSqlStorageInitializer` in Messaging): `CONCURRENTLY` cannot run inside a transaction, so the feature lock must be a session lock held across the transactional DDL and the index builds. Acquire it by polling `pg_try_advisory_lock` with a short delay. Do not block in `pg_advisory_lock`. A blocked statement holds a snapshot open for as long as it waits, and `CREATE INDEX CONCURRENTLY` waits for every snapshot older than the build to finish. The lock holder's build then waits on the waiter, and the waiter waits on the lock holder, so two replicas that boot together deadlock until the DDL timeout. Between polls the waiting connection runs no statement and holds no snapshot. Bound the polling by the DDL timeout, not the OLTP command timeout, because the holder can keep the lock across a multi-minute build. Release the session lock explicitly in a `finally`, even when a build was cancelled, so a pooled connection returns without it.

**SQL Server** (`SqlServerAuditLogStorageInitializer._CreateScript`): `sp_getapplock` (Session scope) guards the script and `sp_releaseapplock` runs on every path. The release in the success path lives at the end of the inner `TRY`; the outer `CATCH` checks `APPLOCK_MODE` and re-releases before re-throwing. Index creation is split into per-index `IF NOT EXISTS` guards so a partial-failure run that committed the table but missed an index self-heals on next start. Each guarded block also catches `2714, 1913, 2759` (object already exists).

```sql
DECLARE @lockResult int;
EXEC @lockResult = sp_getapplock @Resource = N'headless_audit_init:...',
     @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 30000;
IF @lockResult < 0 THROW 50000, N'...', 1;

BEGIN TRY
    {createSchema}
    {createTable}
    {createIndexes}
    {releaseLock}
END TRY
BEGIN CATCH
    IF APPLOCK_MODE('public', N'...', 'Session') <> 'NoLock' {releaseLock}
    THROW;
END CATCH;
```

Without the outer `TRY/CATCH` (the bug fixed in `3f7572895`), an uncaught DDL error during `CREATE INDEX` would leave the Session-scoped applock leaked until the connection physically closed, starving the next replica's `sp_getapplock` call.

> **Second instance (2026-06-07, `Headless.Coordination`, PR #416).** The coordination SqlServer
> membership initializer wrapped `CREATE TABLE` in the defensive `BEGIN TRY/CATCH ... NOT IN (2714, 1913, 2759)`
> but left `CREATE NONCLUSTERED INDEX` *outside* it. Two concurrent initializers both passed the index's
> `IF NOT EXISTS` check and the loser threw error `1913` ("index already exists"). The rule is therefore
> sharper than "guard the table": **every DDL block — table *and* every index — must sit inside the
> swallow-already-exists envelope**, because the `IF NOT EXISTS`/create pair is not atomic for either object
> type. The bug was caught only because a concurrent-startup conformance test was added (see §4); without
> it the race is invisible in single-host CI.

### 3. DB-unreachable and auth-failure handling

Initializers do not swallow infrastructure errors. The async path lets the exception propagate; the host wraps it in `HostFailedToStartException`; `IsInitialized` stays `false` so liveness/readiness checks fail closed. Tests in `*FailureModesTests` assert this:

```csharp
[Fact]
public async Task should_throw_and_keep_initializer_unmarked_when_database_unreachable()
{
    const string unreachable = "Host=127.0.0.1;Port=1;Database=missing;...;Timeout=2";
    using var host = _CreateHost(unreachable);

    await FluentActions.Awaiting(() => host.StartAsync(...))
        .Should().ThrowAsync<Exception>()
        .Where(e => e is NpgsqlException || e.InnerException is NpgsqlException);

    var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
    initializer.IsInitialized.Should().BeFalse();
    await FluentActions.Awaiting(() => initializer.WaitForInitializationAsync(...))
        .Should().ThrowAsync<NpgsqlException>();
}
```

Use a reserved port (1) for unreachable-DB tests so the failure happens at TCP-connect, before the auth handshake. Use a placeholder password (never the real fixture password) so the test does not double as a credential leak vector.

### 4. Concurrent-startup race coverage

`PostgreSqlAuditLogFailureModesTests.should_succeed_when_multiple_hosts_initialize_concurrently_against_same_schema` (commit `8626a2e78`) boots 5 hosts in parallel against a freshly dropped schema and asserts: all initializers report `IsInitialized`, exactly one table exists, and all 5 expected indexes exist. The index assertion was added in `3f7572895` as a regression guard — a swallowed `CREATE INDEX` failure would otherwise pass the table-count check silently.

```csharp
await _DropSchemaAsync("audit_log_pg_concurrent");
var hosts = Enumerable.Range(0, 5).Select(_ => _CreateHost(...)).ToArray();
await Task.WhenAll(hosts.Select(h => h.StartAsync(...)));

hosts.Select(h => h.Services.GetRequiredService<IEnumerable<IInitializer>>().Single().IsInitialized)
    .Should().AllSatisfy(i => i.Should().BeTrue());
(await _CountTablesAsync("audit_log_pg_concurrent", "audit_log")).Should().Be(1);
(await _CountIndexesAsync("audit_log_pg_concurrent", "audit_log")).Should().Be(5);
```

Racing Headless hosts against each other never exercises a creator outside the locks. `PostgreSqlForeignSchemaCreatorTests` in `tests/Headless.Storage.SharedSchema.Tests.Integration` makes that race deterministic: a foreign transaction runs `CREATE SCHEMA "headless"` and stays open, the initializer's `CREATE SCHEMA IF NOT EXISTS` misses the uncommitted row and blocks on the `pg_namespace` unique index, and the test commits the foreign transaction only once `pg_stat_activity` shows the initializer waiting on a lock. The initializer then always fails with `23505`. Poll `pg_stat_activity` from its own autocommit connection: its values are snapshotted once per transaction, so polling inside the foreign transaction never sees the wait.

### 5. Dispose-path correctness for `HeadlessDbContext`

A context that owns a scope (one the factory created for it, or a private scope it opened on first use) must release it on every dispose path, and a secondary scope-dispose exception must never mask the primary base-dispose exception. A pooled context adds an ordering rule: disposing the base context returns the instance to the pool, where another caller may lease and bind it at once, so the lease state is cleared *before* `base.Dispose()` and only the captured scope is disposed afterwards.

```csharp
public override async ValueTask DisposeAsync()
{
    // Clears the binding and returns the scope this lease owned, if any, before the instance can be re-leased.
    var ownedScope = _runtime.Release();

    try
    {
        await base.DisposeAsync().ConfigureAwait(false);
    }
    finally
    {
        // Resolves the logger first, prefers IAsyncDisposable on the scope, and logs a scope-dispose failure at
        // Warning (LogOwnedScopeDisposeFailed) instead of throwing it.
        await HeadlessDbContextRuntime.DisposeScopeAsync(ownedScope, GetType()).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
```

Three pieces matter: releasing the lease state before the base dispose (so a pooled instance never carries one scope into the next lease); the `try/finally` (so scope disposal runs even if `base.Dispose` throws); and the guarded scope dispose (so a secondary failure surfaces at Warning without masking the primary exception, preferring `IAsyncDisposable` because MS DI scopes may hold async-only-disposable services). The synchronous `Dispose` mirrors it with `DisposeScope`.

### 6. Provider-mismatch log dedup — once per shape

The provider-mismatch warning in `PostgreSqlAuditLogStore` / `SqlServerAuditLogStore` was iterated three times during review:

- v1 (`156fef5d0`): instance flag — fired once per request because the store is scoped.
- v2 (`c439be951`): static `int` flag with `Interlocked.Exchange` — fired once per process, but silently swallowed *unrelated* mismatches in multi-tenant or multi-store deployments after the first warning.
- v3 (`3f7572895`, final): `ConcurrentDictionary` keyed on connection type name — each distinct mismatch shape logs once.

```csharp
private static readonly ConcurrentDictionary<string, byte> _WarnedConnectionTypes
    = new(StringComparer.Ordinal);

var connectionTypeName = connection.GetType().FullName ?? "(unknown)";
if (_WarnedConnectionTypes.TryAdd(connectionTypeName, 0))
    LogProviderMismatch(_logger, connectionTypeName);
```

The same lesson applies to any init-time log that should fire once per "shape" rather than once per instance or once per process. Once-per-process is too coarse (silently hides multi-store misconfigs); once-per-instance is too fine (floods on scoped lifetime).

### 7. Test patterns each raw storage provider should ship

Templates live under `tests/Headless.AuditLog.Storage.PostgreSql.Tests.Integration/`:

- **`*FailureModesTests`** — DB-unreachable, auth-failure, and concurrent-startup race (with `_CountIndexesAsync` regression guard).
- **`*AtomicityTests`** — ambient-transaction enrollment commits, ambient-transaction rollback drops the audit row, accessor-null fallback to own connection, and provider-mismatch fallback via a fake `DbConnection`. The mismatch test stubs a non-driver `DbConnection` and asserts the row still persists on the standalone path. Drop the schema at the start of each test (`_DropSchemaAsync`) so the initializer re-runs against a clean slate.
- **`HeadlessDbContextFactoryTests`** (commit `784b48eed`) — resolve from root provider; scope is disposed when factory-created context is disposed; independent contexts have independent scopes; and **scope is disposed when the DbContext ctor throws** (the leak guard for the factory's catch path).

Narrow `ThrowAsync<Exception>` to driver-specific exception types (`NpgsqlException`, `PostgresException`, `SqlException`) so a spurious infra hiccup does not silently pass the test.

### 8. Hoist field limits to prevent DDL/runtime drift

Column-length constants live in a single `AuditLogFieldLimits` class consumed by both DDL builders (raw initializers + EF entity configuration) and runtime truncation (writers). This prevents drift where the DDL says `nvarchar(128)` but runtime truncates at 256.

```csharp
internal static class AuditLogFieldLimits
{
    public const int UserId = 128;
    // ...
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Truncate(string? value, int maxLength) =>
        value is { Length: var len } && len > maxLength ? value[..maxLength] : value;
}
```

## Why This Matters

- **Rolling deploys are the default.** Multi-replica services boot in parallel; without per-provider advisory locks plus idempotent DDL, the first deploy after a schema reset is non-deterministic. PG `23505` and SqlServer `1205` deadlocks are the real-world failure modes.
- **Features share one schema.** Per-feature locks do not serialize two features creating the same schema, and on PostgreSQL the loser's rollback silently discards its DDL. The schema-wide lock from `PostgreSqlSchemaInitLock.AcquireStatement(schema)` is mandatory for every PostgreSQL initializer, and because creators outside the lock (EF migrations) can still win, an absorbed race reruns the DDL once instead of reporting success.
- **Blocking lock waits and `CONCURRENTLY` do not mix.** A waiter blocked in `pg_advisory_lock` holds the snapshot that the holder's `CREATE INDEX CONCURRENTLY` waits for. Poll `pg_try_advisory_lock` instead.
- **Schema leftover from a crashed init must self-heal.** Per-statement `IF NOT EXISTS` guards mean a host that committed the table but crashed before the indexes gets the indexes on next start without operator intervention.
- **Lock release on the failure path is mandatory for SqlServer.** Session-scoped applocks survive past the throw and starve the next replica until the connection is physically reset by the pool. The outer `TRY/CATCH` with `APPLOCK_MODE` guard is non-negotiable.
- **TCS replacement under restart is subtle.** Naive cancel-then-reassign breaks pre-host waiters on first start; naive "just reassign" leaks waiters on restart. The `IsCompleted`-gated `Interlocked.Exchange` is the correct shape.
- **Dispose paths must not mask the primary exception.** Operators need to see the original `_runtime.DisposeAsync` exception, not a downstream `ObjectDisposedException` from the scope.
- **Log dedup granularity matters.** Once-per-shape (keyed on `connection.GetType().FullName`) catches misconfigurations that once-per-process silently swallows after the first warning.

## When to Apply

Apply when writing any storage initializer that:

- Runs idempotent DDL against a relational database at host start
- Can race with other replicas during rolling deploys or in horizontally scaled hosts
- Exposes a `WaitForInitializationAsync` promise to dependents

The same TCS/race/dedup discipline transfers to other startup-time initializers (cache primers, schema migrators, topic creators). The provider-specific lock primitive changes (`pg_advisory_xact_lock` vs `sp_getapplock` vs Redis `SETNX` vs Kafka topic creation idempotency), but the lifecycle shape is identical.

## Examples

| Concern | Source file (branch `xshaheen/refactor-storage-initialization-unification`) |
| --- | --- |
| PG initializer + race lock | `src/Headless.AuditLog.Storage.PostgreSql/PostgreSqlAuditLogStorageInitializer.cs` |
| PG polled session lock around `CREATE INDEX CONCURRENTLY` | `src/Headless.Messaging.Storage.PostgreSql/PostgreSqlStorageInitializer.cs` |
| Every feature initializing one schema concurrently | `tests/Headless.Storage.SharedSchema.Tests.Integration/` |
| Schema created by a foreign transaction first | `tests/Headless.Storage.SharedSchema.Tests.Integration/PostgreSqlForeignSchemaCreatorTests.cs` |
| Schema-wide PostgreSQL lock (single owner) | `src/Headless.Sql.PostgreSql/PostgreSqlSchemaInitLock.cs` |
| SqlServer initializer + applock | `src/Headless.AuditLog.Storage.SqlServer/SqlServerAuditLogStorageInitializer.cs` |
| Settings raw PG initializer | `src/Headless.Settings.Storage.PostgreSql/PostgreSqlSettingsStorageInitializer.cs` |
| Features raw PG initializer | `src/Headless.Features.Storage.PostgreSql/PostgreSqlFeaturesStorageInitializer.cs` |
| Dispose path | `src/Headless.EntityFramework/Contexts/HeadlessDbContext.cs` |
| Factory + scope ownership | `src/Headless.EntityFramework/Contexts/HeadlessDbContextFactory.cs` |
| Ambient transaction abstraction | `src/Headless.AuditLog.Abstractions/IAmbientDbTransactionAccessor.cs` |
| Provider-mismatch dedup | `src/Headless.AuditLog.Storage.PostgreSql/PostgreSqlAuditLogStore.cs` |
| Field-limit hoist | `src/Headless.AuditLog.Abstractions/AuditLogFieldLimits.cs` |
| Failure-mode + race tests | `tests/Headless.AuditLog.Storage.PostgreSql.Tests.Integration/PostgreSqlAuditLogFailureModesTests.cs` |
| Atomicity + provider-mismatch tests | `tests/Headless.AuditLog.Storage.PostgreSql.Tests.Integration/PostgreSqlAuditLogAtomicityTests.cs` |
| Factory + ctor-throws tests | `tests/Headless.EntityFramework.Tests.Integration/HeadlessDbContextFactoryTests.cs` |
| EF startup validator | `src/Headless.Settings.Storage.EntityFramework/Internal/SettingsEntityStartupValidator.cs`, `src/Headless.AuditLog.Storage.EntityFramework/Internal/AuditLogEntityStartupValidator.cs` |

## Related

- [Unified Provider Setup Builder Pattern](../architecture-patterns/unified-provider-setup-builder-pattern.md) — sibling doc covering the `Setup{Feature}` / `HeadlessXxxSetupBuilder` / `IStorageOptionsExtension` registration shape that puts these initializers in front of the host
- [Startup pause gating and half-open recovery](../concurrency/startup-pause-gating-and-half-open-recovery.md) — `IHostedLifecycleService.StartingAsync` runs before `IHostedService.StartAsync`; same primitive the EF startup gate uses
- [Messaging keyed-DI lock isolation](../architecture-patterns/messaging-keyed-di-lock-isolation.md) — applies when multiple features share an initializer host
- [Circuit-breaker transport thread-safety patterns](../concurrency/circuit-breaker-transport-thread-safety-patterns.md) — hosted-service dispose and timer-race prior art for the dispose discipline above
- [Registration must durably establish liveness](../architecture-patterns/coordination-register-establishes-durable-liveness.md) — `Headless.Coordination` membership initializers follow this lifecycle; its concurrent-startup conformance test (boot N initializers via `Task.WhenAll`, assert table/index counts) is the cross-provider template referenced in §4, and it caught the `CREATE INDEX` 1913 second instance noted in §2
