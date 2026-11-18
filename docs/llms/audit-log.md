---
domain: Audit Log
packages: AuditLog.Abstractions, AuditLog, AuditLog.Storage.EntityFramework, AuditLog.Storage.PostgreSql, AuditLog.Storage.PostgreSql.EntityFramework, AuditLog.Storage.SqlServer, AuditLog.Storage.SqlServer.EntityFramework
---

# Audit Log

> Property-level audit rows for EF Core entity changes, plus explicit business events (PII reveals, cross-tenant access, denied requests), stored through EF Core in the saving transaction or through raw PostgreSQL or SQL Server storage.

## Orientation

Install `Headless.AuditLog` and exactly one storage package:

| Package | Use when |
|---|---|
| `Headless.AuditLog.Abstractions` | You need only the contracts, with no DI setup. Every other package references it. |
| `Headless.AuditLog` | Always. It holds `AddHeadlessAuditLog`, the options, and the storage options. |
| `Headless.AuditLog.Storage.PostgreSql.EntityFramework` | You use EF Core on PostgreSQL. It adds migration helpers for an append-only or partitioned table. |
| `Headless.AuditLog.Storage.SqlServer.EntityFramework` | You use EF Core on SQL Server. It adds the migration helper for an append-only table. |
| `Headless.AuditLog.Storage.EntityFramework` | You use EF Core on any other database. |
| `Headless.AuditLog.Storage.PostgreSql` | You want no EF dependency for audit storage, on PostgreSQL. |
| `Headless.AuditLog.Storage.SqlServer` | You want no EF dependency for audit storage, on SQL Server. |

Inject `IAuditLog<TContext>`, `IAuditLogWriter<TContext>`, and `IReadAuditLog<TContext>`. Never reference a provider type. Automatic capture of entity changes comes from `Headless.EntityFramework` (a `HeadlessDbContext`) with any of the storage packages. To audit authorization denials in an API, call `services.AddHeadlessAuthorizationDenialAudit<TContext>()` from `Headless.Api`.

## Agent Rules

