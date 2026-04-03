---
title: "feat: Add Headless.Testing.AspNetCore with WAF integration, HttpContext helpers, and DB reset"
type: feat
status: active
date: 2026-04-03
deepened: 2026-04-03
---

# feat: Add Headless.Testing.AspNetCore with WAF integration, HttpContext helpers, and DB reset

## Overview

Enhance the Headless testing infrastructure by:
1. Adding `AddTestTimeProvider()` to `Headless.Testing` — one-call replacement of `TimeProvider`/`IClock` with fakes
2. Creating a new `Headless.Testing.AspNetCore` package with `HeadlessTestServer<TProgram>` (WAF wrapper), `TestHttpContextExtensions`, and Respawner-based DB reset

These gaps were identified from the Zad consuming project, which builds significant per-project testing infrastructure that should live in the framework.

## Problem Frame

Every project consuming Headless Framework must independently build:
- WAF wrapper with scope management, bootstrapper waiting, and time advancement helpers
- HttpContext setup for integration tests (IP, UserAgent, ClaimsPrincipal)
- Respawner configuration with knowledge of Headless-seeded table exclusions
- FakeTimeProvider DI replacement (identical 4-step pattern in every test server)

This duplication is error-prone and creates drift between consuming projects.

## Requirements Trace

- R1. One-call `AddTestTimeProvider()` that replaces `TimeProvider` + `IClock` with `FakeTimeProvider` + `TestClock`
- R2. `HeadlessTestServer<TProgram>` wrapping `WebApplicationFactory<TProgram>` with auto FakeTimeProvider registration, scope management, readiness waiting, and time advancement
- R3. `TestHttpContextExtensions.SetHttpContext()` wiring `ClaimsPrincipal`, `RemoteIpAddress`, and `UserAgent` on `IHttpContextAccessor`
- R4. `DatabaseReset` helper integrating Respawner with auto-exclusion of EF migrations history and configurable table exclusions
- R5. Generic readiness waiting mechanism that works with `IBootstrapper` without hard-coupling `Headless.Testing.AspNetCore` to `Headless.Messaging.Core`

## Scope Boundaries

- **Out of scope:** Gap 5 (WaitForPublished parameter naming) — already fixed in framework 0.4.5; consumer-side upgrade
- **Out of scope:** Migrating all existing framework integration tests to the new infrastructure (follow-up work)
- **In scope:** Dogfooding the new package in one existing integration test suite to validate the API

## Context & Research

### Relevant Code and Patterns

- `src/Headless.Testing/Helpers/TestClock.cs` — `IClock` implementation wrapping `FakeTimeProvider`, constructor accepts optional `TimeProvider`
- `src/Headless.Testing/Tests/TestBase.cs` — per-test lifecycle base with logger, Faker, `AbortToken`, `IAsyncLifetime`
- `src/Headless.Api/Setup.cs:194-201` — production `AddTimeService()` using `TryAddSingleton` (first-registration-wins enables test override)
- `src/Headless.Messaging.Testing/MessagingTestHarnessExtensions.cs` — pattern for DI extensions in testing packages (`AddMessagingTestHarness()`)
- `src/Headless.Messaging.Core/IBootstrapper.cs` — `IBootstrapper.IsStarted` contract, registered as hosted service
- `tests/Headless.Features.Tests.Integration/TestSetup/FeaturesTestFixture.cs` — existing Respawner pattern: `RespawnerOptions` with `TablesToIgnore = [HistoryRepository.DefaultTableName]`, `DbAdapter = DbAdapter.Postgres`
- `src/Headless.Testing.Testcontainers/Headless.Testing.Testcontainers.csproj` — sibling testing package pattern (PropertyGroup, IsTestProject=false, IsTestableProject=false)

### Key Observations

- All production registrations use `TryAddSingleton` for `TimeProvider`/`IClock` — test registration must happen **before** production setup, or use `Replace` on the `ServiceCollection`
- `IBootstrapper` lives in `Headless.Messaging.Core` — the WAF base should not reference messaging. A generic `WaitForService<T>(Func<T, bool>)` approach keeps coupling loose
- Three integration test fixtures (`Features`, `Permissions`, `Settings`) duplicate identical Respawner setup with `HistoryRepository.DefaultTableName` exclusion
- `Headless.Testing` currently has no `Microsoft.Extensions.DependencyInjection` reference — needs adding for `AddTestTimeProvider()`

