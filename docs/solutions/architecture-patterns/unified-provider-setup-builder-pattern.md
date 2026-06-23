---
title: Unified Provider Setup Builder Pattern
date: 2026-05-25
last_updated: 2026-06-21
category: architecture-patterns
module: headless-provider-setup
problem_type: architecture_pattern
component: service_class
severity: high
related_components:
  - dotnet_entity
  - database
  - tooling
tags:
  - provider-setup
  - dependency-injection
  - setup-builder
  - exactly-one-provider
  - storage
  - coordination
  - entity-framework
  - raw-provider
applies_when:
  - Adding a provider-backed Headless.* package
  - Reviewing a Setup{Feature} root registration or provider Setup{Provider}{Feature} class
  - Wiring exactly one provider while keeping provider-specific options out of the root package
  - Separating shared feature options from provider or storage options
  - Deciding whether a feature needs a builder or a plain AddHeadless{Feature} extension
---

# Unified Provider Setup Builder Pattern

## Context

Every provider-backed package in the framework needs one consumer-facing grammar for selecting the implementation while keeping provider details out of the root package. Storage-bearing packages (`AuditLog`, `Settings`, `Permissions`, `Features`, `Identity`, `Messaging`) first exposed the problem: EF-backed implementations used bespoke DI extension shapes, raw ADO.NET providers (PostgreSql, SqlServer) either did not exist or were registered through entirely separate entry points, and there was no uniform invariant guarding against zero or duplicate provider registrations.

