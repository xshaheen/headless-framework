---
title: "Provider setup classes and the options pattern"
date: 2026-09-18
last_updated: 2026-09-29
module: headless-framework
problem_type: convention
component: dependency_injection
severity: high
tags: [dependency-injection, setup-builder, options, fluentvalidation, relational-storage, schema]
related_components:
  - provider_packages
  - Headless.Hosting
applies_when:
  - "Adding a provider package and its registration surface"
  - "Adding or validating an options class"
  - "Deciding which registration overloads a new Use* or Add* member needs"
  - "Adding a relational storage provider: its schema default, object names, initializer locks, or connection overloads"
---

# Provider setup and options

## Setup classes

Every provider package exposes a single static `Setup{Provider}` class in `Setup.cs` at the package root.

Multi-provider features follow the [unified provider setup builder
pattern](../architecture-patterns/unified-provider-setup-builder-pattern.md): the feature's Core package owns
the root `AddHeadless{Feature}(Action<Headless{Feature}SetupBuilder>)` entry plus the provider gates, and each
provider package contributes `Use{Provider}` extension members on that builder.

```csharp
public static class SetupRedisCache
{
    extension(HeadlessCachingSetupBuilder setup) // C# 14 extension members
    {
        public HeadlessCachingSetupBuilder UseRedis(IConfiguration configuration) { ... }
        public HeadlessCachingSetupBuilder UseRedis(Action<TOptions> setupAction) { ... }
        public HeadlessCachingSetupBuilder UseRedis(Action<TOptions, IServiceProvider> setupAction) { ... }
    }

    private static IServiceCollection _AddCacheCore(...) { /* shared wiring */ }
}
```

Single-backend packages with no provider choice keep plain `Add{Feature}` extensions on `IServiceCollection`,
with the same overload trio. Name the shared private helper `_Add{Feature}Core`.