## Key Technical Decisions

- **Separate package for ASP.NET Core testing:** `Headless.Testing.AspNetCore` keeps `Headless.Testing` free of ASP.NET Core dependencies. Consumers not using WAF don't pay the dependency cost.
- **Respawner in same ASP.NET Core package:** DB reset is almost always paired with WAF-based integration tests. Separate package adds overhead without real benefit. The `HeadlessTestServer` can expose `ResetDatabaseAsync()` directly.
- **Generic readiness waiting (not IBootstrapper-coupled):** `HeadlessTestServer` accepts `Func<IServiceProvider, Task>` readiness checks. Consumers wire `IBootstrapper` themselves — one line, no messaging dependency in the framework testing package.
- **`Replace` pattern for TimeProvider:** Since production code uses `TryAddSingleton`, and `ConfigureTestServices` runs after `ConfigureServices`, the test override must use `ServiceDescriptor.Replace()` to swap the already-registered singletons. Must also handle the EF-only path where `IClock` is registered via `Headless.Orm.EntityFramework` but `TimeProvider` is not — use `RemoveAll` + `Add` pattern instead of pure `Replace` to handle both cases.
- **Replace both `TimeProvider` and `IClock`:** Although replacing only `TimeProvider` and letting production `Clock` resolve naturally is simpler, `TestClock` is the established test contract in the framework (already used across 31+ test files). Replacing `IClock` with `TestClock` backed by the same `FakeTimeProvider` keeps the test API consistent. `TestClock.Normalize()` and `Clock.Normalize()` have identical UTC normalization logic today — if they diverge, it should be caught by `Clock` unit tests.
- **`HeadlessTestServer<TProgram>` as composition over inheritance:** Wraps WAF rather than extending `WebApplicationFactory` directly. Consumers access the inner `Factory` when needed. This avoids deep inheritance chains and gives flexibility.
- **`DbConnection` over `NpgsqlConnection` in `DatabaseReset` public API:** Accept `DbConnection` to allow future SQL Server support without breaking changes. Default `DbAdapter` remains Postgres.

## Open Questions

### Resolved During Planning

- **Should `HeadlessTestServer` be a collection fixture or per-test?** Collection fixture (`ICollectionFixture<>`) — WAF startup is expensive; shared across test collection with per-test DB reset.
- **Should `AddTestTimeProvider()` return `FakeTimeProvider`?** Yes — callers need the reference for `Advance()` calls. Return `FakeTimeProvider` from the extension method.

### Deferred to Implementation

- **Exact table names for Headless-seeded tables:** The current Respawner fixtures only exclude `__EFMigrationsHistory`. Whether additional framework-seeded tables (features, permissions, settings) need auto-exclusion depends on how consuming apps configure their DB. Provide configurable exclusion list with sensible defaults.
- **Whether `HeadlessTestServer` should implement `IAsyncLifetime` or `IAsyncDisposable`:** Likely both for xUnit compatibility, but final shape depends on implementation. Must be idempotent — xUnit calls `IAsyncLifetime.DisposeAsync` and consumers may also `await using`, causing double-dispose.
- **`Respawner.CreateAsync` timing:** Must run after EF migrations complete (needs schema introspection). In `HeadlessTestServer`, this means after host startup (which triggers hosted services including migrations). Exact ordering to be determined during implementation.

## Implementation Units