The refactor on branch `xshaheen/refactor-storage-initialization-unification` (PR #354, against `main`) collapsed storage features onto a single shape: one `AddHeadless{Feature}` extension that takes a `Configure<HeadlessXxxSetupBuilder>` callback, on which the consumer chains exactly one `setup.Use{Provider}()` call. Coordination now uses the same shape outside the storage domain: `AddHeadlessCoordination(setup => setup.UsePostgreSql(...))` with `HeadlessCoordinationSetupBuilder` and provider-owned `SetupPostgresCoordination`, `SetupSqlServerCoordination`, and `SetupRedisCoordination` classes.

The pattern is therefore broader than storage. The root feature package owns the shared builder, shared options, and exactly-one-provider invariant. Each provider package contributes services through a small domain-specific options-extension plug-in (`IAuditLogStorageOptionsExtension`, `ISettingsStorageOptionsExtension`, `IPermissionsStorageOptionsExtension`, `IFeaturesStorageOptionsExtension`, `ICoordinationProviderOptionsExtension`). Each feature owns its own interface to prevent cross-domain dependency leakage — a provider package for one feature cannot accidentally satisfy another feature's extension slot.

## Guidance

### 1. The `Setup{Feature}` extension shape

Use C# 14 extension members on `IServiceCollection` for the root entry and on `HeadlessXxxSetupBuilder` for provider entries. Public surface is one static `Setup{Feature}` class per package; the shared private helper is named `_Add{Feature}Core`, `_Add{Feature}ProviderCore`, or `_Add{Feature}StorageCore` depending on the domain language.

Root entry, `src/Headless.Settings.Core/Setup.cs`:

```csharp
public static class SetupCoreSettings
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

One per feature, internal-constructed, and narrow. The builder exposes only shared feature configuration plus provider registration. It should not become a central switchboard for provider-specific physical details.

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

For provider-backed primitives such as Coordination, prefer a builder like `HeadlessCoordinationSetupBuilder` or `HeadlessPermissionsSetupBuilder` over overload sprawl on `IServiceCollection`. The root builder can expose `Configure(IConfiguration)`, `Configure(Action<TOptions>)`, and `Configure(Action<TOptions, IServiceProvider>)`, while each provider package owns its own `Use{Provider}` overloads and provider option type.

Do not centralize provider-specific table or key names in the root feature package just because multiple providers exist. SQL Server and PostgreSQL commonly use different physical naming conventions, so provider options should own backend-specific defaults and override points unless the name is genuinely a logical cross-provider contract.

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

### 4. Coordination is the non-storage example

Coordination follows the same DI grammar even though it is a membership and liveness primitive, not a storage feature:

```csharp
services.AddHeadlessCoordination(setup =>
{
    setup.Configure(configuration.GetSection("Headless:Coordination"));
    setup.UsePostgreSql(configuration.GetConnectionString("Coordination")!);
});
```

`src/Headless.Coordination.Core/Setup.cs` owns `AddHeadlessCoordination` and the exactly-one-provider gate. `src/Headless.Coordination.Core/HeadlessCoordinationSetupBuilder.cs` owns shared `CoordinationOptions` configuration and `RegisterExtension(ICoordinationProviderOptionsExtension)`. Provider packages then contribute only provider-specific setup:

- `src/Headless.Coordination.PostgreSql/Setup.cs` exposes `SetupPostgresCoordination.UsePostgreSql(...)`
- `src/Headless.Coordination.SqlServer/Setup.cs` exposes `SetupSqlServerCoordination.UseSqlServer(...)`
- `src/Headless.Coordination.Redis/Setup.cs` exposes `SetupRedisCoordination.UseRedis(...)`

The root setup rejects zero providers, multiple providers in one callback, and repeated `AddHeadlessCoordination` calls on the same `IServiceCollection`. The repeated-registration guard matters because multiple calls can otherwise look valid locally while producing an ambiguous service collection globally. The focused regression suite is `tests/Headless.Coordination.Core.Tests.Unit/CoordinationSetupBuilderTests.cs`.

### 5. Caching adapts the invariant to per-slot exactly-one

Caching (`AddHeadlessCaching`, `src/Headless.Caching.Core/Setup.cs`) follows the same grammar but cannot use the global exactly-one-provider gate: one host legitimately composes several cache instances — a default cache, the memory/remote tiers of a default hybrid, any number of named instances, and cross-cutting extensions such as the distributed factory lock. `HeadlessCachingSetupBuilder` therefore keeps four contribution lists with a **per-slot** exactly-one invariant instead:

- **Default slot** — exactly one `Use{InMemory,Redis,Hybrid}`; zero or multiple throws, listing the available `Use*` calls.
- **Tier slots** — at most one provider per reserved role key (`memory`, `remote`); a tier role already claimed by the default provider (e.g. `UseRedis` + `AddRedisTier`, both claiming `remote`) throws.
- **Named slot** — unlimited `AddNamed(name, i => i.Use…(...))` instances; names must be unique, non-empty, and not a reserved role key, and each instance must select exactly one provider. `HeadlessCacheInstanceBuilder` is one shared type, not per-provider.
- **Cross-cutting slot** — unlimited extensions applied after all providers (`UseDistributedFactoryLock` from `Headless.Caching.DistributedLocks`).

Application order is tiers → default → named → cross-cutting, all deferred until the gates pass so a throwing setup leaves the service collection unchanged, and the repeated-`AddHeadlessCaching` sentinel matches coordination. The deviation pays for a real failure mode the global gate cannot express: the predecessor per-provider `Add*Cache(isDefault:)` extensions let two `isDefault: true` registrations silently race for the default `ICache` (first-wins `TryAddSingleton` plus role-key aliasing); the per-slot gate turns that into a hard registration-time error. The focused regression suite is `tests/Headless.Caching.Core.Tests.Unit/CachingSetupBuilderTests.cs`.

**Captcha is the second per-slot instance** (`AddHeadlessCaptcha`, `src/Headless.Captcha.Abstractions/Setup.cs`) and adds two wrinkles Caching does not have. It keeps a **default slot** — at most one `UseTurnstile` / `UseReCaptchaV2` / `UseReCaptchaV3`, resolving unkeyed *and* aliased under its canonical `CaptchaConstants` key — plus an unlimited **named slot** (`Use{Provider}("name", …)`, keyed-only); both are deferred behind an "at least one provider" gate and the repeated-`AddHeadlessCaptcha` sentinel. The wrinkles: (1) **provider sub-interfaces** — the base `ICaptchaVerifier` stays strictly pass/fail, while `IReCaptchaV3Verifier` (Score) and `ITurnstileVerifier` (CData) carry provider-only data on derived result types, so a consumer that needs vendor data injects the concrete interface rather than leaking it onto the shared contract; and (2) a **public by-name resolver**, `ICaptchaProvider.GetVerifier(name)` over `GetKeyedService<ICaptchaVerifier>(name)`, shipped as package surface (Caching's `KeyedServiceCacheProvider` is the same mechanism, kept internal). Per-provider internal singletons — the named `HttpClient` and the language-code provider — are keyed by the slot name so reCAPTCHA and Turnstile cannot collide on a first-wins `TryAdd` (the shadowing trap in the linked keyed-DI doc). Conformance is verified by an HTTP-stub harness rather than Testcontainers (the providers' happy path needs a human-solved token); see the linked harness doc. The focused suites are `tests/Headless.Captcha.{Turnstile,ReCaptcha}.Tests.Unit` over the shared `tests/Headless.Captcha.Tests.Harness`.

### 6. Shared `HeadlessDbContext` + `*EntityValidationStartupGate<TContext>`

When the EF provider is selected, the feature no longer owns a `DbContext`. The consumer's own `HeadlessDbContext` subclass is registered once via `AddHeadlessDbContext<TContext>` and opts into each feature's entities by calling `modelBuilder.AddHeadless{Feature}(storageOptions)` from `OnModelCreating`. A `*EntityValidationStartupGate<TContext>` hosted service (registered automatically by the EF extension) hard-fails at host start if the consumer forgot, with a precise error naming the missing `modelBuilder.AddHeadless{Feature}` call.

This replaces the predecessor pattern from PR #327, which used per-context `IModelCacheKeyFactory` implementations and a `ReplaceService<IModelCacheKeyFactory>` consumer recipe. The factory approach worked but added a surface that consumers had to learn and that did not isolate compiled models across distinct service-provider instances (an EF internal limitation). The startup-gate pattern is simpler, fails earlier, and produces a sharper diagnostic. (session history)

### 7. `IDbContextFactory<TDbContext>` registration alongside `AddHeadlessDbContext`

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

### 8. Raw stores enrolled in the consumer's DbContext transaction

`IAmbientDbTransactionAccessor` lives in `Headless.AuditLog.Abstractions` with a BCL-only signature returning `(DbConnection?, DbTransaction?)`. The EF implementation lives in `Headless.Orm.EntityFramework` and is registered by `AddHeadlessDbContextServices`. Raw writers accept optional `NpgsqlConnection`/`SqlConnection` + transaction; when the accessor resolves a same-provider ambient connection, the store reuses it for true atomicity with the consumer's `SaveChanges`. On provider mismatch the store falls back to its own connection and emits a deduplicated warning (see Doc B for the dedup discipline).

### 9. Raw provider packages do not depend on EF

The shared setup types (`HeadlessXxxSetupBuilder`, `XxxStorageOptions`, `AddHeadlessXxx`) live in `*.Core` or `*.Abstractions`, never in `*.Storage.EntityFramework`. This is the only arrangement that lets raw ADO.NET providers reference only `*.Core` without dragging EF into ADO-only packages. The fix was applied consistently across Features, Settings, Permissions, and AuditLog. (session history)

## Why This Matters

- **One mental model** across audit-log, settings, permissions, features, identity, coordination, and caching. Adding a provider is a `Setup{Feature}{Provider}` / `Setup{Provider}{Feature}` class with `Use{Provider}` extension members and one domain-specific options-extension implementation (`I{Feature}StorageOptionsExtension`, `ICoordinationProviderOptionsExtension`, `ICacheProviderOptionsExtension`, etc.).
- **Exactly-one-provider invariant** is enforced uniformly. The error message lists every available `Use…` call so misconfiguration is recoverable from the exception alone.
- **Provider ownership prevents central leakage.** Backend-specific names, connection settings, initializers, stores, scripts, and validators stay in the provider package instead of becoming a root-package switch statement.
- **Raw providers compose with consumer transactions** without taking an EF dependency. The accessor abstraction keeps the contract on BCL types, so audit-package surface stays narrow.
- **Factory registration removes a footgun.** Without `HeadlessDbContextFactory<TDbContext>`, EF readers consuming `IDbContextFactory<TContext>` fail at first request with a confusing "no service registered"; `AddDbContextFactory` is unsafe to add because it conflicts with `HeadlessDbContext`'s required scoped ctor parameters.
- **Startup gates beat compile-time tricks.** The EF model-builder's previous early-return-on-existing-entity guard skipped configuration silently when a consumer's `DbContext` already had the entity via conventions. Removing the guard and asserting full mapping at start via the gate produces an actionable error instead of a runtime mismatch. (session history)

## When to Apply

Apply this shape to any new feature package that:

- Has more than one valid backend or provider.
- Needs one consumer-facing root registration but provider-owned implementation details.
- Has shared feature options plus provider-specific options that need separate configurators.
- Must fail fast when zero or multiple providers are configured.
- Needs provider packages to remain dependency-isolated from each other.

Skip this shape when the feature has exactly one possible backend and zero configurability — a plain `Add{Feature}` extension is enough.

## Examples

| Feature | Root setup | Builder | Provider extension interface | Provider packages |
| --- | --- | --- | --- | --- |
| AuditLog | `src/Headless.AuditLog.Abstractions/Setup.cs` | `HeadlessAuditLogSetupBuilder.cs` | `IAuditLogStorageOptionsExtension` | EF, PostgreSql, SqlServer |
| Settings | `src/Headless.Settings.Core/Setup.cs` (`SetupCoreSettings`) | `HeadlessSettingsSetupBuilder.cs` | `ISettingsStorageOptionsExtension` | EF, PostgreSql, SqlServer |
| Permissions | `src/Headless.Permissions.Core/Setup.cs` | `HeadlessPermissionsSetupBuilder.cs` | `IPermissionsStorageOptionsExtension` | EF, PostgreSql, SqlServer |
| Features | `src/Headless.Features.Core/Setup.cs` | `HeadlessFeaturesSetupBuilder.cs` | `IFeaturesStorageOptionsExtension` | EF, PostgreSql, SqlServer |
| Identity | `src/Headless.Identity.Storage.EntityFramework/Setup.cs` | `HeadlessIdentitySetupBuilder.cs` | package-local identity storage extension | EF |
| Coordination | `src/Headless.Coordination.Core/Setup.cs` | `HeadlessCoordinationSetupBuilder.cs` | `ICoordinationProviderOptionsExtension` | PostgreSql, SqlServer, Redis |
| Caching | `src/Headless.Caching.Core/Setup.cs` (`SetupCachingCore`) | `HeadlessCachingSetupBuilder.cs` | `ICacheProviderOptionsExtension` | InMemory, Redis, Hybrid (+ DistributedLocks as cross-cutting) |
| Captcha | `src/Headless.Captcha.Abstractions/Setup.cs` (`SetupCaptcha`) | `HeadlessCaptchaSetupBuilder.cs` | per-slot deferred actions + `ICaptchaProvider` by-name resolver | ReCaptcha (v2/v3), Turnstile |
| Blobs | `src/Headless.Blobs.Core/Setup.cs` (`SetupBlobsCore`) | `HeadlessBlobsSetupBuilder.cs` | per-slot deferred actions + `IBlobStorageProvider` by-name resolver | Aws, Azure, CloudflareR2, FileSystem, Redis, SshNet |

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
- [Messaging keyed-DI lock isolation](messaging-keyed-di-lock-isolation.md) — `TryAdd*` shadowing trap applies to multi-feature storage hosts and to captcha's per-provider HttpClient/language-provider keying
- [Named-instance keyed-provider registration](named-instance-keyed-provider-registration.md) — the per-instance implementation recipe (named-options + keyed-factory + per-instance dependencies) layered on this builder contract; used by Blobs
- [HTTP-stub cross-provider conformance harness](../best-practices/http-stub-conformance-harness.md) — how the per-slot captcha providers are conformance-tested without a reachable backend
- [Transport wrapper drift and doc sync](../messaging/transport-wrapper-drift-and-doc-sync.md) — greenfield rename rationale and the per-package README sync chore that follows refactors of this shape
- Source-of-truth brainstorm and plan (on branch `xshaheen/refactor-storage-initialization-unification`):
  - `docs/brainstorms/2026-05-24-storage-initialization-unification-requirements.md`
  - `docs/plans/2026-05-24-001-refactor-storage-initialization-unification-plan.md`
- Coordination membership substrate plan that reused the builder pattern:
  - `docs/plans/2026-06-06-001-feat-coordination-membership-substrate-plan.md`
  - `docs/test-plans/2026-06-07-feat-coordination-membership-substrate-test-plan.md`
