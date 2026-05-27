---
title: Unified Storage Setup Pattern
date: 2026-05-25
category: architecture-patterns
module: headless-storage
problem_type: architecture_pattern
component: service_class
severity: high
related_components:
  - dotnet_entity
  - database
  - tooling
tags:
  - storage
  - dependency-injection
  - entity-framework
  - setup-builder
  - exactly-one-provider
  - dbcontext-factory
  - raw-ddl
applies_when:
  - Adding a new storage-bearing Headless.* package
  - Reviewing a Setup{Feature} extension class
  - Wiring multiple Headless storage packages in one host
  - Routing one feature to a custom HeadlessDbContext and another to a raw provider
  - Deciding between Mode 1 (shared EF context) and Mode 2 (raw DDL) per package
---

# Unified Storage Setup Pattern

## Context

Every storage-bearing package in the framework (`AuditLog`, `Settings`, `Permissions`, `Features`, `Identity`, `Messaging`) historically wired its EF-backed implementation through a bespoke DI extension shape, and raw ADO.NET providers (PostgreSql, SqlServer) either did not exist or were registered through entirely separate entry points. Consumers learned a new grammar per feature, raw provider packages took accidental dependencies on EF infrastructure, and there was no uniform invariant guarding against zero or duplicate provider registrations.

