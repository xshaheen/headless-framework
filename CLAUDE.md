# CLAUDE.md

## Project Overview

**headless-framework** is a modular .NET 10 framework for building APIs and backend services. Composed of 150+ NuGet packages organized by functional domains (API, Blobs, Caching, Messaging, ORM, etc.). Unopinionated, zero lock-in design.

**This is a framework, not a finished application.**
It is designed to support multiple projects and packages, both internal and external. As such, it may contain abstractions, extension points, and utility classes or methods that are not directly used within this repository. These elements exist deliberately to enable extensibility, customization, and reuse by downstream consumers and future integrations.

**This is a greenfield project.**
Prefer simpler, cleaner APIs even when that requires breaking changes. Do not preserve awkward compatibility layers unless explicitly requested.

**Coverage targets:**
- **Line coverage**: ≥85% (minimum: 80%)
- **Branch coverage**: ≥80% (minimum: 70%)
- **Mutation score**: ≥70% (goal: 85%+)

## Architecture Pattern

Each feature follows **abstraction + provider pattern**:
- `Headless.*.Abstractions` — interfaces and contracts
- `Headless.*.<Provider>` — concrete implementation
Example: `Headless.Caching.Abstractions` + `Headless.Caching.Redis`

## Test Structure

- `*.Tests.Unit` — isolated, mocked, no external deps
- `*.Tests.Integration` — real deps via Testcontainers (requires Docker)
- `*.Tests.Harness` — shared fixtures and builders

**Stack**: xUnit v3 (Microsoft Testing Platform), AwesomeAssertions (fork of FluentAssertions), NSubstitute, Bogus

### When to create a `*.Tests.Harness` package

This repo's abstraction-plus-provider pattern (`Headless.<Feature>.Abstractions` + `Headless.<Feature>.<Provider>`) implies that every feature has 2+ providers (EF + PostgreSQL + SqlServer for storage domains; Redis + Memory for caching; etc.). The same observable behavior must hold for each provider. Test that with a `Headless.<Feature>.Tests.Harness` package — do **not** copy-paste fixtures across `<Provider>.Tests.Integration` projects.

**Trigger to extract:**
- Adding the 2nd or later provider-integration project for a feature: extract the harness first, then add the new provider against it.
- 3+ hand-rolled fixtures with substantial overlap (>30 lines of copy-pasteable boilerplate) already exist: queue the extraction as a dedicated batch; do not let the count grow.

**What goes in the harness package:**
- An abstract `<Feature>FixtureBase<TOptions>` owning Testcontainers container lifecycle, host bootstrap (DI + `setup.Use…` pivot), initializer / migration / topology waiters (the `WaitForXxxStorageInitializerAsync` pattern), and inter-test cleanup (DDL drop, container reset).
- An abstract `<Feature>ConformanceTests<TFixture>` carrying the cross-provider scenarios — round-trip, idempotency, concurrency / contention, error paths, cancellation behavior, schema-init re-entry. `TFixture` satisfies xUnit v3's `IClassFixture<>` / `ICollectionFixture<>` pattern.
- Shared test-data builders / `Faker<T>` instances for the contract's value types.

**What stays in each leaf integration project:**
- A concrete `<Provider><Feature>Fixture : <Feature>FixtureBase<<Provider>Options>` that wires the specific `setup.Use<Provider>(…)` extension, container image, and connection-string materialization.
- Tests that exercise behavior unique to that backend (PostgreSQL `pg_advisory_xact_lock`, SqlServer `sp_getapplock`, Redis Lua scripts, EF Core migration-snapshot drift). These intentionally have no sibling — they don't need a base class because they are non-portable by construction.

