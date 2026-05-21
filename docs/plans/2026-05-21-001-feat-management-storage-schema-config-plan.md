---
title: "feat: Make schema and table names configurable on management storage DbContexts"
type: feat
status: completed
created: 2026-05-21
issue: https://github.com/xshaheen/headless-framework/issues/323
---

## Summary

Make `SettingsDbContext`, `FeaturesDbContext`, and `PermissionsDbContext` usable under a non-default schema (and with renamed tables) without subclassing. Today they hardcode `AddSettingsConfiguration()` in `OnModelCreating`, so the only knob — the `schema` parameter on the model-builder extension — is unreachable. Consumers must hand-roll their own DbContext + pooling registration just to change one string.

Introduce per-package `*StorageOptions` types (`Schema`, table names), thread them through the existing `Add*ManagementDbContextStorage` registration via the framework's standard `Configure<TOptions, TValidator>` pipeline, and read them in `OnModelCreating` via `this.GetService<IOptions<...>>()`. Apply the same shape to all three packages for parity. Remove the existing static `Default*TableName` knobs in favor of the new options (greenfield project; no compat shim).

Out of scope: pooling changes to `HeadlessDbContext`, non-EF storage providers, or migration tooling impact (this only changes mapping for new tables — existing migrations in consumer apps are unaffected unless they re-scaffold).

---

## Problem Frame

- Built-in management contexts (`SettingsDbContext`, `FeaturesDbContext`, `PermissionsDbContext`) call `modelBuilder.AddSettingsConfiguration()` / `AddFeaturesConfiguration()` / `AddPermissionsConfiguration()` with no schema argument, locking the tables into `settings` / `features` / `permissions`.
- The model-builder extensions already accept `schema` and read static `Default*TableName` properties — but the built-in context exposes no way for consumers to pass either.
- Result: anyone wanting `myapp_settings` or `mycorp.SettingValues` has to fork three contexts. This forces them to reimplement pooling and factory wiring just to change a string.

---

## Requirements

- R1. Consumers can configure schema and table names on each built-in management context without subclassing.
- R2. Configuration uses the framework's standard FluentValidation-backed options pattern (per `CLAUDE.md` "Options Pattern" section).
- R3. Defaults are unchanged — existing callers compile and run with the same physical schema/table layout.
- R4. The model-builder extensions (`AddSettingsConfiguration` etc.) accept the options object directly so consumers using a shared application DbContext (the `ISettingsDbContext` path) can apply the same configuration.
- R5. Parity across all three packages: same shape, same conventions, same DI overload set.
- R6. Storage option fields are validated (non-empty schema, non-empty table names) and validation runs at startup (`ValidateOnStart`).
- R7. README + `docs/llms/*.md` for each of the three packages document the new options surface.

---

## Key Technical Decisions

- **Options class per package, not shared.** `SettingsStorageOptions`, `FeaturesStorageOptions`, `PermissionsStorageOptions`. Each carries its own table-name properties (different entity sets), so a shared base buys nothing.
- **Options classes are `public`; validators are `internal sealed`.** The options classes are part of the package's NuGet contract (consumers configure them), so they're `public` with `[PublicAPI]`. Validators stay `internal sealed` per `CLAUDE.md` "Public API Discipline".
- **Resolve options inside `OnModelCreating` via `this.GetService<IOptions<TStorageOptions>>()`.** Matches the established repo pattern in `src/Headless.Jobs.EntityFramework/DbContextFactory/JobsDbContext.cs` (`this.GetService<JobsEfCoreOptionBuilder<...>>().Schema`). `IOptions<T>` is singleton, safe with pooled DbContexts, and the model cache key is unaffected because storage options are app-singleton (one schema per process).
- **Model-builder extension takes the options object.** New signature: `AddSettingsConfiguration(this ModelBuilder mb, SettingsStorageOptions options)`. No more `string schema = "settings"` overload — callers pass an options instance (default-constructable; defaults match prior values).
- **Remove the existing static `Default*TableName` properties.** Greenfield framework, no production consumers documented as relying on them. Functionality migrates cleanly into the new options.
- **Optional `configureStorage` parameter on the existing setup methods.** Keeps the call site for the 99% case (no schema override) identical. New signature:
  ```text
  AddSettingsManagementDbContextStorage(
      Action<DbContextOptionsBuilder> setupAction,
      Action<SettingsStorageOptions>? configureStorage = null)
  ```
  Same shape for the `IServiceProvider`-aware overload. Generic `<TContext>` overload also gains `configureStorage`. When `configureStorage` is null, the package still registers options with defaults so `IOptions<T>` resolves at model-build time.