The refactor on branch `xshaheen/refactor-storage-initialization-unification` (PR #354, against `main`) collapses every feature onto a single shape: one `AddHeadless{Feature}` extension that takes a `Configure<HeadlessXxxSetupBuilder>` callback, on which the consumer chains exactly one `setup.Use{Provider}()` call. The builder funnels storage options and storage extensions into the same `_Add{Feature}StorageCore` helper regardless of provider, and each provider package contributes services through a small domain-specific options-extension plug-in (`IAuditLogStorageOptionsExtension`, `ISettingsStorageOptionsExtension`, `IPermissionsStorageOptionsExtension`, `IFeaturesStorageOptionsExtension`). Each feature owns its own interface to prevent cross-domain dependency leakage — a provider package for one feature cannot accidentally satisfy another feature's extension slot.

## Guidance

### 1. The `Setup{Feature}` extension shape

Use C# 14 extension members on `IServiceCollection` for the root entry and on `HeadlessXxxSetupBuilder` for provider entries. Public surface is one static `Setup{Feature}` class per package; the shared private helper is named `_Add{Feature}Core` / `_Add{Feature}StorageCore`.

Root entry, `src/Headless.Settings.Core/Setup.cs`:

```csharp
public static class CoreSettingsSetup
{
    extension(IServiceCollection services)
    {
        public HeadlessSettingsBuilder AddHeadlessSettings(Action<HeadlessSettingsSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);
            var setup = new HeadlessSettingsSetupBuilder(services);
            configure(setup);
            return _AddSettingsStorageCore(services, setup);
        }
    }

    private static HeadlessSettingsBuilder _AddSettingsStorageCore(
        IServiceCollection services,
        HeadlessSettingsSetupBuilder setup
    )
    {
        if (setup.Extensions.Count != 1)
        {
            throw new InvalidOperationException(setup.Extensions.Count == 0
                ? "Headless.Settings requires exactly one storage provider. Call one of `UseEntityFramework`, `UsePostgreSql`, or `UseSqlServer`."
                : "Headless.Settings requires exactly one storage provider. Multiple storage providers were configured.");
        }

        services.Configure<SettingsStorageOptions, SettingsStorageOptionsValidator>(o => {
            o.Schema = setup.StorageOptions.Schema;
            o.SettingValuesTableName = setup.StorageOptions.SettingValuesTableName;
            o.SettingDefinitionsTableName = setup.StorageOptions.SettingDefinitionsTableName;
        });

        foreach (var extension in setup.Extensions) extension.AddServices(services);
        return new HeadlessSettingsBuilder(services);
    }
}
```

The exactly-one-provider gate is the load-bearing invariant. The error message lists every available `Use{Provider}` call so misconfiguration fails fast at registration with an actionable hint.

### 2. The `HeadlessXxxSetupBuilder`

One per feature, internal-constructed, exposing three verbs:

```csharp
public sealed class HeadlessAuditLogSetupBuilder
{
    internal IServiceCollection Services { get; }
    internal AuditLogStorageOptions StorageOptions { get; } = new();
    internal Action<AuditLogOptions>? OptionsConfigurator { get; private set; }
    internal IList<IAuditLogStorageOptionsExtension> Extensions { get; } = new List<IAuditLogStorageOptionsExtension>();

    public HeadlessAuditLogSetupBuilder ConfigureOptions(Action<AuditLogOptions> configure) { ... }
    public HeadlessAuditLogSetupBuilder ConfigureStorage(Action<AuditLogStorageOptions> configure) { ... }
    public void RegisterExtension(IAuditLogStorageOptionsExtension extension) => Extensions.Add(extension);
}
```

Cross-cutting feature options (capture strategy, defaults, etc.) flow through `ConfigureOptions`; storage-layer options (schema, table names) flow through `ConfigureStorage`. Provider packages call `RegisterExtension`.

### 3. Provider packages contribute through domain-specific options extensions

Each provider defines a `Setup{Feature}{Provider}` class with `extension(HeadlessXxxSetupBuilder setup)` extension members exposing `Use{Provider}`. The extension method only adds a domain-specific options extension (`IAuditLogStorageOptionsExtension` for audit-log providers, `ISettingsStorageOptionsExtension` for settings providers, etc.) to the builder; the extension's `AddServices(IServiceCollection)` runs later from `_Add{Feature}StorageCore`, so every provider passes through the same one-provider gate.

`src/Headless.AuditLog.Storage.PostgreSql/Setup.cs`:

```csharp
public static class SetupAuditLogPostgreSql
{
    extension(HeadlessAuditLogSetupBuilder setup)
    {
        public HeadlessAuditLogSetupBuilder UsePostgreSql(string connectionString) { ... }

        public HeadlessAuditLogSetupBuilder UsePostgreSql(Action<PostgreSqlAuditLogOptions> configure)
        {
            setup.RegisterExtension(new PostgreSqlAuditLogOptionsExtension(configure));
            return setup;
        }
    }

    private sealed class PostgreSqlAuditLogOptionsExtension(Action<PostgreSqlAuditLogOptions> configure)
        : IAuditLogStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.Configure<PostgreSqlAuditLogOptions, PostgreSqlAuditLogOptionsValidator>(configure);
            services.AddInitializerHostedService<PostgreSqlAuditLogStorageInitializer>();
            services.TryAddSingleton<PostgreSqlAuditLogWriter>();
            services.TryAddScoped<IAuditLogStore, PostgreSqlAuditLogStore>();
            services.TryAddSingleton(typeof(IAuditLog<>), typeof(PostgreSqlAuditLog<>));
        }
    }
}
```

EF providers parameterize on `TContext` and register repositories via `MakeGenericType` so the consumer's own `DbContext` carries the entities:

```csharp
public HeadlessSettingsSetupBuilder UseEntityFramework<TContext>() where TContext : DbContext
    => setup.RegisterExtension(new EntityFrameworkSettingsOptionsExtension(typeof(TContext)));

private sealed class EntityFrameworkSettingsOptionsExtension(Type dbContextType) : ISettingsStorageOptionsExtension
{
    public void AddServices(IServiceCollection services)
    {
        services.TryAddSingleton(typeof(ISettingValueRecordRepository),
            typeof(EfSettingValueRecordRepository<>).MakeGenericType(dbContextType));
        services.TryAddSingleton(typeof(ISettingDefinitionRecordRepository),
            typeof(EfSettingDefinitionRecordRepository<>).MakeGenericType(dbContextType));
        services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IHostedService),
            typeof(SettingsEntityValidationStartupGate<>).MakeGenericType(dbContextType)));
    }
}
```

### 4. Shared `HeadlessDbContext` + `*EntityValidationStartupGate<TContext>`

When the EF provider is selected, the feature no longer owns a `DbContext`. The consumer's own `HeadlessDbContext` subclass is registered once via `AddHeadlessDbContext<TContext>` and opts into each feature's entities by calling `modelBuilder.AddHeadless{Feature}(storageOptions)` from `OnModelCreating`. A `*EntityValidationStartupGate<TContext>` hosted service (registered automatically by the EF extension) hard-fails at host start if the consumer forgot, with a precise error naming the missing `modelBuilder.AddHeadless{Feature}` call.

This replaces the predecessor pattern from PR #327, which used per-context `IModelCacheKeyFactory` implementations and a `ReplaceService<IModelCacheKeyFactory>` consumer recipe. The factory approach worked but added a surface that consumers had to learn and that did not isolate compiled models across distinct service-provider instances (an EF internal limitation). The startup-gate pattern is simpler, fails earlier, and produces a sharper diagnostic. (session history)

### 5. `IDbContextFactory<TDbContext>` registration alongside `AddHeadlessDbContext`

`HeadlessDbContext` is intentionally non-poolable — it holds private per-request `HeadlessDbContextRuntime` state and uses a non-standard constructor — so `AddDbContextFactory` / `AddPooledDbContextFactory` cannot compose with it. EF readers (`EfReadAuditLog`, every `*EntityValidationStartupGate`) still need a factory, so `AddHeadlessDbContextServices` registers an internal `HeadlessDbContextFactory<TDbContext>` that wraps the scoped DI lifetime.

```csharp
internal sealed class HeadlessDbContextFactory<TDbContext>(IServiceScopeFactory scopeFactory)
    : IDbContextFactory<TDbContext> where TDbContext : HeadlessDbContext
{
    public TDbContext CreateDbContext()
    {
        var scope = scopeFactory.CreateScope();
        try
        {
            var context = scope.ServiceProvider.GetRequiredService<TDbContext>();
            context.OwnedScope = scope;
            return context;
        }
        catch { scope.Dispose(); throw; }
    }
}
```

The scope is transferred to the context's `OwnedScope` property and disposed alongside the context. The `catch { scope.Dispose(); throw; }` guards the ctor-throws path so a failed resolution does not leak the scope. See Doc B (`storage-initializer-lifecycle-correctness.md`) for the matching dispose discipline.

### 6. Raw stores enrolled in the consumer's DbContext transaction

`IAmbientDbTransactionAccessor` lives in `Headless.AuditLog.Abstractions` with a BCL-only signature returning `(DbConnection?, DbTransaction?)`. The EF implementation lives in `Headless.Orm.EntityFramework` and is registered by `AddHeadlessDbContextServices`. Raw writers accept optional `NpgsqlConnection`/`SqlConnection` + transaction; when the accessor resolves a same-provider ambient connection, the store reuses it for true atomicity with the consumer's `SaveChanges`. On provider mismatch the store falls back to its own connection and emits a deduplicated warning (see Doc B for the dedup discipline).

### 7. Raw provider packages do not depend on EF

The shared setup types (`HeadlessXxxSetupBuilder`, `XxxStorageOptions`, `AddHeadlessXxx`) live in `*.Core` or `*.Abstractions`, never in `*.Storage.EntityFramework`. This is the only arrangement that lets raw ADO.NET providers reference only `*.Core` without dragging EF into ADO-only packages. The fix was applied consistently across Features, Settings, Permissions, and AuditLog. (session history)

## Why This Matters

- **One mental model** across audit-log, settings, permissions, features, identity. Adding a new provider (SQLite, MySQL) is a `Setup{Feature}{Provider}` class with `Use{Provider}` extension members and one domain-specific options-extension implementation (`I{Feature}StorageOptionsExtension`).
- **Exactly-one-provider invariant** is enforced uniformly. The error message lists every available `Use…` call so misconfiguration is recoverable from the exception alone.
- **Raw providers compose with consumer transactions** without taking an EF dependency. The accessor abstraction keeps the contract on BCL types, so audit-package surface stays narrow.
- **Factory registration removes a footgun.** Without `HeadlessDbContextFactory<TDbContext>`, EF readers consuming `IDbContextFactory<TContext>` fail at first request with a confusing "no service registered"; `AddDbContextFactory` is unsafe to add because it conflicts with `HeadlessDbContext`'s required scoped ctor parameters.
- **Startup gates beat compile-time tricks.** The EF model-builder's previous early-return-on-existing-entity guard skipped configuration silently when a consumer's `DbContext` already had the entity via conventions. Removing the guard and asserting full mapping at start via the gate produces an actionable error instead of a runtime mismatch. (session history)

## When to Apply

Apply this shape to any new feature package that:

- Has more than one valid persistence backend (e.g. EF + raw provider, or multiple raw providers)
- Wants to support both a shared consumer-owned `HeadlessDbContext` and standalone storage
- Has storage-level options (schema, table names, column types) plus cross-cutting feature options that need separate configurators

Skip this shape when the feature has exactly one possible backend and zero configurability — a plain `Add{Feature}` extension is enough.

## Examples

| Feature | Root setup | Builder | EF provider | Raw PG provider | Raw SqlServer provider |
| --- | --- | --- | --- | --- | --- |
| AuditLog | `src/Headless.AuditLog.Abstractions/Setup.cs` | `HeadlessAuditLogSetupBuilder.cs` | `src/Headless.AuditLog.Storage.EntityFramework/Setup.cs` | `src/Headless.AuditLog.Storage.PostgreSql/Setup.cs` | `src/Headless.AuditLog.Storage.SqlServer/Setup.cs` |
| Settings | `src/Headless.Settings.Core/Setup.cs` (`CoreSettingsSetup`) | `HeadlessSettingsSetupBuilder.cs` | `src/Headless.Settings.Storage.EntityFramework/Setup.cs` | `src/Headless.Settings.Storage.PostgreSql/Setup.cs` | `src/Headless.Settings.Storage.SqlServer/Setup.cs` |
| Permissions | `src/Headless.Permissions.Core/Setup.cs` | `HeadlessPermissionsSetupBuilder.cs` | `src/Headless.Permissions.Storage.EntityFramework/Setup.cs` | `src/Headless.Permissions.Storage.PostgreSql/Setup.cs` | `src/Headless.Permissions.Storage.SqlServer/Setup.cs` |
| Features | `src/Headless.Features.Core/Setup.cs` | `HeadlessFeaturesSetupBuilder.cs` | `src/Headless.Features.Storage.EntityFramework/Setup.cs` | `src/Headless.Features.Storage.PostgreSql/Setup.cs` | `src/Headless.Features.Storage.SqlServer/Setup.cs` |
| Identity | `src/Headless.Identity.Storage.EntityFramework/Setup.cs` | `HeadlessIdentitySetupBuilder.cs` | (same package) | — | — |

Consumer call site, audit-log:

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureOptions(o => o.IsEnabled = true);
    setup.ConfigureStorage(o => { o.Schema = "audit"; o.TableName = "audit_log"; });
    setup.UsePostgreSql(builder.Configuration.GetConnectionString("Audit")!);
});
```

To swap providers, change one line (`setup.UseSqlServer(...)`); nothing else moves.

## Related

- [Storage Initializer Lifecycle & Concurrent-Startup Safety](../best-practices/storage-initializer-lifecycle-correctness.md) — sibling doc covering runtime correctness of the `*StorageInitializer` and `HeadlessDbContext` dispose paths
- [Writing a Headless Messaging Transport Provider](../guides/messaging-transport-provider-guide.md) — the original `IXOptionsExtension` + `Setup{Provider}` template this pattern generalizes
- [Messaging keyed-DI lock isolation](messaging-keyed-di-lock-isolation-2026-05-19.md) — `TryAdd*` shadowing trap applies to multi-feature storage hosts
- [Transport wrapper drift and doc sync](../messaging/transport-wrapper-drift-and-doc-sync.md) — greenfield rename rationale and the per-package README sync chore that follows refactors of this shape
- Source-of-truth brainstorm and plan (on branch `xshaheen/refactor-storage-initialization-unification`):
  - `docs/brainstorms/2026-05-24-storage-initialization-unification-requirements.md`
  - `docs/plans/2026-05-24-001-refactor-storage-initialization-unification-plan.md`