**Existing harnesses to reference for shape:**
- [Headless.Blobs.Tests.Harness](tests/Headless.Blobs.Tests.Harness) — blob-backend conformance (S3, Azure, FS, SSH, Redis)
- [Headless.DistributedLocks.Tests.Harness](tests/Headless.DistributedLocks.Tests.Harness) — lock-provider conformance
- [Headless.Orm.Tests.Harness](tests/Headless.Orm.Tests.Harness) — `HeadlessDbContext` runtime + EF Core base behavior
- [Headless.Messaging.Core.Tests.Harness](tests/Headless.Messaging.Core.Tests.Harness) — messaging dispatch/outbox
- [Headless.Jobs.EntityFramework.Tests.Harness](tests/Headless.Jobs.EntityFramework.Tests.Harness) — Jobs+Coordination conformance across the EF DB providers (Postgres, SqlServer); uses the interface + extensions shape (`IJobsCoordinationFixture`) rather than an abstract fixture base

**Anti-pattern to avoid:** the storage-domain integration tests today (`Headless.{AuditLog,Features,Permissions,Settings}.Storage.{EntityFramework,PostgreSql,SqlServer}.Tests.Integration`) each own a private `<Provider><Feature>Fixture.cs` with substantial overlap. This is the exact shape this rule is meant to prevent — when adding a new domain or provider, extract first.

## Build & Test

- Solution file: [headless-framework.slnx](headless-framework.slnx) (modern XML format).
- Local CLI tools pinned in [dotnet-tools.json](dotnet-tools.json) — `dotnet tool restore`, then `dotnet <tool>`.
- Headless SDKs treat warnings as errors in CI.

**Makefile (preferred entry point):**

Use the [Makefile](Makefile) targets instead of raw `dotnet` invocations — they pin configuration, results directories, and parallelism consistently. `make help` lists everything; `make` alone prints it.

- **Setup**: run `make bootstrap` when initializing a fresh clone or new worktree; it restores tools, packages, and git hooks. Use `make tools` and `make restore` individually only when you need a narrower setup step. Use `make restore-project PROJECT=src/.../X.csproj` for a project-scoped restore.
- **Node prerequisite (dashboards)**: building `Headless.Jobs.Dashboard` / `Headless.Messaging.Dashboard` requires **Node 22** on `PATH` — their `dotnet build` runs `npm ci` + a Vite build (`eng/DashboardSpa.targets`) and embeds the generated `wwwroot/dist` (no longer committed). Run `make dashboards` to (re)build the SPAs. Pass `/p:BuildDashboardSpa=false` (or set the `BuildDashboardSpa=false` env var) to skip the npm build when a `wwwroot/dist` is already present.
- **Build**: `make build` (incremental, errors-only), `make rebuild` (no incremental), `make build-project PROJECT=src/.../X.csproj`. Prefer `build-project` when the work is scoped to a specified project; it restores only the selected project graph before building and does not restore the full solution.
- **Format**: `make format` (CSharpier write), `make format-check` (verify only).
- **Code quality analyzers**: `make quality-analyzers` for the solution, or `make quality-analyzers-project PROJECT=src/.../X.csproj` for focused work. These run a quiet no-incremental build that reports only warning/error diagnostics, then `dotnet format analyzers` in verify-only mode for analyzer suggestions. Scope with `QUALITY_SEVERITY=warn` or `QUALITY_DIAGNOSTICS=MA0154`.
- **Test**: `make test` (build + run all). `make test-fast` / `make test-project-fast` skip restore/build when outputs exist. Scope with `make test-project TEST_PROJECT=…`, `test-class CLASS='*ClockTests'`, `test-method METHOD=…`, `test-namespace`, `test-trait`, `test-query`. Group runs: `test-unit`, `test-integration` (needs Docker).
- **Before opening a PR**: run `make quality-analyzers` after the relevant build/test/format gates so analyzer warnings and suggestions are visible and fix them before CI or review.
- **Coverage**: `make coverage` (Cobertura), `coverage-html`, `coverage-json` (Summary.json), `coverage-open`.
- **Package/release**: `make pack`, `pack-sbom`, `outdated`, `version`.
- **Discovery**: `make list-projects`, `list-tests`, `clean`.
- **Overrides**: pass vars on the command line, e.g. `make test CONFIGURATION=Debug`, `make test TEST_FILTER='--filter-class X'`, `make coverage TEST_MAX_PARALLEL=2`.