- Configure capture in the EF model, not on the entity class: `modelBuilder.Entity<TEntity>().IsAudited()` opts in, `ExcludeFromAudit()` opts an entity or property out, and `IsAuditSensitive(...)` marks a property sensitive. The methods come from `Headless.EntityFramework.Primitives`, which `Headless.EntityFramework` references.
- Entity policy is tri-state. Explicit `IsAudited()` or `ExcludeFromAudit()` wins. An unconfigured entity follows `AuditLogOptions.AuditByDefault` (default `false`). An owned type follows its root owner. A derived type follows the nearest configured base unless it is configured itself.
- Property policy runs in this order: framework default exclusions, explicit `ExcludeFromAudit()`, then the `PropertyFilter` veto, and only then sensitive handling. A strategy passed to `IsAuditSensitive(...)` overrides `AuditLogOptions.SensitiveDataStrategy`.
- Properties of an EF complex type (`ComplexProperty(...)`, nested ones included) are recorded on the owning entity's row under a dotted path, such as `Shipping.City` or `Shipping.Geo.Latitude`. Configure them with `ExcludeFromAudit()` and `IsAuditSensitive(...)` on the `ComplexTypePropertyBuilder`, or on the `ComplexPropertyBuilder` for the whole complex property (`customer.ComplexProperty(c => c.Contact).ExcludeFromAudit()`). Any excluded ancestor excludes a member. The nearest sensitive marking, from the member outward, sets its strategy. `DefaultExcludedProperties` and `PropertyFilter` see the dotted path. Complex collections are not captured.
- Register once: `services.AddHeadlessAuditLog(setup => setup.Use...)` with exactly one `Use...` call. Put audit behavior in `setup.ConfigureOptions(...)` and table shape in `setup.ConfigureStorage(...)`.
- With EF storage, call the registration for the database the context runs on: `UsePostgreSqlEntityFramework<TContext>()`, `UseSqlServerEntityFramework<TContext>()`, or `UseEntityFramework<TContext>()` for any other database. Startup refuses a mismatch and names the right one. Also register the context, register a singleton `IDbContextFactory<TContext>` (`AddDbContextFactory`, `AddPooledDbContextFactory`, `AddHeadlessDbContext`, or `AddHeadlessDbContextPool`; all default to singleton), and call `modelBuilder.ConfigureHeadlessAuditLog(this)` in `OnModelCreating`. Startup fails when the call is missing, and refuses a scoped or transient factory with `InvalidServiceLifetimeException`.
- With raw storage, call `setup.UsePostgreSql(connectionString)` or `setup.UseSqlServer(connectionString)`, or the parameterless overload to reuse the connection from `AddPostgreSqlSql` or `AddSqlServerSql`. Run `resources provision` to create the table. A host start only verifies it.
- Raw storage stores rows only. Automatic capture and the fluent policy need EF Core. Without EF Core, log explicit events.
- Use `IAuditLog<TContext>` for an explicit event that must commit or roll back with the caller's changes. Never insert `AuditLogEntry` rows yourself. In an application with several contexts, pick the logger by `TContext`.
- Use `IAuditLogWriter<TContext>` for an event that must persist even though no `SaveChanges` follows, such as a denied request. It commits at once. On EF storage it needs `IDbContextFactory<TContext>`.
- Query history only through `IReadAuditLog<TContext>`. Page with the continuation token, never with offsets. See [Paging audit history](#paging-audit-history).
- Soft-delete transitions record `entity.soft_deleted` and `entity.restored`, not `entity.updated`.
- Keep `EntityFilter` and `PropertyFilter` pure and deterministic. Their results are cached per type and per `(Type, propertyName)`.
- `IpAddress` and `UserAgent` come from the registered `IAuditClientInfoProvider` whenever the writer leaves them `null`. `AddHeadless()` (`Headless.Api.ServiceDefaults`) registers one that reads the current request. A worker host without it records `null` unless `AuditLogWriteRequest` sets the values.
- Delete audit rows only through `IAuditLogRetention<TContext>.PurgeAsync(olderThan, limit)`, from a scheduled job. To make the table append-only, set `AuditLogStorageOptions.AppendOnly = true` with raw storage, or call the database's `migrationBuilder.AddHeadless...AuditLogAppendOnly(...)` helper with EF storage. Never hand-write the trigger. See [Retention and append-only storage](#retention-and-append-only-storage).
- On PostgreSQL, when the application's login must not be able to delete audit rows by itself, use `PurgeAuthority = AuditLogPurgeAuthority.Owner`. See [Who may purge](#who-may-purge).
- A partitioned table has no default partition. Call `IAuditLogPartitions<TContext>.EnsureAsync` from a job at least monthly, or audited writes fail once the last partition's month ends. See [Monthly partitions (PostgreSQL)](#monthly-partitions-postgresql).
- On SQLite, replace the default composite key `(CreatedAt, Id)` with a key on `Id`, because SQLite cannot generate values for a composite key.
- Read `OldValues` and `NewValues` values as `JsonElement` after a round trip (`GetDecimal()`, `GetBoolean()`). Keys keep the captured property names, so `NewValues[ChangedFields[0]]` works. Enums are stored by camelCase name (`"active"`). The stored format ignores the host's JSON options. To reproduce the stored text, for example for an integrity hash, serialize with `AuditLogJson.SerializerOptions` (`Headless.AuditLog`).
- `CaptureErrorStrategy` defaults to `Continue`: a capture failure logs an error and the save proceeds. A failure for one entity skips that entity's row. A failure of the whole capture skips the batch. Set `Throw` to abort the save instead.
- `MissingTransactionStrategy` defaults to `Continue`: when raw storage cannot join the caller's transaction, it writes on its own connection and logs a warning. Set `Throw` when rows for rolled-back work are unacceptable. EF storage ignores the setting. See [Transaction enrollment on raw storage](#transaction-enrollment-on-raw-storage).

## Core Concepts

### Two flavors of audit entry

1. **Automatic property-level entries.** A `SaveChanges` on an audited entity writes one row per changed entity with `OldValues`, `NewValues`, `ChangedFields`, and a `ChangeType` of `Created`, `Updated`, or `Deleted`. The action is `entity.created`, `entity.updated`, `entity.deleted`, `entity.soft_deleted`, or `entity.restored`.
2. **Explicit business events.** `IAuditLog<TContext>.LogAsync(request)` records an event with no entity change: a read, a PII reveal, cross-tenant access, a failure. You set every field through `AuditLogWriteRequest`, including `Action` (such as `"pii.revealed"` or `"report.downloaded"`) and an optional `Data` payload. The row has no `OldValues` and no `ChangeType`. `IAuditLogWriter<TContext>.WriteAsync(request)` records the same kind of row but commits it at once. See [Standalone writes](#standalone-writes).

### What gets captured

- **Actor.** `UserId`, `AccountId`, and `TenantId` from `ICurrentUser` and `ICurrentTenant`. `CorrelationId` from `ICorrelationIdProvider`, by default the W3C trace ID. `IpAddress` and `UserAgent` from the request, else from `IAuditClientInfoProvider`, else `null`.
- **Entity.** `EntityType` is the full CLR type name. `EntityId` is a plain string for a single-column key and a JSON array for a composite key.
- **Change data.** `OldValues` and `NewValues` as `Dictionary<string, object?>`, and `ChangedFields` as `List<string>`.
- **Outcome.** `Success` and an optional `ErrorCode`.
- **Time.** `CreatedAt`, in UTC.

### Sensitive data handling

A property marked `IsAuditSensitive()` gets a strategy at capture time:

| Strategy | Effect |
|---|---|
| `Redact` (default) | The value becomes `"***"`. The name still appears in `ChangedFields`. |
| `Exclude` | The property is omitted from `OldValues`, `NewValues`, and `ChangedFields`. |
| `Transform` | The value passes through `AuditLogOptions.SensitiveValueTransformer` (hash, mask, tokenize). The transformer receives a `SensitiveValueContext` (`EntityType`, `PropertyName`, `PropertyClrType`, `Value`) and must be pure and synchronous. Startup validation fails when `Transform` is in effect and no transformer is set. |

An explicitly excluded property or a `PropertyFilter` veto is never treated as sensitive, because exclusion runs first.

### Testing audit coverage

`model.GetAuditPolicies(options)` and `entityType.GetAuditPolicy(options)` (`Headless.EntityFramework.Primitives`, namespace `Microsoft.EntityFrameworkCore`) return the resolved policy from a built model. Capture uses the same rules, so a test asserts what a save records. Pass the `AuditLogOptions` the host resolves:

```csharp
var options = host.Services.GetRequiredService<IOptions<AuditLogOptions>>().Value;

db.Model.GetAuditPolicies(options)
    .Where(p => p.ConfiguredIsAudited is null)
    .Should().BeEmpty("every entity needs an explicit IsAudited() or ExcludeFromAudit()");
```

- `AuditEntityPolicy.IsAudited` is the effective answer: the explicit configuration on the type or its nearest base, else `AuditByDefault`, unless `EntityFilter` vetoes it. `ConfiguredIsAudited` is the explicit part alone, `null` when unconfigured. `GetAuditPolicies` returns one policy per entity type, owned types included.
- `AuditEntityPolicy.PolicyEntityType` is the type whose configuration decides: the entity itself, or the root owner for an owned type. At save time an owned entry follows its runtime owner, so a derived owner's override shows on the derived owner's policy.
- `AuditPropertyPolicy.Path` is the recorded name: the property name, or the dotted path for a complex type's property. `Properties` lists the entity's own properties, then its complex types' properties depth first.
- `AuditPropertyPolicy.IsSensitive`, `SensitiveDataStrategy` (effective, `null` when not sensitive), and `IsCaptured` (`false` for an explicit exclusion, a default exclusion, a `PropertyFilter` veto, or an unconfigured shadow property). A sensitive property with the `Exclude` strategy is captured but records no value.

### Scope and unit-of-work

With EF storage, audit rows join the same `DbContext` and commit in the same transaction as the entity changes. A rollback removes both. When every row's entity id is known at capture (client-assigned keys, updates, deletes), the rows save in the same `SaveChanges` call. Rows for store-generated keys save in a second call after the key exists. The `SaveChanges` return value never counts audit rows.

With raw storage, atomicity depends on joining the caller's transaction. See the next section.

### Transaction enrollment on raw storage

PostgreSQL and SQL Server storage write with their own SQL. To commit with the caller, they join the context's current connection and transaction, which `Headless.EntityFramework` exposes.

| Write | Transaction it joins | When there is none |
|---|---|---|
| Automatic entries from a `HeadlessDbContext` save | The saving context's transaction. The save begins one when the caller has none, so a plain `SaveChangesAsync()` is atomic. | Only when `Headless.EntityFramework` is not registered or the context's connection uses another driver (another engine, or a connection wrapped by a profiler). |
| `IAuditLog<TContext>.LogAsync` | The transaction of the scope's `TContext`. The row is written immediately inside it. | Whenever the caller has not begun a transaction or unit of work on that context, or `TContext` is not registered. |
| `IAuditLogWriter<TContext>.WriteAsync` | None. It always commits on its own connection. | Not applicable. `MissingTransactionStrategy` does not apply. |

`AuditLogOptions.MissingTransactionStrategy` decides what happens with no transaction to join:

| Strategy | Behavior |
|---|---|
| `Continue` (default) | Writes on a separate connection and commits at once. Logs `AuditLogProviderMissingAmbientTransaction` once per context type, or `AuditLogProviderMismatch` once per connection type. A later failure in the caller's work leaves the audit rows in place. |
| `Throw` | Throws `InvalidOperationException` before any row is written. A `SaveChanges` fails and commits nothing. A `LogAsync` call fails and the caller decides. |

- With `Throw`, startup fails when `Headless.EntityFramework` is not registered, because every write would throw. A driver mismatch throws on the first write.
- With `Throw`, call `LogAsync` inside a transaction or unit of work on the context, such as `IUnitOfWorkFactory.RunAsync(db, …)` or after `db.Database.BeginTransactionAsync()`.
- A row `LogAsync` writes inside a transaction is invisible to other connections until the caller commits, and it is gone after a rollback.

Choose by what the trail must guarantee:

- **Rows only for committed work.** Use EF storage, or raw storage with `MissingTransactionStrategy.Throw`.
- **An event recorded even when the caller's work fails or never saves** (a denied request, a failed attempt). Use `IAuditLogWriter<TContext>`.
- **Row-change history only.** Database-level history (SQL Server temporal tables, PostgreSQL triggers) is always atomic but cannot record reads or denied requests.

### Paging audit history

`IReadAuditLog<TContext>.QueryAsync` returns one `ContinuationPage<AuditLogEntryData>` (`Headless.Primitives`). Entries sort by `CreatedAt`, then `Id`, in `AuditLogQuery.Direction`: `NewestFirst` (default) or `OldestFirst`. The `Id` tie-break means no entry is skipped or repeated at a page boundary. `Size` defaults to 100. The page's `ContinuationToken` is `null` when no further entries match. There is no count query.

```csharp
string? token = null;

do
{
    var page = await readAuditLog.QueryAsync(
        new AuditLogQuery
        {
            TenantId = tenantId,
            Action = "authorization.forbidden",
            Size = 100,
            ContinuationToken = token,
        },
        ct
    );

    Render(page.Items);
    token = page.ContinuationToken;
} while (token is not null);
```

- Send a token with the same filters and `Direction` that produced it. The token holds only a position, so a changed filter returns entries after that position under the new filter.
- Treat the token as opaque. It stays valid across providers and process restarts.
- Entries written during a walk appear only when they sort after the current position: at the end of an `OldestFirst` walk, and with `NewestFirst` only when you restart from the first page.
- `QueryAsync` throws `ArgumentException` for a token it did not issue, an undefined `Direction`, both `Action` and `ActionPrefix` set, or an empty `ActionPrefix`. It throws `ArgumentOutOfRangeException` when `Size` is less than 1 or equal to `int.MaxValue`.
- Filters: `Action`, `EntityType`, `EntityId`, `UserId` (the actor), `AccountId`, `TenantId`, `CorrelationId`, `From` (inclusive), and `To` (exclusive). Each common filter has an index ending in `(CreatedAt, Id)`, so a filtered page seeks instead of scanning.
- `ActionPrefix` matches every action that starts with the text, such as `"authorization."`. `%`, `_`, `[`, and `\` in it match themselves. Case sensitivity follows the database's `LIKE`: case-sensitive on PostgreSQL, the column collation on SQL Server (both the same as `Action`), and ASCII case-insensitive on SQLite (unlike `Action`). A prefix reads a range of the tenant-and-action index and sorts the matches, so filter by `TenantId` and a narrow `From`/`To`. On PostgreSQL the index serves the prefix only under the `C` collation.

### Standalone writes

`IAuditLogWriter<TContext>.WriteAsync` records an explicit event and commits it in its own transaction before it returns. Use it when the request ends without a `SaveChanges`, such as an authorization denial or a read-only request that must leave a trail. The row survives a later rollback of the caller's work.

- EF storage writes through a new context from `IDbContextFactory<TContext>`. The caller's scoped context and its pending changes are untouched.
- Raw storage writes on its own connection.
- `WriteAsync` does nothing when `AuditLogOptions.IsEnabled` is `false`.

### Authorization denial entries

`services.AddHeadlessAuthorizationDenialAudit<TContext>()` (`Headless.Api`) writes one row through `IAuditLogWriter<TContext>` each time the authorization middleware challenges or forbids a request. Register audit log storage too, because the writer comes from it.

| Field | Value |
|---|---|
| `Action` | `authorization.challenged` (no or rejected authentication, usually 401) or `authorization.forbidden` (authenticated, a requirement failed, usually 403). |
| `Success` | `false` |
| `UserId`, `AccountId`, `TenantId`, `CorrelationId` | From `ICurrentUser`, `ICurrentTenant`, and `ICorrelationIdProvider`. `UserId` is empty for an unauthenticated caller. |
| `NewValues` | `method` (HTTP method), `route` (the route template, such as `/orders/{id}`, never the request path), and `policies` (the named policies on the endpoint, empty for the default policy). |

- The request body, query string, headers, and raw path are never recorded.
- The row commits before the 401 or 403 response is written. A client disconnect does not cancel the write.
- A failed write logs `AuthorizationDenialAuditWriteFailed` as an error. The denial response proceeds unchanged.
- Call the registration after any custom `IAuthorizationMiddlewareResultHandler`, which it wraps. A handler registered after it replaces it.
- Every audited denial costs one insert and commit, including anonymous traffic. Put rate limiting ahead of authorization on endpoints that attract scanners or credential stuffing.
- A denial raised outside the authorization middleware, such as a `Results.Forbid()` returned by an endpoint, is not audited.

Query denials with `new AuditLogQuery { TenantId = tenantId, Action = "authorization.forbidden" }`, or both kinds with `ActionPrefix = "authorization."`.

**Verify:** in an integration test, send a request that fails a policy, assert a 403, then assert that `IReadAuditLog<TContext>.QueryAsync(new AuditLogQuery { Action = "authorization.forbidden" })` returns one entry with the endpoint's route.

### Field length limits

Every provider truncates string fields to the column limit before it writes, with no error: `Action` 256, `EntityType` 512, `EntityId` 256, `UserId`, `AccountId`, `TenantId`, and `CorrelationId` 128, `UserAgent` 512, `IpAddress` 45.

### Retention and append-only storage

`IAuditLogRetention<TContext>.PurgeAsync(olderThan, limit, ct)` deletes up to `limit` entries whose `CreatedAt` is more than `olderThan` before the registered `TimeProvider`'s now, oldest first, in one transaction, and returns the count. Call it from a scheduled job and repeat while it returns `limit`. A non-positive `olderThan` or `limit` throws `ArgumentOutOfRangeException`. Every storage package registers it.

An append-only table refuses every change to a written row except that purge:

| Statement | PostgreSQL | SQL Server |
| --- | --- | --- |
| `UPDATE` | refused (`42501`, `insufficient_privilege`) | refused (`THROW 50000`) |
| `DELETE` | refused, except the purge | refused, except the purge |
| `TRUNCATE` | refused, on the table and on each partition | not covered: SQL Server fires no trigger on `TRUNCATE`. Deny `ALTER` on the table to the application login. |

| Storage | Switch | Applied by |
| --- | --- | --- |
| `UsePostgreSql`, `UseSqlServer` | `AuditLogStorageOptions.AppendOnly = true` (default `false`) | `resources provision` |
| `UsePostgreSqlEntityFramework`, `UseSqlServerEntityFramework` | `migrationBuilder.AddHeadlessPostgreSqlAuditLogAppendOnly(...)` or `migrationBuilder.AddHeadlessSqlServerAuditLogAppendOnly(...)` | the application's migrations |

- Both paths emit the same SQL. Do not copy the trigger into a migration by hand.
- EF storage rejects `AppendOnly`, `Partitioning`, and `MinimumRetention` at startup. The migration helpers take their place.
- Each helper names its database, so one project can reference both EF packages. A helper throws `NotSupportedException` in a migration for another database. Calling an `Add...` helper again in a later migration is safe. Pass `table:` and `schema:` when `TableName` or `Schema` is set. The helpers live in namespace `Microsoft.EntityFrameworkCore.Migrations`.
- Turning `AppendOnly` on later adds the trigger at the next `resources provision`. Turning it off leaves the trigger in place until you drop it: `{table}_append_only` and `{table}_append_only_truncate` with function `{schema}.{table}_append_only` on PostgreSQL, `TR_{Table}_AppendOnly` on SQL Server. In a migration, `DropHeadlessPostgreSqlAuditLogAppendOnly(...)` or `DropHeadlessSqlServerAuditLogAppendOnly(...)` removes them.

```csharp
// EF storage: in the migration, after the table exists. On SQL Server use
// AddHeadlessSqlServerAuditLogAppendOnly and DropHeadlessSqlServerAuditLogAppendOnly.
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddHeadlessPostgreSqlAuditLogAppendOnly(); // schema "headless", the conventional table name
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropHeadlessPostgreSqlAuditLogAppendOnly();
}
```

**Verify:** run `UPDATE` on one audit row as the application login and expect the error above. Then call `PurgeAsync` with a short `olderThan` in a test database and expect a positive count.

#### Who may purge

`AuditLogStorageOptions.PurgeAuthority` decides what the trigger lets through:

| `PurgeAuthority` | The trigger passes a `DELETE` when | Stops |
| --- | --- | --- |
| `SessionSetting` (default) | the session setting `headless.audit_log_purge` is on. The purge sets it for its own transaction and clears it before it commits. | application code and accidental statements. Any login that can run arbitrary SQL can set the same setting. |
| `Owner` (PostgreSQL only) | the setting is on **and** the deleting role holds the table owner's rights | every login without those rights. Such a login purges only by calling `{schema}.{table}_purge(cutoff, limit)`. |

With `Owner`, the purge function is `SECURITY DEFINER`, owned by whoever provisioned it, and closed to `PUBLIC`. `IAuditLogRetention<TContext>` calls it. Grant it to the login that runs the retention, and to no other:

```sql
GRANT EXECUTE ON FUNCTION "headless"."audit_log_entries_purge" TO app_login;
```

`MinimumRetention` compiles a floor into the function: a cutoff newer than the server's `now()` minus that period is refused with `42501`. Set it when the application login runs the retention, so a compromised login can delete only entries already past the floor.

```csharp
// Raw PostgreSQL storage
setup.ConfigureStorage(options =>
{
    options.AppendOnly = true;
    options.PurgeAuthority = AuditLogPurgeAuthority.Owner;
    options.MinimumRetention = TimeSpan.FromDays(365);
});

// EF storage: the migration installs the function, and the option tells the retention to call it
migrationBuilder.AddHeadlessPostgreSqlAuditLogAppendOnly(
    purgeAuthority: AuditLogPurgeAuthority.Owner,
    minimumRetention: TimeSpan.FromDays(365)
);
setup.ConfigureStorage(options => options.PurgeAuthority = AuditLogPurgeAuthority.Owner);
```

- `Owner` matters only when the application's login is not the table's owner. Provision or migrate with the owner login, and run the host with another.
- On raw PostgreSQL storage, `Owner` needs `AppendOnly`. Turning `Owner` off later leaves the owner check in place, so the retention's own `DELETE` is refused for a non-owner login until you replace the trigger function by hand.
- Changing `MinimumRetention` after `resources provision` fails verification with a checksum mismatch. With EF storage, call the helper again in a new migration.
- A role with the owner's rights, a superuser included, passes the check.
- SQL Server has no equivalent. Its options validation and its migration helper refuse `Owner`.

#### Monthly partitions (PostgreSQL)

A partitioned table keeps one range partition per calendar month of `created_at`, in UTC, named `{table}_YYYY_MM`. Retention then drops a whole month instead of deleting rows, which leaves no dead tuples and no index bloat.

| Storage | Switch | Creates |
| --- | --- | --- |
| `UsePostgreSql` | `AuditLogStorageOptions.Partitioning = AuditLogPartitioning.Monthly` | the partitioned table, the partition functions, and the partitions, all through `resources provision` |
| `UsePostgreSqlEntityFramework` | `migrationBuilder.AddHeadlessPostgreSqlAuditLogPartitions(...)` in the migration that creates the table | the partitioned table from the scaffolded `CreateTable`, the same functions, and partitions from the current month through `monthsAhead` months ahead (default 3) |

```csharp
// Raw PostgreSQL storage
setup.ConfigureStorage(options =>
{
    options.Partitioning = AuditLogPartitioning.Monthly;
    options.PartitionMonthsAhead = 6; // default 3, range 0 to 120
});

// EF storage: in Up, after the scaffolded CreateTable and CreateIndex calls of the audit table, left as scaffolded.
migrationBuilder.AddHeadlessPostgreSqlAuditLogPartitions(monthsAhead: 6);
migrationBuilder.AddHeadlessPostgreSqlAuditLogAppendOnly();
migrationBuilder.Sql(PostgreSqlAuditLogPartitionFunctions.GrantExecuteStatement("app_login"));

// In Down, before the scaffolded DropTable.
migrationBuilder.DropHeadlessPostgreSqlAuditLogAppendOnly();
migrationBuilder.DropHeadlessPostgreSqlAuditLogPartitions();
```

`IAuditLogPartitions<TContext>` keeps the partitions at run time. The framework schedules nothing, so call both members from one job of your own, at least once a month:

```csharp
public sealed class AuditLogPartitionsJob(IAuditLogPartitions<AppDbContext> partitions)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await partitions.EnsureAsync(cancellationToken); // current month through PartitionMonthsAhead
        await partitions.DropExpiredAsync(TimeSpan.FromDays(730), cancellationToken);
    }
}
```

- **No default partition.** A row whose `created_at` falls in no partition fails its insert with `23514`. With EF storage that fails the `SaveChanges` that produced it. Keep the horizon at one month or more, and run the job.
- **`EnsureAsync`** returns how many partitions it created. A second call returns 0. It measures the current month by the registered `TimeProvider`. On an append-only table it also adds the `TRUNCATE` guard to every partition that lacks one.
- **`DropExpiredAsync(olderThan)`** drops every partition whose whole range ended more than `olderThan` ago and returns the count. A month with one day still inside the period stays. `PurgeAsync` still deletes single rows on a partitioned table.
- **Least privilege.** Both members call `SECURITY DEFINER` functions closed to `PUBLIC`: `{schema}.{table}_ensure_partitions(from, months_ahead)` and `{schema}.{table}_drop_partitions(cutoff)`. A login with no DDL rights keeps the partitions once granted `EXECUTE`. `PostgreSqlAuditLogPartitionFunctions.GrantExecuteStatement(role, table, schema)` (`Headless.AuditLog.Storage.PostgreSql`) returns that grant for both functions. Run it as their owner in a migration or a deploy step. The framework runs it for nobody. `MinimumRetention` (or the helper's `minimumRetention:`) puts the same floor under the drop function.
- **Locks.** Creating or dropping a partition takes an `ACCESS EXCLUSIVE` lock on the table briefly. Both functions set `lock_timeout` to 5 seconds, so a call blocked by a long transaction fails with `55P03` instead of queueing writers. Run the job again.
- **Raw storage.** `resources provision` tops the horizon up on every release, as `EnsureAsync` does. A starting host checks that the current month has a partition and holds the `warmup` readiness check `Unhealthy` until it does. Changing `PartitionMonthsAhead` needs no migration. `EnsureAsync` and `DropExpiredAsync` throw `InvalidOperationException` when `Partitioning` is `None`.
- **EF storage.** The helper turns the scaffolded `CreateTable` in the same migration into a partitioned table. It keeps the scaffolded primary key, unique constraints, foreign keys, and any columns, defaults, check constraints, and comments you added to `AuditLogEntry`. The scaffolded `CreateIndex` calls then run against it. Call it before `AddHeadlessPostgreSqlAuditLogAppendOnly`, and `DropHeadlessPostgreSqlAuditLogPartitions` in `Down`. The table name must leave room for a `_template` suffix within 63 characters. On PostgreSQL 16, an insert aimed at one partition directly must supply `id`. Inserts through the audit table, which is how the store writes, always get one.
- **Later migrations.** PostgreSQL cannot partition an existing table. In a migration that does not create the table, the helper installs the functions only, and fails with `55000` (installing nothing) when the table is not partitioned.
- **Idempotent scripts.** The helpers' statements also work inside `dotnet ef migrations script --idempotent`.
- **Existing tables.** On raw storage, turning `Partitioning` on for a table that exists fails verification with a checksum mismatch. Move the rows by hand, or start with a new table name.
- SQL Server partitioning is not provided. Its options validation refuses `Partitioning`, and no `IAuditLogPartitions<TContext>` is registered.

**Verify:** on raw storage, `dotnet app.dll resources verify` reports the `AuditLogPartitions` resource `Ok`. On either storage, a test that calls `EnsureAsync` twice gets 0 from the second call.

### Schema setup

Raw storage contributes the table, its indexes, and, when enabled, the append-only trigger, the owner purge function, and the partition functions, as schema steps. Run `resources provision` (or `deploy`) in the deployment to create them. A host start only verifies them and fails when a step is missing. See [Resource provisioning](utilities.md#resource-provisioning) and the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts).

- `JsonColumnType`, `CreatedAtColumnType`, `Partitioning`, and `MinimumRetention` shape the DDL. Changing one after `resources provision` fails startup with a checksum mismatch.
- A configured `TableName` is part of the step's history id, so a second table gets its own steps.
- When a migration tool owns the schema, run `resources provision` anyway. Every step is idempotent, so it only records history for objects that already exist.

## Choosing a Provider

| | EF Core | PostgreSql | SqlServer |
|---|---|---|---|
| **Use when** | You use EF Core and want rows to commit with the entity changes, with migrations managing the table. | You are on PostgreSQL and want no EF dependency for audit storage, or native `jsonb` columns. | You are on SQL Server and want no EF dependency for audit storage. |
| **Atomicity** | Always: same `DbContext`, same transaction. | When the saving context uses Npgsql. `IAuditLog` only inside a caller transaction. Otherwise a separate connection, or an exception under `MissingTransactionStrategy.Throw`. | When the saving context uses SqlClient. Same rules as PostgreSql. |
| **Schema** | EF migrations. | `resources provision`. | `resources provision`. |
| **JSON columns** | String columns by default. Opt into `jsonb` or `json` with `AuditLogJsonColumnType`. | `jsonb` by default. `Json` accepted. `NvarcharMax` rejected. | `nvarchar(max)` only. |
| **Change capture** | With `Headless.EntityFramework`. | With `Headless.EntityFramework`, or explicit events only. | Same as PostgreSql. |
| **Extra columns** | Yes, through `IAuditLogEntryEnricher`. | No. The table is fixed. | No. The table is fixed. |
| **Append-only, owner purge, partitions** | Through the database's EF package and migration helpers. | `AppendOnly`, `PurgeAuthority.Owner`, `Partitioning.Monthly`. | `AppendOnly` only. |

---

## Headless.AuditLog.Abstractions

The audit log contracts. It registers no services.

### API and behavior

- `IAuditLog<TContext>`: explicit events that commit with the caller's work. `TContext` picks the logger in a multi-context application.
- `IAuditLogWriter<TContext>`: explicit events committed at once. See [Standalone writes](#standalone-writes).
- `IReadAuditLog<TContext>`: keyset-paged queries returning `ContinuationPage<AuditLogEntryData>`. See [Paging audit history](#paging-audit-history).
- `IAuditLogRetention<TContext>`: `PurgeAsync(olderThan, limit)`, the only sanctioned delete. See [Retention and append-only storage](#retention-and-append-only-storage).
- `IAuditLogPartitions<TContext>`: `EnsureAsync()` and `DropExpiredAsync(olderThan)`, PostgreSQL only. See [Monthly partitions (PostgreSQL)](#monthly-partitions-postgresql).
- `AuditLogWriteRequest`: required `Action`, optional `EntityType`, `EntityId`, `Data`, `Success` (default `true`), `ErrorCode`, `IpAddress`, and `UserAgent`.
- `AuditLogQuery`: the filters, `Direction` (`AuditLogSortDirection.NewestFirst` or `OldestFirst`), `Size` (default 100), and `ContinuationToken`.
- `AuditLogEntryData`: the stored entry. `OldValues` and `NewValues` are `Dictionary<string, object?>`.
- `AuditLogOptions`, `SensitiveDataStrategy`, `SensitiveValueContext`, `AuditChangeType`, and `MissingTransactionStrategy`.
- `IAuditClientInfoProvider`: the `IpAddress` and `UserAgent` used when a writer leaves them unset. `Headless.Api.ServiceDefaults` registers the request-backed one. Register your own to supply client info in another kind of host.

### Install

```bash
dotnet add package Headless.AuditLog.Abstractions
```

### Setup and use

Log an explicit event:

```csharp
await auditLog.LogAsync(
    new AuditLogWriteRequest
    {
        Action = "pii.revealed",
        EntityType = typeof(Patient).FullName,
        EntityId = id.ToString(),
        Data = new Dictionary<string, object?> { ["requestedBy"] = currentUser.UserId },
    }
);
```

Query history:

```csharp
var page = await readAuditLog.QueryAsync(
    new AuditLogQuery
    {
        EntityType = typeof(Patient).FullName,
        EntityId = patientId.ToString(),
        Size = 50,
    },
    ct
);
// page.Items holds the entries. Pass page.ContinuationToken to fetch the next page.
```

**Verify:** in a test, call `LogAsync` (then `SaveChangesAsync` on EF storage), and assert that `QueryAsync` with the same `Action` returns the entry.

### Configuration

`AuditLogOptions`, set through `setup.ConfigureOptions(...)`:

| Option | Default | Description |
|---|---|---|
| `IsEnabled` | `true` | Master switch. `false` disables capture and explicit writes. |
| `AuditByDefault` | `false` | Policy for entities with no explicit `IsAudited()` or `ExcludeFromAudit()`. |
| `SensitiveDataStrategy` | `Redact` | Global strategy for properties marked `IsAuditSensitive()`. |
| `SensitiveValueTransformer` | `null` | Required when the effective strategy is `Transform`. Pure and synchronous. |
| `EntityFilter` | `null` | Returns `true` to exclude a type. Cached per type. |
| `PropertyFilter` | `null` | Returns `true` to exclude a property. Receives the dotted path for a complex type's property. Cached per `(Type, propertyName)`. |
| `DefaultExcludedProperties` | `ConcurrencyStamp`, `CreatedAt`, `UpdatedAt`, `DeletedAt`, `RestoredAt`, `CreatedById`, `UpdatedById`, `DeletedById`, `RestoredById` | Property names capture skips. Add or remove entries. |
| `CaptureErrorStrategy` | `Continue` | `Continue` logs an error and saves. `Throw` aborts the save. |
| `MissingTransactionStrategy` | `Continue` | Raw storage only. See [Transaction enrollment on raw storage](#transaction-enrollment-on-raw-storage). |

---

## Headless.AuditLog

The registration, the options, and the storage options.

### API and behavior

- `AddHeadlessAuditLog(setup => setup.Use...)` (namespace `Headless.AuditLog`) is the only entry point. It requires exactly one storage registration.
- The setup builder exposes `ConfigureOptions(...)` for `AuditLogOptions` and `ConfigureStorage(...)` for `AuditLogStorageOptions`. `ConfigureStorage` also accepts the `Headless:AuditLog:Storage` configuration section.
- `AuditLogJson.SerializerOptions` is the fixed format of the JSON columns. Use it only to reproduce stored text.

### Install

```bash
dotnet add package Headless.AuditLog
```

### Setup and use

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureOptions(options =>
    {
        options.SensitiveDataStrategy = SensitiveDataStrategy.Redact;
    });

    setup.ConfigureStorage(options =>
    {
        options.Schema = "compliance"; // default: "headless"
        options.TableName = "compliance_audit"; // default: audit_log_entries on PostgreSQL, AuditLogEntries elsewhere
    });

    setup.UseEntityFramework<AppDbContext>();
});
```

