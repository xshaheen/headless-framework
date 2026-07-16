---
title: Storage Initializer Lifecycle & Concurrent-Startup Safety
date: 2026-05-25
last_updated: 2026-06-07
category: best-practices
module: headless-storage
problem_type: best_practice
component: background_job
severity: high
related_components:
  - database
  - service_class
  - testing_framework
tags:
  - storage-initializer
  - hosted-service
  - idempotent-ddl
  - startup-race
  - dispose-order
  - postgres
  - sqlserver
  - log-dedup
applies_when:
  - Writing a new I{Feature}StorageInitializer for Postgres or SqlServer
  - Reviewing concurrent-startup behavior of multiple replicas against one DB
  - Diagnosing startup hangs or duplicate-DDL errors at host boot
  - Handling DB-unreachable or auth-failure during the initializer phase
  - Auditing dispose ordering between bootstrapper and repo-held resources
---

# Storage Initializer Lifecycle & Concurrent-Startup Safety

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

**PostgreSQL** (`PostgreSqlAuditLogStorageInitializer._CreateScript`): each statement is `CREATE … IF NOT EXISTS` and the whole script is preceded by a transaction-scoped advisory lock keyed on `(schema, table)`. The lock serializes racing `CREATE SCHEMA IF NOT EXISTS` calls because PG's `IF NOT EXISTS` check is not transactional with the catalog insert — two concurrent transactions can both pass the check and one fails with `23505`. The initializer also catches `42P06 / 42P07 / 42710 / 23505` to absorb residual races driven by foreign initializers running concurrent DDL.

```csharp
var lockResource = $"headless_audit_init:{options.Schema}.{options.TableName}";
var acquireLock = $"""SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));""";
// ...
catch (PostgresException ex) when (ex.SqlState is "42P06" or "42P07" or "42710" or "23505")
{
    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
}
```

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

### 5. Dispose-path correctness for `HeadlessDbContext`

Once `OwnedScope` is owned by the context, dispose must release it on every path — and never let a secondary scope-dispose exception mask the primary runtime/base exception.

```csharp
public override void Dispose()
{
    var logger = OwnedScope?.ServiceProvider.GetService<ILogger<HeadlessDbContext>>();
    try
    {
        var disposeTask = _runtime.DisposeAsync();
        if (!disposeTask.IsCompletedSuccessfully) disposeTask.AsTask().GetAwaiter().GetResult();
        base.Dispose();
    }
    finally
    {
        try { OwnedScope?.Dispose(); }
        catch (Exception scopeEx) { logger?.LogOwnedScopeDisposeFailed(scopeEx); }
        GC.SuppressFinalize(this);
    }
}

public override async ValueTask DisposeAsync()
{
    var logger = OwnedScope?.ServiceProvider.GetService<ILogger<HeadlessDbContext>>();
    try
    {
        await _runtime.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
    finally
    {
        try
        {
            // Prefer async — MS DI scopes implement IAsyncDisposable (AsyncServiceScope)
            // and may hold async-only-disposable scoped services.
            if (OwnedScope is IAsyncDisposable async) await async.DisposeAsync().ConfigureAwait(false);
            else OwnedScope?.Dispose();
        }
        catch (Exception scopeEx) { logger?.LogOwnedScopeDisposeFailed(scopeEx); }
        GC.SuppressFinalize(this);
    }
}
```

Three pieces matter: the `try/finally` (so scope disposal runs even if `_runtime.DisposeAsync` or `base.Dispose` throws); the inner `try/catch` around the scope dispose (so a secondary failure surfaces at Warning via `LogOwnedScopeDisposeFailed` without masking the primary exception); and preferring `IAsyncDisposable` on the scope in `DisposeAsync`.

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
| EF startup validation gate | `src/Headless.Settings.Storage.EntityFramework/Internal/SettingsEntityValidationStartupGate.cs`, `src/Headless.AuditLog.Storage.EntityFramework/Internal/AuditLogEntityValidationStartupGate.cs` |

## Related

- [Unified Provider Setup Builder Pattern](../architecture-patterns/unified-provider-setup-builder-pattern.md) — sibling doc covering the `Setup{Feature}` / `HeadlessXxxSetupBuilder` / `IStorageOptionsExtension` registration shape that puts these initializers in front of the host
- [Startup pause gating and half-open recovery](../concurrency/startup-pause-gating-and-half-open-recovery.md) — `IHostedLifecycleService.StartingAsync` runs before `IHostedService.StartAsync`; same primitive the EF startup gate uses
- [Messaging keyed-DI lock isolation](../architecture-patterns/messaging-keyed-di-lock-isolation-2026-05-19.md) — applies when multiple features share an initializer host
- [Circuit-breaker transport thread-safety patterns](../concurrency/circuit-breaker-transport-thread-safety-patterns.md) — hosted-service dispose and timer-race prior art for the dispose discipline above
- [Registration must durably establish liveness](../architecture-patterns/coordination-register-establishes-durable-liveness.md) — `Headless.Coordination` membership initializers follow this lifecycle; its concurrent-startup conformance test (boot N initializers via `Task.WhenAll`, assert table/index counts) is the cross-provider template referenced in §4, and it caught the `CREATE INDEX` 1913 second instance noted in §2