## Conventions

**Reuse Before Reinventing (check `Headless.Extensions` first):**

- Before writing any general-purpose utility — a string/collection/date/IO/reflection helper, a result or error type, a guard, a domain primitive or value object, pagination, a constant, or a validator — check [docs/llms/extensions.md](docs/llms/extensions.md). `Headless.Extensions` is the framework's base library and almost certainly already ships it; reuse it instead of hand-rolling a duplicate.
- Read the relevant **Design Notes** before using a type, not just to confirm it exists — they document non-obvious behavior you would otherwise miss (e.g. `Currency` `*`/`/` take a `decimal` scalar, `KeyedAsyncLock`'s timeout overload returns `null` instead of throwing, `ParallelForEachAsync` does not preserve order).
- Search by capability, not by package name: types live across several `Headless.*` namespaces (`Headless.Primitives`, `Headless.Collections`, `Headless.Threading`, `Headless.IO`, …) and many are extension methods surfaced on BCL types in `System.*`.
- If `Headless.Extensions` genuinely lacks it, prefer adding the helper there (or to the matching foundational package) over duplicating it locally — then update `docs/llms/extensions.md` and the package README per [docs/authoring/AUTHORING.md](docs/authoring/AUTHORING.md).

**Argument Validation:**

- Use `Headless.Checks` (`Argument.*`, `Ensure.*`) for argument validation; avoid `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfGreaterThan`, etc.

**Options Pattern**:

- Validate options only when needed; when you do, use FluentValidation via the `Headless.Hosting` extensions: `AddOptions<TOptions, TValidator>()` / `Configure<TOptions, TValidator>(...)`. Avoid custom `IValidateOptions<T>` when hosting covers it.
- Create an `internal sealed class {OptionsName}Validator : AbstractValidator<{OptionsName}>` in the same file as the options class, directly below it if the option has any property that need validation.
- Register validators via DI using `services.Configure<TOption, TValidator>(action)` or `services.AddOptions<TOption, TValidator>()` from `Headless.Hosting`; these wire up FluentValidation + `ValidateOnStart()` automatically.
- Higher-level bootstrap APIs may auto-bind required options from owned default sections (for example `Headless:*`) when part of the package contract.
- If options are required, do not offer a parameterless registration overload; require options and delegate to the optioned path.
- Never call `new Validator().ValidateAndThrow()` manually; use the DI pipeline.

**DI Registration (Setup classes):**

Every provider package exposes a single static `Setup{Provider}` class in `Setup.cs` at the package root. Multi-provider features follow the unified setup builder pattern (see `docs/solutions/architecture-patterns/unified-provider-setup-builder-pattern.md`): the feature's Core package owns the root `AddHeadless{Feature}(Action<Headless{Feature}SetupBuilder>)` entry plus the provider gates, and each provider package contributes `Use{Provider}` extension members on the builder:

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

Single-backend packages with no provider choice keep plain `Add{Feature}` extensions on `IServiceCollection` (same overload trio).

- Name the shared private helper `_Add{Feature}Core`.
- The overload trio applies to **provider** `Use{Provider}` / `Add{Feature}` members that bind that backend's options. **Cross-cutting consumer extensions** that adapt an already-composed feature (for example `UseOutputCache` / `UseBclCache`, which consume a named `ICache` rather than supply a provider) intentionally expose a single `Action<TOptions>` overload plus the consumed feature's builder — they do not bind a provider's option section, so the `IConfiguration` / `Action<TOptions, IServiceProvider>` overloads do not apply.

**Public API Discipline:**

- Each package's `public` surface IS its NuGet contract — keep types `internal sealed` and promote to `public` only when consumers must reference them — "DI resolves it" is not a reason to be public.
- Every public type AND every registration/extension holder (`Add*`/`Use*`/`Map*`) lives in the owning package's own namespace — feature packages never place anything in System.*/Microsoft.*/third-party namespaces; only the augmentation packages listed under Namespace Policy may, and their holder class names must be prefixed (HeadlessXxxExtensions), never bare BCL-collision names.
- Async public methods take a trailing CancellationToken cancellationToken = default — including "callback" and "handler" interfaces.
- Public contracts return IReadOnlyList<>/IReadOnlyDictionary<,>, never mutable List<>/Dictionary<,>.
- New enums get explicit values; enums consumers may switch on ship a catch-all member and a "members may be added" doc note; sentinel value = 0.
- [PublicAPI] on externally-consumed types; [EditorBrowsable(Never)] on must-be-public plumbing; XML docs on everything public

**Namespace Policy:**

- A feature package's ENTIRE public surface — types, interfaces, enums, AND registration/extension holder classes (`Add*`/`Use*`/`Map*`) — lives in the package's own namespace (matching or prefixed by the package identity). Feature packages never declare `Microsoft.*`, `System.*`, `OpenTelemetry.*`, or any other foreign namespace, even for extension-method holders. Consumers add an explicit `using Headless.<Feature>;` to reach the registration and helper extensions (this is by design — the registration surface is part of the package's owned namespace, not a foreign one).
- **Sole exception — augmentation packages** whose reason for existing is to augment a foreign namespace keep extending it: `Headless.Extensions`, `Headless.Primitives`, `Headless.Urls`, `Headless.Hosting`, `Headless.Testing` (plus `Testing.AspNetCore` / `Testing.Testcontainers` where they extend test-library namespaces), and `Headless.NetTopologySuite`. Compiler polyfills (e.g. `IsExternalInit` in `System.Runtime.CompilerServices`) are also exempt. In these packages the foreign-namespace holder class names must stay collision-proof — prefix with `Headless` or the augmented type (`HeadlessHttpContextExtensions`, `TaskExtensions`) — never bare BCL-adjacent names (`ServiceCollectionExtensions`, `CollectionExtensions`), which collide with same-name BCL/ASP.NET types and produce CS0433 for consumers.
- No package may declare a namespace that IS another package's identity (e.g. only the `Headless.Api.Abstractions` package may own the `Headless.Api.Abstractions` namespace).

**Source File Header:**

Every `.cs` file starts with: `// Copyright (c) Mahmoud Shaheen. All rights reserved.`

**Problem Details Error Codes:**

- Error codes embedded in `ProblemDetails` responses use the `g:lower_snake_case` shape (the `g:` prefix marks "general" codes intended for the framework's shared descriptor space). Example: `g:tenant_required`, `g:idempotency_key_reused`, `g:concurrency_failure`.
- Do not use kebab-case (`g:tenant-required`) or other separators — the existing framework codes are all snake_case and clients parsing `errors[].code` should see a single consistent shape.
- New codes go in the relevant `MessageDescriber` class plus matching `Messages.resx` / `Messages.ar.resx` entries. The resx `<data name="...">` attribute uses the same `g:snake_case` form; the generated C# field (`Messages.g_snake_case`) collapses the `:` to `_`.
- Expose externally-referenced codes as `public const string` on a `*ErrorCodes` static class (`[PublicAPI]`) so client code has a compile-time link.

**Input Validation Responsibility:**

This framework delegates certain input validation to consuming applications:

- **Cache key length limits**: Not enforced by `ICache` implementations. Consumers should validate key lengths at their application boundaries if DoS protection is needed.
- **Message payload sizes**: `CacheInvalidationMessage` and similar DTOs don't enforce size limits. Consumers should configure their messaging infrastructure (RabbitMQ, Redis, etc.) with appropriate limits.

### Annotations Usage

Use `JetBrains.Annotations` is globally imported via [Directory.Build.props](Directory.Build.props) only when it adds value beyond standard .NET/BCL annotations and C# nullable reference types.

### Prefer important JetBrains annotations

Use these annotations when appropriate:

- `[PublicAPI]` for public framework/package APIs that are consumed externally but may look unused internally.
- `[UsedImplicitly]` for types, members, constructors, or properties used by reflection, DI, serializers, source generators, EF Core, ASP.NET Core, test frameworks, or conventions.
- `[ContractAnnotation]` for guard/helper methods where Rider cannot infer null-state or control flow.
- `[Pure]` for side-effect-free methods where ignoring the result is likely a bug.
- `[InstantHandle]` for delegates/lambdas that are invoked immediately and are not stored.
- `[RequireStaticDelegate]` for hot-path APIs where delegate captures should be avoided.
- `[StringFormatMethod]` for custom formatting/logging methods.
- `[RegexPattern]` for parameters that expect regular expression patterns.
- `[LocalizationRequired]` for string values, properties, parameters, or APIs where localization intent must be explicit.

Do not over-annotate. Add annotations only when they prevent false positives, document framework/convention usage, or help detect real bugs.

## Package Management

- All versions in `Directory.Packages.props`. **Never** add `Version` attribute in `.csproj` files.

## New .NET Projects

When adding a new `.csproj` to the solution, set the project SDK to one of the Headless MSBuild SDKs declared in [global.json](global.json). Do not use the stock `Microsoft.NET.Sdk` family for new projects. Versions are pinned in `global.json`'s `msbuild-sdks` block, so omit the version from the project declaration, for example `<Project Sdk="Headless.NET.Sdk.Web">`.

| Project type | SDK |
| --- | --- |
| Library, console app | `Headless.NET.Sdk` |
| ASP.NET Core / Web API | `Headless.NET.Sdk.Web` |
| Test project (xUnit v3, MTP) | `Headless.NET.Sdk.Test` |
| Razor class library | `Headless.NET.Sdk.Razor` |
| Blazor WebAssembly | `Headless.NET.Sdk.BlazorWebAssembly` |
| WPF / Windows Forms | `Headless.NET.Sdk.WindowsDesktop` |

After creating the project, attach it to [headless-framework.slnx](headless-framework.slnx). The Headless SDKs apply the project's strict baseline, including nullable references, current analyzers, banned `Newtonsoft.Json`, deterministic builds, and CI-aware warning handling. Do not disable defaults without a documented reason. Configuration switches and `Disable*` properties are documented at https://raw.githubusercontent.com/xshaheen/headless-sdk/refs/heads/main/README.md.

## Documentation

- `docs/solutions/` is a searchable knowledge store of past fixes and patterns, organized by category (`api`, `concurrency`, `guides`, `messaging`, etc.) with YAML frontmatter (`module`, `tags`, `problem_type`). Search it before implementing features, debugging issues, or making decisions in a documented area.
- Two agent-facing doc surfaces must stay in lockstep: `docs/llms/<domain>.md` and `src/Headless.<Package>/README.md`. Authoring rules, templates, and lifecycle workflows live in [docs/authoring/AUTHORING.md](docs/authoring/AUTHORING.md) — **read it before editing either surface**. Docs are not pure API reference; they must explain core concepts, trade-offs, and provider decisions.
- **Sync trigger** — a code change in `src/Headless.*` requires a docs update when any of these are true: public API surface changes, package added/renamed/removed, consumer-visible behavior changes (defaults, ordering, retry, cancellation, threading), or configuration options added/removed. Internal refactors, perf-only, test-only, and formatting changes do **not** require doc updates. When triggered, follow the drift checks in `docs/authoring/AUTHORING.md`.

## Learnings

- `Range<T>` uses `null` bounds as infinities; range-to-range operations must compare lower and upper bounds with side-specific semantics instead of reusing value containment. (2026-07-04)
- Kafka concurrent consumers must commit offsets by per-partition contiguous completed watermark; committing a high completed offset directly can acknowledge lower in-flight messages and lose them after a crash. (2026-07-06)