**Verify:** `AddHeadlessAuditLog` throws `InvalidOperationException` at registration when no or several `Use...` calls are made. `dotnet app.dll check` validates options like a start, so it fails when `Transform` has no transformer.

### Configuration

`AuditLogStorageOptions`:

| Option | Default | Description |
|---|---|---|
| `Schema` | `"headless"` | Shared with every Headless feature. See [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features). |
| `TableName` | `null` | `null` uses `audit_log_entries` on PostgreSQL and `AuditLogEntries` elsewhere. A set name is used verbatim, at most 40 characters, and key and index names derive from it, so two audit tables can share a schema. |
| `JsonColumnType` | `null` (provider default) | `Jsonb`, `Json`, or `NvarcharMax`. Each provider accepts only its own types. |
| `CreatedAtColumnType` | `null` (provider default) | Timestamp column type. Defaults: `timestamp with time zone` on PostgreSQL, `datetimeoffset(7)` on SQL Server. |
| `AppendOnly` | `false` | Raw storage only. See [Retention and append-only storage](#retention-and-append-only-storage). |
| `PurgeAuthority` | `SessionSetting` | `Owner` is PostgreSQL only. With EF storage it tells the retention to call the function a migration installed. See [Who may purge](#who-may-purge). |
| `MinimumRetention` | `null` | Raw PostgreSQL only, with `PurgeAuthority.Owner` or `Partitioning.Monthly`. At least one second. EF storage uses the helpers' `minimumRetention:` instead. |
| `Partitioning` | `None` | Raw PostgreSQL only. EF storage uses `AddHeadlessPostgreSqlAuditLogPartitions(...)`. See [Monthly partitions (PostgreSQL)](#monthly-partitions-postgresql). |
| `PartitionMonthsAhead` | `3` | Months after the current one that `EnsureAsync` and `resources provision` keep a partition for. Range 0 to 120. |

---

## Headless.AuditLog.Storage.EntityFramework

EF Core audit storage for any database. On PostgreSQL or SQL Server, use that database's EF package instead.

### API and behavior

- `setup.UseEntityFramework<TContext>()` registers EF storage. Audit rows join the saving `DbContext`.
- `modelBuilder.ConfigureHeadlessAuditLog(this)` maps `AuditLogEntry`. It reads `AuditLogStorageOptions` from the context's services and names objects in the database's convention: snake_case on PostgreSQL (`audit_log_entries`, `created_at`, `pk_audit_log_entries`, `ix_audit_log_entries_tenant_time`), PascalCase elsewhere (`AuditLogEntries`, `PK_AuditLogEntries`, `IX_AuditLogEntries_TenantTime`). It is idempotent. The `(AuditLogStorageOptions, StorageNamingStyle)` overload takes both explicitly. Pass `HeadlessStorageNaming.ForProvider(Database.ProviderName)` so names match the raw providers.
- `AuditLogEntry` is the mapped entity. `ConfigureHeadlessAuditLog` excludes it from capture. Never call `IsAudited()` on it: audit rows would audit themselves.
- The key is `(CreatedAt, Id)`, ready for range partitioning. Indexes cover tenant+time, tenant+action+time, tenant+entity+time, tenant+actor+time, tenant+account+time, and correlation id, each ending in `(CreatedAt, Id)`.
- `IAuditLogEntryEnricher` fills columns you add to the table. See [Adding columns to the audit table](#adding-columns-to-the-audit-table).
- The retention deletes in a transaction inside the context's execution strategy, so it works with `EnableRetryOnFailure`. A retry runs the whole purge again. This package's retention passes no append-only trigger and throws `NotSupportedException` for `PurgeAuthority.Owner`. The PostgreSQL and SQL Server EF packages replace it.
- This package has no migration helper and no `IAuditLogPartitions<TContext>`.
- At startup, a validator fails when `ConfigureHeadlessAuditLog` was not called, even if `AuditLogEntry` was mapped another way. It also fails when the registration does not match the context's database, and names the one to use.
- JSON columns are strings by default, portable to every EF database. Set `JsonColumnType = AuditLogJsonColumnType.Jsonb` for native `jsonb` on PostgreSQL.
- To write audit rows to a different database or schema than the audited entities, use raw storage. EF storage needs `AuditLogEntry` in the same model.

### Install

```bash
dotnet add package Headless.AuditLog.Storage.EntityFramework
```

### Setup and use

#### DI setup

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureOptions(o =>
    {
        o.SensitiveDataStrategy = SensitiveDataStrategy.Redact;
    });
    setup.ConfigureStorage(options =>
    {
        options.JsonColumnType = AuditLogJsonColumnType.Jsonb; // for PostgreSQL
    });
    setup.UsePostgreSqlEntityFramework<AppDbContext>(); // Headless.AuditLog.Storage.PostgreSql.EntityFramework
    // setup.UseSqlServerEntityFramework<AppDbContext>(); // Headless.AuditLog.Storage.SqlServer.EntityFramework
    // setup.UseEntityFramework<AppDbContext>();          // any other database
});
```

Register the same context with EF Core and a singleton `IDbContextFactory<TContext>` (`AddDbContextFactory`, `AddPooledDbContextFactory`, `AddHeadlessDbContext`, or `AddHeadlessDbContextPool`). The startup validator and `IReadAuditLog<TContext>` read through the factory, so the host fails to start without one.

#### DbContext setup

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureHeadlessAuditLog(this);

        modelBuilder.Entity<Patient>(patient =>
        {
            patient.IsAudited();
            patient.Property(x => x.NationalId).IsAuditSensitive();
            patient.Property(x => x.CreditCardToken).IsAuditSensitive(SensitiveDataStrategy.Exclude);
            patient.Property(x => x.LastComputedAt).ExcludeFromAudit();
        });
    }
}
```

On SQLite, replace the key after `ConfigureHeadlessAuditLog`:

```csharp
modelBuilder.Entity<AuditLogEntry>().HasKey(e => e.Id);
```

Then add a migration that creates the audit table, as for any entity.

**Verify:** `dotnet app.dll check` fails with a message naming `modelBuilder.ConfigureHeadlessAuditLog(this)` when the call is missing. In an integration test, save a change to an audited entity and assert that `IReadAuditLog<AppDbContext>.QueryAsync(new AuditLogQuery { Action = "entity.updated" })` returns the entry, with sensitive values as `"***"`.

#### Adding columns to the audit table

The table belongs to your model. Map a property on `AuditLogEntry` after `ConfigureHeadlessAuditLog`, and fill it with an `IAuditLogEntryEnricher`:

```csharp
// OnModelCreating, after ConfigureHeadlessAuditLog
modelBuilder.Entity<AuditLogEntry>(entry =>
{
    entry.Property<string>("Category").HasMaxLength(32).IsRequired();
    entry.HasIndex("TenantId", "Category", "CreatedAt", "Id");
});

public sealed class AuditCategoryEnricher : IAuditLogEntryEnricher
{
    public void Enrich(EntityEntry<AuditLogEntry> entry)
    {
        entry.Property<string>("Category").CurrentValue = entry.Entity.Action.Split('.')[0];
    }
}

services.AddSingleton<IAuditLogEntryEnricher, AuditCategoryEnricher>();
```

- **When it runs.** Once per row, when the row is added to its `DbContext` and before it is saved. This covers change capture, `IAuditLog<TContext>.LogAsync`, and `IAuditLogWriter<TContext>.WriteAsync`, on all three EF registrations. Enrichers run in registration order, at any lifetime.
- **What it reads.** The row's own fields are set. The `EntityId` of an entity with a database-generated key is not known yet.
- **Failure.** An enricher exception fails the write and removes the row from the context, so a caller that catches it and saves anyway writes no half-filled row.
- **Reading.** `AuditLogEntryData` does not carry added columns. Query `DbSet<AuditLogEntry>` and read them with `EF.Property<T>(entry, "Category")`, which also translates in a `Where`.
- **Raw storage.** `UsePostgreSql` and `UseSqlServer` have a fixed table and call no enricher.

#### Explicit event logging

```csharp
await auditLog.LogAsync(new AuditLogWriteRequest
{
    Action = "pii.revealed",
    EntityType = typeof(Patient).FullName,
    EntityId = id.ToString(),
});
```

On EF storage the row commits with the next `SaveChanges` on the context.

#### Query audit entries

```csharp
var page = await readAuditLog.QueryAsync(
    new AuditLogQuery
    {
        Action = "entity.updated",
        EntityType = typeof(Patient).FullName,
        Size = 50,
    },
    ct
);
```

### Configuration

```csharp
setup.ConfigureStorage(options =>
{
    options.TableName = "audit_entries"; // optional, used verbatim, at most 40 characters
    options.Schema = "compliance"; // default: "headless"
    options.JsonColumnType = AuditLogJsonColumnType.Jsonb; // optional
    options.CreatedAtColumnType = "timestamp with time zone"; // optional
});
```

`AppendOnly`, `Partitioning`, and `MinimumRetention` fail startup on EF storage.

### Runtime behavior

- `IAuditLog<TContext>` and `IAuditLogWriter<TContext>` are scoped. `IReadAuditLog<TContext>` and `IAuditLogRetention<TContext>` are singletons.
- The package registers no enricher. It calls every registered `IAuditLogEntryEnricher`.

---

## Headless.AuditLog.Storage.PostgreSql.EntityFramework

EF Core audit storage on PostgreSQL: everything `Headless.AuditLog.Storage.EntityFramework` provides, plus append-only, owner purge, and partitions through migrations.

### API and behavior

- `setup.UsePostgreSqlEntityFramework<TContext>()` registers EF storage with a PostgreSQL retention and `IAuditLogPartitions<TContext>`.
- Migration helpers (namespace `Microsoft.EntityFrameworkCore.Migrations`): `AddHeadlessPostgreSqlAuditLogAppendOnly`, `DropHeadlessPostgreSqlAuditLogAppendOnly`, `AddHeadlessPostgreSqlAuditLogPartitions`, and `DropHeadlessPostgreSqlAuditLogPartitions`. See [Retention and append-only storage](#retention-and-append-only-storage) and [Monthly partitions (PostgreSQL)](#monthly-partitions-postgresql).
- The retention passes the append-only trigger. With `PurgeAuthority.Owner` it calls the `{table}_purge` function instead.
- The helpers emit the same SQL as raw PostgreSQL storage. Referencing that package registers nothing. Raw storage is selected only by `UsePostgreSql(...)`.

### Install

```bash
dotnet add package Headless.AuditLog.Storage.PostgreSql.EntityFramework
```

### Setup and use

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(options => options.JsonColumnType = AuditLogJsonColumnType.Jsonb);
    setup.UsePostgreSqlEntityFramework<AppDbContext>();
});

// In a migration, after the audit table exists.
migrationBuilder.AddHeadlessPostgreSqlAuditLogAppendOnly();
```

The `DbContext` setup, explicit logging, and queries are those of [Headless.AuditLog.Storage.EntityFramework](#headlessauditlogstorageentityframework).

**Verify:** `dotnet app.dll check` passes, and fails naming the right registration when the context does not run on PostgreSQL. After the migration, an `UPDATE` on an audit row fails with `42501`.

### Configuration

`AuditLogStorageOptions`, as in [Headless.AuditLog](#headlessauditlog). `PurgeAuthority` and `PartitionMonthsAhead` apply. `AppendOnly`, `Partitioning`, and `MinimumRetention` fail startup, because the migration helpers take their place.

### Runtime behavior

- `IAuditLogRetention<TContext>` and `IAuditLogPartitions<TContext>` are singletons.
- Startup refuses a context that does not run on PostgreSQL.

---

## Headless.AuditLog.Storage.SqlServer.EntityFramework

EF Core audit storage on SQL Server: everything `Headless.AuditLog.Storage.EntityFramework` provides, plus append-only through a migration.

### API and behavior

- `setup.UseSqlServerEntityFramework<TContext>()` registers EF storage with a SQL Server retention.
- Migration helpers (namespace `Microsoft.EntityFrameworkCore.Migrations`): `AddHeadlessSqlServerAuditLogAppendOnly` and `DropHeadlessSqlServerAuditLogAppendOnly`. See [Retention and append-only storage](#retention-and-append-only-storage).
- The retention passes the append-only trigger and clears its session key before the connection returns to the pool.
- No owner purge and no partitions: startup refuses `PurgeAuthority.Owner`, and no `IAuditLogPartitions<TContext>` is registered.

### Install

```bash
dotnet add package Headless.AuditLog.Storage.SqlServer.EntityFramework
```

### Setup and use

```csharp
services.AddHeadlessAuditLog(setup => setup.UseSqlServerEntityFramework<AppDbContext>());

// In a migration, after the audit table exists.
migrationBuilder.AddHeadlessSqlServerAuditLogAppendOnly();
```

The `DbContext` setup, explicit logging, and queries are those of [Headless.AuditLog.Storage.EntityFramework](#headlessauditlogstorageentityframework).

**Verify:** `dotnet app.dll check` passes, and fails naming the right registration when the context does not run on SQL Server. After the migration, an `UPDATE` on an audit row fails with error 50000.

### Configuration

`AuditLogStorageOptions`, as in [Headless.AuditLog](#headlessauditlog). `AppendOnly`, `Partitioning`, and `MinimumRetention` fail startup.

### Runtime behavior

- `IAuditLogRetention<TContext>` is a singleton.
- Startup refuses a context that does not run on SQL Server.

---

## Headless.AuditLog.Storage.PostgreSql

Raw PostgreSQL audit storage over Npgsql, with no EF Core dependency.

### API and behavior

- `UsePostgreSql` has the overloads `(string connectionString)`, `(IConfiguration)`, `(Action<PostgreSqlAuditLogOptions>)`, and `(Action<PostgreSqlAuditLogOptions, IServiceProvider>)`, plus a parameterless `UsePostgreSql()` that reads the connection registered by `AddPostgreSqlSql`.
- Automatic entries join the saving context's Npgsql transaction. `IAuditLog<TContext>` joins the transaction of the scope's `TContext`. Otherwise `MissingTransactionStrategy` applies. See [Transaction enrollment on raw storage](#transaction-enrollment-on-raw-storage).
- `IAuditLogWriter<TContext>` always commits on its own connection.
- The table is `audit_log_entries` (unless `TableName` is set), with snake_case columns, key `pk_{table}`, and indexes `ix_{table}_tenant_time`, `ix_{table}_tenant_action_time`, `ix_{table}_tenant_entity_time`, `ix_{table}_tenant_actor_time`, `ix_{table}_tenant_account_time`, and `ix_{table}_correlation`, each ending in `(created_at, id)`.
- JSON columns are `jsonb` by default. `Json` is accepted. `NvarcharMax` fails options validation.
- `AppendOnly`, `PurgeAuthority.Owner`, `MinimumRetention`, and `Partitioning.Monthly` are all supported.
- `PostgreSqlAuditLogPartitionFunctions.GrantExecuteStatement(role, table, schema)` returns the `GRANT EXECUTE` for both partition functions, for raw and EF storage. Any role name is accepted. The schema and table must be plain PostgreSQL identifiers.

### Install

```bash
dotnet add package Headless.AuditLog.Storage.PostgreSql
```

### Setup and use

```csharp
// Reuse the connection every Headless feature shares:
services.AddPostgreSqlSql(builder.Configuration.GetConnectionString("Default")!);
services.AddHeadlessAuditLog(setup => setup.UsePostgreSql());

// Or give the audit log its own database and schema:
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(options => options.Schema = "compliance");
    setup.UsePostgreSql(builder.Configuration.GetConnectionString("AuditLog")!);
});
```

The other overloads:

```csharp
setup.UsePostgreSql(options =>
{
    options.ConnectionString = connectionString;
    options.CommandTimeout = TimeSpan.FromSeconds(60);
});