- [ ] **Unit 1: `AddTestTimeProvider()` in Headless.Testing**

  **Goal:** One-call DI extension that swaps `TimeProvider`/`IClock` with `FakeTimeProvider`/`TestClock`

  **Requirements:** R1

  **Dependencies:** None

  **Files:**
  - Modify: `src/Headless.Testing/Headless.Testing.csproj`
  - Create: `src/Headless.Testing/DependencyInjection/TestTimeProviderServiceCollectionExtensions.cs`
  - Test: `tests/Headless.Testing.Tests.Unit/DependencyInjection/TestTimeProviderServiceCollectionExtensionsTests.cs`

  **Approach:**
  - Add `Microsoft.Extensions.DependencyInjection.Abstractions` package reference to `Headless.Testing.csproj`
  - Extension method `AddTestTimeProvider(this IServiceCollection)` that:
    1. Creates `FakeTimeProvider` instance
    2. Creates `TestClock(fakeTimeProvider)` instance
    3. Uses `RemoveAll<TimeProvider>()` + `AddSingleton<TimeProvider>(fakeTimeProvider)` (handles both pre-registered and not-registered cases)
    4. Uses `RemoveAll<IClock>()` + `AddSingleton<IClock>(testClock)`
    5. Returns `FakeTimeProvider` so callers can hold a reference for `Advance()` calls
  - `RemoveAll` + `Add` pattern handles all registration paths: full `AddTimeService()` (Api), EF-only (`IClock` without `TimeProvider`), and no prior registration

  **Patterns to follow:**
  - `src/Headless.Messaging.Testing/MessagingTestHarnessExtensions.cs` — extension method style
  - `src/Headless.Api/Setup.cs:194-201` — the production registrations this replaces

  **Test scenarios:**
  - Happy path: After calling `AddTestTimeProvider()`, resolving `TimeProvider` returns a `FakeTimeProvider`
  - Happy path: After calling `AddTestTimeProvider()`, resolving `IClock` returns a `TestClock` backed by the same `FakeTimeProvider`
  - Happy path: Returned `FakeTimeProvider` is the same instance as resolved from DI
  - Happy path: Advancing the returned `FakeTimeProvider` is reflected in `IClock.UtcNow`
  - Happy path: Resolved `TestClock.TimeProvider` is the same `FakeTimeProvider` instance (wiring verification, not just type check)
  - Edge case: Calling after production `AddTimeService()` correctly replaces both registrations
  - Edge case: Calling without prior registrations still works (registers fresh)
  - Edge case: EF-only path — `IClock` registered via `TryAddSingleton<IClock, Clock>` without `TimeProvider` — both get replaced correctly

  **Verification:**
  - Tests pass. `FakeTimeProvider`, `TimeProvider`, `IClock`, and `TestClock` all resolve consistently across all registration paths.

- [ ] **Unit 2: Scaffold `Headless.Testing.AspNetCore` package**

  **Goal:** Create the new package project with correct dependencies, solution placement, and NuGet metadata

  **Requirements:** R2, R3, R4 (foundation)

  **Dependencies:** Unit 1

  **Files:**
  - Create: `src/Headless.Testing.AspNetCore/Headless.Testing.AspNetCore.csproj`
  - Modify: `headless-framework.slnx`

  **Approach:**
  - Follow `Headless.Testing.Testcontainers` csproj pattern: `IsTestProject=false`, `IsTestableProject=false`, `net10.0`
  - Package references: `Microsoft.AspNetCore.Mvc.Testing`, `Respawn`, `Npgsql`
  - Project references: `Headless.Testing`
  - Add to `.slnx` in same solution folder as other Testing packages

  **Patterns to follow:**
  - `src/Headless.Testing.Testcontainers/Headless.Testing.Testcontainers.csproj` — sibling package structure

  **Test expectation:** none — scaffolding only, verified by successful build

  **Verification:**
  - `dotnet build src/Headless.Testing.AspNetCore` succeeds

