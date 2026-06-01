---
date: 2026-05-24
topic: storage-initialization-unification
---

# Storage Initialization Unification across Headless packages

## Summary

Unify storage registration across Settings, Permissions, Features, Identity, Outbox, Jobs, and Audit behind a single per-package builder verb (`Use*`) and two modes per package: **Mode 1 — shared EF Core** (consumer's `DbContext` hosts the framework entities; repos go through `IDbContextFactory<TContext>` and `context.Set<TEntity>()`; writes are atomic with the consumer's app data) and **Mode 2 — raw-DDL per provider** (Postgres, SqlServer; framework auto-creates its tables at startup; zero EF ceremony). Drop the `IXxxDbContext` interface, the `IModelCacheKeyFactory` ritual, the `AddXxxConfiguration(ModelBuilder, DbContext)` service-lookup pattern, and any path that requires the consumer to scaffold an EF migration against a `DbContext` they do not own.

---

## Problem Frame

The framework today registers storage three different ways depending on which package the consumer is wiring:

- **Settings / Permissions / Features** use `services.AddXxxManagementDbContextStorage(...)` — long compound `IServiceCollection` extension names, three overloads each (dedicated `Action<DbContextOptionsBuilder>`, dedicated `Action<IServiceProvider, DbContextOptionsBuilder>`, shared `<TContext>`), an `IXxxDbContext` interface the consumer must implement on their own `DbContext`, a `ReplaceService<IModelCacheKeyFactory>` ritual in the dedicated path, and an `AddXxxConfiguration(ModelBuilder, DbContext)` call that resolves options off the context via `GetService<IOptions<...>>()` inside `OnModelCreating`.
- **Messaging Outbox** uses a fluent `MessagingSetupBuilder` with `UsePostgreSql(connStr)` / `UseSqlServer(connStr)` + optional `UseEntityFramework<TContext>()` for transaction enlistment. Schema lifecycle is owned by the framework via raw DDL in `IStorageInitializer` implementations. Consumers do not run migrations.
- **Jobs.EntityFramework** uses `JobsOptionsBuilder.AddOperationalStore(...)` hung off a feature-specific builder, with its own `JobsEfCoreOptionBuilder<TTimeJob, TCronJob>`, a `JobsModelCustomizer`, and a generic `JobsDbContext<TTimeJob, TCronJob>` base class.

Three concrete pains result:

1. **The dedicated-context path is a migration trap.** `SettingsDbContext` / `FeaturesDbContext` / `PermissionsDbContext` are real `DbContext` types, but the packages ship no migrations. The consumer must wire `dotnet ef` against a context they do not own, generate the migration, and apply it — without that being documented as a required step. The reasonable mental model ("dedicated context = framework-managed") does not match reality ("dedicated context = framework-named-but-consumer-migrated").
2. **The shared-context path leaks framework types into the consumer's public API.** `ISettingsDbContext` forces the consumer's `AppDbContext` to expose `DbSet<SettingValueRecord>` / `DbSet<SettingDefinitionRecord>` (etc.) as public properties, polluting their domain-shaped API surface with framework internals. ABP has shipped the same pattern for years and it is one of the most-recurring complaints in their issue tracker.
3. **The shapes themselves do not match.** A consumer wiring Outbox + Settings + Jobs in one project must learn three different builder idioms, three different naming conventions, and three different schema-lifecycle stories. There is no "I now know how Headless storage works" moment.

For domain-adjacent data (Settings, Permissions, Features), atomicity with the consumer's own writes is the primary value EF brings — admin-time changes occasionally need to coordinate with a domain event. For operational data (Outbox, Jobs, Audit), atomicity is only needed at the outbox-enlistment seam; the rest of the schema is opaque framework state where the user benefits more from zero-friction startup than from EF semantics.

---

## Actors

- A1. **Framework consumer adopting one package.** Wires Settings only (or Permissions only) into an existing ASP.NET Core app with its own `AppDbContext`. Wants the shortest path from `dotnet add package` to working endpoint.
- A2. **Framework consumer adopting multiple packages.** Wires several Headless storage packages into the same app. Benefits proportionally from a unified shape — the second package onward should feel like the first.
- A3. **Consumer in a locked-down environment.** Cannot allow runtime DDL (DBA controls schema). Must be able to operate the EF mode of any domain package against a context they already manage via their own migration pipeline.
- A4. **Consumer with no EF investment.** Has no `DbContext` at all (Dapper, raw ADO, or an entirely different ORM). Wants Settings/Permissions/Features without being forced into EF Core.
- A5. **Microservice / bounded-context consumer.** Routes different framework storage to different `DbContext` types — e.g., Settings on `AdminDbContext`, Permissions on `AuthDbContext`. Today's single-registration shape implicitly assumes one context.
- A6. **Framework / provider package author.** Maintains the per-provider raw-DDL implementations (`Headless.X.Storage.PostgreSql`, `Headless.X.Storage.SqlServer`) and the shared EF Core entity configurations.

