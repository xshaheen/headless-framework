---
domain: Testing
packages: Testing, Testing.AspNetCore, Testing.Testcontainers, Messaging.Testing
---

# Testing

## Table of Contents
- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Testing](#headlesstesting)
  - [Problem Solved](#problem-solved)
  - [Key Features](#key-features)
  - [Installation](#installation)
  - [Quick Start](#quick-start)
  - [Usage](#usage)
    - [Test Lifecycle](#test-lifecycle)
    - [Controllable Time](#controllable-time)
  - [Configuration](#configuration)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)
- [Headless.Testing.AspNetCore](#headlesstestingaspnetcore)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start-1)
  - [Usage](#usage-1)
    - [Deferred Initialization](#deferred-initialization)
    - [Test Fixture Composition](#test-fixture-composition)
    - [Wrapping `HeadlessTestServer`](#wrapping-headlesstestserver)
    - [Resolving `FakeTimeProvider` and `TestClock`](#resolving-faketimeprovider-and-testclock)
    - [Auto-Applied EF Query Filters in Tests](#auto-applied-ef-query-filters-in-tests)
    - [State That DB Reset Doesn't Clear](#state-that-db-reset-doesnt-clear)
    - [Time Advancement](#time-advancement)
    - [DB Round-Trip Precision](#db-round-trip-precision)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)
- [Headless.Testing.Testcontainers](#headlesstestingtestcontainers)
  - [Problem Solved](#problem-solved-2)
  - [Key Features](#key-features-2)
  - [Installation](#installation-2)
  - [Quick Start](#quick-start-2)
  - [Prerequisites](#prerequisites)
  - [Configuration](#configuration-1)
  - [Dependencies](#dependencies-2)
  - [Side Effects](#side-effects-2)
- [Headless.Messaging.Testing](#headlessmessagingtesting)
  - [Problem Solved](#problem-solved-3)
  - [Key Features](#key-features-3)
  - [Installation](#installation-3)
  - [Quick Start](#quick-start-3)
  - [Usage](#usage-2)
    - [Choosing the Right Wait Method](#choosing-the-right-wait-method)
    - [Why Not Query the Outbox Directly](#why-not-query-the-outbox-directly)
    - [Isolation Between Tests](#isolation-between-tests)
  - [Dependencies](#dependencies-3)
  - [Side Effects](#side-effects-3)

> Base classes and Docker-backed fixtures for xUnit unit and integration tests.

## Quick Orientation

- `Headless.Testing` -- base classes (`TestBase`), retry attributes, fake helpers (`TestClock`, `TestCurrentUser`, `TestCurrentTenant`), assertion extensions. Used for unit tests.
- `Headless.Testing.AspNetCore` -- `HeadlessTestServer<TProgram>`, a `WebApplicationFactory<TProgram>` wrapper with deterministic time, DI-scope helpers, readiness polling, and Respawner-based database reset. Used for ASP.NET Core integration tests.
- `Headless.Testing.Testcontainers` -- pre-configured Docker container fixtures (e.g., `HeadlessRedisFixture`). Used for integration tests requiring real infrastructure.
- `Headless.Messaging.Testing` -- `MessagingTestHarness` that records messages at the transport boundary (covers outboxed and direct-published) and exposes typed `WaitForPublished`/`Consumed`/`Faulted`/`Exhausted` APIs.

Typical unit test inherits from `TestBase`, which provides `Logger`, `Faker`, and `AbortToken` out of the box. Integration tests typically build a shared xUnit collection fixture around `HeadlessTestServer<TProgram>` plus any required Testcontainers fixtures, then derive per-test classes from an `IntegrationTestBase : TestBase` that resets fixture state per test.

## Agent Instructions

- Use `Headless.Testing` for all unit tests. Inherit from `TestBase` to get `Logger` (ILogger), `Faker` (Bogus), and `AbortToken` (CancellationToken) for free.
- Use `RetryFactAttribute` / `RetryTheoryAttribute` for flaky tests (e.g., network-dependent). Set `MaxRetries` explicitly.
- Use `TestClock` to control time in tests -- call `clock.Advance(TimeSpan)` to simulate time passing. Inject it wherever `TimeProvider` is needed.
- Use `TestCurrentUser` and `TestCurrentTenant` for faking auth/tenant context in unit tests.
- For ASP.NET Core integration tests, use `HeadlessTestServer<TProgram>` from `Headless.Testing.AspNetCore` rather than wiring `WebApplicationFactory<TProgram>` by hand. Wrap it for project-shaped helpers; do not reimplement its time control, DB reset, or scope-execution surface.
- Tag every integration test with `[Trait("Category", "Integration")]` so CI can filter it from the unit-test lane and run it on a Docker-capable runner.
- Use an xUnit collection fixture (not a class fixture) to share the test server across an entire test collection. Reset per-test state (DB, messaging harness, ambient time) via a `Fixture.ResetStateAsync()`-style hook called from `IntegrationTestBase.InitializeAsync()` so tests have no ordering dependency.
- Resolve test doubles for time through the abstract service types: `serviceProvider.GetRequiredService<TimeProvider>()` returns the `FakeTimeProvider`; `serviceProvider.GetRequiredService<IClock>()` returns the `TestClock`. The concrete types are not registered.
- Advance time before seeding test data so timestamps have a known reference point. Prefer `App.AdvanceTime(...)` / `App.SetTime(...)` so `IClock` and `TimeProvider` move together.
- Use `MessagingTestHarness` from `Headless.Messaging.Testing` to assert published, consumed, faulted, and exhausted messages. Do not query the outbox table directly -- direct-published messages bypass it.
- When asserting EF-persisted timestamps, use `Should().BeCloseTo(expected, TimeSpan.FromMicroseconds(1))` rather than exact equality to absorb storage-precision truncation.
- Remember what Respawner-based DB reset does **not** clear: distributed caches, in-process singletons, `MessagingTestHarness` observation buffers, ambient tenant/user scopes. Reset those explicitly per test.
- Use `Headless.Testing.Testcontainers` only for integration tests. It requires Docker to be running.
- `HeadlessRedisFixture` provides a Redis 7 Alpine container. Access connection string via `_redis.Container.GetConnectionString()`.
- Test lifecycle: override `InitializeAsync()` for setup and `DisposeAsyncCore()` for teardown in `TestBase` subclasses.
- The project uses `xunit.v3`, `AwesomeAssertions` (fork of FluentAssertions), `NSubstitute`, and `Bogus`. Use these, not alternatives.

---
# Headless.Testing

Core testing utilities and base classes for xUnit tests.

## Problem Solved

Provides reusable test infrastructure including base classes, retry attributes, test ordering, fake helpers, and assertion extensions for consistent, reliable testing across the framework.

## Key Features

- `TestBase` - Abstract base class with lifecycle, logging, and Faker
- `RetryFactAttribute` / `RetryTheoryAttribute` - Automatic test retry on failure
- `AlfaTestsOrderer` - Alphabetical test ordering
- `TestHelpers` - Logging factory and utility methods
- `TestCurrentUser` / `TestCurrentTenant` - Fake context implementations
- `TestClock` - Controllable time provider for tests
- Assertion extensions for async operations

## Installation

```bash
dotnet add package Headless.Testing
```

## Quick Start

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

## Usage

### Test Lifecycle

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

### Controllable Time

```csharp
var clock = new TestClock(new DateTime(2024, 1, 1));
var service = new ExpirationService(clock);

clock.Advance(TimeSpan.FromDays(30));
var isExpired = service.IsExpired(); // true
```

## Configuration

No configuration required.

## Dependencies

- `xunit.v3`
- `Bogus`
- `Microsoft.Extensions.Logging`

## Side Effects

None.
---
# Headless.Testing.AspNetCore

ASP.NET Core integration-test host wrapper with controllable time, DI-scope helpers, readiness polling, database reset, and messaging-harness reset.

## Problem Solved

`WebApplicationFactory<TProgram>` is the standard ASP.NET integration-test host, but it leaves the consumer to wire deterministic time, scoped DI execution with a principal, database reset, readiness polling, and messaging-harness teardown by hand. `HeadlessTestServer<TProgram>` owns the WAF lifecycle and exposes those helpers as a single surface, so every integration suite starts from the same deterministic baseline.

## Key Features

- `HeadlessTestServer<TProgram>` -- owns the `WebApplicationFactory<TProgram>` and lifts its surface to a deterministic-by-default API.
- Replaces `TimeProvider` and `IClock` with `FakeTimeProvider` + `TestClock` so tests control time end-to-end.
- `AdvanceTime(TimeSpan)` and `SetTime(DateTimeOffset)` move both providers together.
- `ExecuteScopeAsync(...)` opens a DI scope (optionally with a `ClaimsPrincipal`) for scoped operations.
- `WaitForReadiness(...)` polls a host-readiness predicate before tests run.
- `ConfigureDatabaseReset(...)` + `ResetDatabaseAsync()` integrate Respawner with retry.
- `ResetMessagingHarness()` clears `MessagingTestHarness` observation buffers between tests.

## Installation

```bash
dotnet add package Headless.Testing.AspNetCore
```

## Quick Start

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
            await bootstrapper.WaitUntilStartedAsync();
        });

        App.ConfigureDatabaseReset(options => options.ConnectionString = "...");

        await App.InitializeAsync();
    }

    public async Task ResetStateAsync()
    {
        await App.ResetDatabaseAsync();
        App.ResetMessagingHarness();
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

## Usage

### Deferred Initialization

`HeadlessTestServer<TProgram>` follows xUnit v3 `IAsyncLifetime` semantics -- the host starts inside `InitializeAsync()`, not the constructor. Fixture types should call `App.InitializeAsync()` from their own `InitializeAsync()` so the WAF, time provider, readiness checks, and DI scope are wired before the first test runs. Resolving from `App.Services` before `InitializeAsync()` completes throws.

### Test Fixture Composition

- Use an xUnit **collection fixture** (`ICollectionFixture<TFixture>` + `[CollectionDefinition]`) rather than a class fixture. The integration host is expensive to construct, so sharing it across an entire test collection (often the whole assembly) is the right unit of reuse.
- Expose a `Fixture.ResetStateAsync()` hook from the fixture that resets every piece of state that crosses tests: DB rows via `App.ResetDatabaseAsync()`, messaging observations via `App.ResetMessagingHarness()`, ambient `ICurrentTenant`/`ICurrentUser` scopes, time, and any test-owned WireMock servers.
- Call `Fixture.ResetStateAsync()` from `IntegrationTestBase.InitializeAsync()` so tests run in any order without ordering coupling. Avoid relying on xUnit test ordering or `[Trait("Order", ...)]` for state setup.
- Tag every integration class with `[Trait("Category", "Integration")]` so CI can run integration and unit lanes on different runners (only the integration lane needs Docker).

### Wrapping `HeadlessTestServer`

Most projects benefit from a thin app-specific wrapper that adds project-shaped helpers on top of the framework type. When you wrap:

- **Delegate** WAF lifecycle, time control, DB reset, readiness, scope execution, and messaging-harness reset to `HeadlessTestServer<TProgram>`.
- **Add only** project-shaped helpers: typed scoped resolvers (e.g., `GetDbExecutor<T>()`), default-argument overloads (e.g., `AdvanceTime()` defaulting to one hour), or app-specific service factories.
- **Do not** reimplement `ExecuteScopeAsync`, time advancement, or database reset. Duplicating the lifecycle is the most common source of drift between the wrapper and the framework type.

### Resolving `FakeTimeProvider` and `TestClock`

`AddTestTimeProvider()` (called internally during host setup) replaces `TimeProvider` and `IClock` registrations with `FakeTimeProvider` and `TestClock`. The registrations target the abstract service types only:

```csharp
// Correct
var fake = (FakeTimeProvider)serviceProvider.GetRequiredService<TimeProvider>();
var clock = (TestClock)serviceProvider.GetRequiredService<IClock>();

// Incorrect -- concrete types are not registered
var fake = serviceProvider.GetRequiredService<FakeTimeProvider>();
var clock = serviceProvider.GetRequiredService<TestClock>();
```

`TestClock.UtcNow` delegates to the underlying `FakeTimeProvider`, so a single `SetUtcNow()` or `Advance()` call moves both forward together. Prefer `App.AdvanceTime(...)` / `App.SetTime(...)` over reaching into either provider directly.

### Auto-Applied EF Query Filters in Tests

`HeadlessEntityModelProcessor` (from `Headless.Orm.EntityFramework`) auto-applies global query filters for three interfaces. They apply in integration tests exactly as in production:

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

### State That DB Reset Doesn't Clear

`App.ResetDatabaseAsync()` (Respawner) truncates configured database tables only. Tests that touch state outside the database must clear it themselves, otherwise observations from a previous test bleed into the next:

- **Distributed caches.** Redis / hybrid caches are not touched by Respawner. Prefer registering `Headless.Caching.Memory` (or the in-memory hybrid L1) for integration tests so the cache lives for the test run and dies with the host. When a test genuinely needs a distributed cache, clear it explicitly in `ResetStateAsync()`.
- **In-process singletons.** Any state held on singleton services (caches, registries, schedulers) survives DB reset. Either reset them explicitly or design the test to seed them via the public API rather than relying on a pristine state.
- **`MessagingTestHarness` observation buffers.** Call `App.ResetMessagingHarness()` in `ResetStateAsync()` -- otherwise `WaitForPublished<T>()` may match a message from a prior test.
- **Ambient `ICurrentTenant` / `ICurrentUser` scopes.** Disposable scopes opened by one test must not leak into the next; close them inside the test's own `using` block or reset them in `ResetStateAsync()`.
- **External fakes** (WireMock, Stripe test server, etc.). Reset their recorded requests and reconfigure their stubs as part of `ResetStateAsync()`.

### Time Advancement

Advance time before seeding so timestamps have a known reference point. The framework helpers move `TimeProvider` and `IClock` together:

```csharp
// Defaults to +1 hour and returns the new time
var now = App.AdvanceTime();

// Specific delta
now = App.AdvanceTime(TimeSpan.FromMinutes(30));

// Absolute set when precise timestamps matter
App.SetTime(now.AddSeconds(5));
```

### DB Round-Trip Precision

PostgreSQL `timestamptz` stores microsecond precision (6 digits), while .NET `DateTimeOffset` ticks at 100 ns (7 digits). Exact equality across a DB round-trip will intermittently fail on the trailing tick. Assert with microsecond tolerance:

```csharp
// Brittle -- fails when the persisted value truncates the 100 ns tick
result.DateCreated.Should().Be(expected);

// Stable -- accommodates the storage precision difference
result.DateCreated.Should().BeCloseTo(expected, TimeSpan.FromMicroseconds(1));
```

The same caveat applies to other databases with sub-tick storage precision (MySQL `DATETIME(6)`, SQL Server `datetime2(N)` for `N < 7`). Match the assertion tolerance to the column precision.

## Dependencies

- `Headless.Testing`
- `Microsoft.AspNetCore.Mvc.Testing`
- `Microsoft.Extensions.Time.Testing`

## Side Effects

- Starts the application host under test for the lifetime of the fixture.
- Replaces `TimeProvider` and `IClock` in DI with deterministic test doubles.
- (Optional) Truncates configured database tables between tests when `ConfigureDatabaseReset(...)` is wired.
---
# Headless.Testing.Testcontainers

Testcontainers fixtures for integration testing.

## Problem Solved

Provides pre-configured Testcontainers fixtures for common infrastructure (Redis, databases) enabling reliable integration tests with real dependencies running in Docker.

## Key Features

- `HeadlessRedisFixture` - Redis 7 Alpine container fixture
- `TestContextMessageSink` - xUnit output integration
- Automatic container lifecycle management
- Clean test isolation

## Installation

```bash
dotnet add package Headless.Testing.Testcontainers
```

## Quick Start

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
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(
            _redis.Container.GetConnectionString()
        );
        var db = multiplexer.GetDatabase();

        await db.StringSetAsync("key", "value");
        var result = await db.StringGetAsync("key");

        result.ToString().Should().Be("value");
    }
}
```

## Prerequisites

- Docker must be running

## Configuration

No configuration required. Containers use sensible defaults.

## Dependencies

- `Headless.Testing`
- `Testcontainers`
- `Testcontainers.Redis`
- `Testcontainers.Xunit`

## Side Effects

- Starts Docker containers during test execution
- Containers are automatically stopped after tests complete
---
# Headless.Messaging.Testing

Transport-level test harness for asserting on published, consumed, faulted, and exhausted messages without binding tests to a specific broker.

## Problem Solved

Asserting on messaging by querying the outbox table covers only messages that flow through the outbox -- direct publishes bypass it and leave assertions blind. `MessagingTestHarness` records every message at the `ITransport` boundary, so tests can wait on and assert against outboxed and direct-published messages through one API.

## Key Features

- `MessagingTestHarness` records messages at the transport layer (covers outbox + direct publish).
- `WaitForPublished<T>(...)`, `WaitForConsumed<T>(...)`, `WaitForFaulted<T>(...)`, `WaitForExhausted<T>(...)` block until a match arrives or the configured timeout elapses (`MessageObservationTimeoutException`).
- Predicate overloads for filtering by payload shape.
- `Published`, `Consumed`, `Faulted`, `Exhausted` collections for non-blocking assertions.
- `Clear()` for clean test isolation; integrates with `HeadlessTestServer.ResetMessagingHarness()`.

## Installation

```bash
dotnet add package Headless.Messaging.Testing
```

## Quick Start

```csharp
// Setup (typically inside a collection fixture)
services.AddMessagingTestHarness();

// In a test
var harness = App.Services.GetRequiredService<MessagingTestHarness>();

// Execute the action that should publish a message...
await sut.HandleAsync(input, AbortToken);

// Wait for a specific message type, optionally filtered by predicate
var recorded = await harness.WaitForPublished<UserCreatedMessage>(
    m => m.UserId == userId,
    cancellationToken: AbortToken);

var message = (UserCreatedMessage)recorded.Message;
message.UserId.Should().Be(userId);
```

## Usage

### Choosing the Right Wait Method

| Method | Use when |
|--------|----------|
| `WaitForPublished<T>(...)` | Asserting a publish-side effect |
| `WaitForConsumed<T>(...)` | Asserting a consumer ran and finished without faulting |
| `WaitForFaulted<T>(...)` | Asserting a consumer error path |
| `WaitForExhausted<T>(...)` | Asserting retry exhaustion / DLQ routing |

Each method has a no-predicate overload (matches by type only) and a predicate overload (matches by type + payload shape). All four return a `RecordedMessage` whose `.Message` is the deserialized payload.

### Why Not Query the Outbox Directly

A common reflex is to assert by selecting from the outbox table (`outbox.published` or the configured equivalent). That only works when the producer goes through the outbox pipeline. Code paths that call `IMessagePublisher` directly -- system messages, retries that opt out of the outbox, framework internals -- never write a row there. `MessagingTestHarness` records at the transport boundary, so the assertion is single-source-of-truth regardless of how the message was produced.

### Isolation Between Tests

Call `harness.Clear()` (or `App.ResetMessagingHarness()` when using `HeadlessTestServer`) from your fixture's `ResetStateAsync()` so observations from one test do not leak into the next. Both methods drop the accumulated `Published` / `Consumed` / `Faulted` / `Exhausted` collections without re-creating the transport stub. Tests that observe asynchronous publish-then-consume flows should rely on the `WaitFor*` APIs rather than reading the collections immediately, since the transport stub records on the consumer's thread.

## Dependencies

- `Headless.Messaging.Abstractions`
- `Headless.Messaging.Core`

## Side Effects

- Replaces the configured `ITransport` with a recording stub. Tests using the harness do not exchange messages with any real broker.