- [ ] **Unit 3: `HeadlessTestServer<TProgram>`**

  **Goal:** WAF wrapper with auto FakeTimeProvider registration, DI scope management, readiness waiting, and time advancement

  **Requirements:** R2, R5

  **Dependencies:** Unit 1, Unit 2

  **Files:**
  - Create: `src/Headless.Testing.AspNetCore/HeadlessTestServer.cs`
  - Test: `tests/Headless.Testing.AspNetCore.Tests.Unit/HeadlessTestServerTests.cs`
  - Create: `tests/Headless.Testing.AspNetCore.Tests.Unit/Headless.Testing.AspNetCore.Tests.Unit.csproj`
  - Modify: `headless-framework.slnx`

  **Approach:**
  - `HeadlessTestServer<TProgram> where TProgram : class` — sealed class, implements `IAsyncLifetime` and `IAsyncDisposable`
  - Constructor accepts optional `Action<IServiceCollection>? configureTestServices` and optional `Action<IWebHostBuilder>? configureWebHost`
  - `InitializeAsync()`:
    1. Creates `WebApplicationFactory<TProgram>` with `ConfigureTestServices` calling `AddTestTimeProvider()` + user's `configureTestServices`
    2. Creates initial client to force host startup
    3. Executes readiness checks (if configured)
    4. On failure: disposes partially-created WAF before rethrowing
  - `DisposeAsync()` must be idempotent (guard with `_disposed` flag) — xUnit calls `IAsyncLifetime.DisposeAsync` and consumers may also `await using`
  - Exposes:
    - `WebApplicationFactory<TProgram> Factory` — escape hatch for advanced scenarios
    - `HttpClient CreateClient()` — delegates to factory
    - `FakeTimeProvider TimeProvider` — the registered fake
    - `IServiceProvider Services` — delegates to `Factory.Services`
    - `ExecuteScopeAsync(Func<IServiceProvider, Task>)` — creates scope, invokes delegate, disposes scope
    - `ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>>)` — generic variant
    - `AdvanceTime(TimeSpan)` — shorthand for `TimeProvider.Advance()`
    - `SetTime(DateTimeOffset)` — shorthand for `TimeProvider.SetUtcNow()`
  - Readiness: `WaitForReadiness(Func<IServiceProvider, Task> check, TimeSpan? timeout)` method added to server config. Consumers wire their own checks:
    ```
    server.WaitForReadiness(async sp => {
        var bootstrapper = sp.GetRequiredService<IBootstrapper>();
        while (!bootstrapper.IsStarted) await Task.Delay(50);
    });
    ```
  - Builder pattern or fluent config for optional features (readiness checks, additional service overrides)

  **Patterns to follow:**
  - `src/Headless.Messaging.Testing/MessagingTestHarness.cs` — `IAsyncDisposable` lifecycle, `ServiceProvider` exposure
  - `tests/Headless.Api.Tests.Integration/ProblemDetailsTests.cs` — existing ad-hoc WAF usage in framework

  **Test scenarios:**
  - Happy path: Server starts, `Services` resolves services from the host
  - Happy path: `CreateClient()` returns a working `HttpClient`
  - Happy path: `FakeTimeProvider` is auto-registered and matches `Services.GetService<TimeProvider>()`
  - Happy path: `ExecuteScopeAsync` creates and disposes a scope, delegate receives scoped provider
  - Happy path: `AdvanceTime(TimeSpan.FromMinutes(5))` advances `TimeProvider.GetUtcNow()` by 5 minutes
  - Happy path: `SetTime(someDate)` sets `TimeProvider.GetUtcNow()` to that date
  - Happy path: Custom `configureTestServices` is invoked during host setup
  - Integration: Readiness check runs during `InitializeAsync` and blocks until satisfied
  - Edge case: Readiness check timeout throws with clear `TimeoutException` message
  - Edge case: `DisposeAsync` disposes WAF and underlying host
  - Edge case: Double `DisposeAsync` is safe (idempotent)
  - Edge case: `InitializeAsync` failure cleans up partially-created WAF
  - Edge case: Concurrent `ExecuteScopeAsync` calls from parallel tests get independent scopes

  **Verification:**
  - Tests pass. A minimal `Program` class can be used as `TProgram` in unit tests via a test web app.