- **Validator lives in the same file as the options class** per `CLAUDE.md` conventions: `internal sealed class SettingsStorageOptionsValidator : AbstractValidator<SettingsStorageOptions>` directly below the options class.
- **Registration uses `services.Configure<TOptions, TValidator>(action)` / `services.AddOptions<TOptions, TValidator>()` from `Headless.Hosting`** — the same path used by every other validated-options package in the framework (e.g., `src/Headless.Caching.Redis/Setup.cs`).

---

## High-Level Technical Design

*Directional guidance for review, not implementation specification.*

```text
  Consumer code
       │
       ▼
  services.AddSettingsManagementDbContextStorage(
      o => o.UseNpgsql(cs),
      storage => { storage.Schema = "myapp_settings"; })
       │
       ├──► services.Configure<SettingsStorageOptions, SettingsStorageOptionsValidator>(...)
       │         (singleton IOptions<SettingsStorageOptions>)
       │
       └──► services.AddPooledDbContextFactory<SettingsDbContext>(setupAction)
                 │
                 ▼
            SettingsDbContext.OnModelCreating(mb)
                 │
                 ├─ var opts = this.GetService<IOptions<SettingsStorageOptions>>().Value;
                 └─ mb.AddSettingsConfiguration(opts);
                          │
                          └─ b.ToTable(opts.SettingValuesTableName, opts.Schema)
```

Same flow for Features and Permissions. The model-builder extensions stay pure functions of `(ModelBuilder, TStorageOptions)`, so a consumer using their own shared application DbContext (the `ISettingsDbContext` path) gets the same configurability by constructing the options themselves and calling the extension.

---

## Implementation Units

### U1. Settings storage — add options, refactor model builder, wire context, update setup

**Goal:** Make `SettingsDbContext` schema/table-name configurable end-to-end via the standard options pipeline.

**Requirements:** R1, R2, R3, R4, R6.

**Dependencies:** none.

**Files:**
- `src/Headless.Settings.Storage.EntityFramework/SettingsStorageOptions.cs` (new — options + validator)
- `src/Headless.Settings.Storage.EntityFramework/SettingsModelBuilderExtensions.cs` (modify — remove static defaults, change signature)
- `src/Headless.Settings.Storage.EntityFramework/SettingsDbContext.cs` (modify — read options in `OnModelCreating`)
- `src/Headless.Settings.Storage.EntityFramework/Setup.cs` (modify — add `configureStorage` parameter)
- `tests/Headless.Settings.Tests.Integration/SettingsCustomSchemaTests.cs` (new — schema override integration coverage)

**Approach:**
- New `SettingsStorageOptions { Schema = "settings", SettingValuesTableName = "SettingValues", SettingDefinitionsTableName = "SettingDefinitions" }` plus an `internal sealed SettingsStorageOptionsValidator : AbstractValidator<SettingsStorageOptions>` in the same file.
- Change `AddSettingsConfiguration(this ModelBuilder mb, string schema = "settings")` → `AddSettingsConfiguration(this ModelBuilder mb, SettingsStorageOptions options)`. Use `options.Schema` and `options.SettingValuesTableName` / `options.SettingDefinitionsTableName` in `ToTable(...)` calls.
- Delete the `DefaultSettingValuesTableName` and `DefaultSettingDefinitionTableName` static properties.
- In `SettingsDbContext.OnModelCreating`: `var opts = this.GetService<IOptions<SettingsStorageOptions>>().Value; modelBuilder.AddSettingsConfiguration(opts);` (mirrors the `JobsDbContext` pattern).
- In `Setup.cs`: add optional `Action<SettingsStorageOptions>? configureStorage = null` to both `AddSettingsManagementDbContextStorage` overloads. When null, call `services.AddOptions<SettingsStorageOptions, SettingsStorageOptionsValidator>()`. When non-null, call `services.Configure<SettingsStorageOptions, SettingsStorageOptionsValidator>(configureStorage)`. The generic `<TContext>` overload also takes `configureStorage` so consumers wiring their own `ISettingsDbContext` get the same option pipeline.