setup.UsePostgreSql(builder.Configuration.GetSection("Headless:AuditLog:PostgreSql"));

setup.UsePostgreSql((options, sp) =>
    options.ConnectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("AuditLog")!);
```

Then run `dotnet app.dll resources provision` (or `deploy`) in the deployment. See [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

**Verify:** `dotnet app.dll resources verify -k Schema` reports the audit log steps `Ok`. Before provisioning, a host start fails naming `resources provision`.

### Configuration

`PostgreSqlAuditLogOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | (required) | Npgsql connection string. |
| `CommandTimeout` | 30 seconds | Applies to every write and read. |

### Runtime behavior

- `IAuditLog<TContext>`, `IAuditLogWriter<TContext>`, `IReadAuditLog<TContext>`, `IAuditLogRetention<TContext>`, and `IAuditLogPartitions<TContext>` are singletons.
- The retention deletes oldest-first on its own connection, after turning on `headless.audit_log_purge` for its transaction. With `PurgeAuthority.Owner` it makes one call to `{table}_purge`.
- With `Partitioning.Monthly`, `resources provision` also runs the `AuditLogPartitions` resource.
- If missing, it registers `TimeProvider.System`, null `ICurrentTenant` and `ICurrentUser` fallbacks, and a trace-id `ICorrelationIdProvider`. `AddHeadless()` replaces the tenant and user fallbacks with the HTTP implementations in either call order.

