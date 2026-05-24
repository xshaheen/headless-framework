---
title: Storage Initialization Unification (Settings/Permissions/Features/Identity/Audit + Messaging renames)
type: refactor
status: active
date: 2026-05-24
origin: docs/brainstorms/2026-05-24-storage-initialization-unification-requirements.md
---

# Storage Initialization Unification

## Summary

Apply the unified per-package builder shape (`AddHeadlessX(s => s.Use…)`) to Settings, Permissions, Features, Identity, and Audit using the existing `MessagingSetupBuilder` + `IMessagesOptionsExtension` pattern as the template (lifted to a shared `IStorageOptionsExtension` in `Headless.Hosting` to avoid 5× plumbing duplication). Each affected feature ships **Mode 1** (shared EF — consumer's `DbContext` with `modelBuilder.AddHeadlessX(options)`, `context.Set<TEntity>()` + `AsNoTracking` reads via `IDbContextFactory<TContext>` for read-side and stateless-write paths; **AuditLog write-side stays scoped on the consumer's `TContext`** to preserve transactional atomicity) and, where applicable, **Mode 2** raw-DDL per provider (Postgres, SqlServer) via per-feature `IStorageInitializer` that itself implements `IHostedService + IInitializer` (registered via the existing `AddInitializerHostedService<T>` primitive in `Headless.Hosting` — no per-feature `XBootstrapper` wrapper class). Mode 1 missing-entity diagnostic uses `IModel.FindEntityType` inspection (no DB round-trip). The `IXxxDbContext` interface, the `IModelCacheKeyFactory` ritual, the dedicated `SettingsDbContext`/`FeaturesDbContext`/`PermissionsDbContext` types, and the compound `IServiceCollection` extension names are deleted. The existing `Headless.Messaging.PostgreSql` / `.SqlServer` packages are renamed to the unified `Headless.Messaging.Storage.<Provider>` shape (bundled with the Identity Setup-class rename into U5); `Headless.AuditLog.EntityFramework` is renamed to `Headless.AuditLog.Storage.EntityFramework`. **Jobs unification is explicitly deferred** to a follow-up plan per user direction (Jobs' `<TTimeJob, TCronJob>` generic story threads through six packages beyond `Headless.Jobs.EntityFramework`, requires its own scoping).

---

## Problem Frame

The framework today registers storage three different ways depending on package, with one path (dedicated `XxxDbContext` shipped by the framework but unmigrated) acting as a documented trap. Consumers wiring multiple storage packages must learn multiple idioms, and the shared-context happy path leaks framework `DbSet`s into the consumer's public `DbContext` API. See origin document `## Problem Frame` for the full pain narrative.

---

## Requirements

This plan inherits R1–R19 from origin (`docs/brainstorms/2026-05-24-storage-initialization-unification-requirements.md`). Jobs-specific items (R17, R14's Jobs entries, R15's `Headless.Jobs.EntityFramework` rename) are explicitly deferred — see **Scope Boundaries → Deferred to Follow-Up Work**. All other R-IDs apply unchanged.

**Origin actors:** A1 (consumer adopting one package), A2 (consumer adopting multiple packages), A3 (consumer in a locked-down environment), A4 (consumer with no EF investment), A5 (microservice / bounded-context consumer), A6 (framework / provider package author).
**Origin flows:** F1 (Mode 1 happy path), F2 (Mode 2 no-EF path), F3 (operational package with optional Mode 1 enlistment), F4 (microservice / bounded-context split).
**Origin acceptance examples:** AE1 (mutual exclusion), AE2 (missing entity-config diagnostic), AE3 (singleton repo + factory + `AsNoTracking`), AE4 (multi-context coexistence), AE5 (Mode 2 startup DDL ordering), AE6 (outbox enlistment atomicity — Messaging, unchanged), AE7 (single options class drives both modes).

---

## Scope Boundaries

- Mode 3 (dedicated `DbContext` + shipped migration assembly + auto-apply hosted service) — explicitly out of scope per origin.
- CLI for offline schema-script generation (Wolverine/Marten style) — explicitly out of scope per origin.
- Raw-DDL Identity packages (`Headless.Identity.Storage.PostgreSql`/`SqlServer`) — explicitly out of scope per origin.
- Backwards-compatibility shims, `[Obsolete]` retention of old names — greenfield posture per `CLAUDE.md`.
- Additional database providers beyond Postgres and SqlServer (MySQL, SQLite, Oracle) — deferred per origin.
- Dashboard / observability surfaces for the new initializers — unchanged.
- Multi-tenant per-tenant-DB topologies as a first-class concern — out of scope per origin.
- **Shared `Headless.Storage.Abstractions` package** — explicitly rejected per user direction in this plan's synthesis. Each affected feature owns its own `IStorageInitializer` interface and `Bootstrapper`-style hosted service; ~10 lines of interface duplication per feature accepted in exchange for keeping the package count flat.

### Deferred to Follow-Up Work

- **Jobs unification** (origin R17, R14's Jobs entries, R15's `Headless.Jobs.EntityFramework` rename): future plan. Jobs' `<TTimeJob, TCronJob>` generic story is woven through `Headless.Jobs.Abstractions`, `Headless.Jobs.Core`, `Headless.Jobs.Dashboard`, `Headless.Jobs.OpenTelemetry`, and `Headless.Jobs.Caching.Redis`. The unification must preserve those generics, which requires its own scoping pass.
- **Identity builder overload reshape**: the existing `OrmEntityFrameworkIdentitySetup.AddHeadlessDbContext<TDbContext, TUser, ...>` exposes 8–9 generic-parameter overloads required by ASP.NET Core Identity. This plan renames the setup class and routes registration through a `HeadlessIdentityBuilder` shell, but **does not collapse the overload generic surface**. Reshaping the overloads to a flatter API is a separate plan.
- **`Headless.Identity.Storage.Postgres` / `.SqlServer` raw-DDL variants**: out of scope per origin and deferred as a separate "Identity Mode 2" plan if ever needed.
- **AuditLog Mode 2 + EF-context enlistment combination** (R12's "additive `UseEntityFramework<TContext>()` on operational Mode 2 packages" for AuditLog specifically): deferred to a follow-up plan. This plan ships AuditLog Mode 1 (consumer's `TContext` as storage, atomic by being on the same context) and AuditLog Mode 2 (dedicated table on a separate connection, not atomic with consumer writes) as mutually-exclusive choices. The third combination — Mode 2 raw-DDL persistence with the writes enlisted in the consumer's `DbContext` transaction via a borrowed `DbConnection`, the way `Headless.Messaging.PostgreSql/SqlServer` already supports for outbox — is a separate iteration. Messaging Outbox's enlistment behavior is unchanged by this plan (U6 is purely a package rename).

---

## Context & Research

### Relevant Code and Patterns

- **Unified builder pattern (template)**: `src/Headless.Messaging.Core/Configuration/MessagingSetupBuilder.cs`, `src/Headless.Messaging.Core/Configuration/IMessagesOptionsExtension.cs`, `src/Headless.Messaging.Core/Setup.cs` — the `AddHeadlessMessaging(Action<MessagingSetupBuilder>)` entry point, `setup.RegisterExtension(IXOptionsExtension)`, foreach-extensions-AddServices wiring. Mirror per-feature.
- **Mode-verb pattern (template)**: `src/Headless.Messaging.PostgreSql/Setup.cs`, `src/Headless.Messaging.SqlServer/Setup.cs` — `extension(MessagingSetupBuilder setup) { public … UsePostgreSql(…); public … UseEntityFramework<TContext>(); }` with a private `sealed class XOptionsExtension(Action<XOptions> configure) : IMessagesOptionsExtension` doing the `AddServices` wiring.
- **`IStorageInitializer` + Bootstrapper (template)**: `src/Headless.Messaging.Core/Persistence/IStorageInitializer.cs`, `src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs` — initializer runs first, processors start second, hosted-service shape via `BackgroundService`. Per-feature copy of this contract (without the messaging-specific `GetPublishedTableName` / `GetReceivedTableName` methods).
- **Raw-DDL composition prior art**:
  - Postgres: `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs` — `CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`, transactional DDL block, `CREATE INDEX CONCURRENTLY` post-transaction for partial indexes, `Npgsql` connection, schema validation regex (`^[a-zA-Z_][a-zA-Z0-9_]{0,62}$`).
  - SqlServer: `src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs` — `IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name='…')` blocks wrapped in `BEGIN TRY ... CATCH IF ERROR_NUMBER() <> 2714 THROW`, `Microsoft.Data.SqlClient` connection, schema validation regex (`^[a-zA-Z_@#][a-zA-Z0-9_@#$]{0,127}$`).
- **Mode 1 read pattern (closest prior art)**: `src/Headless.AuditLog.EntityFramework/EfReadAuditLog.cs` — `context.Set<AuditLogEntry>().AsNoTracking()`, no `IXxxDbContext` interface. The new repos for Settings/Permissions/Features follow this shape exactly, swapping the scoped `TContext` injection for `IDbContextFactory<TContext>` (singleton-safe).
- **Idempotent `ModelBuilder` extension (template)**: `src/Headless.AuditLog.EntityFramework/AuditLogModelBuilderExtensions.cs` (`if (modelBuilder.Model.FindEntityType(typeof(...)) is not null) return; ApplyConfiguration(...)`). This is the canonical shape for the new `modelBuilder.AddHeadlessSettings(options)` / `AddHeadlessPermissions(options)` / `AddHeadlessFeatures(options)` extensions, replacing the existing service-locating overloads.
- **`IEntityTypeConfiguration<T>` placement**: `src/Headless.AuditLog.EntityFramework/AuditLogEntryConfiguration.cs` — `internal sealed class XConfiguration(SchemaOptions options) : IEntityTypeConfiguration<X>` with options taken explicitly. The Settings/Features/Permissions plan rewrites their inline `modelBuilder.Entity<T>(b => ...)` blocks into `IEntityTypeConfiguration<T>` types following this shape.
- **Startup validation primitive**: `src/Headless.Hosting/Initialization/IInitializer.cs` (`bool IsInitialized`, `WaitForInitializationAsync`), `src/Headless.Hosting/DependencyInjection/DependencyInjectionExtensions.cs` (line 616: `AddInitializerHostedService<T>`). Use this primitive for the per-feature `Bootstrapper` hosted service.
- **`IHostedLifecycleService.StartingAsync` pre-start gate**: `src/Headless.MultiTenancy/HeadlessTenancyStartupValidator.cs` — runs *before* any `IHostedService.StartAsync`. This is the pattern for the Mode 1 missing-entity-config diagnostic (AE2).
- **`Configure<TOption, TValidator>(…)` + `AddOptions<TOption, TValidator>()` with auto-`ValidateOnStart()`**: `src/Headless.Hosting/Options/OptionsServiceCollectionExtensions.cs`. Required by `CLAUDE.md` for options registration.
- **`HeadlessPostgreSqlFixture` (test fixture base)**: `src/Headless.Testing.Testcontainers/` — extended by `tests/Headless.Messaging.PostgreSql.Tests.Integration/PostgreSqlTestFixture.cs`. Reuse for all new Mode 2 integration tests.
- **`AddDbContextFactory<T>` + `AddDbContext<T>` coexistence**: verified in `tests/Headless.Settings.Tests.Integration/SettingsCustomSchemaTests.cs` line 97 under EF Core 10. Plays well together.

### Institutional Learnings

- **`docs/solutions/architecture-patterns/messaging-keyed-di-lock-isolation-2026-05-19.md`**: framework-internal services must use keyed DI to prevent app-level shadowing. For the per-feature `IStorageInitializer` registrations, **register and resolve via keyed singleton** so that two affected features (e.g., Settings and Permissions) cannot accidentally collide if a consumer wires both against the same `TContext`. Each feature uses a package-private key constant (e.g., `internal static class SettingsKeys { public const string StorageInitializer = "headless:settings:storage-init"; }`) — never exposed publicly.
- **`docs/solutions/guides/messaging-transport-provider-guide.md`**: codifies the `Setup{Provider}.cs` + extension members + `IXOptionsExtension` + `MarkerService("Provider")` shape. This plan generalizes it across features. Adopt the marker-service convention per feature (`SettingsStorageMarkerService("EntityFramework" | "PostgreSql" | "SqlServer")`) for the runtime "exactly one provider" check tightening (AE1).
- **`docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md`** — Greenfield Scope Rule: *"For greenfield modules with no live consumers yet, fix the current runtime path and explicitly drop speculative migration logic."* Endorses the rename + delete-the-old-types approach.
- **No prior `docs/solutions/` content covers EF Core specifics, raw DDL idempotency, or hosted-bootstrapper ordering** — this plan will be the first authoritative source. Document the patterns in `docs/solutions/` when this plan lands.

### External References

External research was not run for this plan — Phase 1.1 found rich, direct prior art in the codebase (the Messaging pattern is a near-perfect template). The brainstorm dialog already covered external prior art (Hangfire, Duende IdentityServer, MassTransit, ABP) at the design level.

---

## Key Technical Decisions

- **Per-feature builder pattern (Messaging shape replicated)**: each affected feature ships its own `HeadlessXSetupBuilder` class. The generic builder-plumbing contract (`IStorageOptionsExtension { void AddServices(IServiceCollection); }`) is **lifted into `Headless.Hosting`** rather than duplicated 5× per feature — it has no feature-specific methods and is purely DI plumbing, so it does not violate the user-direction rejection of `Headless.Storage.Abstractions` (which was about per-feature `IStorageInitializer` contracts). Per-feature `IXStorageInitializer` interfaces remain duplicated per the user direction. Rationale: pattern is proven in `Headless.Messaging`, reuses idioms framework consumers already learn.
- **Provider segment spelling: `PostgreSql` + `SqlServer`**. Four existing packages use this exact spelling (`Headless.Messaging.PostgreSql`, `Headless.Sql.PostgreSql`, `Headless.Sql.SqlServer`, `Headless.Sql.Sqlite`). Brainstorm doc currently uses `Postgres` in places — the brainstorm doc gets updated as part of U1 doc work so the source-of-truth aligns.
- **Setup class naming convention**: two layers. (1) Per-feature **entry-point class** on `IServiceCollection` is `Setup{Feature}` (e.g., `SetupSettings`, `SetupPermissions`, `SetupFeatures`, `SetupAuditLog`, `SetupIdentity`) — exposes `AddHeadless{Feature}(Action<Headless{Feature}SetupBuilder>)`. (2) Per-provider **extension classes** on the per-feature setup builder are `Setup{Feature}{Provider}` (e.g., `SetupSettingsEntityFramework`, `SetupSettingsPostgreSql`, `SetupSettingsSqlServer`, `SetupAuditLogEntityFramework`, etc.) — expose `Use{Provider}(...)` extension members on the builder. Matches the existing `SetupMessaging` (entry point) + `SetupPostgreSqlMessaging` (provider) precedent. Renames: `OrmEntityFrameworkIdentitySetup` → `SetupIdentityEntityFramework`; `EntityFrameworkSettingsSetup` → `SetupSettingsEntityFramework`; etc.
- **Setup namespace: `Microsoft.Extensions.DependencyInjection`** (with `#pragma warning disable IDE0130`) for primary entry-point classes. Matches the existing Messaging convention for `Setup.cs` files exposing `AddHeadlessX` on `IServiceCollection`. Internal types stay in their feature's namespace.
- **Mode mutual-exclusion (AE1)**: count `Extensions` on the per-feature builder at `_RegisterCore…Services` time. Throw `InvalidOperationException` naming both verbs when count != 1. **Single enforcement point** — the prior marker-service + bootstrapper-time recount was double-enforcement of the same invariant; the registration-time check fires earlier and is sufficient. Applies uniformly to Settings, Permissions, Features, and Identity. **AuditLog shape exception**: AuditLog's `UseEntityFramework<TContext>()` is a **sub-modifier on the provider verb**, not a peer Extension — `UsePostgreSql(opt)` / `UseSqlServer(opt)` registers Mode 2 and `.WithEntityFrameworkEnlistment<TContext>()` augments it with transaction enlistment (Messaging-outbox shape). This keeps `Extensions.Count == 1` invariant intact even when R12's deferred Mode 2 + enlistment combination lands later — no future re-architecture of the AuditLog builder. Messaging Outbox's existing enlistment behavior is unchanged by U6 (pure package rename). R12's enlistment for AuditLog stays deferred per Scope Boundaries but the builder shape is now future-compatible.
- **Mode 1 entity access**: `context.Set<TEntity>()`, never `((IXxxDbContext)context).PropertyName`. Removes the `IXxxDbContext` interface entirely.
- **Mode 1 read tracking**: `AsNoTracking()` is the default for every read in every repo. Writes use change tracking inside the repo's unit of work. Today's repos use tracking by default — convert during the rewrite.
- **Mode 1 repository lifetime — split by read vs write semantics**:
  - **Read-side repos** (`ISettingValueRecordRepository`/Read paths, `IPermissionGrantRepository`/Read paths, `IFeatureValueRecordRepository`/Read paths, `IReadAuditLog<TContext>`): singleton, taking `IDbContextFactory<TContext>`. `AsNoTracking()` reads through a fresh per-call context. Reads are not enlisted in the consumer's outer transaction — they go through application-level caches anyway.
  - **Write-side repos with required transactional enlistment** (`IAuditLog<TContext>`, `IAuditLogStore`, `IAuditChangeCapture`): **stay scoped** on the consumer's `TContext`. Today's `EfAuditLog<TContext>` writes via `context.Set<AuditLogEntry>().Add(...)` WITHOUT calling `SaveChanges` — entries commit atomically with the consumer's `SaveChangesAsync` for the rest of their unit of work. Converting these to singleton-with-factory would break that contract (separate connection, audit row not in the same transaction). The R7 singleton-with-factory mandate is carved out for these three types in U4.
  - **Write-side repos without transactional enlistment** (`ISettingValueRecordRepository`/Write paths, `IPermissionGrantRepository`/Write paths, `IFeatureValueRecordRepository`/Write paths): singleton, taking `IDbContextFactory<TContext>`. Writes commit on a separate connection from the consumer's request transaction — consumers needing atomicity with their own writes must reach for the entity directly via `Set<T>()` on their context. Documented as a trade-off; consistent with the existing repository shape today.
  - Consumer must register `AddDbContextFactory<TContext>()` for the factory-based repos; they may additionally register `AddDbContext<TContext>` as scoped for their own use and for the scoped AuditLog write-side repos (coexistence verified under EF Core 10).
- **Mode 1 startup validation (AE2) — IModel inspection, no DB round-trip**: per-feature validator implements `IHostedLifecycleService` and is registered via `services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(sp => new SettingsEntityValidationStartupGate(...)))` so the host enumerates it as a hosted service and discovers `StartingAsync` via interface cast (the precedent from `Headless.MultiTenancy/HeadlessTenancyStartupValidator.cs` line 42). In `StartingAsync`, resolve `IDbContextFactory<TContext>` (typed via `typeof(IDbContextFactory<>).MakeGenericType(dbContextType)`), open a context, then **inspect `context.Model.FindEntityType(typeof(SettingValueRecord))`** — no DB round-trip, no exception-type ambiguity vs transient DB unavailability. When the entity is not present, throw `InvalidOperationException` with the actionable message: *"Headless.Settings: the registered DbContext `<TContext>` does not contain `SettingValueRecord`. Call `modelBuilder.AddHeadlessSettings(settingsStorageOptions)` in your `OnModelCreating`."*. Runs before any `IHostedService.StartAsync` because `IHostedLifecycleService.StartingAsync` fires first.
- **Mode 2 hosted bootstrapper — use `AddInitializerHostedService<T>`**: the per-feature `IStorageInitializer` implementation itself implements `IHostedService + IInitializer` (no separate `XBootstrapper` wrapper class). Registered via the existing `AddInitializerHostedService<TInitializer>()` primitive in `Headless.Hosting/DependencyInjection/DependencyInjectionExtensions.cs` (line 616), which wires `TryAddSingleton<T>`, `TryAddEnumerable IInitializer → T`, and `TryAddEnumerable IHostedService → T` in one call. This eliminates ~5 nearly-identical `XBootstrapper` wrapper classes. **Startup ordering is NOT load-bearing on registration order.** `BackgroundService.StartAsync` returns immediately and fires `ExecuteAsync` on a thread-pool thread, so subsequent hosted services start in parallel — consumers (including Core seeding services) MUST gate any read/write path against the storage's existence by awaiting `IInitializer.WaitForInitializationAsync` at their own entry point. Document this contract in the per-package README; do not rely on the order `AddHostedService` calls fire.
- **Keyed DI for per-feature internals**: the `messaging-keyed-di-lock-isolation` learning is honored consistently across the plan. Per-feature internal services that have collision risk (the framework registers them but a consumer could shadow them with `TryAdd*`) — specifically the per-feature `IStorageInitializer` resolved by its hosted service — are registered as `TryAddKeyedSingleton(SettingsKeys.StorageInitializer, ...)` and resolved via `[FromKeyedServices(SettingsKeys.StorageInitializer)]` inside the per-feature `IStorageInitializer` implementation. The key constants (`internal static class SettingsKeys { public const string StorageInitializer = "headless:settings:storage-init"; }`) are package-internal — consumers never see them. Repository registrations remain non-keyed because their service types (`ISettingValueRecordRepository`, etc.) are unique per feature and not subject to multi-context-collision — multi-context (R8/AE4) is supported by **registering each feature against exactly one `TContext`** in its `AddHeadlessX` call; consumers needing per-tenant routing fall back to factory-of-factories patterns outside this plan (multi-tenant deferred per origin). The "two `AddHeadlessSettings` calls with different `TContext`" scenario is documented as **not supported** (R8 is satisfied by one feature per TContext, with different features free to target different contexts; AE4 covers that case).
- **Mode mutual-exclusion is enforced once, at registration time**. The prior plan iteration listed a separate marker-service Count check inside the Bootstrapper; that has been removed as double-enforcement of the same invariant (see "Mode mutual-exclusion (AE1)" above).
- **Single options class drives both modes (R13)**: `SettingsStorageOptions` / `PermissionsStorageOptions` / `FeaturesStorageOptions` / `AuditLogStorageOptions` consumed by both EF entity configuration (Mode 1) and raw-DDL initializer (Mode 2). Validator stays in the same file directly below options per `CLAUDE.md`.
- **`AddPooledDbContextFactory<TContext>` shortcut NOT added to builder**. Consumer registers their own factory with their pooling preference. Keeps the builder API minimal.
- **DI namespace for primary entry-point class**: `Microsoft.Extensions.DependencyInjection` (matches Messaging precedent for `Setup.cs` exposing `IServiceCollection` extensions).
- **`IEntityTypeConfiguration<T>` types are `internal sealed`** with primary constructor `(XStorageOptions options)` taking options explicitly. Applied via `modelBuilder.ApplyConfiguration(new …Configuration(options))` inside the idempotent `modelBuilder.AddHeadlessX(options)` extension.
- **`ConfigureSqlOptionsFromDbContext` reflection pattern (Messaging prior art) re-used for the new Mode 2 packages** when the user calls `UseEntityFramework<TContext>()` on the operational outbox path. Domain packages (Settings/Permissions/Features) do not need this — they don't enlist in user transactions.
- **Mode 2 repositories use raw ADO (`Npgsql` / `Microsoft.Data.SqlClient`)**, no Dapper. Decision: Dapper is not a current framework dependency, adding it for ~8 new packages × 2–3 entities of CRUD is disproportionate; raw ADO matches the existing `PostgreSqlDataStorage` / `SqlServerDataStorage` precedent. Implementation hand-writes `NpgsqlCommand` / `SqlCommand` with parameterized SQL per repository method. Boilerplate is bounded (the repos are simple key-lookup/insert/upsert shapes).
- **Mode 2 concurrent-bootstrap race handling**: multi-replica startup races on `CREATE TABLE IF NOT EXISTS` / `CREATE INDEX IF NOT EXISTS` are handled by **catching duplicate-creation errors explicitly** rather than acquiring a distributed lock. Postgres: catch SQLSTATE `42P07` (duplicate_table), `42P06` (duplicate_schema), `42P05` (duplicate_index); SqlServer: catch error numbers `2714` (object exists), `1913` (index exists), `2759` (column exists). Existing `Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs` already wraps blocks in `BEGIN TRY/CATCH IF ERROR_NUMBER() <> 2714 THROW;` — extend the same pattern to cover index races. Distributed-lock-on-bootstrap (Messaging's `UseStorageLock` precedent) is **not adopted** for this plan because it would force a `IDistributedLockProvider` dependency on every Mode 2 package; the error-catch approach is sufficient for the bounded set of DDL statements these packages emit.
- **AuditLog Mode 2 JSON column type — committed decision**: `nvarchar(max)` for SqlServer (with optional `ISJSON` constraint for SqlServer 2016+). Native `JSON` type (SqlServer 2022+) is NOT auto-detected — keeps the DDL deterministic across versions. Postgres uses `jsonb`. Consumer can override via `AuditLogStorageOptions.JsonColumnType` if they need a different shape. This closes the question rather than leaving it in Deferred to Implementation.
- **Provider spelling precedent verified at planning time**: `find src tests -maxdepth 2 -type d` confirms 100% precedent for `PostgreSql` + `SqlServer` across `Headless.Messaging.{PostgreSql,SqlServer}`, `Headless.Sql.{PostgreSql,SqlServer,Sqlite}`. Zero counter-precedent for `Postgres` (without `Sql`) or `MsSql`. Decision is settled.
- **Two-layer builder shape clarification**: `HeadlessSettingsSetupBuilder` is the inside-the-lambda builder the consumer configures inside `AddHeadlessSettings(s => ...)` — exposes mode verbs (`UseEntityFramework`, `UsePostgreSql`, `UseSqlServer`) and `ConfigureStorage`. `HeadlessSettingsBuilder` is the post-configure builder returned from `AddHeadlessSettings`, exposing any post-registration verbs (parallel to Messaging's `MessagingSetupBuilder` → `MessagingBuilder` split). For features with no post-configure verbs, `HeadlessXBuilder` may be a thin marker carrying `IServiceCollection` for chaining.
- **Verb `AddHeadlessSettings` is intentionally overloaded across two extension surfaces**: as an `IServiceCollection` extension (entry point registering services) AND as a `ModelBuilder` extension (registering EF entity configurations). The two are method-overload-distinguishable by their receiver type. Same name on both surfaces is deliberate — keeps the unified `AddHeadlessX` naming consistent regardless of which extension surface the consumer is calling.

---

## Testing Strategy

- **Suite ownership**:
  - Per-feature Mode 1 rewrites: existing `Headless.<Feature>.Tests.Integration` projects own the coverage. Tests are rewritten to use the new builder shape, the new `modelBuilder.AddHeadlessX` extension, and the shared-context registration pattern. The existing `Settings.Tests.Integration` already proves `AddDbContextFactory<SharedSettingsDbContext>` works end-to-end (see `SettingsCustomSchemaTests`).
  - Each new Mode 2 package: new `Headless.<Feature>.Storage.<Provider>.Tests.Integration` project per package, against Testcontainers. Mirrors the existing `Headless.Messaging.PostgreSql.Tests.Integration` shape: extend `HeadlessPostgreSqlFixture` (or the SqlServer equivalent), assert tables/indexes exist after bootstrapper runs, assert reads/writes succeed via the repo.
  - Identity rename: covered by the existing `Headless.Identity.Storage.EntityFramework.Tests.Integration` project. Update the test registration paths to match the renamed Setup class.
  - Messaging provider renames: covered by the existing `Headless.Messaging.PostgreSql.Tests.Integration` and `Headless.Messaging.SqlServer.Tests.Integration` projects — behavior unchanged, only namespace/class renames flow through.
- **Fixtures and harnesses**: `HeadlessPostgreSqlFixture` (Testcontainers base in `Headless.Testing.Testcontainers`), `RespawnerFactory` if used in current Settings fixture, existing `xUnit v3 + Microsoft Testing Platform` runner per `global.json`.
- **Required coverage** per the Testing Diamond (`CLAUDE.md`): integration tests dominate, covering AE1 (mutual exclusion at registration), AE2 (missing entity config diagnostic at startup), AE3 (`AsNoTracking` read path through factory), AE4 (two `TContext` types coexist), AE5 (Mode 2 bootstrapper creates schema before consumer access), AE7 (one options class drives both modes — assert both EF model and raw DDL respect schema/table-name overrides).
- **Out of scope**: AE6 (outbox enlistment atomicity) — Messaging behavior unchanged in this plan, existing tests cover it. Identity overload-surface tests — not reshaping the overloads.

---

## Open Questions

### Resolved During Planning

- **Provider segment spelling**: resolved to `PostgreSql` + `SqlServer`. Verified zero counter-precedent via `find src tests -maxdepth 2 -type d`.
- **Shared `IStorageInitializer` abstractions**: resolved per user direction — per-feature `IStorageInitializer` interfaces (~10 lines duplication × 5 features accepted). However, the **generic builder plumbing interface** `IStorageOptionsExtension { void AddServices(IServiceCollection); }` IS lifted into `Headless.Hosting` (not a new package) — it is pure DI plumbing with no feature-specific methods, and the user-direction was about storage-domain abstractions, not generic plumbing.
- **`AddPooledDbContextFactory<TContext>` shortcut on builder**: resolved — not added; consumer registers their preferred factory.
- **Mode 1 startup-validation**: `IHostedLifecycleService.StartingAsync` per `HeadlessTenancyStartupValidator` prior art, registered via `TryAddEnumerable<IHostedService>`. Validates via `context.Model.FindEntityType(...)` (IModel inspection, no DB round-trip).
- **Mode 2 hosted bootstrapper**: use existing `AddInitializerHostedService<T>` from `Headless.Hosting`. The per-feature `IStorageInitializer` implementation itself implements `IHostedService + IInitializer`; no separate `XBootstrapper` wrapper class.
- **Mode mutual-exclusion enforcement**: single check at `AddHeadlessX` registration time via `setup.Extensions.Count != 1`. No bootstrapper-time marker-service recount.
- **AuditLog repo lifetime split**: write-side (`EfAuditLog<TContext>`, `EfAuditChangeCapture`, `EfAuditLogStore`) stays scoped to preserve transactional atomicity with consumer's `SaveChangesAsync`. Read-side (`EfReadAuditLog<TContext>`) moves to singleton-with-factory. R7's singleton-with-factory mandate carved out for AuditLog write paths.
- **AuditLog builder shape**: provider verbs (`UsePostgreSql`, `UseSqlServer`) are Mode 2 entry points; `.WithEntityFrameworkEnlistment<TContext>()` sub-modifier reserved for the deferred R12 enlistment capability. Keeps `Extensions.Count == 1` invariant intact when enlistment lands later.
- **AuditLog Mode 2 JSON column type**: `nvarchar(max)` for SqlServer (no SqlServer 2022+ version detection); `jsonb` for Postgres. Consumer override via `AuditLogStorageOptions.JsonColumnType`.
- **Concurrent-bootstrap race handling**: catch duplicate-creation errors explicitly (Postgres SQLSTATE `42P05`/`42P06`/`42P07`; SqlServer error numbers `2714`/`1913`/`2759`). No distributed lock.
- **Mode 2 repository implementation**: raw ADO via `Npgsql` / `Microsoft.Data.SqlClient`. No Dapper.
- **Keyed DI for per-feature internals**: per-feature `IStorageInitializer` registrations are keyed (package-internal key constants); repository registrations are non-keyed (unique service types per feature). Multi-context (R8/AE4) is supported as one feature per `TContext`; same `TContext` shared by multiple features is the common case and works with non-keyed repo registrations because service types are distinct per feature.
- **Jobs `<TTimeJob, TCronJob>` generic preservation**: moot for this plan — Jobs deferred. When the Jobs plan runs, generics must be preserved (research confirmed `TTimeJob`/`TCronJob` thread through `Headless.Jobs.Abstractions`, `Headless.Jobs.Core`, `Headless.Jobs.Dashboard`, `Headless.Jobs.OpenTelemetry`, `Headless.Jobs.Caching.Redis`).
- **DI namespace for `AddHeadlessX(s => s.Use…)`**: `Microsoft.Extensions.DependencyInjection` (matches Messaging precedent).
- **Setup class naming convention**: entry point `Setup{Feature}` on `IServiceCollection`; provider extensions `Setup{Feature}{Provider}` on the per-feature setup builder.

### Deferred to Implementation

- **Exact list of indexes the Mode 2 DDL emits per table** — derive from the existing `modelBuilder.Entity<T>(b => ...HasIndex(...))` calls in `SettingsModelBuilderExtensions` / `FeaturesModelBuilderExtensions` / `PermissionsModelBuilderExtensions` / `AuditLogEntryConfiguration` at implementation time. Some indexes may need `CREATE INDEX CONCURRENTLY` (Postgres) post-transaction per the Messaging precedent.
- **Schema-name validation regex** — copy the existing Messaging regexes for Postgres (`^[a-zA-Z_][a-zA-Z0-9_]{0,62}$`) and SqlServer (`^[a-zA-Z_@#][a-zA-Z0-9_@#$]{0,127}$`) into each new options class.
- **Whether to use `partial` schema property + `GeneratedRegex` validator** (Messaging precedent) or a plain validator — implementation-time style consistency choice.
- **`AuditLog` rename: do internal types' namespaces change?** Today `EfAuditLog` is in `Headless.AuditLog` namespace. With `Headless.AuditLog.Storage.EntityFramework` as the package name, the internal namespace becomes `Headless.AuditLog.Storage` — confirm during implementation that no consumer-visible internal type's namespace shifts.

---

## Output Structure

This plan creates 8 new packages and renames 3 existing ones across 5 Implementation Units (U1–U5: Settings, Permissions, Features, AuditLog, Identity-rename + Messaging-renames bundled together). The expected layout after this plan lands:

    src/
      Headless.Settings.Storage.EntityFramework/        # rewritten
      Headless.Settings.Storage.PostgreSql/             # NEW
      Headless.Settings.Storage.SqlServer/              # NEW
      Headless.Permissions.Storage.EntityFramework/     # rewritten
      Headless.Permissions.Storage.PostgreSql/          # NEW
      Headless.Permissions.Storage.SqlServer/           # NEW
      Headless.Features.Storage.EntityFramework/        # rewritten
      Headless.Features.Storage.PostgreSql/             # NEW
      Headless.Features.Storage.SqlServer/              # NEW
      Headless.AuditLog.Storage.EntityFramework/        # RENAMED from Headless.AuditLog.EntityFramework + rewritten
      Headless.AuditLog.Storage.PostgreSql/             # NEW
      Headless.AuditLog.Storage.SqlServer/              # NEW
      Headless.Identity.Storage.EntityFramework/        # in place, Setup class renamed only
      Headless.Messaging.Storage.PostgreSql/            # RENAMED from Headless.Messaging.PostgreSql
      Headless.Messaging.Storage.SqlServer/             # RENAMED from Headless.Messaging.SqlServer

    tests/
      Headless.Settings.Storage.PostgreSql.Tests.Integration/    # NEW
      Headless.Settings.Storage.SqlServer.Tests.Integration/     # NEW
      Headless.Permissions.Storage.PostgreSql.Tests.Integration/ # NEW
      Headless.Permissions.Storage.SqlServer.Tests.Integration/  # NEW
      Headless.Features.Storage.PostgreSql.Tests.Integration/    # NEW
      Headless.Features.Storage.SqlServer.Tests.Integration/     # NEW
      Headless.AuditLog.Storage.PostgreSql.Tests.Integration/    # NEW
      Headless.AuditLog.Storage.SqlServer.Tests.Integration/     # NEW

Per-package internal layout (canonical, established by U1):

    Headless.<Feature>.Storage.<Provider>/
      Setup.cs                                  # SetupXFeatureProvider class with extension members
      HeadlessXBuilder.cs                       # (Mode 1 EF only) per-feature setup builder + IXOptionsExtension contract
      I<Feature>StorageInitializer.cs           # (Mode 2 raw-DDL only) per-feature initializer interface
      <Provider><Feature>StorageInitializer.cs  # (Mode 2 raw-DDL only) implementation
      <Provider><Feature>Options.cs             # provider-specific options (connection string, version)
      <Feature>Bootstrapper.cs                  # (Mode 2 raw-DDL only) per-feature hosted-service bootstrapper
      README.md                                 # per CLAUDE.md docs sync
      Headless.<Feature>.Storage.<Provider>.csproj

---

## High-Level Technical Design

> *This illustrates the intended approach and is directional guidance for review, not implementation specification. The implementing agent should treat it as context, not code to reproduce.*

The per-feature builder shape mirrors `Headless.Messaging.Core`'s `MessagingSetupBuilder` pattern. For Settings (canonical shape established by U1; other features mirror it):

    namespace Microsoft.Extensions.DependencyInjection;

    // Entry point on IServiceCollection
    public static class SetupSettings
    {
        extension(IServiceCollection services)
        {
            public HeadlessSettingsBuilder AddHeadlessSettings(Action<HeadlessSettingsSetupBuilder> configure)
            {
                var setup = new HeadlessSettingsSetupBuilder(services);
                configure(setup);
                // Tighten R2 (AE1): throw if Extensions.Count != 1
                _RegisterCoreSettingsServices(services, setup);
                return new HeadlessSettingsBuilder(services);
            }
        }
    }

    // Per-feature builder, mirrors MessagingSetupBuilder
    public sealed class HeadlessSettingsSetupBuilder
    {
        internal IServiceCollection Services { get; }
        internal SettingsStorageOptions Options { get; } = new();
        internal IList<ISettingsOptionsExtension> Extensions { get; } = new List<ISettingsOptionsExtension>();
        public void RegisterExtension(ISettingsOptionsExtension extension) => Extensions.Add(extension);
        public HeadlessSettingsSetupBuilder ConfigureStorage(Action<SettingsStorageOptions> configure) { ... }
    }

    public interface ISettingsOptionsExtension
    {
        void AddServices(IServiceCollection services);
    }

    // Per-feature EF provider, mirrors SetupPostgreSqlMessaging.UseEntityFramework
    public static class SetupSettingsEntityFramework
    {
        extension(HeadlessSettingsSetupBuilder setup)
        {
            public HeadlessSettingsSetupBuilder UseEntityFramework<TContext>() where TContext : DbContext
            {
                setup.RegisterExtension(new EntityFrameworkSettingsOptionsExtension(typeof(TContext)));
                return setup;
            }
        }

        private sealed class EntityFrameworkSettingsOptionsExtension(Type dbContextType) : ISettingsOptionsExtension
        {
            public void AddServices(IServiceCollection services)
            {
                services.TryAddSingleton(new SettingsStorageMarkerService("EntityFramework"));
                // Resolve open generic repo bound to TContext
                services.TryAddSingleton(typeof(ISettingValueRecordRepository), typeof(EfSettingValueRecordRepository<>).MakeGenericType(dbContextType));
                services.TryAddSingleton(typeof(ISettingDefinitionRecordRepository), typeof(EfSettingDefinitionRecordRepository<>).MakeGenericType(dbContextType));
                // Register Mode-1 startup validator (IHostedLifecycleService.StartingAsync)
                services.AddSingleton<IHostedLifecycleService>(sp => new SettingsEntityValidationStartupGate(dbContextType, sp));
            }
        }
    }

    // Per-feature raw-DDL provider, mirrors SetupPostgreSqlMessaging.UsePostgreSql
    public static class SetupSettingsPostgreSql
    {
        extension(HeadlessSettingsSetupBuilder setup)
        {
            public HeadlessSettingsSetupBuilder UsePostgreSql(string connectionString) { ... }
            public HeadlessSettingsSetupBuilder UsePostgreSql(Action<PostgreSqlSettingsOptions> configure) { ... }
        }

        private sealed class PostgreSqlSettingsOptionsExtension(Action<PostgreSqlSettingsOptions> configure) : ISettingsOptionsExtension
        {
            public void AddServices(IServiceCollection services)
            {
                services.TryAddSingleton(new SettingsStorageMarkerService("PostgreSql"));
                services.Configure<PostgreSqlSettingsOptions, PostgreSqlSettingsOptionsValidator>(configure);
                services.TryAddSingleton<ISettingsStorageInitializer, PostgreSqlSettingsStorageInitializer>();
                services.TryAddSingleton<SettingsBootstrapper>();
                services.AddHostedService(sp => sp.GetRequiredService<SettingsBootstrapper>());
                services.TryAddSingleton<ISettingValueRecordRepository, PostgreSqlSettingValueRecordRepository>();
                services.TryAddSingleton<ISettingDefinitionRecordRepository, PostgreSqlSettingDefinitionRecordRepository>();
            }
        }
    }

Idempotent `ModelBuilder` extension (mirrors `AuditLogModelBuilderExtensions.ConfigureAuditLog`):

    public static class SettingsModelBuilderExtensions
    {
        public static ModelBuilder AddHeadlessSettings(this ModelBuilder modelBuilder, SettingsStorageOptions options)
        {
            if (modelBuilder.Model.FindEntityType(typeof(SettingValueRecord)) is not null) return modelBuilder;
            modelBuilder.ApplyConfiguration(new SettingValueRecordConfiguration(options));
            modelBuilder.ApplyConfiguration(new SettingDefinitionRecordConfiguration(options));
            return modelBuilder;
        }
    }

Per-feature initializer + bootstrapper (mirrors `Headless.Messaging.Core/Internal/Bootstrapper.cs` and `Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs`):

    // Per-feature contract (10 lines of duplication accepted vs. a shared abstractions package per user direction)
    public interface ISettingsStorageInitializer
    {
        Task InitializeAsync(CancellationToken cancellationToken = default);
    }

    internal sealed class SettingsBootstrapper(
        ISettingsStorageInitializer initializer,
        IEnumerable<SettingsStorageMarkerService> markers,
        ILogger<SettingsBootstrapper> logger
    ) : BackgroundService, IInitializer
    {
        public bool IsInitialized { get; private set; }
        public Task WaitForInitializationAsync(CancellationToken ct) { ... }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _CheckExactlyOneProvider(markers);  // throws if Count != 1
            await initializer.InitializeAsync(stoppingToken);
            IsInitialized = true;
        }
    }

The consumer's surface (Mode 1, R5/AE2):

    services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(connStr));
    services.AddHeadlessSettings(s =>
    {
        s.UseEntityFramework<AppDbContext>();
        s.ConfigureStorage(o => o.Schema = "app_settings");
    });

    // In AppDbContext.OnModelCreating:
    modelBuilder.AddHeadlessSettings(settingsStorageOptions);

The consumer's surface (Mode 2, F2):

    services.AddHeadlessSettings(s => s.UsePostgreSql(connStr));
    // Tables created by SettingsBootstrapper at startup; no EF involvement.

---

## Implementation Units

### U1. Settings: rewrite Mode 1 + ship Mode 2 (Postgres, SqlServer) + docs

**Goal:** Establish the canonical per-feature builder shape for the entire plan. Rewrite `Headless.Settings.Storage.EntityFramework` to drop the `IXxxDbContext` / `IModelCacheKeyFactory` / dedicated `SettingsDbContext` types and adopt the unified `AddHeadlessSettings(s => s.Use…)` shape. Ship `Headless.Settings.Storage.PostgreSql` and `Headless.Settings.Storage.SqlServer` with per-feature `ISettingsStorageInitializer` + `SettingsBootstrapper`. Update `docs/llms/settings.md`, the package README, and the brainstorm doc's provider-segment spelling.

**Requirements:** R1, R2, R3, R4, R5, R6, R7, R8, R9, R10 (Settings rows), R11, R13, R14 (Settings rows), R16, R18, R19.

**Dependencies:** None.

**Files:**
- Create: `src/Headless.Settings.Storage.EntityFramework/HeadlessSettingsSetupBuilder.cs` (per-feature setup builder + `ISettingsOptionsExtension`)
- Create: `src/Headless.Settings.Storage.EntityFramework/HeadlessSettingsBuilder.cs` (post-configure builder returned from `AddHeadlessSettings`)
- Create: `src/Headless.Settings.Storage.EntityFramework/SettingsStorageMarkerService.cs`
- Create: `src/Headless.Settings.Storage.EntityFramework/Internal/SettingsEntityValidationStartupGate.cs` (`IHostedLifecycleService`)
- Create: `src/Headless.Settings.Storage.EntityFramework/SettingValueRecordConfiguration.cs` (`IEntityTypeConfiguration<SettingValueRecord>`, internal sealed, takes `SettingsStorageOptions` explicitly)
- Create: `src/Headless.Settings.Storage.EntityFramework/SettingDefinitionRecordConfiguration.cs`
- Modify: `src/Headless.Settings.Storage.EntityFramework/Setup.cs` (rewrite to the canonical shape — `SetupSettings` + `SetupSettingsEntityFramework` classes)
- Modify: `src/Headless.Settings.Storage.EntityFramework/SettingsModelBuilderExtensions.cs` (replace existing two overloads with the idempotent `AddHeadlessSettings(ModelBuilder, SettingsStorageOptions)`)
- Modify: `src/Headless.Settings.Storage.EntityFramework/EfSettingValueRecordRepository.cs` (drop `where TContext : ISettingsDbContext`, switch from `db.SettingValues` to `db.Set<SettingValueRecord>()`, add `.AsNoTracking()` to all read paths)
- Modify: `src/Headless.Settings.Storage.EntityFramework/EfSettingDefinitionRecordRepository.cs` (parallel changes)
- Modify: `src/Headless.Settings.Storage.EntityFramework/SettingsStorageOptions.cs` (no functional change; verify validator regex matches new provider-segment additions)
- Delete: `src/Headless.Settings.Storage.EntityFramework/ISettingsDbContext.cs`
- Delete: `src/Headless.Settings.Storage.EntityFramework/SettingsDbContext.cs` (which also removes `SettingsStorageModelCacheKeyFactory`)
- Modify: `src/Headless.Settings.Storage.EntityFramework/README.md` (rewrite to the new shape)
- Create (new package): `src/Headless.Settings.Storage.PostgreSql/` — `Setup.cs` (`SetupSettingsPostgreSql`), `PostgreSqlSettingsOptions.cs` + validator, `ISettingsStorageInitializer.cs`, `PostgreSqlSettingsStorageInitializer.cs`, `SettingsBootstrapper.cs`, `PostgreSqlSettingValueRecordRepository.cs`, `PostgreSqlSettingDefinitionRecordRepository.cs`, `README.md`, `Headless.Settings.Storage.PostgreSql.csproj` (SDK `Headless.NET.Sdk`)
- Create (new package): `src/Headless.Settings.Storage.SqlServer/` — parallel shape with `SqlServerSettingsOptions`, `SqlServerSettingsStorageInitializer`, etc.
- Modify: `headless-framework.slnx` (add the two new packages and their test projects)
- Modify: `docs/llms/settings.md` (rewrite all `AddSettingsManagementDbContextStorage` / `ISettingsDbContext` / `SettingsDbContext` references to the new shape)
- Modify: `docs/brainstorms/2026-05-24-storage-initialization-unification-requirements.md` (search/replace `Postgres` → `PostgreSql` in R14/R15 to align with the picked spelling)
- Test: `tests/Headless.Settings.Tests.Integration/TestSetup/SettingsTestBase.cs` (update wiring to new shape — `AddDbContextFactory` + `modelBuilder.AddHeadlessSettings` + `AddHeadlessSettings(s => s.UseEntityFramework<TContext>())`)
- Test: `tests/Headless.Settings.Tests.Integration/SettingsCustomSchemaTests.cs` (remove `SettingsStorageModelCacheKeyFactory` test cases per R9; rewrite remaining cases to the new shape; keep coverage of custom schema/table-name overrides per AE7)
- Test (new): `tests/Headless.Settings.Storage.PostgreSql.Tests.Integration/` — `PostgreSqlSettingsFixture.cs` (extends `HeadlessPostgreSqlFixture`), `PostgreSqlSettingsTests.cs`, `Headless.Settings.Storage.PostgreSql.Tests.Integration.csproj`
- Test (new): `tests/Headless.Settings.Storage.SqlServer.Tests.Integration/` — parallel shape against SqlServer Testcontainers fixture

**Approach:**
- Follow the canonical shape laid out in High-Level Technical Design. This unit is the template all subsequent unit work mirrors.
- Mode 1 entry point: `AddHeadlessSettings(Action<HeadlessSettingsSetupBuilder>)` in `Microsoft.Extensions.DependencyInjection` namespace, returns `HeadlessSettingsBuilder`. `_RegisterCoreSettingsServices` runs all `setup.Extensions` `AddServices` calls and throws `InvalidOperationException` if `setup.Extensions.Count != 1` naming the conflicting provider verbs.
- Mode 1 startup gate: `SettingsEntityValidationStartupGate` implements `IHostedLifecycleService`. **Registered as `services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(sp => new SettingsEntityValidationStartupGate(dbContextType, sp)))`** so the host enumerates it via the `IHostedService` collection and discovers `StartingAsync` via interface cast (matches `Headless.MultiTenancy/HeadlessTenancyStartupValidator.cs:42`). In `StartingAsync`, resolve `IDbContextFactory<TContext>` via the captured `Type dbContextType` (reflection: `IServiceProvider.GetRequiredService(typeof(IDbContextFactory<>).MakeGenericType(dbContextType))`), open a context, then inspect `context.Model.FindEntityType(typeof(SettingValueRecord))` — **no DB round-trip**. If null, throw `InvalidOperationException` with the actionable message naming `modelBuilder.AddHeadlessSettings`. Same for `SettingDefinitionRecord`.
- Mode 1 entity configuration: `internal sealed class SettingValueRecordConfiguration(SettingsStorageOptions options) : IEntityTypeConfiguration<SettingValueRecord>` ports the existing inline `modelBuilder.Entity<SettingValueRecord>(b => ...)` block. Same for definition record.
- Mode 1 repos: drop the `ISettingsDbContext` constraint, switch `db.SettingValues` → `db.Set<SettingValueRecord>()`. Audit every public method; prepend `.AsNoTracking()` to read queries (anything that returns `Task<…>` / `Task<List<…>>` without subsequent change tracking).
- Mode 2 Postgres: copy `PostgreSqlStorageInitializer` shape from `Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs`. DDL composes `CREATE SCHEMA IF NOT EXISTS`, `CREATE TABLE IF NOT EXISTS` for `SettingValues` + `SettingDefinitions`, `CREATE UNIQUE INDEX IF NOT EXISTS` for `(Name, ProviderName, ProviderKey)` (mirroring the EF-side unique index in `SettingsModelBuilderExtensions`). Wrap main batch in transactional DDL; no CONCURRENT indexes needed (no hot retry-pickup path).
- Mode 2 SqlServer: copy `SqlServerStorageInitializer` shape. Use `IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name='…')` blocks wrapped in `BEGIN TRY ... BEGIN CATCH IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW; END CATCH;` — extending the existing pattern to also tolerate concurrent-replica races on index/column creation.
- Mode 2 Postgres concurrent-race handling: catch SQLSTATE `42P07` (duplicate_table), `42P06` (duplicate_schema), `42P05` (duplicate_index) in addition to the `IF NOT EXISTS` guards.
- Mode 2 hosted service: the per-feature `PostgreSqlSettingsStorageInitializer` and `SqlServerSettingsStorageInitializer` **themselves implement `IHostedService + IInitializer`** (no separate `SettingsBootstrapper` wrapper class). Registered via `services.AddInitializerHostedService<PostgreSqlSettingsStorageInitializer>()` from `Headless.Hosting`, which wires `TryAddSingleton`, `TryAddEnumerable IInitializer`, `TryAddEnumerable IHostedService` in one call. `IInitializer.IsInitialized` flips to `true` after `InitializeAsync` completes. Consumers gate any read/write path against `IInitializer.WaitForInitializationAsync` rather than registration order. Mode mutual-exclusion is enforced at `AddHeadlessSettings` registration time (`Extensions.Count != 1`), not at bootstrap time.
- Mode 2 repos: new `PostgreSqlSettingValueRecordRepository` and `SqlServerSettingValueRecordRepository` using raw ADO (`Npgsql` / `Microsoft.Data.SqlClient`). No Dapper. Hand-written `NpgsqlCommand` / `SqlCommand` with parameterized SQL per method. Mirror the existing `PostgreSqlDataStorage` style from `Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs`.
- New `.csproj` files: SDK `Headless.NET.Sdk` (omit version per global.json). Reference `Headless.Settings.Core`, `Headless.Settings.Storage.EntityFramework` (for shared options class only — confirm during impl, may move options to Core to avoid this cross-package dep), `Npgsql` / `Microsoft.Data.SqlClient` as needed. No package versions in csproj (Central Package Management).
- Brainstorm doc update: search/replace `Postgres` → `PostgreSql` in R14, R15 (literal package names like `Headless.Settings.Storage.Postgres` become `Headless.Settings.Storage.PostgreSql`). Update the dropped "Note: today's package uses `PostgreSql` ... the literal provider segment is a planning-deferred consistency choice" since the choice is now made.

**Execution note:** Start with a failing integration test for AE2 (missing entity-config diagnostic) — the most surprising new behavior to a consumer. Then port the existing Settings integration tests to the new shape and confirm they pass before deleting `ISettingsDbContext` and `SettingsDbContext`.

**Initialization gating (not registration order):** `BackgroundService.StartAsync` returns immediately and fires `ExecuteAsync` on a thread-pool thread; subsequent hosted services start in parallel with the initializer's DDL. Consumers — including `Headless.Settings.Core` seeding services and any other code that reads/writes settings during host startup — MUST gate their entry by awaiting `IInitializer.WaitForInitializationAsync(ct)` (resolved from the registered `IEnumerable<IInitializer>` filtered to the Settings instance, or from the concrete `PostgreSqlSettingsStorageInitializer` keyed singleton). Document this contract in `Headless.Settings.Storage.EntityFramework/README.md` and the new provider package READMEs. If a Core seeding service today does not gate on `WaitForInitializationAsync`, U1 also adds the gate there.

**Patterns to follow:**
- `src/Headless.Messaging.Core/Configuration/MessagingSetupBuilder.cs` (per-feature builder + `IXOptionsExtension`)
- `src/Headless.Messaging.PostgreSql/Setup.cs` + `src/Headless.Messaging.SqlServer/Setup.cs` (provider Setup classes)
- `src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs` (`Bootstrapper` shape)
- `src/Headless.AuditLog.EntityFramework/AuditLogModelBuilderExtensions.cs` (idempotent `ModelBuilder` extension)
- `src/Headless.AuditLog.EntityFramework/AuditLogEntryConfiguration.cs` (`IEntityTypeConfiguration<T>` shape)
- `src/Headless.AuditLog.EntityFramework/EfReadAuditLog.cs` (`Set<T>() + AsNoTracking` repo shape)
- `src/Headless.MultiTenancy/HeadlessTenancyStartupValidator.cs` (`IHostedLifecycleService.StartingAsync` pattern)
- `src/Headless.Hosting/Initialization/IInitializer.cs` + `AddInitializerHostedService<T>` (hosted-service primitive)

**Test suite design:** Mode 1 work covered by the existing `Headless.Settings.Tests.Integration` project (test base + custom schema tests rewritten in-place). Mode 2 work covered by two new integration test projects: `Headless.Settings.Storage.PostgreSql.Tests.Integration` (extends `HeadlessPostgreSqlFixture`) and `Headless.Settings.Storage.SqlServer.Tests.Integration` (extends the SqlServer equivalent from `Headless.Testing.Testcontainers`). Both new projects use the standard `Headless.NET.Sdk.Test` SDK and `xunit.v3.mtp-v2`.

**Test scenarios:**
- Happy path (Mode 1): Given a consumer with `AddDbContextFactory<AppDbContext>` + `modelBuilder.AddHeadlessSettings(opts)` + `AddHeadlessSettings(s => s.UseEntityFramework<AppDbContext>())`, when the test reads a setting via `ISettingValueRecordRepository.GetAsync`, then a `SELECT … AsNoTracking` is issued via a fresh `DbContext` from the factory and the value is returned.
- Happy path (Mode 1): Given a consumer overrides `o.Schema = "tenant_a"`, when the EF model is built, then `SettingValueRecord` and `SettingDefinitionRecord` map to tables in the `tenant_a` schema.
- Happy path (Mode 2 Postgres): Given a consumer calls `AddHeadlessSettings(s => s.UsePostgreSql(connStr))` against an empty Postgres database, when the host starts, then the `SettingsBootstrapper` runs `CREATE SCHEMA settings`, `CREATE TABLE … IF NOT EXISTS`, `CREATE UNIQUE INDEX … IF NOT EXISTS` before any consumer endpoint can serve a request. **Covers AE5.**
- Happy path (Mode 2 Postgres): Given the bootstrapper has run, when the test inserts a setting via `PostgreSqlSettingValueRecordRepository`, then `SELECT` returns the inserted row.
- Happy path (Mode 2 SqlServer): Mirror of Postgres scenario.
- Edge case (Mode 1): Given a consumer registers two contexts (`AddDbContextFactory<AdminDbContext>` + `AddDbContextFactory<AuthDbContext>`) and calls `AddHeadlessSettings(s => s.UseEntityFramework<AdminDbContext>())` once, when both Settings and a separate Permissions registration use different contexts, then each resolves to its own context with no cross-contamination. **Covers AE4.**
- Edge case (Mode 2): Given the bootstrapper runs against a database where the schema already exists, when `InitializeAsync` is called a second time (in another test run), then no exception is thrown and the existing tables are preserved (idempotency).
- Edge case (Mode 1): Given a custom `Schema = "_invalid space"`, when options validate at startup, then `OptionsValidationException` is thrown by the FluentValidation pipeline (`ValidateOnStart`).
- Error path (Mode 1, R5): Given a consumer registers `AddHeadlessSettings(s => s.UseEntityFramework<AppDbContext>())` but does NOT call `modelBuilder.AddHeadlessSettings(opts)` in `OnModelCreating`, when the host starts, then `IHostedLifecycleService.StartingAsync` throws `InvalidOperationException` whose message names `SettingValueRecord` AND instructs the consumer to call `modelBuilder.AddHeadlessSettings`. **Covers AE2.**
- Error path (R2): Given a consumer calls both `s.UseEntityFramework<AppDbContext>()` and `s.UsePostgreSql(connStr)` inside the same `AddHeadlessSettings`, when the host builds, then `InvalidOperationException` is thrown at `_RegisterCoreSettingsServices` time naming both provider verbs. **Covers AE1.**
- Error path (Mode 2): Given the connection string points to an unreachable database, when the bootstrapper runs, then the exception propagates and the host fails to start with a clear message.
- Integration: Given the same `SettingsStorageOptions.Schema = "settings_v2"`, when Mode 1 EF model is built and Mode 2 DDL is run, then both target the `settings_v2` schema (one options class drives both modes). **Covers AE7.**
- Integration: Given a singleton `ISettingValueRecordRepository` (Mode 1) injected into multiple scoped services, when concurrent reads execute, then each resolves a fresh `DbContext` via the factory, no captured-scope race, all reads `AsNoTracking`. **Covers AE3.**

**Verification:**
- All test scenarios above pass in `Headless.Settings.Tests.Integration`, `Headless.Settings.Storage.PostgreSql.Tests.Integration`, `Headless.Settings.Storage.SqlServer.Tests.Integration`.
- `ISettingsDbContext.cs`, `SettingsDbContext.cs` are deleted; no references in `git grep`.
- `git grep AddSettingsManagementDbContextStorage` returns zero hits.
- `docs/llms/settings.md` references only the new shape.
- `src/Headless.Settings.Storage.EntityFramework/README.md` exemplifies the new shape using a consumer's `AppDbContext`.
- `docs/brainstorms/2026-05-24-storage-initialization-unification-requirements.md` uses `PostgreSql` consistently (no `Postgres` package names).
- `make build` succeeds across the worktree.
- All new `.csproj` files appear in `headless-framework.slnx`.

---

### U2. Permissions: rewrite Mode 1 + ship Mode 2 (Postgres, SqlServer) + docs

**Goal:** Mirror U1 for Permissions. Rewrite `Headless.Permissions.Storage.EntityFramework`, ship `Headless.Permissions.Storage.PostgreSql` and `Headless.Permissions.Storage.SqlServer`. Update `docs/llms/permissions.md` and the package README.

**Requirements:** R1, R2, R3, R4, R5, R6, R7, R8, R9, R10 (Permissions rows), R11, R13, R14 (Permissions rows), R16, R18, R19.

**Dependencies:** U1 (canonical shape established).

**Files:**
- Same structure as U1 substituting `Permissions` / `PermissionGrant` / `PermissionDefinitionRecord` / `PermissionsStorageOptions` for Settings names.
- Modify: `src/Headless.Permissions.Storage.EntityFramework/Setup.cs`, `EfPermissionGrantRepository.cs`, `EfPermissionDefinitionRecordRepository.cs`, `PermissionsModelBuilderExtensions.cs`, `PermissionsStorageOptions.cs`, `README.md`
- Delete: `src/Headless.Permissions.Storage.EntityFramework/IPermissionsDbContext.cs`, `PermissionsDbContext.cs`
- Create: per-entity `IEntityTypeConfiguration<>` files, `HeadlessPermissionsSetupBuilder.cs`, `IPermissionsOptionsExtension.cs`, `SetupPermissionsEntityFramework`, `SettingsBootstrapper.cs`-equivalent, `IPermissionsStorageInitializer.cs`, validation startup gate
- Create (new package): `src/Headless.Permissions.Storage.PostgreSql/` (full shape)
- Create (new package): `src/Headless.Permissions.Storage.SqlServer/` (full shape)
- Modify: `headless-framework.slnx`
- Modify: `docs/llms/permissions.md`
- Test: `tests/Headless.Permissions.Tests.Integration/` — update test base + custom schema tests to new shape
- Test (new): `tests/Headless.Permissions.Storage.PostgreSql.Tests.Integration/`
- Test (new): `tests/Headless.Permissions.Storage.SqlServer.Tests.Integration/`

**Approach:**
- Apply the U1 canonical shape verbatim, substituting Permissions-specific entity types (`PermissionGrant`, `PermissionDefinitionRecord`).
- Verify Permissions has no additional concepts beyond Settings (e.g., a permission *check* path vs. the storage path) — focus this unit on storage only.

**Patterns to follow:** Same as U1, plus U1 itself as the canonical reference.

**Test suite design:** Same as U1 (existing `Headless.Permissions.Tests.Integration` + two new Mode 2 integration projects).

**Test scenarios:** Same shape as U1, substituting Permissions entities (`PermissionGrant`, `PermissionDefinitionRecord`) for Settings. **Covers AE1, AE2, AE3, AE4, AE5, AE7** for Permissions.

**Verification:**
- Same shape as U1: tests pass, deleted types are gone via `git grep`, docs updated, build succeeds, slnx updated.

---

### U3. Features: rewrite Mode 1 + ship Mode 2 (Postgres, SqlServer) + docs

**Goal:** Mirror U1 for Features. Rewrite `Headless.Features.Storage.EntityFramework`, ship `Headless.Features.Storage.PostgreSql` and `Headless.Features.Storage.SqlServer`. Update `docs/llms/features.md` and the package README.

**Requirements:** R1, R2, R3, R4, R5, R6, R7, R8, R9, R10 (Features rows), R11, R13, R14 (Features rows), R16, R18, R19.

**Dependencies:** U1.

**Files:**
- Same structure as U1 substituting `Features` / `FeatureValueRecord` / `FeatureDefinitionRecord` / `FeatureGroupDefinitionRecord` / `FeaturesStorageOptions` for Settings names.
- Modify: `src/Headless.Features.Storage.EntityFramework/Setup.cs`, `EfFeatureValueRecordRecordRepository.cs`, `EfFeatureDefinitionRecordRepository.cs`, `FeaturesModelBuilderExtensions.cs`, `FeaturesStorageOptions.cs`, `README.md`
- Delete: `src/Headless.Features.Storage.EntityFramework/IFeaturesDbContext.cs`, `FeaturesDbContext.cs`
- Create: per-entity `IEntityTypeConfiguration<>` files (three entities: `FeatureValueRecord`, `FeatureDefinitionRecord`, `FeatureGroupDefinitionRecord`), `HeadlessFeaturesSetupBuilder.cs`, `IFeaturesOptionsExtension.cs`, `SetupFeaturesEntityFramework`, bootstrapper, initializer interface, validation startup gate
- Create (new package): `src/Headless.Features.Storage.PostgreSql/` (full shape — note three tables instead of Settings' two)
- Create (new package): `src/Headless.Features.Storage.SqlServer/` (full shape)
- Modify: `headless-framework.slnx`
- Modify: `docs/llms/features.md`
- Test: `tests/Headless.Features.Tests.Integration/` — update test base + custom schema tests
- Test (new): `tests/Headless.Features.Storage.PostgreSql.Tests.Integration/`
- Test (new): `tests/Headless.Features.Storage.SqlServer.Tests.Integration/`

**Approach:**
- Mirror U1. Features has three entities (Settings has two) — DDL composition needs three `CREATE TABLE` blocks and the corresponding indexes.
- The startup gate validates all three entities; the error message names whichever is missing first.

**Patterns to follow:** Same as U1.

**Test suite design:** Same as U1 (existing `Headless.Features.Tests.Integration` + two new Mode 2 integration projects).

**Test scenarios:** Same shape as U1, substituting Features entities. **Covers AE1, AE2, AE3, AE4, AE5, AE7** for Features.

**Verification:**
- Same shape as U1.

---

### U4. AuditLog: rename package + rewrite Mode 1 + ship Mode 2 (Postgres, SqlServer) + docs

**Goal:** Rename `Headless.AuditLog.EntityFramework` to `Headless.AuditLog.Storage.EntityFramework`. Rewrite its `Setup.cs` to expose `AddHeadlessAuditLog(s => s.UseEntityFramework<TContext>())` replacing the existing `AddHeadlessAuditLogEntity<TContext>()`. Ship `Headless.AuditLog.Storage.PostgreSql` and `Headless.AuditLog.Storage.SqlServer`. Update `docs/llms/audit-log.md` and the package README.

**Requirements:** R1, R2, R3, R4, R5, R6, R7, R10 (AuditLog rows), R11, R13, R14 (AuditLog rows), R15 (AuditLog rename row), R16, R18, R19. **Explicitly excluded:** R12's "Mode 2 + EF enlistment" combination for AuditLog — deferred per Scope Boundaries → Deferred to Follow-Up Work.

**Dependencies:** U1.

**Files:**
- Move: `src/Headless.AuditLog.EntityFramework/` → `src/Headless.AuditLog.Storage.EntityFramework/`
- Rename: `Headless.AuditLog.EntityFramework.csproj` → `Headless.AuditLog.Storage.EntityFramework.csproj`
- Modify: `src/Headless.AuditLog.Storage.EntityFramework/Setup.cs` (rename class to `SetupAuditLogEntityFramework`; rewrite to `AddHeadlessAuditLog(s => s.UseEntityFramework<TContext>())` via per-feature setup builder)
- Modify: `src/Headless.AuditLog.Storage.EntityFramework/AuditLogModelBuilderExtensions.cs` (rename extension to `AddHeadlessAuditLog` for naming consistency; keep idempotent shape; take `AuditLogStorageOptions` explicitly)
- Modify: `src/Headless.AuditLog.Storage.EntityFramework/EfAuditLog.cs`, `EfAuditChangeCapture.cs`, `EfAuditLogStore.cs` — **STAY SCOPED** on consumer's `TContext` (carved-out exception to R7). These three types write via `context.Set<AuditLogEntry>().Add(...)` WITHOUT calling `SaveChanges` so audit rows commit atomically inside the consumer's unit of work; converting to singleton-with-factory would break that contract. Verify `Set<T>()` access (already present per research) and explicit-options entity configuration are the only changes here.
- Modify: `src/Headless.AuditLog.Storage.EntityFramework/EfReadAuditLog.cs` — read-only path, may move to singleton with `IDbContextFactory<TContext>` + `AsNoTracking()`. This is the only AuditLog repo that follows the R7 singleton-with-factory shape.
- Create: `src/Headless.AuditLog.Storage.EntityFramework/HeadlessAuditLogSetupBuilder.cs`, `AuditLogStorageOptions.cs` (extract from inline parameters in current extension), `Internal/AuditLogEntityValidationStartupGate.cs`. **No** `IAuditLogOptionsExtension.cs` — the generic `IStorageOptionsExtension` lives in `Headless.Hosting` (see U1). **No** `AuditLogStorageMarkerService.cs` — mode mutual-exclusion is enforced at registration time via `Extensions.Count != 1` (see Key Technical Decisions).
- Modify: `src/Headless.AuditLog.Storage.EntityFramework/README.md`
- Create (new package): `src/Headless.AuditLog.Storage.PostgreSql/` (full shape — single table `AuditLogEntry` with the JSON column)
- Create (new package): `src/Headless.AuditLog.Storage.SqlServer/`
- Modify: `headless-framework.slnx` (rename existing entry, add new entries)
- Modify: `docs/llms/audit-log.md`
- Test: `tests/Headless.AuditLog.EntityFramework.Tests.Integration/` — rename project to `Headless.AuditLog.Storage.EntityFramework.Tests.Integration`, update fixture wiring to new shape (`AddHeadlessAuditLog(s => s.UseEntityFramework<TContext>())`)
- Test (new): `tests/Headless.AuditLog.Storage.PostgreSql.Tests.Integration/`
- Test (new): `tests/Headless.AuditLog.Storage.SqlServer.Tests.Integration/`
- Modify: every `<ProjectReference Include="...Headless.AuditLog.EntityFramework..." />` across the entire worktree (search `**/*.csproj` for the old package name and update; likely includes test projects, sample apps, and any inter-package references). Run a `git grep "Headless.AuditLog.EntityFramework"` after the rename to confirm zero stale references remain outside the rename commit.

**Approach:**
- Rename first via `git mv`, commit the rename in isolation (so blame/history tracks).
- Then rewrite the `Setup.cs` to the new builder shape. The existing AuditLog repos already use `context.Set<T>()` + `AsNoTracking` per research. The lifetime change is split: `EfReadAuditLog<TContext>` moves to singleton-with-`IDbContextFactory`; `EfAuditLog<TContext>`, `EfAuditChangeCapture`, `EfAuditLogStore` **stay scoped** to preserve transactional atomicity (R7 carve-out documented in Key Technical Decisions).
- The AuditLog rewrite also extracts the inline `tableName`/`schema`/`jsonColumnType` parameters from `ConfigureAuditLog` into `AuditLogStorageOptions` for consistency with R13 (one options class drives both modes). The current `ConfigureAuditLog(modelBuilder, tableName, schema, jsonColumnType)` signature is replaced by `AddHeadlessAuditLog(modelBuilder, AuditLogStorageOptions options)` — same idempotent shape, renamed to match the unified `AddHeadlessX(ModelBuilder, XStorageOptions)` convention per R6, options taken explicitly (no service location off the context).
- **AuditLog builder shape — sub-modifier, not peer Extension**: `UsePostgreSql(connStr, opts)` and `UseSqlServer(connStr, opts)` are the Mode 2 entry verbs; `WithEntityFrameworkEnlistment<TContext>()` is a sub-modifier the consumer can chain onto either provider verb to enable transactional enlistment in the future. Today this plan implements `WithEntityFrameworkEnlistment` as a no-op (the deferred R12 capability), but the shape is in place so `Extensions.Count == 1` invariant survives when enlistment lands. Mode 1 `UseEntityFramework<TContext>()` remains a separate Extension (still mutually exclusive with `UsePostgreSql` / `UseSqlServer`).
- Mode 2 packages: single table `audit_log` with the JSON column (`jsonb` Postgres, `nvarchar(max)` SqlServer; native SqlServer 2022+ `JSON` type NOT auto-detected — keeps DDL deterministic across versions; consumer can override via `AuditLogStorageOptions.JsonColumnType`).

**Patterns to follow:** Same as U1, plus the existing `AuditLogModelBuilderExtensions` for the idempotent extension shape.

**Test suite design:** Existing renamed integration project + two new Mode 2 integration projects.

**Test scenarios:**
- All U1 scenario categories apply (happy paths, edge cases, error paths, integration), substituting `AuditLogEntry` for Settings entities.
- Plus: Edge case — when `jsonColumnType` defaults to `jsonb` (Postgres) / `nvarchar(max)` (SqlServer), the column type is set accordingly. Override via `AuditLogStorageOptions.JsonColumnType` honored in both EF model and raw DDL.

**Verification:**
- Tests pass; rename is clean (`git log --follow` works on moved files); `git grep AddHeadlessAuditLogEntity` returns zero hits outside the rename commit; `docs/llms/audit-log.md` references only the new shape; build succeeds; slnx updated.

---

### U5. Identity Setup rename + Messaging provider renames

**Goal:** Pure-cosmetic renames bundled together (both share rationale: naming consistency, zero behavior change). (1) Rename `OrmEntityFrameworkIdentitySetup` → `SetupIdentityEntityFramework` and route the existing `AddHeadlessDbContext<TDbContext, TUser, …>` overloads through a `HeadlessIdentitySetupBuilder` shell so the entry point matches `AddHeadlessIdentity(s => s.UseEntityFramework<…>())` per R1. Preserve the existing 8-9 generic-parameter overloads intact — reshaping them is deferred. (2) Rename `Headless.Messaging.PostgreSql` → `Headless.Messaging.Storage.PostgreSql` and `Headless.Messaging.SqlServer` → `Headless.Messaging.Storage.SqlServer`.

**Requirements:** R1, R4, R10 (Identity row), R15 (Messaging rename row), R16, R19.

**Dependencies:** U1 (for the builder shape Identity adopts).

**Files (Identity):**
- Modify: `src/Headless.Identity.Storage.EntityFramework/Setup.cs` (rename class to `SetupIdentityEntityFramework`; introduce `HeadlessIdentitySetupBuilder` + entry point `AddHeadlessIdentity(Action<HeadlessIdentitySetupBuilder>)`; existing overloads become extension members on the setup builder via a `SetupIdentityEntityFramework` provider-extension class)
- Create: `src/Headless.Identity.Storage.EntityFramework/HeadlessIdentitySetupBuilder.cs`
- Modify: `src/Headless.Identity.Storage.EntityFramework/README.md`
- Modify: `docs/llms/identity.md`
- Test: `tests/Headless.Identity.Storage.EntityFramework.Tests.Integration/` — update registration call sites

**Files (Messaging renames):**
- Move: `src/Headless.Messaging.PostgreSql/` → `src/Headless.Messaging.Storage.PostgreSql/`; same for SqlServer (via `git mv`)
- Rename: csproj filenames to match the new directory names
- Modify: every `<ProjectReference Include="...Headless.Messaging.{PostgreSql,SqlServer}..." />` across the worktree (run `git grep "Headless.Messaging.PostgreSql\|Headless.Messaging.SqlServer"` after the rename to confirm zero stale references outside the rename commit)
- Modify: `headless-framework.slnx` (rename entries)
- Modify: `docs/llms/messaging.md` (search/replace package names; user-facing API verbs `UsePostgreSql` / `UseSqlServer` are unchanged)
- Modify: `src/Headless.Messaging.Storage.PostgreSql/README.md`, `src/Headless.Messaging.Storage.SqlServer/README.md`
- Test: `tests/Headless.Messaging.PostgreSql.Tests.Integration/` → `tests/Headless.Messaging.Storage.PostgreSql.Tests.Integration/`; same for SqlServer

**Approach:**
- Identity: minimum-viable. The existing `HeadlessIdentityDefaultsSentinel` pattern continues to gate idempotent `IdentityOptions.Stores.SchemaVersion`. Overload signatures stay literally unchanged; they become extension members on `HeadlessIdentitySetupBuilder` instead of `IServiceCollection`. No `IStorageInitializer`/Mode 2 work per R10. Startup validation (R5) is not applicable — ASP.NET Core Identity enforces entity-config presence itself via `IdentityDbContext`.
- Messaging: `git mv` to preserve history; commit each package rename in isolation. Leave internal namespaces (`Headless.Messaging.PostgreSql`, etc.) and class names (`PostgreSqlOptions`) alone — the package name is the only intended change. Confirm no consumer-facing public API references the old package name in a way that wouldn't be transparently fixed by the rename.

**Patterns to follow:**
- `src/Headless.Messaging.Core/Configuration/MessagingSetupBuilder.cs` (per-feature builder shape; Identity's builder is simpler since there's only Mode 1)
- Existing `OrmEntityFrameworkIdentitySetup` (preserve its sentinel + multi-overload generic surface)
- `git mv` for atomic rename
- `headless-framework.slnx` group structure

**Test suite design:** Existing `Headless.Identity.Storage.EntityFramework.Tests.Integration` and existing renamed Messaging integration projects continue to own coverage.

**Test scenarios:**
- Happy path (Identity): Given the existing Identity test fixture registers `AddHeadlessIdentity(s => s.UseEntityFramework<…>())` using the new shape with the same generic parameters as today's `AddHeadlessDbContext<…>`, when ASP.NET Core Identity `UserManager` resolves, then create/find user operations succeed end-to-end.
- Edge case (Identity): Given a second registration of `AddHeadlessIdentity` against a different `TContext`, when the host builds, then both register without collision (multi-context per R8 — note: this is two different `TContext` types, not the same `TContext` twice).
- Happy path (Messaging): All existing Messaging integration tests continue to pass against the renamed projects — behavior is unchanged.

**Verification:**
- Identity tests pass; `git grep OrmEntityFrameworkIdentitySetup` returns zero hits; `docs/llms/identity.md` references only the new shape.
- `git log --follow` works on moved Messaging files; `headless-framework.slnx` references the new names; `git grep "Headless.Messaging.PostgreSql\|Headless.Messaging.SqlServer"` returns zero hits outside the new path itself; Messaging integration tests pass.
- `make build` succeeds across the worktree.

---

## System-Wide Impact

- **Interaction graph:** No new cross-feature interactions. Each per-feature `Bootstrapper` runs independently as an `IHostedService`. The Mode 1 startup-validation gates (`IHostedLifecycleService.StartingAsync`) run before any `IHostedService.StartAsync`, ensuring missing-entity diagnostics fire before the consumer can serve traffic.
- **Error propagation:** Mode 1 validation errors surface as `InvalidOperationException` at startup with actionable messages naming the missing `modelBuilder.AddHeadlessX(options)` call. Mode 2 DDL errors propagate from `IStorageInitializer.InitializeAsync` to `BackgroundService.ExecuteAsync` to the host's startup fault path.
- **State lifecycle risks:** Mode 2 DDL is idempotent by construction (`CREATE … IF NOT EXISTS`). The `IInitializer.IsInitialized` flag is set after `InitializeAsync` completes successfully; consumers depending on `WaitForInitializationAsync` will block correctly until ready. No partial-write risk for DDL (Postgres wraps in transactional DDL; SqlServer uses `BEGIN TRY/CATCH` per existing Messaging precedent).
- **API surface parity:** Settings/Permissions/Features/Identity/AuditLog/Messaging all converge on `AddHeadlessX(s => s.Use…)`. Future packages (including the deferred Jobs unification) should follow this shape.
- **Integration coverage:** Each new Mode 2 package gets its own Testcontainers-backed integration project. Mode 1 startup-validation tests cover the AE2 missing-entity diagnostic — a behavioral seam unit tests alone cannot prove (requires a real `DbContext` lifecycle).
- **Unchanged invariants:**
  - Application-layer caches (`ISettingValueProvider`, `IFeatureValueProvider`, `IPermissionGrantProvider`) and their `Headless.<Feature>.Core` packages are NOT modified. The repository contracts (`ISettingValueRecordRepository`, etc.) remain unchanged at the interface level — only their EF implementations change to drop `IXxxDbContext`. Caching invalidation behavior unchanged.
  - `Headless.Messaging` runtime behavior is unchanged. U6 is a pure package rename.
  - Identity's ASP.NET Core Identity surface (`UserManager`, `RoleManager`, `IdentityDbContext` overloads, `IdentityOptions`) is unchanged.
  - `Headless.Settings.Core` / `Headless.Permissions.Core` / `Headless.Features.Core` are NOT modified. The unification is at the storage layer only.

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Per-feature `IStorageInitializer` interface duplication (~5 features × ~10 lines = ~50 lines of identical code). Future changes (e.g., adding `Reset` for tests) require touching every copy. | Accepted per user direction. The generic builder-plumbing contract `IStorageOptionsExtension` is lifted into `Headless.Hosting` to keep duplication scoped to the storage-domain interface only. Document the convention in U1's README. |
| Mode 1 writes (Settings/Permissions/Features) commit on a separate connection from the consumer's request transaction — singleton-with-factory shape does not enlist in the outer scoped `DbContext` transaction. | Documented as a trade-off in the Mode 1 README. Consumers needing atomicity with their own writes use `Set<T>()` on their own context directly. AuditLog write-side repos are explicitly excluded from this shape — they stay scoped to preserve atomicity (see U4). |
| Mode 1 startup validation surprises consumers used to `AddDbContext<T>` scoped only. | The startup-validation gate (`IModel.FindEntityType`-based, no DB round-trip) produces a clear error naming the missing `modelBuilder.AddHeadlessX(options)` call. README and `docs/llms` show the `AddDbContextFactory<T>` registration explicitly. |
| Mode 2 raw DDL drift from EF-side entity configuration over time (e.g., a new index added to Mode 1 isn't mirrored in Mode 2). | The new Mode 2 integration tests assert table+index existence post-bootstrapper. Adding a new index to Mode 1 should produce a failing Mode 2 integration test that catches the drift. Document this in U1's README. |
| AuditLog write-path lifetime preservation: the carve-out keeping `EfAuditLog<TContext>`, `EfAuditChangeCapture`, `EfAuditLogStore` scoped is load-bearing — they write via `context.Set<AuditLogEntry>().Add(...)` without calling `SaveChanges`, relying on the consumer's `SaveChangesAsync` to commit atomically. Implementer must not "fix" the lifetime to singleton without preserving this contract. | U4 Approach explicitly enumerates which AuditLog types stay scoped vs. move to factory. Test scenarios include "audit entry rolls back when consumer transaction rolls back" to guard the atomicity invariant. |
| Concurrent-replica bootstrap race on `CREATE TABLE` / `CREATE INDEX` could surface duplicate-object errors past `IF NOT EXISTS` guards (TOCTOU). | Catch SQLSTATE `42P05`/`42P06`/`42P07` (Postgres) and error numbers `2714`/`1913`/`2759` (SqlServer) in the `IStorageInitializer.InitializeAsync` paths. Mode 2 integration test should assert that concurrent bootstrapper runs against the same database both succeed. |
| The Identity rename might leak the `OrmEntityFrameworkIdentitySetup` class name through test helpers in `Headless.Testing.Identity` or similar internal test scaffolding. | Grep for `OrmEntityFrameworkIdentitySetup` across all `tests/` projects during U5; rename all references. The renamed Setup class is the only point of friction. |
| Messaging provider renames break any external consumer's `<PackageReference>` to `Headless.Messaging.PostgreSql`. | Greenfield posture per `CLAUDE.md` — no shims. NuGet consumers (none yet) update their package reference. Document in U5's `README.md` and in `docs/llms/messaging.md` change notes. |
| Mode 2 SqlServer DDL needs `Microsoft.Data.SqlClient` 7.0.1 (pinned in `Directory.Packages.props`). The existing `Headless.Messaging.SqlServer` already uses it — no new dependency surface. | Verify during U1 that the new `Headless.Settings.Storage.SqlServer` `.csproj` references `Microsoft.Data.SqlClient` (without version, per Central Package Management). |
| "Clean consumer `DbContext` public API" framing oversells the win — framework entity types remain reachable via `Set<T>()` and visible in the EF `Model`. | Reframe in Problem Frame and README: the win is removing the `IXxxDbContext` interface ceremony and named `DbSet<T>` properties from the consumer's public API. Framework entities remain in the model (unavoidable for shared-context Mode 1). |

---

## Documentation / Operational Notes

- **Docs sync per `CLAUDE.md`**: `src/Headless.<Package>/README.md` and `docs/llms/<domain>.md` must update in the same commit as the code change. Each implementation unit explicitly includes both surfaces in `**Files:**`.
- **Authoring rules**: `docs/authoring/AUTHORING.md` describes the README + llms doc lockstep policy. Re-read before editing either surface.
- **Brainstorm doc update** (part of U1): align the brainstorm doc's R14/R15 wording with the picked `PostgreSql` spelling.
- **Capture the EF Core + raw-DDL patterns in `docs/solutions/`** when the plan lands: per the institutional-learning gap noted in research, this is the first work in the codebase to establish per-feature `IStorageInitializer` + `Bootstrapper` patterns outside Messaging. Add entries under `docs/solutions/architecture-patterns/` documenting (a) the duplication-vs-shared-abstraction decision and (b) the Mode 1 `IDbContextFactory` + `Set<T>()` + `AsNoTracking` shape.

---

## Sources & References

- **Origin document:** `docs/brainstorms/2026-05-24-storage-initialization-unification-requirements.md`
- **Prior-art reference (template for unified builder):**
  - `src/Headless.Messaging.Core/Configuration/MessagingSetupBuilder.cs`
  - `src/Headless.Messaging.Core/Configuration/IMessagesOptionsExtension.cs`
  - `src/Headless.Messaging.Core/Setup.cs`
  - `src/Headless.Messaging.Core/Internal/IBootstrapper.Default.cs`
  - `src/Headless.Messaging.PostgreSql/Setup.cs` (mode-verb pattern)
  - `src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs` (raw-DDL composition + idempotency)
  - `src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs`
- **Prior-art reference (Mode 1 repo shape):**
  - `src/Headless.AuditLog.EntityFramework/EfReadAuditLog.cs` (`Set<T>().AsNoTracking()`)
  - `src/Headless.AuditLog.EntityFramework/AuditLogModelBuilderExtensions.cs` (idempotent `ModelBuilder` extension)
  - `src/Headless.AuditLog.EntityFramework/AuditLogEntryConfiguration.cs` (`IEntityTypeConfiguration<T>` shape)
- **Prior-art reference (startup validation + hosted bootstrapper):**
  - `src/Headless.Hosting/Initialization/IInitializer.cs`
  - `src/Headless.Hosting/DependencyInjection/DependencyInjectionExtensions.cs` (`AddInitializerHostedService<T>`)
  - `src/Headless.MultiTenancy/HeadlessTenancyStartupValidator.cs` (`IHostedLifecycleService.StartingAsync`)
- **Institutional learnings:**
  - `docs/solutions/architecture-patterns/messaging-keyed-di-lock-isolation-2026-05-19.md` (keyed DI isolation)
  - `docs/solutions/guides/messaging-transport-provider-guide.md` (Setup/Builder/Options shape)
  - `docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md` (greenfield rule + docs sync)
- **Existing test infrastructure to reuse:** `src/Headless.Testing.Testcontainers/HeadlessPostgreSqlFixture.cs` and SqlServer equivalent
- **Package metadata to follow:** `global.json` (Headless SDKs), `Directory.Packages.props` (EF Core 10.0.8, Npgsql 10.0.2, Microsoft.Data.SqlClient 7.0.1, FluentValidation 12.1.1), `headless-framework.slnx`