**Patterns to follow:**
- Options + validator co-location and `services.Configure<TOptions, TValidator>`: `src/Headless.Caching.Redis/Setup.cs`, `src/Headless.Messaging.SqlServer/Setup.cs`.
- `OnModelCreating` resolving DI for schema: `src/Headless.Jobs.EntityFramework/DbContextFactory/JobsDbContext.cs:20`.
- `Setup{Provider}` class shape with extension members: `src/Headless.Settings.Storage.EntityFramework/Setup.cs` (current file).

**Test suite design:**
- Owner: `tests/Headless.Settings.Tests.Integration` (existing Testcontainers Postgres harness — `SettingsTestBase.cs:70` already wires `AddSettingsManagementDbContextStorage`). No new harness needed.
- New test class `SettingsCustomSchemaTests` either subclasses `SettingsTestBase` with overridden config OR builds a parallel host that registers with `configureStorage: o => { o.Schema = "myapp_settings"; ... }`. Subclassing is cleaner if `SettingsTestBase` exposes a hook; otherwise a small parallel `IHost` setup keeps it isolated.
- Validation tests (validator behavior) belong in `tests/Headless.Settings.Tests.Unit` — small, no infra.

**Test scenarios:**
- **Happy path:** With `configureStorage` setting `Schema = "myapp_settings"`, querying the live Postgres database (`pg_catalog`/`information_schema`) confirms the `SettingValues` and `SettingDefinitions` tables exist under the `myapp_settings` schema and not under `settings`.
- **Happy path:** With `configureStorage` setting custom `SettingValuesTableName = "tbl_setting_values"`, the migrated table name in the database is `tbl_setting_values`.
- **Default behavior:** Without any `configureStorage` argument, tables land in schema `settings` with names `SettingValues` / `SettingDefinitions` — confirms the change is backward-compatible at the physical-schema layer.
- **End-to-end CRUD under custom schema:** Insert a `SettingValueRecord` via the repository under a custom schema, retrieve it, assert round-trip. Proves the configuration actually flows through to query generation, not just DDL.
- **Validator (unit):** `SettingsStorageOptions { Schema = "" }` fails validation with a non-empty-schema rule. Same for empty table names.
- **Startup validation:** Registering invalid options (e.g., empty schema) causes host startup to throw via `ValidateOnStart()`.

**Verification:**
- All new tests pass via `make test-project TEST_PROJECT=tests/Headless.Settings.Tests.Integration`.
- Existing settings tests continue to pass (defaults preserved).
- Solution builds clean: `make build`.

---

### U2. Features storage — same shape as U1

**Goal:** Apply the same options/validator/context/setup pattern to the Features package.

**Requirements:** R1, R2, R3, R4, R5, R6.

**Dependencies:** none functional. Land after U1 if you want a single review template; both can land independently.

**Files:**
- `src/Headless.Features.Storage.EntityFramework/FeaturesStorageOptions.cs` (new)
- `src/Headless.Features.Storage.EntityFramework/FeaturesModelBuilderExtensions.cs` (modify)
- `src/Headless.Features.Storage.EntityFramework/FeaturesDbContext.cs` (modify)
- `src/Headless.Features.Storage.EntityFramework/Setup.cs` (modify — rename class + namespace fix + add `configureStorage`)
- `demo/Headless.EntityFramework.Migrations.Startup/Program.cs` (modify — update `using` directive after namespace change)
- `tests/Headless.Features.Tests.Integration/TestSetup/FeaturesTestBase.cs` (modify — update `using` directive)
- `tests/Headless.Features.Tests.Integration/FeaturesCustomSchemaTests.cs` (new)

**Approach:**
- `FeaturesStorageOptions` carries `Schema = "features"`, `FeatureValuesTableName = "FeatureValues"`, `FeatureDefinitionsTableName = "FeatureDefinitions"`, `FeatureGroupDefinitionsTableName = "FeatureGroupDefinitions"`. Validator asserts all non-empty.
- Model builder takes `FeaturesStorageOptions options`. Three `ToTable(...)` sites use the corresponding table-name property.
- `FeaturesDbContext.OnModelCreating` resolves `IOptions<FeaturesStorageOptions>` and calls the extension.
- `Setup.cs` adds `configureStorage` parameter on all three overloads. Also rename the class `SetupEntityFramework` → `EntityFrameworkFeaturesSetup` and move it from the `Headless.Features` namespace into `Headless.Features.Storage.EntityFramework`, to match the Settings/Permissions parity. The PR is already shipping a breaking API change here; folding in the naming fix avoids a separate breaking release. Update the two demo/test callers (their `using` directives) accordingly.