- [ ] **Unit 4: `TestHttpContextExtensions`**

  **Goal:** Extension for setting up `HttpContext` on `IHttpContextAccessor` with principal, IP, and user agent

  **Requirements:** R3

  **Dependencies:** Unit 2

  **Files:**
  - Create: `src/Headless.Testing.AspNetCore/TestHttpContextExtensions.cs`
  - Test: `tests/Headless.Testing.AspNetCore.Tests.Unit/TestHttpContextExtensionsTests.cs`

  **Approach:**
  - Extension method on `IServiceProvider`:
    - `SetHttpContext(this IServiceProvider sp, ClaimsPrincipal? principal = null, IPAddress? remoteIp = null, string? userAgent = null)`
  - Implementation:
    1. Resolves `IHttpContextAccessor` from `sp`
    2. Creates new `DefaultHttpContext` with `RequestServices = sp`
    3. Sets `HttpContext.User` to `principal` (or anonymous `ClaimsPrincipal` if null)
    4. Sets `HttpContext.Connection.RemoteIpAddress` to `remoteIp`
    5. Sets `UserAgent` header if provided
    6. Assigns context to `IHttpContextAccessor.HttpContext`
    7. Returns the `HttpContext` for further customization
  - **Scoping constraint:** `SetHttpContext` should be called with a scoped `IServiceProvider` (e.g., inside `ExecuteScopeAsync`), not the root provider. Using root provider as `RequestServices` would cause scoped services to resolve incorrectly. The `HeadlessTestServer.SetHttpContext()` convenience overload should internally create a scope or document that it must be called within `ExecuteScopeAsync`.
  - `DefaultHttpContext` has minimal feature collection — code reading `IHttpConnectionFeature` directly (rather than via `HttpContext.Connection`) may get null. This is an inherent limitation; document it in XML docs.

  **Patterns to follow:**
  - ASP.NET Core `DefaultHttpContext` usage in test scenarios

  **Test scenarios:**
  - Happy path: After `SetHttpContext(principal)`, `IHttpContextAccessor.HttpContext.User` is the supplied principal
  - Happy path: After `SetHttpContext(remoteIp: IPAddress.Loopback)`, `HttpContext.Connection.RemoteIpAddress` is `127.0.0.1`
  - Happy path: After `SetHttpContext(userAgent: "TestBot/1.0")`, `HttpContext.Request.Headers.UserAgent` is `"TestBot/1.0"`
  - Happy path: All parameters combined wire correctly
  - Edge case: `SetHttpContext()` with no args creates a minimal valid context (anonymous user, no IP)
  - Edge case: Called with scoped provider — scoped services resolve correctly from `HttpContext.RequestServices`
  - Error path: Throws if `IHttpContextAccessor` is not registered

  **Verification:**
  - Tests pass. HttpContext properties are accessible from downstream services that depend on `IHttpContextAccessor`.

- [ ] **Unit 5: `DatabaseReset` helper**

  **Goal:** Respawner-based DB reset with configurable table exclusions and retry logic

  **Requirements:** R4

  **Dependencies:** Unit 2, Unit 3

  **Files:**
  - Create: `src/Headless.Testing.AspNetCore/DatabaseReset.cs`
  - Create: `src/Headless.Testing.AspNetCore/DatabaseResetOptions.cs`
  - Test: `tests/Headless.Testing.AspNetCore.Tests.Unit/DatabaseResetTests.cs`

  **Approach:**
  - `DatabaseResetOptions`:
    - `DbAdapter` — defaults to `DbAdapter.Postgres`
    - `TablesToIgnore` — `List<Table>`, auto-includes `HistoryRepository.DefaultTableName`
    - `ConnectionStringProvider` — `Func<IServiceProvider, string>` to resolve connection string from DI (e.g., from DbContext options or configuration)
  - `DatabaseReset`:
    - `CreateAsync(DbConnection connection, DatabaseResetOptions? options = null)` — creates Respawner with merged exclusions. Must be called **after** migrations complete (needs schema introspection).
    - `ResetAsync(DbConnection connection)` — executes reset with connection retry (transient fault handling)
    - Retry logic: 3 attempts with exponential backoff for transient exceptions on both `CreateAsync` and `ResetAsync`
  - Integration with `HeadlessTestServer`:
    - `ConfigureDatabaseReset(Action<DatabaseResetOptions>)` on server builder
    - `ResetDatabaseAsync()` on server — opens connection, calls reset, closes. `Respawner.CreateAsync` is called lazily on first reset (by which time host startup and migrations have completed).
  - Keep it composable: `DatabaseReset` is usable standalone outside `HeadlessTestServer`
  - Public API uses `DbConnection` (not `NpgsqlConnection`) for future SQL Server extensibility

  **Patterns to follow:**
  - `tests/Headless.Features.Tests.Integration/TestSetup/FeaturesTestFixture.cs` — existing Respawner setup pattern

  **Test scenarios:**
  - Happy path: `DatabaseReset.CreateAsync` creates a Respawner with `HistoryRepository.DefaultTableName` excluded by default
  - Happy path: Custom tables added to `TablesToIgnore` are merged with defaults
  - Happy path: `ResetAsync` calls Respawner.ResetAsync on the connection
  - Edge case: Empty `TablesToIgnore` still excludes EF migrations history
  - Edge case: Lazy `Respawner.CreateAsync` only runs once even with concurrent `ResetAsync` calls
  - Integration: Full round-trip — create, reset, verify data is cleared (requires a real DB, so integration test or deferred to dogfooding)

  **Verification:**
  - Unit tests for options merging and configuration pass. Integration test in dogfooding unit validates actual DB reset.