---

## Key Flows

- F1. **Adopt a domain package with the consumer's existing `DbContext` (Mode 1, the documented happy path).**
  - **Trigger:** Consumer installs `Headless.Settings.Storage.EntityFramework` to add Settings to an ASP.NET app that already has `AppDbContext`.
  - **Actors:** A1, A2.
  - **Steps:**
    1. Consumer adds the package and registers `services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(...))` (alongside any existing `AddDbContext<AppDbContext>` scoped registration).
    2. Consumer calls `modelBuilder.AddHeadlessSettings(settingsOptions)` inside their `OnModelCreating`.
    3. Consumer calls `services.AddHeadlessSettings(s => { s.UseEntityFramework<AppDbContext>(); s.ConfigureStorage(o => ...); })`.
    4. Consumer's existing migration pipeline (`dotnet ef migrations add ...`) picks up the new entities. They apply it via their normal deploy path.
    5. At startup, the framework validates that `Set<SettingValueRecord>()` (etc.) resolves on the registered `TContext`; throws a clear error message naming the missing `AddHeadlessSettings(modelBuilder, options)` call if not.
  - **Outcome:** Settings reads and writes are durable, settings writes that occur inside a consumer-controlled transaction are atomic with the consumer's domain writes, and the consumer's `AppDbContext` carries no framework `DbSet`s in its public API.
  - **Covered by:** R1, R2, R5, R6, R8, R9, R12.

- F2. **Adopt a domain package without EF (Mode 2 raw DDL).**
  - **Trigger:** Consumer with no `DbContext` installs `Headless.Settings.Storage.PostgreSql` to get Settings persistence backed by a Postgres database.
  - **Actors:** A1, A4.
  - **Steps:**
    1. Consumer adds the per-provider package and calls `services.AddHeadlessSettings(s => s.UsePostgreSql(connectionString))`.
    2. At startup, the framework's `IStorageInitializer` for Settings runs `CREATE TABLE IF NOT EXISTS` / `CREATE INDEX IF NOT EXISTS` against the configured schema, idempotently.
    3. Settings reads and writes operate against the dedicated tables; no EF is loaded.
  - **Outcome:** Settings work end-to-end without the consumer ever touching EF Core. Writes are not atomic with any consumer-owned transaction (Settings tables live on a separate connection); this is documented as the trade-off for Mode 2 on domain packages.
  - **Covered by:** R3, R4, R7, R10, R12.

- F3. **Adopt an operational package (default Mode 2 with optional Mode 1 transaction enlistment).**
  - **Trigger:** Consumer installs `Headless.Messaging.PostgreSql` for the messaging outbox, with an EF-based domain layer that needs publish-on-commit semantics.
  - **Actors:** A1, A2.
  - **Steps:**
    1. Consumer calls `services.AddHeadlessMessaging(s => { s.UsePostgreSql(connStr); s.UseEntityFramework<AppDbContext>(); ... })`.
    2. The outbox initializer creates the messaging tables at startup via raw DDL.
    3. When the consumer publishes inside a `DbContext`-managed transaction, the outbox borrows the same `DbConnection` and `DbTransaction` so the publish row commits with the domain row.
  - **Outcome:** Outbox onboarding stays zero-friction for the no-EF case while transaction enlistment continues to work in the EF case. Operational packages keep the existing Outbox behavior; the change for them is purely cosmetic (builder verb consistency) plus the addition of net-new per-provider variants for Jobs and Audit.
  - **Covered by:** R1, R3, R11, R12, R13.

- F4. **Microservice / bounded-context split.**
  - **Trigger:** Consumer routes Settings to `AdminDbContext` and Permissions to `AuthDbContext` within one process.
  - **Actors:** A5.
  - **Steps:**
    1. Consumer registers both contexts (via factory + scoped as needed).
    2. Consumer calls `AddHeadlessSettings(s => s.UseEntityFramework<AdminDbContext>())` and `AddHeadlessPermissions(s => s.UseEntityFramework<AuthDbContext>())`.
    3. Framework keys its singleton repos by `TContext` so the two registrations do not collide.
  - **Outcome:** Two independent storage targets in one process, no cross-package coupling enforced by the framework.
  - **Covered by:** R5, R12.