Setup classes live in the family root namespace, not the provider's own namespace. See
[Namespace policy § tier 2](namespace-policy.md#three-tier-placement).

### When the overload trio does not apply

The trio applies to **provider** `Use{Provider}` and `Add{Feature}` members that bind that backend's options.

**Cross-cutting consumer extensions** adapt an already-composed feature. `UseOutputCache` and `UseBclCache`
consume a named `ICache` rather than supply a provider, so they expose a single `Action<TOptions>` overload plus
the consumed feature's builder. They bind no provider option section, so the `IConfiguration` and
`Action<TOptions, IServiceProvider>` overloads do not apply.

## Options

Validate options only when they need it. When they do, use FluentValidation through the `Headless.Hosting`
extensions — `AddOptions<TOptions, TValidator>()` and `Configure<TOptions, TValidator>(...)` — rather than a
custom `IValidateOptions<T>`.

- Put an `internal sealed class {OptionsName}Validator : AbstractValidator<{OptionsName}>` in the same file as
  the options class, directly below it, when any property needs validation.
- Register the validator through DI with `services.Configure<TOption, TValidator>(action)` or
  `services.AddOptions<TOption, TValidator>()` from `Headless.Hosting`. Both wire up FluentValidation and
  `ValidateOnStart()`.
- Never call `new Validator().ValidateAndThrow()` by hand. Use the DI pipeline.
- Higher-level bootstrap APIs may auto-bind required options from the sections they own (for example
  `Headless:*`) when that binding is part of the package contract.
- When options are required, offer no parameterless registration overload. Require the options and delegate to
  the optioned path. The one exception is a relational storage provider's parameterless `UsePostgreSql()` /
  `UseSqlServer()`: it still requires the connection string, and it sources it from the shared `Headless.Sql`
  registration. See [Relational storage providers](#relational-storage-providers).

## Relational storage providers

Every raw PostgreSQL and SQL Server storage provider follows these rules, so one connection and one schema serve
every Headless feature in an application.

### Default schema

The feature's storage options default `Schema` to `HeadlessStorageDefaults.Schema` (`"headless"`, in
`Headless.Hosting.Initialization`). Do not declare a per-feature `DefaultSchema` constant. The feature owns the
setting on its storage options (`ConfigureStorage(o => o.Schema = ...)`), so overriding one feature's schema never
moves another's. A feature with no storage options type, such as Sequences, puts `Schema` on each provider's
options with the same default.

### Feature-prefixed object names

Every table, sequence, index, and constraint the feature creates carries the feature in its name, because
PostgreSQL index and constraint names are unique per schema, not per table.

The rule is one casing per database: PascalCase on SQL Server for tables, columns, and sequences, with
constraint and index names derived from them (`PK_FencingLeases`, `IX_FencingLeases_ActiveExpiry`,
`CK_IdempotencyRecords_Status`, `DF_FencingLeases_TakeoverCount`), and unquoted snake_case on PostgreSQL
(`fencing_leases`, `ix_fencing_leases_active_expiry`). A new feature follows it on both providers.

Messaging, Coordination, Fencing, Idempotency, Sequences, Features, Permissions, Settings, AuditLog, and Jobs follow
the rule on both providers (`messaging_published` / `MessagingPublished`, `fencing_leases` / `FencingLeases`,
`idempotency_record_generations` / `IdempotencyRecordGenerations`, `sequences` / `Sequences`,
`feature_values` / `FeatureValues`, `permission_grants` / `PermissionGrants`, `setting_values` / `SettingValues`,
`audit_log_entries` / `AuditLogEntries`, `cron_jobs` / `CronJobs`). The DistributedLocks SQL Server fence sequence is
`DistributedLocksFence_{KeyPrefix}`, the prefix kept verbatim after normalization so distinct prefixes never share a
sequence; its PostgreSQL counterpart stays `headless_distributed_locks_fence`, one sequence for every prefix.

#### One naming source for raw SQL and EF Core

A feature that ships both a raw provider and an EF Core mapping derives every name from the same place, so a
database provisioned by one reads correctly through the other. `Headless.Hosting.Initialization` owns it:

- `StorageNamingStyle` is `PascalCase` or `SnakeCase`. A raw provider hardcodes its database's style;
  `HeadlessStorageNaming.ForProvider(DbContext.Database.ProviderName)` returns `SnakeCase` for Npgsql and
  `PascalCase` for every other EF provider.
- `HeadlessStorageNaming.Apply(style, "ProviderKey")` converts one PascalCase part. `PrimaryKeyName(style, table)`
  and `IndexName(style, table, "ProviderName", "ProviderKey")` build `pk_`/`PK_` and `ix_`/`IX_` names from
  converted parts around the resolved table name, which they never convert.
- The EF mapping sets the table, every column (`HasColumnName`, applied last so it also renames columns that
  `ConfigureHeadlessConvention()` and `TryConfigureExtraProperties()` added), the key (`HasKey(...).HasName`), and
  every index (`HasDatabaseName`, with filters quoting the converted column). The model-builder entry point that
  takes a `DbContext` detects the style from `Database.ProviderName`; the one that takes options also takes a
  required `StorageNamingStyle`.
- Configurable table names are nullable. `null` means the convention's default
  (`HeadlessStorageNaming.Resolve(configured, style, "FeatureValues")`), a configured name is used verbatim,
  validators check it only when set, and derived key and index names embed it unchanged (`pk_MyValues`).
- A configured table name is refused when its longest derived PostgreSQL name would pass 63 bytes, since
  PostgreSQL truncates rather than rejects it. The feature's Core package lists each table's index name parts once
  (`FeaturesStorageNames`); the EF mapping builds its index names from them, and every provider's options validator
  passes them to `FitsDerivedPostgreSqlNames` (namespace `FluentValidation`, package `Headless.Hosting`). The
  conventional default names must fit too: the permission grants index for host grants ends in `_no_tenant`, because
  `_null_tenant_id` made its default name 67 bytes.
- A parity test per provider creates the schema with the raw initializer and compares the catalog's columns and
  index names with the EF model built for that provider. Features and Settings have one in their conformance
  harness; Permissions and AuditLog have one in each provider's integration project.

- A noun unique to the feature's family already counts as the prefix: `CronJobs`, `FeatureValues`,
  `PermissionGrants`, and `DistributedLocksFence_*` need no extra `jobs_` or `features_`.
- Derive index and constraint names from the table name, not the schema: `IX_CronJobs_Function_Expression`,
  `ix_{TableName}_tenant_time`. When the table name is configurable, validate its length so the longest derived
  name fits PostgreSQL's 63-byte identifier limit; AuditLog's longest, `ix_{TableName}_tenant_account_time`, caps
  `TableName` at 40 characters.

### Initializer locks

- Namespace the feature's init lock by feature and key it on the objects it owns:
  `headless_{feature}_init:{schema}` or `headless_{feature}_init:{schema}.{table}`. Two features in one schema must
  never share a lock resource by accident.
- Every PostgreSQL initializer also takes the schema-wide lock immediately before `CREATE SCHEMA IF NOT EXISTS`,
  built only through `PostgreSqlSchemaInitLock.AcquireStatement(schema)` in `Headless.Sql.PostgreSql`, which owns
  the lock key. Without it, two features creating the shared schema at the same moment collide, and the loser's
  rollback silently discards its DDL.
- A creator outside these locks (a consumer's EF migration) can still commit the schema first. An initializer
  that absorbs `42P06 / 42P07 / 42710 / 23505` reruns its DDL once in a fresh transaction and lets a second
  failure propagate; it never reports success after a rollback alone.
- An initializer that runs `CREATE INDEX CONCURRENTLY` polls `pg_try_advisory_lock` for its session lock instead
  of blocking in `pg_advisory_lock`.

[Storage initializer lifecycle](../best-practices/storage-initializer-lifecycle-correctness.md) explains the
PostgreSQL rules.

### Parameterless connection overload

Each raw provider adds a parameterless `UsePostgreSql()` / `UseSqlServer()` next to its overload trio. It delegates
to the `(options, IServiceProvider)` overload and reads the connection string registered by
`AddPostgreSqlSql` / `AddSqlServerSql`:

```csharp
public HeadlessFencingSetupBuilder UsePostgreSql()
{
    return setup.UsePostgreSql(
        (options, services) => options.ConnectionString = services.GetPostgreSqlConnectionString()
    );
}
```

`GetPostgreSqlConnectionString()` and `GetSqlServerConnectionString()` (namespace `Headless.Sql`) throw
`InvalidOperationException` when no `ISqlConnectionFactory` is registered or the registered one belongs to the
other provider. Because the read happens at options resolution, registration order does not matter. EF Core
storage variants and Jobs take their connection from the `DbContext` and have no such overload. The consumer
contract is in [docs/llms/sql.md](../../llms/sql.md#shared-connection-and-schema-for-storage-features).