- [ ] **Unit 6: Dogfood in existing framework integration tests**

  **Goal:** Migrate one existing integration test fixture to use the new `HeadlessTestServer` and `DatabaseReset`, validating the API in practice

  **Requirements:** All (validation)

  **Dependencies:** Units 1–5

  **Files:**
  - Modify: `tests/Headless.Features.Tests.Integration/TestSetup/FeaturesTestFixture.cs`
  - Modify: `tests/Headless.Features.Tests.Integration/Headless.Features.Tests.Integration.csproj`

  **Approach:**
  - Replace the hand-rolled WAF + Respawner setup in `FeaturesTestFixture` with `HeadlessTestServer` + `DatabaseReset`
  - Verify all existing tests still pass with no behavioral changes
  - Document any API friction discovered during migration for follow-up refinement

  **Execution note:** Characterization-first — run existing tests before and after migration to ensure no regressions.

  **Patterns to follow:**
  - `tests/Headless.Features.Tests.Integration/TestSetup/FeaturesTestFixture.cs` — the fixture being replaced

  **Test scenarios:**
  - Integration: All existing `Headless.Features.Tests.Integration` tests pass with the new fixture
  - Integration: DB reset between tests works correctly (data from test A doesn't leak to test B)

  **Verification:**
  - All existing integration tests pass. No behavioral changes. Fixture code is significantly shorter.

## System-Wide Impact

- **Interaction graph:** `HeadlessTestServer` composes `WebApplicationFactory`, `AddTestTimeProvider()`, `DatabaseReset`, and readiness checks. Consumers' `IBootstrapper` implementations interact via the generic readiness mechanism.
- **Error propagation:** Readiness timeout throws a descriptive `TimeoutException`. DB reset retry exhaustion throws the inner `NpgsqlException`. WAF startup failures propagate from `WebApplicationFactory`.
- **API surface parity:** `AddTestTimeProvider()` must correctly replace all registrations (`TimeProvider`, `IClock`) regardless of registration order with production code. Must handle three production paths: full `AddTimeService()` (Api), EF-only (`IClock` without `TimeProvider`), and no prior registration.
- **State lifecycle risks:** `SetHttpContext` uses `AsyncLocal`-backed `IHttpContextAccessor` — safe per async flow but tests must not share `HttpContext` across parallel methods. `Respawner.CreateAsync` is lazy and must be thread-safe (single initialization).
- **Unchanged invariants:** `TestBase`, `TestClock`, `TestCurrentUser`, `TestCurrentTenant` in `Headless.Testing` are not modified. The messaging test harness (`MessagingTestHarness`) is not modified. Existing integration test fixtures continue to work as-is (migration is opt-in).

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| `ConfigureTestServices` timing vs `TryAddSingleton` | Use `Replace()` on `ServiceCollection` instead of `TryAdd`, which is guaranteed to override regardless of registration order |
| Respawner version compatibility | Already using v7.0.0 in `Directory.Packages.props`; no upgrade needed |
| `HeadlessTestServer` API doesn't fit all consuming patterns | Expose `Factory` escape hatch; keep composition-based design so consumers can use parts independently |
| Generic readiness waiting may be verbose for common IBootstrapper case | Provide a convenience extension method or documentation snippet; avoid hard coupling |
| `TestClock` vs production `Clock` divergence | Both have identical `Normalize()` logic. If they diverge, `Clock` unit tests catch it. `TestClock` is the established test contract (31+ test files). |
| `SetHttpContext` with root provider breaks scoped services | Document constraint; `HeadlessTestServer.SetHttpContext()` convenience method should require or create a scope |

## Documentation / Operational Notes

- Add `README.md` for `src/Headless.Testing.AspNetCore/` following existing package README pattern
- Update `src/Headless.Testing/README.md` to document `AddTestTimeProvider()`
- XML doc comments on all public API surface

## Sources & References

- Related code: `src/Headless.Testing/`, `src/Headless.Testing.Testcontainers/`, `src/Headless.Messaging.Testing/`
- Related code: `tests/Headless.Features.Tests.Integration/TestSetup/FeaturesTestFixture.cs`
- Related code: `src/Headless.Api/Setup.cs` (production `AddTimeService()`)