**Patterns to follow:** same as U1.

**Test suite design:** mirror U1 in `tests/Headless.Features.Tests.Integration`. `FeaturesTestBase.cs:68` is the equivalent harness entrypoint.

**Test scenarios:**
- Custom schema lands all three Features tables under the overridden schema (DDL inspection via `information_schema`).
- Custom table names apply across all three tables.
- Default behavior unchanged.
- CRUD round-trip under custom schema for a `FeatureValueRecord` via the repository.
- Validator unit tests covering empty schema, empty value-table, empty definition-table, empty group-table.
- Startup `ValidateOnStart` failure on invalid options.

**Verification:**
- New Features integration tests pass.
- Existing Features tests unchanged.

---

### U3. Permissions storage — same shape as U1

**Goal:** Apply the same options/validator/context/setup pattern to the Permissions package.

**Requirements:** R1, R2, R3, R4, R5, R6.

**Dependencies:** none functional. Independent of U1 and U2.

**Files:**
- `src/Headless.Permissions.Storage.EntityFramework/PermissionsStorageOptions.cs` (new)
- `src/Headless.Permissions.Storage.EntityFramework/PermissionsModelBuilderExtensions.cs` (modify)
- `src/Headless.Permissions.Storage.EntityFramework/PermissionsDbContext.cs` (modify)
- `src/Headless.Permissions.Storage.EntityFramework/Setup.cs` (modify)
- `tests/Headless.Permissions.Tests.Integration/PermissionsCustomSchemaTests.cs` (new)

**Approach:**
- `PermissionsStorageOptions` carries `Schema = "permissions"`, `PermissionGrantsTableName = "PermissionGrants"`, `PermissionDefinitionsTableName = "PermissionDefinitions"`, `PermissionGroupDefinitionsTableName = "PermissionGroupDefinitions"`. Validator asserts all non-empty.
- Same model-builder/context/Setup changes as U1.

**Patterns to follow:** same as U1.

**Test suite design:** `tests/Headless.Permissions.Tests.Integration` — equivalent harness already exists (file confusingly named `SettingsTestBase.cs` in the Permissions test project, line 70 — leave the name; do not rename in this PR).

**Test scenarios:**
- Custom schema lands all three Permissions tables under the overridden schema.
- Custom table names apply.
- Default behavior unchanged.
- CRUD round-trip for a `PermissionGrantRecord` under custom schema.
- Validator unit tests for each empty-field case.
- Startup `ValidateOnStart` failure.

**Verification:**
- New Permissions integration tests pass.
- Existing Permissions tests unchanged.

---

### U4. Documentation sync — READMEs and llms docs

**Goal:** Update both agent-facing doc surfaces (`docs/llms/*.md` and `src/Headless.*/README.md`) to reflect the new options surface, per `CLAUDE.md` sync trigger ("public API surface changes").

**Requirements:** R7.

**Dependencies:** U1, U2, U3 (need final API shape).

**Files:**
- `src/Headless.Settings.Storage.EntityFramework/README.md` (modify — document `SettingsStorageOptions` and `configureStorage` parameter)
- `src/Headless.Features.Storage.EntityFramework/README.md` (modify)
- `src/Headless.Permissions.Storage.EntityFramework/README.md` (modify)
- `docs/llms/settings.md` (modify — add schema-config section)
- `docs/llms/features.md` (modify)
- `docs/llms/permissions.md` (modify)

**Approach:**
- For each README: add a "Custom Schema / Table Names" subsection under Quick Start showing the `configureStorage` overload with a non-default schema example.
- For each `docs/llms/*.md`: add a concept note explaining that the management storage tables are schema-configurable, with a small code snippet. Follow the authoring template referenced in `docs/authoring/AUTHORING.md`.
- No code or doc changes to packages that don't ship management storage (e.g., abstractions packages).

**Patterns to follow:** existing README structure (`## Quick Start` then provider-specific subsections); existing llms-doc structure (concept → API → example).

**Test suite design:** docs only — no automated coverage.

**Test scenarios:**
- Test expectation: none -- documentation-only changes; no behavioral surface.

**Verification:**
- Manual review of the six doc files against the actual API shape produced by U1-U3.
- Per `docs/authoring/AUTHORING.md` drift checks: README and llms doc say the same thing about the options surface.

---

### U5. Caller migration — update demos and test harnesses to use defaults explicitly

