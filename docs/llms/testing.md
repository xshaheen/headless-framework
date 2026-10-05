---
domain: Testing
packages: Testing, Testing.AspNetCore, Testing.Testcontainers, EntityFramework.Testing
---

# Testing

> Base classes and Docker-backed fixtures for xUnit unit and integration tests.

## Orientation

- `Headless.Testing` -- base classes (`TestBase`), retry attributes, fake helpers (`TestCurrentUser`, `TestCurrentTenant`), the `AddTestTimeProvider()` DI extension, and assertion extensions. Used for unit tests.
- `Headless.Testing.AspNetCore` -- `HeadlessTestServer<TProgram>`, a `WebApplicationFactory<TProgram>` wrapper with deterministic time, DI-scope helpers, readiness polling, and Respawner-based database reset. Used for ASP.NET Core integration tests.
- `Headless.Testing.Testcontainers` -- pre-configured Docker container fixtures (e.g., `HeadlessRedisFixture`). Used for integration tests requiring real infrastructure.
- `Headless.EntityFramework.Testing` -- `TenantIsolationDbAssertions`, which prove a tenant-owned EF Core entity is invisible and unwritable to another tenant. Pairs with `TenantWorld` and `TenantIsolationHttpAssertions` from `Headless.Testing`; see [Tenant isolation](#tenant-isolation).
- `Headless.Messaging.Testing` -- `MessagingTestHarness` that records messages at the transport boundary (covers outboxed and direct-published) and exposes typed `WaitForPublishedAsync`/`WaitForConsumedAsync`/`WaitForFaultedAsync`/`WaitForExhaustedAsync` APIs.

Typical unit test inherits from `TestBase`, which provides `Logger`, `Faker`, and `AbortToken` out of the box. Integration tests typically build a shared xUnit collection fixture around `HeadlessTestServer<TProgram>` plus any required Testcontainers fixtures, then derive per-test classes from an `IntegrationTestBase : TestBase` that resets fixture state per test.

## Agent Rules

- Use `Headless.Testing` for all unit tests. Inherit from `TestBase` to get `Logger` (ILogger), `Faker` (Bogus), and `AbortToken` (CancellationToken) for free.
- Use `RetryFactAttribute` / `RetryTheoryAttribute` for flaky tests (e.g., network-dependent). Set `MaxRetries` explicitly.
- Use `FakeTimeProvider` (from `Microsoft.Extensions.TimeProvider.Testing`) to control time in tests: construct one, inject it wherever a `TimeProvider` is needed, and call `Advance(TimeSpan)` / `SetUtcNow(...)` to simulate time passing. The framework ships no clock wrapper of its own -- `TimeProvider` **is** the abstraction.
- Use `TestCurrentUser` and `TestCurrentTenant` for faking auth/tenant context in unit tests.
- For tenant-isolation tests, use `TenantWorld` for the two tenants and their scopes, `TenantIsolationDbAssertions` for EF Core reads and writes, and `TenantIsolationHttpAssertions` for routes. Do not hand-roll a forged-claim battery: the tenant catalog's identifier/claim mismatch enforcement already rejects a principal whose tenant claim disagrees with the resolved tenant.
- For ASP.NET Core integration tests, use `HeadlessTestServer<TProgram>` from `Headless.Testing.AspNetCore` rather than wiring `WebApplicationFactory<TProgram>` by hand. Wrap it for project-shaped helpers; do not reimplement its time control, DB reset, or scope-execution surface.
- Tag every integration test with `[Trait("Category", "Integration")]` so CI can filter it from the unit-test lane and run it on a Docker-capable runner.
- Use an xUnit collection fixture (not a class fixture) to share the test server across an entire test collection. Reset per-test state (DB, messaging harness, ambient time) via a `Fixture.ResetStateAsync()`-style hook called from `IntegrationTestBase.InitializeAsync()` so tests have no ordering dependency.
- Resolve the time double through the abstract service type: `serviceProvider.GetRequiredService<TimeProvider>()` returns the `FakeTimeProvider` registered by `AddTestTimeProvider()`. The concrete `FakeTimeProvider` type is not registered as itself, so cast the resolved `TimeProvider` (or keep the instance `AddTestTimeProvider()` returned).
- Advance time before seeding test data so timestamps have a known reference point. Prefer `App.AdvanceTime(...)` / `App.SetTime(...)` over reaching into the provider directly.
- A `FakeTimeProvider` only fakes the **app clock** -- the "when did this happen?" authority (audit fields, `CreatedAt`, logs). It cannot fake the store's clock, so lease/lock/TTL expiry driven by PostgreSQL, SQL Server, or Redis still needs a real wall-clock wait in an integration test. Anything that decides ownership deserves a clock-skew test: set the `FakeTimeProvider` far away from real time and assert the behavior is unchanged. See [temporal-authority-standard](../solutions/design-patterns/temporal-authority-standard.md).
- Use `MessagingTestHarness` from `Headless.Messaging.Testing` to assert published, consumed, faulted, and exhausted messages. Do not query the outbox table directly -- direct-published messages bypass it.
- When asserting EF-persisted timestamps, use `Should().BeCloseTo(expected, TimeSpan.FromMicroseconds(1))` rather than exact equality to absorb storage-precision truncation.
- Remember what Respawner-based DB reset does **not** clear: distributed caches, in-process singletons, `MessagingTestHarness` observation buffers, ambient tenant/user scopes. Reset those explicitly per test.
- Use `Headless.Testing.Testcontainers` only for integration tests. It requires Docker to be running.
- `HeadlessRedisFixture` provides a Redis 7 Alpine container. Access connection string via `_redis.Container.GetConnectionString()`.
- Test lifecycle: override `InitializeAsync()` for setup and `DisposeAsyncCore()` for teardown in `TestBase` subclasses.
- The project uses `xunit.v3`, `AwesomeAssertions` (fork of FluentAssertions), `NSubstitute`, and `Bogus`. Use these, not alternatives.

---
## Headless.Testing

Core testing utilities and base classes for xUnit tests.

### API and behavior

- `TestBase` - Abstract base class with lifecycle, logging, and Faker
- `RetryFactAttribute` / `RetryTheoryAttribute` - Automatic test retry on failure
- `AlfaTestsOrderer` - Alphabetical test ordering
- `TestHelpers` - Logging factory and utility methods
- `TestCurrentUser` / `TestCurrentTenant` - Fake context implementations
- `TenantIsolationHttpAssertions.ShouldAnswerNotFoundAcrossTenantsAsync(...)` - Asserts a cross-tenant request answers exactly like a missing id, over any `HttpClient`; see [Tenant isolation](#tenant-isolation)
- `TenantWorld` - Tenants A and B over one `ICurrentTenant`, with `AsTenantA()`, `AsTenantB()`, `AsTenant(id)`, and `AsHost()` scopes and a static `CreatePrincipal(tenantId)`; see [Tenant isolation](#tenant-isolation)
- `RecordedTestActivity.Start()` - Starts a fully recorded `Activity` on a private source and makes it `Activity.Current`, so a test can assert span tags without an OpenTelemetry pipeline; the listener samples only that source
- `TelemetryRecorder(sourceName, spans, metrics)` - Records the stopped activities and the meter measurements (instrument, unit, value, tags) of one named source, through `Activities`, `Measurements`, and `Of(instrument)`. Use it instead of a hand-written listener that appends to a `List`: listeners are process-wide, so other tests running in parallel fire the callback on their own threads, and the recorder's queues are safe for that. It still receives those tests' telemetry for the same source name, so filter assertions by a tag unique to the test, or run the class in a non-parallel collection
- `AddTestTimeProvider()` - Replaces the container's `TimeProvider` with a `FakeTimeProvider` and returns it
- Assertion extensions for async operations
- `AllBeSecretHashes(algorithmId)` - Asserts a string collection (for example a queried hash column) holds only PHC-encoded secret hashes of one algorithm; failures name the offending index and reason, never the value (see [security.md](security.md))

### Install

```bash
dotnet add package Headless.Testing
```

### Setup and use

```csharp
public sealed class OrderServiceTests : TestBase
{
    private readonly OrderService _sut;

    public OrderServiceTests()
    {
        _sut = new OrderService(Logger);
    }

    [Fact]
    public async Task should_create_order()
    {
        // given
        var order = Faker.OrderFaker().Generate();

        // when
        var result = await _sut.CreateAsync(order, AbortToken);

        // then
        result.Should().NotBeNull();
    }

    [RetryFact(MaxRetries = 3)]
    public async Task should_handle_flaky_operation()
    {
        // Test with automatic retry on failure
    }
}
```

### Setup and use

#### Test Lifecycle

```csharp
public sealed class MyTests : TestBase
{
    public override async ValueTask InitializeAsync()
    {
        // Called before each test
        await base.InitializeAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        // Called after each test
        await base.DisposeAsyncCore();
    }
}
```

#### Controllable Time

```csharp
var timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
var service = new ExpirationService(timeProvider); // takes a TimeProvider

timeProvider.Advance(TimeSpan.FromDays(30));
var isExpired = service.IsExpired(); // true
```

To swap the double into a whole container, use `AddTestTimeProvider()` from `Headless.Testing.DependencyInjection`. It does `RemoveAll<TimeProvider>()` + `AddSingleton<TimeProvider>(fake)` -- so it overrides the `TryAddSingleton(TimeProvider.System)` that provider packages register defensively -- and returns the instance:

```csharp
var services = new ServiceCollection();
services.AddMyFeature();

var timeProvider = services.AddTestTimeProvider(); // returns the registered FakeTimeProvider

var provider = services.BuildServiceProvider();
timeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
```

### Configuration

No configuration required.

### Runtime behavior

None.
---
## Headless.Testing.AspNetCore

ASP.NET Core integration-test host wrapper with controllable time, DI-scope helpers, readiness polling, database reset, and messaging-harness reset.

### API and behavior

- `HeadlessTestServer<TProgram>` -- owns the `WebApplicationFactory<TProgram>` and lifts its surface to a deterministic-by-default API.
- Replaces the host's `TimeProvider` with a `FakeTimeProvider` so tests control the app clock end-to-end.
- `AdvanceTime(TimeSpan)` and `SetTime(DateTimeOffset)` move it and return the resulting UTC time.
- `ExecuteScopeAsync(...)` opens a DI scope (optionally with a `ClaimsPrincipal`) for scoped operations.
- `WaitForReadiness(...)` polls a host-readiness predicate before tests run.
- `ConfigureDatabaseReset(...)` + `ResetDatabaseAsync()` integrate Respawner with retry.
- Database reset APIs default to the active xUnit test's cancellation token. The server retries
  database, I/O, socket, and broken-connection failures up to three times, replacing the reset
  connection between attempts.
- `ResetMessagingHarnessAsync()` waits for the `MessagingTestHarness`'s in-flight publish and consume work, then clears its observation buffers and in-memory storage between tests.

### Design constraints

Respawn 7 does not expose cancellation tokens for its internal database commands. Headless closes
the active reset connection when cancellation is requested and keeps the reset gate held until
Respawn unwinds, preventing abandoned commands from racing the next reset. A cancelled standalone
reset therefore leaves its caller-owned connection closed. The server replaces closed connections
and transiently failed connections through `ConnectionProvider` before the next attempt.

The built-in retry set covers `DbException`, `IOException`, `SocketException`, and exceptions that
wrap one of those types. Use `AdditionalTransientExceptionFilter` for a provider-specific transient
shape such as a bare `InvalidOperationException`; deterministic exceptions fail immediately.

### Install

```bash
dotnet add package Headless.Testing.AspNetCore
```

### Setup and use

```csharp
[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<TestFixture>;

public sealed class TestFixture : IAsyncLifetime
{
    public HeadlessTestServer<Program> App { get; } = new();

    public async ValueTask InitializeAsync()
    {
        App.WaitForReadiness(async sp =>
        {
            var bootstrapper = sp.GetRequiredService<IBootstrapper>();
            await bootstrapper.BootstrapAsync(); // joins the in-flight startup; returns once it completes
        });

        App.ConfigureDatabaseReset(options => options.ConnectionProvider = _ => new NpgsqlConnection("..."));

        await App.InitializeAsync();
    }

    public async Task ResetStateAsync()
    {
        await App.ResetDatabaseAsync();
        await App.ResetMessagingHarnessAsync();
    }

    public async ValueTask DisposeAsync() => await App.DisposeAsync();
}

[Trait("Category", "Integration")]
[Collection(nameof(IntegrationTestCollection))]
public abstract class IntegrationTestBase(TestFixture fixture) : TestBase
{
    protected TestFixture Fixture { get; } = fixture;
    protected HeadlessTestServer<Program> App => Fixture.App;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await Fixture.ResetStateAsync();
    }
}
```

### Setup and use

#### Deferred Initialization

`HeadlessTestServer<TProgram>` follows xUnit v3 `IAsyncLifetime` semantics -- the host starts inside `InitializeAsync()`, not the constructor. Fixture types should call `App.InitializeAsync()` from their own `InitializeAsync()` so the WAF, time provider, readiness checks, and DI scope are wired before the first test runs. Resolving from `App.Services` before `InitializeAsync()` completes throws.

#### Test Fixture Composition

- Use an xUnit **collection fixture** (`ICollectionFixture<TFixture>` + `[CollectionDefinition]`) rather than a class fixture. The integration host is expensive to construct, so sharing it across an entire test collection (often the whole assembly) is the right unit of reuse.
- Expose a `Fixture.ResetStateAsync()` hook from the fixture that resets every piece of state that crosses tests: DB rows via `App.ResetDatabaseAsync()`, messaging observations via `await App.ResetMessagingHarnessAsync()`, ambient `ICurrentTenant`/`ICurrentUser` scopes, time, and any test-owned WireMock servers.
- Call `Fixture.ResetStateAsync()` from `IntegrationTestBase.InitializeAsync()` so tests run in any order without ordering coupling. Avoid relying on xUnit test ordering or `[Trait("Order", ...)]` for state setup.
- Tag every integration class with `[Trait("Category", "Integration")]` so CI can run integration and unit lanes on different runners (only the integration lane needs Docker).

#### Wrapping `HeadlessTestServer`

Most projects benefit from a thin app-specific wrapper that adds project-shaped helpers on top of the framework type. When you wrap:

- **Delegate** WAF lifecycle, time control, DB reset, readiness, scope execution, and messaging-harness reset to `HeadlessTestServer<TProgram>`.
- **Add only** project-shaped helpers: typed scoped resolvers (e.g., `GetDbExecutor<T>()`), default-argument overloads (e.g., `AdvanceTime()` defaulting to one hour), or app-specific service factories.
- **Do not** reimplement `ExecuteScopeAsync`, time advancement, or database reset. Duplicating the lifecycle is the most common source of drift between the wrapper and the framework type.

#### Resolving `FakeTimeProvider`

`AddTestTimeProvider()` (called internally during host setup) replaces the `TimeProvider` registration with a `FakeTimeProvider`. It registers against the abstract service type only:

```csharp
// Correct
var fake = (FakeTimeProvider)serviceProvider.GetRequiredService<TimeProvider>();

// Incorrect -- the concrete type is not registered
var fake = serviceProvider.GetRequiredService<FakeTimeProvider>();
```

Prefer `App.AdvanceTime(...)` / `App.SetTime(...)` over reaching into the provider directly; both return the resulting UTC time.

This controls the **app clock** only -- the authority for "when did this happen?" timestamps (audit fields, `CreatedAt`, logs). Lease, lock, and TTL expiry are owned by the store's clock (PostgreSQL, SQL Server, Redis) and are unaffected by advancing the fake, so those still need a real wall-clock wait. That separation is deliberate: because ownership time never passes through the app clock, a test can hold the `FakeTimeProvider` far from real time and correct behavior must not change. See [temporal-authority-standard](../solutions/design-patterns/temporal-authority-standard.md).

#### Auto-Applied EF Query Filters in Tests

`HeadlessEntityModelProcessor` (from `Headless.EntityFramework`) auto-applies global query filters for three interfaces. They apply in integration tests exactly as in production:

| Interface | Filter predicate | Effect |
|-----------|------------------|--------|
| `IMultiTenant` | `TenantId == ICurrentTenant.Id` | Rows scoped to current tenant |
| `IDeleteAudit` | `IsDeleted == false` | Soft-deleted rows hidden |
| `ISuspendAudit` | `IsSuspended == false` | Suspended rows hidden |

`IgnoreQueryFilters()` is rarely needed in tests because seeded data uses default flag values (`IsDeleted = false`, `IsSuspended = false`) and runs under whatever tenant scope the test established. Reach for it only when:

- The test explicitly seeds `IsDeleted = true` or `IsSuspended = true` and needs to read the row back.
- The test is verifying the filter's own behavior (bypass, cross-tenant isolation, etc.).

For multi-tenant assertions, change the current tenant inside a `using` scope rather than bypassing the filter:

```csharp
var tenant = serviceProvider.GetRequiredService<ICurrentTenant>();
using (tenant.Change(tenantId, "Test Tenant"))
{
    // Code inside this scope runs as the specified tenant.
}
```

`ICurrentTenant.Change(...)` returns a disposable that restores the previous tenant on exit. See [multi-tenancy.md](multi-tenancy.md) for the full ownership and bypass model, including the `// MULTI-TENANCY-BYPASS:` comment convention for legitimate `IgnoreMultiTenancyFilter()` use.

#### State That DB Reset Doesn't Clear

`App.ResetDatabaseAsync()` (Respawner) truncates configured database tables only. Tests that touch state outside the database must clear it themselves, otherwise observations from a previous test bleed into the next:

- **Distributed caches.** Redis / hybrid caches are not touched by Respawner. Prefer registering `Headless.Caching.InMemory` (or the in-memory hybrid L1) for integration tests so the cache lives for the test run and dies with the host. When a test genuinely needs a distributed cache, clear it explicitly in `ResetStateAsync()`.
- **In-process singletons.** Any state held on singleton services (caches, registries, schedulers) survives DB reset. Either reset them explicitly or design the test to seed them via the public API rather than relying on a pristine state.
- **`MessagingTestHarness` observation buffers.** Call `await App.ResetMessagingHarnessAsync()` in `ResetStateAsync()` -- otherwise `WaitForPublishedAsync<T>()` may match a message from a prior test, or a store-first publish still in flight from the prior test lands in this one.
- **Ambient `ICurrentTenant` / `ICurrentUser` scopes.** Disposable scopes opened by one test must not leak into the next; close them inside the test's own `using` block or reset them in `ResetStateAsync()`.
- **External fakes** (WireMock, Stripe test server, etc.). Reset their recorded requests and reconfigure their stubs as part of `ResetStateAsync()`.

#### Time Advancement

Advance time before seeding so timestamps have a known reference point. The framework helpers move the host's `TimeProvider` and return the resulting UTC time (`App.AdvanceTime()` with no argument is a project-wrapper convenience overload, not part of `HeadlessTestServer`):

```csharp
// Defaults to +1 hour and returns the new time
var now = App.AdvanceTime();

// Specific delta
now = App.AdvanceTime(TimeSpan.FromMinutes(30));

// Absolute set when precise timestamps matter
App.SetTime(now.AddSeconds(5));
```

#### DB Round-Trip Precision

PostgreSQL `timestamptz` stores microsecond precision (6 digits), while .NET `DateTimeOffset` ticks at 100 ns (7 digits). Exact equality across a DB round-trip will intermittently fail on the trailing tick. Assert with microsecond tolerance:

```csharp
// Brittle -- fails when the persisted value truncates the 100 ns tick
result.CreatedAt.Should().Be(expected);

// Stable -- accommodates the storage precision difference
result.CreatedAt.Should().BeCloseTo(expected, TimeSpan.FromMicroseconds(1));
```

The same caveat applies to other databases with sub-tick storage precision (MySQL `DATETIME(6)`, SQL Server `datetime2(N)` for `N < 7`). Match the assertion tolerance to the column precision.

### Configuration

`HeadlessTestServer<TProgram>` is configured through its constructor and fluent pre-initialization methods — there is no options class bound from `appsettings.json`.

| Parameter / Method | Type | Default | Description |
|---|---|---|---|
| `configureTestServices` | `Action<IServiceCollection>?` | `null` | Additional DI registrations layered on top of the application's own `ConfigureTestServices`. |
| `configureWebHost` | `Action<IWebHostBuilder>?` | `null` | Additional web host configuration (e.g., environment, configuration sources). |
| `initializerTimeout` | `TimeSpan?` | 60 s | Per-`IInitializer` wait budget before a `TimeoutException` is thrown. |
| `WaitForReadiness(check, timeout)` | fluent | 30 s per check | Registers a post-startup readiness probe. Must be called before `InitializeAsync()`. |
| `ConfigureDatabaseReset(configure)` | fluent | (disabled) | Opts into Respawner-based DB reset. Must be called before `InitializeAsync()`. |
| `ResetDatabaseAsync(cancellationToken)` | `Task` | active xUnit test token | Resets database state and retries transient database or transport failures up to three times. |

`DatabaseResetOptions` properties (passed to `ConfigureDatabaseReset`):

| Property | Type | Default | Description |
|---|---|---|---|
| `DbAdapter` | `IDbAdapter` | `DbAdapter.Postgres` | Respawner adapter matching the target database engine. |
| `TablesToIgnore` | `List<Table>` | `[]` | Additional tables to skip during reset. `__EFMigrationsHistory` is always excluded automatically. |
| `ConnectionProvider` | `Func<IServiceProvider, DbConnection>?` | `null` | **Required** when using `ResetDatabaseAsync()`. Factory for an unopened `DbConnection` to the test database. |
| `AdditionalTransientExceptionFilter` | `Func<Exception, bool>?` | `null` | Adds provider-specific transient exception shapes to the built-in database and transport retry set. |

### Runtime behavior

- Starts the application host under test for the lifetime of the fixture.
- Replaces `TimeProvider` in DI with a deterministic `FakeTimeProvider`.
- (Optional) Truncates configured database tables between tests when `ConfigureDatabaseReset(...)` is wired.
---
## Headless.Testing.Testcontainers

Testcontainers fixtures for integration testing.

### API and behavior

- `TestImages` — single source of truth for all container image tags (pinned, no `:latest`)
- Shared `ContainerFixture` subclasses for every backing service used in the framework:
  - `HeadlessPostgreSqlFixture`
  - `HeadlessRedisFixture`
  - `HeadlessRabbitMqFixture`
  - `HeadlessNatsFixture`
  - `HeadlessKafkaFixture`
  - `HeadlessPulsarFixture`
  - `HeadlessAzuriteFixture`
  - `HeadlessLocalStackFixture`
  - `HeadlessMinioFixture`
  - `HeadlessSqlServerFixture` (architecture-aware: SQL Server 2022 on x86_64, Azure SQL Edge on ARM64; set `HEADLESS_SQLSERVER_IMAGE` to override, see below)
- `TestContextMessageSink` — xUnit v3 diagnostic-message forwarder
- Automatic container lifecycle management via `Testcontainers.Xunit`

### Design constraints

#### Why pin image tags

Floating tags such as `:latest` force Docker to hit the registry on every pull to check the digest, even when the local image is current. Pinning each image in `TestImages` keeps the working set small and reproducible across CI and local runs. Bump versions in one place when you want a refresh.

#### Container reuse

The fixtures create their containers with Testcontainers reuse enabled, except `HeadlessRabbitMqFixture`, `HeadlessKafkaFixture`, and `HeadlessPulsarFixture`. These broker fixtures need clean restart semantics, so they always create fresh containers. When the host opts in with `testcontainers.reuse.enable=true` in `~/.testcontainers.properties` or the `TESTCONTAINERS_REUSE_ENABLE=true` environment variable, repeated local runs reattach to an already-warm reusable container instead of paying the cold-start cost. CI leaves reuse disabled, so reuse becomes a no-op and Ryuk reaps containers as usual.

Because a reused container keeps state between runs, tests must be idempotent across runs: use drop-before-create (`DROP TABLE IF EXISTS` / `IF OBJECT_ID(...) IS NOT NULL DROP ...`) or guarded create (`CREATE ... IF NOT EXISTS`) rather than assuming a clean database. Each integration project reuses its own container in each checkout of the repository, keyed by the test assembly name (`headless.fixture` label) and the checkout's root directory (`headless.checkout` label), so neither two projects nor two worktrees running the same project share state. A removed worktree leaves its stopped containers behind; find them with `docker ps -a --filter label=headless.checkout=<path>`.

#### SQL Server image override

On ARM64, `HeadlessSqlServerFixture` runs Azure SQL Edge, which is a different engine build from SQL Server 2022: deadlock detection, catalog visibility, and error numbers can differ. To run a suite against real SQL Server on an ARM64 host that can emulate x86_64 (OrbStack or Docker Desktop with Rosetta), set `HEADLESS_SQLSERVER_IMAGE` to the image before the test process starts:

```bash
HEADLESS_SQLSERVER_IMAGE=mcr.microsoft.com/mssql/server:2022-latest \
  make test-project TEST_PROJECT=tests/Headless.Sql.SqlServer.Tests.Integration/Headless.Sql.SqlServer.Tests.Integration.csproj
```

Unset, the default is unchanged. The image name is part of the reuse hash, so the override gets its own container instead of reattaching to the Azure SQL Edge one. Emulated startup is slower; the fixture still waits for the "SQL Server is now ready" log line and a successful login.

### Install

```bash
dotnet add package Headless.Testing.Testcontainers
```

### Setup and use

```csharp
public sealed class CacheIntegrationTests : IClassFixture<HeadlessRedisFixture>
{
    private readonly HeadlessRedisFixture _redis;

    public CacheIntegrationTests(HeadlessRedisFixture redis)
    {
        _redis = redis;
    }

    [Fact]
    public async Task should_cache_value()
    {
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(_redis.Container.GetConnectionString());
        var db = multiplexer.GetDatabase();

        await db.StringSetAsync("key", "value");
        var result = await db.StringGetAsync("key");

        result.ToString().Should().Be("value");
    }
}
```

### Prerequisites

- Docker must be running

### Configuration

No configuration required. Containers use sensible defaults.

### Runtime behavior

- Starts Docker containers during test execution
- Containers are stopped after tests complete; with reuse enabled on the host they are kept stopped for the next run to reattach
## Tenant isolation

A tenant-isolation test seeds a row as tenant A and then probes it as tenant B. The kit spans two packages so `Headless.Testing` stays free of EF Core. The HTTP assertion needs only an `HttpClient`, so it works with `HeadlessTestServer.CreateClient()` or any other client:

| Package | Type | Proves |
| --- | --- | --- |
| `Headless.Testing` | `TenantWorld` | Nothing by itself: supplies tenants A and B and the scopes that make one current. |
| `Headless.Testing` | `TenantIsolationHttpAssertions` | A route answers a request for A's resource exactly as it answers a missing id. |
| `Headless.EntityFramework.Testing` | `TenantIsolationDbAssertions` | Tenant B's query for A's row is empty, and B's update and delete of it throw `CrossTenantWriteException`. |

### What the kit asserts, and what the framework guarantees

The HTTP assertion checks an **application convention**, not a framework invariant: answer another tenant's id with the same 404 a missing id gets, so a response never confirms the resource exists. The framework's own tenant statuses are narrower:

| Situation | Framework answer |
| --- | --- |
| Tenant resolution rejects the request (unknown, disabled, or claim-mismatched identifier) | 404 `g:tenant_resolution_failed` |
| The EF write guard refuses a cross-tenant write | 409 `g:cross_tenant_write` |
| A tenant-required path runs with no tenant | 403 `g:tenant_required` |

An endpoint meets the convention when the tenant query filter hides the other tenant's row, so the handler takes its normal not-found path. A 403 `g:tenant_required` from the assertion is not a leak: the request carried no tenant, which usually means the test client was not authenticated as tenant B.

Other cross-tenant surfaces already have their own enforcement and tests; the kit does not duplicate them: catalog [mismatch enforcement](multi-tenancy.md#mismatch-enforcement) for forged tenant claims, `RejectCrossTenantEnqueue()` for [background jobs](multi-tenancy.md#background-jobs), and [strict publish tenancy](multi-tenancy.md#strict-publish-tenancy-tenantcontextrequired) for messaging.

### The two-tenant world

`TenantWorld` wraps the `ICurrentTenant` the code under test reads. `TenantWorld.Create()` builds one over a fresh `TestCurrentTenant`; `new TenantWorld(currentTenant)` wraps an existing one, including the application's ambient `CurrentTenant`. Tenant ids default to `tenant-a` and `tenant-b`; blank or equal ids are rejected. Scopes nest and restore the previous tenant on dispose.

The world writes nothing to the tenant catalog. EF Core's filter and guard key on the tenant id alone. A test that also exercises HTTP resolution by identifier seeds the same ids into the catalog store:

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy.Catalog(catalog => catalog.UseInMemory(options =>
{
    options.Tenants.Add(new TenantInfo(TenantWorld.DefaultTenantA, "tenant-a", name: null, isEnabled: true));
    options.Tenants.Add(new TenantInfo(TenantWorld.DefaultTenantB, "tenant-b", name: null, isEnabled: true));
})));
```

`TenantWorld.CreatePrincipal(tenantId, userId)` returns an authenticated `ClaimsPrincipal` carrying the `UserClaimTypes.TenantId` claim, for a test authentication handler that signs a client in as one tenant's user.

### EF Core assertions

```csharp
var world = new TenantWorld(currentTenant);

Guid orderId;
using (world.AsTenantA())
await using (var db = contextFactory.CreateDbContext())
{
    var order = new Order();
    db.Add(order);
    await db.SaveChangesAsync(AbortToken);
    orderId = order.Id;
}

await TenantIsolationDbAssertions.ShouldNotSeeAcrossTenantsAsync<Order>(
    world,
    () => contextFactory.CreateDbContext(),
    orderId,
    AbortToken
);
```

- `ShouldNotReadAcrossTenantsAsync<TEntity>` fails when tenant B's untracked query finds the row: the entity has no tenant query filter.
- `ShouldRefuseWritesAcrossTenantsAsync<TEntity>` loads the row as tenant B with only `HeadlessQueryFilters.MultiTenancyFilter` ignored, then expects `CrossTenantWriteException` from an update (an optional `Action<TEntity>` mutates the row first) and from a delete. It fails when either save succeeds, which means `GuardTenantWrites()` is off, and names any other exception a save throws.
- `ShouldNotSeeAcrossTenantsAsync<TEntity>` runs both.
- Every assertion first reads the row as tenant A and fails when it finds nothing, so a wrong key or a row seeded under the wrong tenant cannot pass.
- The key is the entity's single primary-key value, of the key property's CLR type. Composite keys are rejected with `ArgumentException`.
- The context factory is called inside each tenant scope and must return a new context each call; the assertion disposes it. A factory that picks a schema or database by the current tenant gets the probing tenant's placement.
- Tenant B's probes identify tenant A's row by key and tenant, so under per-tenant placement a row tenant B owns under the same key is not mistaken for tenant A's. When tenant B's context cannot reach tenant A's row with every query filter off, the data is physically separate and the write check passes without writing. When another query filter hides the row from tenant B, the write check fails: the tenant write guard covers only entities marked `IsTenantOwned(...)` or implementing `IMultiTenant`.

### HTTP assertion

```csharp
using var client = server.CreateClient(); // authenticated as tenant B

await TenantIsolationHttpAssertions.ShouldAnswerNotFoundAcrossTenantsAsync(
    client,
    crossTenantUri: $"/orders/{orderOwnedByTenantA}",
    missingUri: $"/orders/{Guid.NewGuid()}",
    cancellationToken: AbortToken
);
```

- The missing-id request runs first as a control and must answer 404; otherwise the comparison proves nothing.
- Either request answering `g:tenant_required` (no tenant context) or `g:tenant_resolution_failed` (tenant resolution rejected it) fails the assertion as a test-setup problem, because the request never reached the endpoint as the probing tenant. Two identical tenant-resolution 404s would otherwise pass. Seed the probing tenant in the catalog, and authenticate the client as that tenant.
- The cross-tenant request must answer 404 with the same content type and the same body once `traceId`, `timestamp`, and `instance` are removed and each request's own path is replaced with a placeholder, because the framework's endpoint-not-found detail quotes the path. Pass `ignoredMembers` for application-specific per-request members.
- The `Func<HttpRequestMessage>` overload sends any verb or body.
- Failure messages name the cause: a 2xx returned another tenant's resource; a bare 403 is an existence leak under the convention; 403 `g:tenant_required` and 409 `g:cross_tenant_write` are reported as the framework answers they are.
- Build the test host with a production-style environment. The common ASP.NET pattern calls `UseExceptionHandler()` only outside Development, so a Development host answers framework exceptions with the developer error page instead of the ProblemDetails bodies this assertion reads.

---
## Headless.EntityFramework.Testing

Tenant-isolation assertions for Headless EF Core contexts. The API and behavior are in [EF Core assertions](#ef-core-assertions).

### Install

```bash
dotnet add package Headless.EntityFramework.Testing
```

### Runtime behavior

Test-only: queries and saves through the contexts the caller's factory returns, and disposes each one.

---

## Messaging test harness

`Headless.Messaging.Testing` is documented in [messaging.md](messaging.md#headlessmessagingtesting) because its delivery, unit-of-work, and observation semantics depend on the Messaging runtime. Use that package for transport-boundary assertions; use the packages in this guide for the shared xUnit and integration-test infrastructure.