---

## Headless.AuditLog.Storage.SqlServer

Raw SQL Server audit storage over `Microsoft.Data.SqlClient`, with no EF Core dependency.

### API and behavior

- `UseSqlServer` has the overloads `(string connectionString)`, `(IConfiguration)`, `(Action<SqlServerAuditLogOptions>)`, and `(Action<SqlServerAuditLogOptions, IServiceProvider>)`, plus a parameterless `UseSqlServer()` that reads the connection registered by `AddSqlServerSql`.
- Automatic entries join the saving context's `SqlTransaction`. `IAuditLog<TContext>` joins the transaction of the scope's `TContext`. Otherwise `MissingTransactionStrategy` applies. See [Transaction enrollment on raw storage](#transaction-enrollment-on-raw-storage).
- `IAuditLogWriter<TContext>` always commits on its own connection.
- The table is `AuditLogEntries` (unless `TableName` is set), with PascalCase columns, key `PK_{table}`, and indexes `IX_{table}_TenantTime`, `IX_{table}_TenantActionTime`, `IX_{table}_TenantEntityTime`, `IX_{table}_TenantActorTime`, `IX_{table}_TenantAccountTime`, and `IX_{table}_Correlation`, each ending in `(CreatedAt, Id)`.
- Timestamps keep full precision, as `datetimeoffset` by default or `datetime2` when `CreatedAtColumnType` says so.
- JSON columns are `nvarchar(max)`. `NvarcharMax` is the only accepted `JsonColumnType`.
- `AppendOnly` is supported. The trigger does not cover `TRUNCATE`. `PurgeAuthority.Owner` and `Partitioning` fail options validation. `MinimumRetention` is PostgreSQL only.

### Install

```bash
dotnet add package Headless.AuditLog.Storage.SqlServer
```

### Setup and use

```csharp
// Reuse the connection every Headless feature shares:
services.AddSqlServerSql(builder.Configuration.GetConnectionString("Default")!);
services.AddHeadlessAuditLog(setup => setup.UseSqlServer());

// Or give the audit log its own database and schema:
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(options => options.Schema = "compliance");
    setup.UseSqlServer(builder.Configuration.GetConnectionString("AuditLog")!);
});
```

The other overloads:

```csharp
setup.UseSqlServer(options =>
{
    options.ConnectionString = connectionString;
    options.CommandTimeout = TimeSpan.FromSeconds(60);
});

setup.UseSqlServer(builder.Configuration.GetSection("Headless:AuditLog:SqlServer"));

setup.UseSqlServer((options, sp) =>
    options.ConnectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("AuditLog")!);
```

Then run `dotnet app.dll resources provision` (or `deploy`) in the deployment. See [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

**Verify:** `dotnet app.dll resources verify -k Schema` reports the audit log steps `Ok`. Before provisioning, a host start fails naming `resources provision`.

### Configuration

`SqlServerAuditLogOptions`:

| Option | Default | Description |
|---|---|---|
| `ConnectionString` | (required) | SQL Server connection string. |
| `CommandTimeout` | 30 seconds | Applies to every write and read. |

### Runtime behavior

- `IAuditLog<TContext>`, `IAuditLogWriter<TContext>`, `IReadAuditLog<TContext>`, and `IAuditLogRetention<TContext>` are singletons.
- The retention deletes oldest-first on its own connection, between setting and clearing the `headless.audit_log_purge` session context key.
- If missing, it registers `TimeProvider.System`, null `ICurrentTenant` and `ICurrentUser` fallbacks, and a trace-id `ICorrelationIdProvider`. `AddHeadless()` replaces the tenant and user fallbacks with the HTTP implementations in either call order.