**Goal:** Confirm every internal caller still compiles and the default-path code path is exercised in CI.

**Requirements:** R3.

**Dependencies:** U1, U2, U3.

**Files (read/verify; modify only if needed):**
- `demo/Headless.EntityFramework.Migrations.Startup/Program.cs` (verify defaults still work; no change expected since `configureStorage` is optional)
- `demo/Headless.Permissions.Api.Demo/Program.cs` (verify; commented-out caller)
- `tests/Headless.Settings.Tests.Integration/TestSetup/SettingsTestBase.cs` (verify defaults still work)
- `tests/Headless.Features.Tests.Integration/TestSetup/FeaturesTestBase.cs` (verify)
- `tests/Headless.Permissions.Tests.Integration/TestSetup/SettingsTestBase.cs` (verify)

**Approach:**
- Since `configureStorage` is optional with a null default, no caller should require code changes. Verify by running `make build` after U1-U3 land.
- If any compile error surfaces (e.g., from removed static `Default*TableName` properties being referenced somewhere), fix at the call site.

**Patterns to follow:** existing demo and test-base wiring.

**Test suite design:** existing integration test suites — they already exercise the default path.

**Test scenarios:**
- Test expectation: none -- caller-verification unit only. Coverage of the default path lives in U1/U2/U3 integration tests and the pre-existing test suites.

**Verification:**
- `make build` succeeds.
- `make test-project TEST_PROJECT=tests/Headless.Settings.Tests.Integration` (and the Features + Permissions equivalents) all green.

---

## Scope Boundaries

### In scope
- `SettingsDbContext`, `FeaturesDbContext`, `PermissionsDbContext` (the three built-in management storage contexts).
- Schema + table-name configurability via the standard options pipeline.
- Validator co-located with each options class.
- README + llms doc updates for the three packages.

### Not in scope
- `HeadlessDbContext` pooling — explicit non-goal per the issue.
- Non-EF storage providers for these features (Dapper, raw SQL, etc.) — none currently exist in tree.
- (Folded into U2.)
- Migrating consumer apps' existing migration scripts when they change schemas — that's a per-consumer concern (this PR ships the API; consumers regenerate migrations).

### Deferred to follow-up work
- Naming consistency on the three `Setup*` classes (`EntityFrameworkSettingsSetup`, `SetupEntityFramework`, `EntityFrameworkPermissionsSetup`).
- Configuration-section binding overloads (`IConfiguration configuration` directly on `Add*ManagementDbContextStorage`) — consumers can still do `configureStorage: o => config.GetSection("...").Bind(o)` inline today.

---

## Risks

- **Pooled DbContext + `this.GetService<IOptions<T>>()`.** `AddPooledDbContextFactory` configures the application service provider on `DbContextOptionsBuilder`, so `IOptions<T>` resolves correctly inside `OnModelCreating`. Precedent: `src/Headless.Jobs.EntityFramework/DbContextFactory/JobsDbContext.cs:20`. Mitigation: integration tests in U1-U3 exercise this code path against real Postgres.
- **EF model cache — first-resolution-wins.** EF caches the compiled model per context type. `OnModelCreating` runs exactly once per context type per process; whatever `IOptions<TStorageOptions>` resolves to on that first call is baked into the cached model for the lifetime of the process. Because storage options are app-singleton (set once at startup, immutable), this is safe — but if a future need arises for multiple schemas in the same process for the same context type, an `IModelCacheKeyFactory` override would be required. Explicitly out of scope here.
- **Static-property removal.** If any external consumer set `DefaultSettingValuesTableName = "..."` before calling `AddSettingsConfiguration()`, that path no longer exists. Mitigated by greenfield-project context (no documented external dependents) and the equivalent capability landing on `SettingsStorageOptions.SettingValuesTableName`.
- **Validator runs at startup.** A misconfigured options object now fails fast at host build time rather than at first model-build. Behavioral change is desirable but worth noting.

---

## Verification Strategy

- `make build` clean.
- `make test-project TEST_PROJECT=tests/Headless.Settings.Tests.Integration`, same for Features and Permissions — all green, including the new custom-schema test classes.
- Targeted unit-test runs for the validators: `make test-project TEST_PROJECT=tests/Headless.Settings.Tests.Unit` (etc.).
- Spot-check `demo/Headless.EntityFramework.Migrations.Startup/Program.cs` still compiles and the migration scaffolding produces tables under default schemas.