---

## Requirements

**Unified builder shape**

- R1. Every storage-bearing package exposes one entry point on `IServiceCollection`: `AddHeadlessX(Action<HeadlessXBuilder> configure)`. The shape of `HeadlessXBuilder` is consistent across packages: it carries a mode-selecting verb (`UseEntityFramework<TContext>()`, `UsePostgreSql(...)`, `UseSqlServer(...)`), an options-shaping verb (`ConfigureStorage(Action<XStorageOptions>)`), and the package-specific feature verbs already present today (e.g., `Subscribe<...>()` for Messaging).
- R2. Mode-selecting verbs are mutually exclusive within one `AddHeadlessX` call; calling more than one MUST fail fast at registration time with a clear error message naming the conflicting verbs.
- R3. Mode availability per package is fixed by the matrix in **Key Decisions**. Calling a mode verb that the package does not support MUST be a compile error (verb not defined on that package's builder), not a runtime failure.

**Mode 1 — shared EF Core**

- R4. Mode 1 is the documented default for domain packages (Settings, Permissions, Features, Identity).
- R5. The consumer's `DbContext` MUST NOT be required to implement any framework interface (no `IXxxDbContext`). Repositories resolve entities via `context.Set<TEntity>()`. The presence of the entities is validated at startup with a clear, actionable error if missing (naming the `AddHeadlessX(modelBuilder, options)` call the consumer needs to add).
- R6. Entity configuration is exposed as a single `ModelBuilder` extension per package: `modelBuilder.AddHeadlessX(XStorageOptions options)`. The extension MUST take options explicitly and MUST NOT pull options off the `DbContext` via service location.
- R7. Repositories MUST take `IDbContextFactory<TContext>` (registered as singleton). Reads MUST default to `AsNoTracking()`. Writes use change tracking inside a unit of work owned by the repo method. The repo MUST NOT capture a scoped `DbContext` and MUST NOT require one to be registered as scoped.
- R8. Multiple Mode-1 registrations against different `TContext` types MUST coexist within one process. Repositories are keyed by `TContext` under the hood; no global "the Headless Settings context" assumption is allowed.
- R9. The `IModelCacheKeyFactory` replacement (`XStorageModelCacheKeyFactory`) is removed from Mode 1. Mode 1 assumes one storage-options binding per `TContext` lifetime; this assumption is documented.

**Mode 2 — raw DDL per provider**

- R10. Each domain and operational package ships `Headless.X.Storage.PostgreSql` and `Headless.X.Storage.SqlServer` as separate NuGet packages providing `IStorageInitializer` implementations for Postgres and SqlServer respectively. The initializer auto-creates tables and indexes via `CREATE … IF NOT EXISTS` at startup, idempotently. Identity is excluded (see Scope Boundaries).
- R11. Mode 2 packages register a hosted bootstrapper that runs the initializer before any consumer code can take a dependency on the storage being ready. The bootstrapper follows the same pattern as today's Messaging `Bootstrapper` (already a `IHostedService`).
- R12. Mode 2 on operational packages (Outbox, Jobs, Audit) MUST continue to support transaction enlistment via `UseEntityFramework<TContext>()`. This is additive to `UsePostgreSql` / `UseSqlServer`, not a replacement. Mode 2 on domain packages (Settings, Permissions, Features) does NOT offer transaction enlistment with consumer EF contexts — the trade-off is documented.
- R13. Schema, table-name, and index-name customization is exposed via `XStorageOptions` and threaded through both Mode 1 entity configuration and Mode 2 DDL. The same options class drives both modes per package.

**Package layout and naming**

- R14. All storage-bearing packages — existing and net-new — follow the unified `Headless.<Feature>.Storage.<Provider>` shape. Net-new per-provider packages: `Headless.Settings.Storage.PostgreSql`, `Headless.Settings.Storage.SqlServer`, `Headless.Permissions.Storage.PostgreSql`, `Headless.Permissions.Storage.SqlServer`, `Headless.Features.Storage.PostgreSql`, `Headless.Features.Storage.SqlServer`, `Headless.Jobs.Storage.PostgreSql`, `Headless.Jobs.Storage.SqlServer`, `Headless.AuditLog.Storage.PostgreSql`, `Headless.AuditLog.Storage.SqlServer`. Existing EF packages keep their `Headless.X.Storage.EntityFramework` names: `Headless.Settings.Storage.EntityFramework`, `Headless.Permissions.Storage.EntityFramework`, `Headless.Features.Storage.EntityFramework`, `Headless.Identity.Storage.EntityFramework`.
- R15. Existing packages whose names break the unified shape are renamed: `Headless.AuditLog.EntityFramework` → `Headless.AuditLog.Storage.EntityFramework`; `Headless.Jobs.EntityFramework` → `Headless.Jobs.Storage.EntityFramework`; `Headless.Messaging.PostgreSql` → `Headless.Messaging.Storage.PostgreSql`; `Headless.Messaging.SqlServer` → `Headless.Messaging.Storage.SqlServer`. 
- R16. The compound `IServiceCollection` extension names (`AddSettingsManagementDbContextStorage`, etc.) are removed in favor of the unified `AddHeadlessX(s => s.Use…())` shape.

**Migration of existing packages**

- R17. `Headless.Jobs.EntityFramework` is rewritten to expose the unified builder shape. The `JobsOptionsBuilder.AddOperationalStore(...)` entry point is removed; the equivalent wiring lives on the new `HeadlessJobsBuilder.UseEntityFramework<TContext>()`. `JobsDbContext<TTimeJob, TCronJob>` and the `JobsModelCustomizer` infrastructure are reshaped to publish `IEntityTypeConfiguration<T>` + a `modelBuilder.AddHeadlessJobs(options)` extension matching R6.
- R18. `Headless.Settings.Storage.EntityFramework`, `Headless.Permissions.Storage.EntityFramework`, `Headless.Features.Storage.EntityFramework`: the dedicated `XxxDbContext`, `IXxxDbContext`, and `XStorageModelCacheKeyFactory` are removed. The packages contain only `IEntityTypeConfiguration<T>` types, the `modelBuilder.AddHeadlessX(options)` extension, the `XStorageOptions` class, and the Mode-1 wiring on `HeadlessXBuilder`.
- R19. Existing READMEs and the `docs/llms/<domain>.md` companion docs are rewritten against the new shape. The README for each affected package is treated as in-scope for this work (the framework's authoring policy already requires README + llms docs to stay in lockstep).

---

## Acceptance Examples

- AE1. **Covers R2.** Given a consumer call `services.AddHeadlessSettings(s => { s.UseEntityFramework<AppDbContext>(); s.UsePostgreSql("…"); })`, when the host builds, the registration MUST throw a `MessagingConfigurationException`-equivalent at build time naming both `UseEntityFramework` and `UsePostgreSql` as the conflicting verbs.
- AE2. **Covers R5.** Given a consumer who registered `AddHeadlessSettings(s => s.UseEntityFramework<AppDbContext>())` but forgot to call `modelBuilder.AddHeadlessSettings(options)` in `OnModelCreating`, when the application starts, the framework MUST throw an exception during the startup validation phase whose message names `SettingValueRecord` and instructs the consumer to call `modelBuilder.AddHeadlessSettings(options)`.
- AE3. **Covers R7.** Given a Settings read operation issued by a singleton service against a consumer-registered `AppDbContext`, when the read executes, the repo MUST resolve a fresh `DbContext` from `IDbContextFactory<AppDbContext>`, execute the query as `AsNoTracking`, dispose the context, and return.
- AE4. **Covers R8.** Given two registrations `AddHeadlessSettings(s => s.UseEntityFramework<AdminDbContext>())` and `AddHeadlessPermissions(s => s.UseEntityFramework<AuthDbContext>())` in one container, when the application starts, both packages MUST resolve and operate against their respective contexts with no cross-contamination.
- AE5. **Covers R10, R11.** Given a consumer wires `AddHeadlessSettings(s => s.UsePostgreSql(connStr))` against an empty Postgres database, when the host starts, the bootstrapper MUST create the configured schema (if missing) and the settings tables before any consumer endpoint can serve a request that reads or writes settings.
- AE6. **Covers R12.** Given a consumer wires `AddHeadlessMessaging(s => { s.UsePostgreSql(connStr); s.UseEntityFramework<AppDbContext>(); })` and publishes a message inside a `dbContext.Database.BeginTransactionAsync()` scope, when `transaction.CommitAsync()` runs, the outbox row and the consumer's domain row MUST commit atomically against the same physical `DbConnection`.
- AE7. **Covers R13.** Given a consumer overrides `o.Schema = "auth"` on `PermissionsStorageOptions`, when Mode 1 builds the model the entity tables MUST be configured for the `auth` schema AND when Mode 2 runs its initializer the DDL MUST target the `auth` schema. Both modes draw from the same options class.

---

## Success Criteria

- A consumer wiring two Headless storage packages for the first time can transfer the mental model from package one to package two without consulting docs. The verb names, the builder shape, the entity-configuration extension shape, and the mode selection are the same.
- The "do I need to create a migration against a context I don't own?" question is structurally impossible to ask: Mode 1 always uses the consumer's context (their migration pipeline), Mode 2 has no migrations at all. The dedicated-context-without-shipped-migrations trap is gone.
- The consumer's `DbContext` public surface area contains zero framework `DbSet` properties after adopting any Mode 1 package.
- `dev-plan` can take this document and emit a concrete implementation plan covering at least: the shared `HeadlessXBuilder` infrastructure, the per-package builder verbs, the EF Mode 1 wiring (entity configs + extension + factory-based repos + startup validation), the Mode 2 raw-DDL initializers for Postgres and SqlServer, the rewrites of Jobs.EntityFramework and the domain Storage.EntityFramework packages, and the deletion of the obsolete types. Nothing in this list should require dev-plan to invent product behavior.

---

## Scope Boundaries

- Mode 3 (dedicated `DbContext` + shipped migration assembly + `MigrationHostedService` auto-apply) is explicitly out of scope. The isolation-mode user — one who genuinely needs a dedicated Settings/Permissions/Features context with its own connection — has Mode 2 raw-DDL as their alternative if they accept the loss of atomicity with their app data, or stays on the pre-unification packages.
- A CLI for offline schema-script generation (Wolverine / Marten style: `dotnet headless storage script --package Settings --provider Postgres > settings.sql`) is out of scope. The Mode 2 runtime DDL covers the common case; consumers in environments that forbid runtime DDL would need this CLI, but no such consumer has been identified.
- Raw-DDL variants of `Headless.Identity.Storage.*` (Postgres, SqlServer) are out of scope. ASP.NET Core Identity is deeply tied to its EF entity types and `UserManager` / `RoleManager` plumbing; a non-EF Identity store is its own large project, not part of this unification.
- Migration of existing downstream consumers is out of scope per the project's greenfield posture (`CLAUDE.md`: "Prefer simpler, cleaner APIs even when that requires breaking changes"). No backwards-compatibility shims, no `[Obsolete]` retention of the old extension names.
- Additional database providers beyond Postgres and SqlServer (MySQL, SQLite, Oracle, etc.) are deferred. The Mode 2 abstraction (`IStorageInitializer` + per-provider DDL) is the extension point for them when needed.
- Dashboard / observability surfaces for the new initializers are out of scope. Existing `Headless.Jobs.Dashboard` and `Headless.Messaging.Dashboard` continue to operate unchanged.
- Multi-tenant per-tenant-DB topologies are out of scope as a first-class concern. R8 (multi-context support) covers the bounded-context split case; full per-tenant routing is a separate brainstorm.

---

## Key Decisions

- **Two modes per package, not three.** Mode 3 (dedicated EF + shipped migrations + auto-apply) was explored and dropped because it doubles the per-provider artifact count for a narrow isolation-mode audience. Mode 2 raw-DDL is the substitute for that audience. The decision accepts that one specific scenario gets worse (isolation-mode users who also want EF semantics) in exchange for materially less framework surface area.
- **Domain packages default to Mode 1, operational packages default to Mode 2.** The split tracks the data's nature — domain data benefits from atomicity with the consumer's writes (Mode 1's strength); operational data benefits from zero-friction startup (Mode 2's strength). Per-package mode availability:

  | Package | Mode 1 (Shared EF) | Mode 2 (Raw DDL) | Default |
  |---|---|---|---|
  | Settings | yes | yes | Mode 1 |
  | Permissions | yes | yes | Mode 1 |
  | Features | yes | yes | Mode 1 |
  | Identity Storage | yes | no | Mode 1 |
  | Audit Log | yes | yes | Mode 2 |
  | Jobs | yes | yes | Mode 2 |
  | Messaging Outbox | yes (enlist-only) | yes | Mode 2 |

- **Drop `IXxxDbContext`.** The compile-time guarantee it provided does not justify forcing framework `DbSet`s into the consumer's public API. Startup validation via `Set<TEntity>()` provides equivalent safety with a clearer failure message and no API pollution.
- **Drop `XStorageModelCacheKeyFactory` in Mode 1.** The custom factory exists to handle "same context type, different storage options at different times" (test fixtures re-binding options). In Mode 1 the consumer owns the context lifecycle; that scenario does not arise. Documented assumption: one storage-options binding per `TContext` lifetime in Mode 1.
- **`AddDbContextFactory<TContext>()` is mandatory for Mode 1 consumers.** Repos are registered as singletons (correct shape for stateless storage abstractions) and singletons cannot capture scoped contexts. The consumer registers the factory; they may additionally register `AddDbContext<TContext>` as scoped for their own use.
- **`AsNoTracking()` is the default for Mode 1 reads.** Settings, Permissions, and Features reads feed into application-level caches or DTO projections, never into change tracking. Writes use tracking within a repo-method-owned unit of work.
- **One options class drives both modes per package.** `XStorageOptions` (Schema, table names, index names) is consumed by both the Mode 1 entity configuration and the Mode 2 raw DDL. The framework does not maintain two parallel option surfaces.
- **Postgres and SqlServer are the v1 providers for Mode 2.** Decision is driven by what the framework already supports for Messaging and what the project's CI exercises. Additional providers are deferred per Scope Boundaries.
- **Rename existing Storage packages for consistency.** `Headless.Jobs.EntityFramework` and `Headless.AuditLog.EntityFramework` get a `.Storage.` segment to match the existing `Headless.{Settings,Permissions,Features}.Storage.EntityFramework` naming. Messaging provider packages keep their current names because the cost of renaming them outweighs the cosmetic gain (the user-facing builder verbs are already consistent).

---

## Dependencies / Assumptions

- Assumes the project's EF Core version supports `AddDbContextFactory<T>()` and `AddDbContext<T>()` coexistence on the same type without warnings. True for EF Core 5+; the project targets .NET 10 / current EF Core.
- Assumes the project already has (or will accept) a host-level "validate on start" pattern analogous to FluentValidation's `ValidateOnStart()`, suitable for the Mode 1 startup-validation requirement (R5). If absent, a small per-package `IHostedService` that runs validation at startup is acceptable.
- Assumes the existing `Headless.Messaging.Core` `IStorageInitializer` + `Bootstrapper` pattern is reusable as the prior art for the Mode 2 hosted-bootstrapper requirement (R11). Plan should consider whether to lift these into a shared `Headless.Storage.Abstractions` package or replicate per-feature.
- Assumes Postgres and SqlServer connection-string + provider primitives can be reused across all five domain/operational packages without re-implementing per-provider connection management. Messaging already encapsulates this; lifting the pattern is part of the implementation plan.
- Assumes greenfield posture per project `CLAUDE.md`: no backwards-compatibility shims, breaking changes acceptable.

---

## Outstanding Questions

### Resolve Before Planning

_None — both prior blocking questions resolved: package naming unified to `Headless.<Feature>.Storage.<Provider>` shape across all packages (existing EF retained, AuditLog/Jobs/Messaging providers renamed to add `.Storage.` segment, all in-scope for this work)._

### Deferred to Planning

- [Affects R5, R11][Technical] Concrete location of the shared startup-validation primitive: lift into `Headless.Hosting`, `Headless.Core`, or per-package? Plan should investigate what already exists.
- [Affects R10][Technical] Whether `IStorageInitializer` lives in a new `Headless.Storage.Abstractions` shared package or remains per-feature (one `IStorageInitializer` per package's namespace). Plan should investigate the trade-off against existing `Headless.Messaging.Persistence.IStorageInitializer`.
- [Affects R7][Technical] Whether to register a `AddPooledDbContextFactory<TContext>()` shortcut on the builder, or leave pooled-vs-non-pooled entirely to the consumer's `AddDbContextFactory` call. Plan should evaluate based on the typical consumer pattern.
- [Affects R14][Technical] Provider-segment spelling: `PostgreSql` (and similarly `SqlServer` vs `MsSql` etc.). Today's Messaging variant is `Headless.Messaging.PostgreSql`; the new packages should match a single spelling. Plan should pick one and apply uniformly.
- [Affects R17][Technical] Whether `JobsDbContext<TTimeJob, TCronJob>` can be deleted outright or whether its generic-typed-entity story (custom `TTimeJob` and `TCronJob` types) needs preserving in the new shape. Plan should map the existing capability set before deciding.
- [Affects R19][Needs research] Inventory of every `docs/llms/<domain>.md` and per-package README that mentions the old registration shape, to size the docs sync work.
