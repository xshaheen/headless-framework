---
domain: Testing
packages: Testing, Testing.AspNetCore, Testing.Testcontainers, EntityFramework.Testing
---

# Testing

> Test base classes, an ASP.NET Core test host, Docker-backed fixtures, and tenant-isolation assertions for an application's xUnit v3 tests.

## Orientation

Pick the package by the kind of test you write:

| Package | Use for | Main types |
| --- | --- | --- |
| `Headless.Testing` | Unit tests | `TestBase`, `Bounded()`, `Eventually`, `AddTestTimeProvider()`, `TestCurrentUser`, `TestCurrentTenant`, `TelemetryRecorder`, `TenantWorld`, `TenantIsolationHttpAssertions` |
| `Headless.Testing.AspNetCore` | Integration tests of an ASP.NET Core host | `HeadlessTestServer<TProgram>`, `DatabaseReset` |
| `Headless.Testing.Testcontainers` | Integration tests that need a real database, cache, broker, or blob store | `HeadlessPostgreSqlFixture`, `HeadlessSqlServerFixture`, `HeadlessRedisFixture`, `HeadlessRabbitMqFixture`, and the other fixtures |
| `Headless.EntityFramework.Testing` | Proving an EF Core entity is isolated per tenant | `TenantIsolationDbAssertions` |
| `Headless.Messaging.Testing` | Asserting published, consumed, and faulted messages | `MessagingTestHarness`, documented in [messaging.md](messaging/test-harness.md#headlessmessagingtesting) |

The packages build on xUnit v3, AwesomeAssertions (a FluentAssertions fork), Bogus, and `Microsoft.Extensions.TimeProvider.Testing`. Write the application's tests on the same stack.

The default integration setup is one xUnit collection fixture that owns a `HeadlessTestServer<Program>` and any container fixtures, plus an `IntegrationTestBase : TestBase` that resets shared state before each test.

## Agent Rules

- Derive test classes from `TestBase`. It gives `Logger` (written to the test's output), `Faker` (Bogus), and `AbortToken` (the running test's cancellation token). Pass `AbortToken` to every async call.
- Bound every wait on a signal that another thread, timer, or handler completes with `.Bounded()`: `await released.Task.Bounded()`. An unbounded `await` turns one regression into a hang of the whole test run. The limit is a ceiling for a stuck wait, so do not tighten it to assert speed.
- Poll state that raises no signal, such as a row's status or a counter, with `Eventually.UntilAsync(...)`, not a `while (...) await Task.Delay(...)` loop: `await Eventually.UntilAsync(ct => store.GetStatusAsync(id, ct), s => s == Status.Done)`. It bounds the wait and fails naming the last value it read. Prefer `.Bounded()` whenever the code under test completes a signal. The poll waits real time, so do not pass it the `FakeTimeProvider` that drives the code under test.
- To control the app clock, use `FakeTimeProvider`. In a container, call `services.AddTestTimeProvider()`. In a `HeadlessTestServer`, call `App.AdvanceTime(...)` or `App.SetTime(...)`. Do not write a clock wrapper: `TimeProvider` is the abstraction.
- Resolve the fake clock as `TimeProvider`, not as `FakeTimeProvider`: `(FakeTimeProvider)provider.GetRequiredService<TimeProvider>()`. The concrete type is not registered.
- A `FakeTimeProvider` does not move lease, lock, or TTL expiry that PostgreSQL, SQL Server, or Redis decides, and it does not drive retry back-offs, reconnects, or I/O timeouts, which wait real time. Test those with a real wait in an integration test.
- For ASP.NET Core integration tests, use `HeadlessTestServer<TProgram>`, not a hand-wired `WebApplicationFactory<TProgram>`. Wrap it for project helpers. Do not reimplement its clock, database reset, or scope execution.
- To run a test against a host configured differently, call `await using var variant = await App.DeriveAsync(...)`. Never call `App.Factory.WithWebHostBuilder(...)`: that host skips the warm-up wait, readiness checks, shared clock, and database reset, and stays alive until the fixture is disposed.
- Share the test server through an xUnit collection fixture, not a class fixture. Reset shared state from `IntegrationTestBase.InitializeAsync()` so tests run in any order.
- `ResetDatabaseAsync()` clears database rows only. Reset distributed caches, singleton state, the `MessagingTestHarness` (`App.Services.ResetMessagingHarnessAsync()`), ambient tenant and user scopes, and external fakes yourself.
- Assert EF-persisted timestamps with `Should().BeCloseTo(expected, TimeSpan.FromMicroseconds(1))`, not exact equality. The column stores fewer digits than a .NET tick.
- Use `MessagingTestHarness` to assert messages. Do not query the outbox table: a direct-published message never enters it.
- For tenant isolation, use `TenantWorld`, `TenantIsolationDbAssertions`, and `TenantIsolationHttpAssertions`. Do not hand-roll a forged-claim test: tenant resolution already rejects a principal whose tenant claim disagrees with the resolved tenant.
- Use `Headless.Testing.Testcontainers` only in integration tests. It needs a running Docker engine.

---

## Headless.Testing

Base classes, waits, clock and context fakes, telemetry recording, and assertions for unit tests.

### Setup

```bash
dotnet add package Headless.Testing
```

```csharp
using Headless.Testing.Tests;

public sealed class OrderServiceTests : TestBase
{
    [Fact]
    public async Task should_create_order()
    {
        // given
        var sut = new OrderService(Logger);
        var customer = Faker.Person.FullName;

        // when
        var result = await sut.CreateAsync(customer, AbortToken);

        // then
        result.Should().NotBeNull();
    }
}
```

**Verify:** the test runs under `dotnet test`, and lines written through `Logger` appear in that test's output.

### API

| Member | Namespace | Behavior |
| --- | --- | --- |
| `TestBase` | `Headless.Testing.Tests` | `Logger`, `LoggerFactory`, `LoggerProvider`, `Faker`, and the static `AbortToken`. Override `InitializeAsync()` for per-test setup and `DisposeAsyncCore()` for teardown; call the base method. |
| `task.Bounded(timeout?)` | `System.Threading.Tasks` (no `using` needed) | Waits for a `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>` for at most `HeadlessTestTaskExtensions.DefaultTimeout` (30 seconds) or the given limit, and until the running test is cancelled. On expiry it throws `TimeoutException` naming the awaited expression: `Waited 30 s for 'released.Task', which did not complete.` A `TimeoutException` the task throws itself passes through unchanged. |
| `Eventually.UntilAsync(...)` | `Headless.Testing` | Probes a condition, or reads a value until it satisfies a condition, every `Eventually.DefaultInterval` (10 ms) or the given `interval`, for at most `HeadlessTestTaskExtensions.DefaultTimeout` (30 seconds) or the given `timeout`, and until the running test or the given `cancellationToken` is cancelled. The value overloads return the first value that passed. On expiry it throws `TimeoutException` naming the probe and the last value: `Waited 30 s for '_ => store.CountAsync()' to satisfy 'n => n == 3'; the last of 41 probe(s) returned 2.` An async probe receives a token cancelled at the limit, so a probe that hangs ends the wait too. Probe exceptions propagate. `timeProvider` is the poll's own clock and defaults to `TimeProvider.System`. |
| `services.AddTestTimeProvider()` | `Headless.Testing.DependencyInjection` | Removes every `TimeProvider` registration, registers a `FakeTimeProvider` as `TimeProvider`, and returns it. It replaces the `TimeProvider.System` default that provider packages register. |
| `RetryFactAttribute`, `RetryTheoryAttribute` | `Headless.Testing.Retry` | Run the test up to `MaxRetries` total attempts (default 3) and report the last failure. A retry hides a race in the code under test, so reserve them for a dependency you do not control. |
| `AlphaTestsOrderer` | `Headless.Testing.Order` | Orders test cases alphabetically by method name: `[TestCaseOrderer(typeof(AlphaTestsOrderer))]`. |
| `TestCurrentUser`, `TestCurrentTenant` | `Headless.Testing` | Settable `ICurrentUser` and `ICurrentTenant` for unit tests. `TestCurrentTenant.Change(id, name)` returns a scope that restores the previous tenant. |
| `TestHelpers` | `Headless.Testing` | `CreateXUnitLoggerFactory(...)`, a shared `JsonSerializerOptions`, and `PrettyPrintJson()`. |
| `RecordedTestActivity.Start()` | `Headless.Testing` | Starts a fully recorded `Activity` on a private source and makes it `Activity.Current`, so a test asserts span tags without an OpenTelemetry pipeline. Dispose it to stop it. |
| `TelemetryRecorder(sourceName, spans, metrics)` | `Headless.Testing` | Records the stopped activities and meter measurements of one named source through `Activities`, `Measurements`, and `Of(instrument)`. See [Telemetry assertions](#telemetry-assertions). |
| `ThrowConflictExceptionAsync()`, `ThrowEntityNotFoundExceptionAsync()` | `AwesomeAssertions.Specialized` | Assert an async call throws `ConflictException` or `EntityNotFoundException`. |
| `AllBeSecretHashes(algorithmId)` | `AwesomeAssertions.Collections` | Asserts a string collection, such as a queried hash column, holds only PHC-encoded secret hashes of one algorithm. A failure names the index and the reason, never the value. See [hashing.md](hashing.md). |
| `faker.NullableBool()` | `Bogus` | A random `bool?`. |
| `TenantWorld`, `TenantIsolationHttpAssertions` | `Headless.Testing` | See [Tenant isolation](#tenant-isolation). |

### Controllable time

```csharp
using Microsoft.Extensions.Time.Testing;

var timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
var service = new ExpirationService(timeProvider); // takes a TimeProvider

timeProvider.Advance(TimeSpan.FromDays(30));
var isExpired = service.IsExpired(); // true
```

To put the fake clock in a container:

```csharp
using Headless.Testing.DependencyInjection;

var services = new ServiceCollection();
services.AddMyFeature();

var timeProvider = services.AddTestTimeProvider(); // the registered FakeTimeProvider

var provider = services.BuildServiceProvider();
timeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
```

The fake clock is the app clock: the authority for when something happened (audit fields, `CreatedAt`, logs). Ownership time, such as lease, lock, and TTL expiry, belongs to the store's clock. To prove code that decides ownership does not read the app clock, set the `FakeTimeProvider` far from real time and assert the behavior does not change.

### Telemetry assertions

Use `TelemetryRecorder` instead of a hand-written `ActivityListener` that appends to a `List`. Listeners are process-wide, so tests running in parallel fire the callback on their own threads, and the recorder's queues are safe for that. The recorder still receives those tests' telemetry for the same source name. Filter assertions by a tag unique to the test, or run the class in a non-parallel collection.

### Configuration

None.

---

## Headless.Testing.AspNetCore

`HeadlessTestServer<TProgram>`: an integration-test host over `WebApplicationFactory<TProgram>` with a fake clock, scope helpers, warm-up and readiness waits, and database reset.

### Setup

```bash
dotnet add package Headless.Testing.AspNetCore
```

```csharp
using Headless.Testing.AspNetCore;
using Headless.Testing.Tests;
using Npgsql;

[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<TestFixture>;

public sealed class TestFixture : IAsyncLifetime
{
    public HeadlessTestServer<Program> App { get; } = new();

    public async ValueTask InitializeAsync()
    {
        App.ConfigureDatabaseReset(options => options.ConnectionProvider = _ => new NpgsqlConnection("..."));

        await App.InitializeAsync();
    }

    public async Task ResetStateAsync()
    {
        await App.ResetDatabaseAsync();
        await App.Services.ResetMessagingHarnessAsync(); // Headless.Messaging.Testing, when the host uses it
    }

    public async ValueTask DisposeAsync() => await App.DisposeAsync();
}

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

**Verify:** `App.InitializeAsync()` completes, and `App.Services.GetRequiredService<TimeProvider>()` is a `FakeTimeProvider`. A host whose gating warm-ups do not finish fails the fixture with a `TimeoutException` naming each pending warm-up. A missing resource fails it with the host's startup error.

### What the server does on start

`InitializeAsync()` starts the host, not the constructor. Fixture types call `App.InitializeAsync()` from their own `InitializeAsync()`. Reading `App.Factory`, `App.Services`, or `CreateClient()` before it completes throws `InvalidOperationException` ("Server not initialized").

On start, the server does these steps in order:

1. Registers a `FakeTimeProvider` as the host's `TimeProvider`, then runs `configureTestServices`.
2. Runs the deploy steps that `AddDeployStepsOnStartup` runs: it provisions every resource and runs every seeder, through `TryAddDeployStepsOnStartup`. Pass `setupResources: false` or `seed: false` to skip either step. An application that registers the startup deploy steps itself keeps its own choice. See [utilities.md](utilities.md#resource-provisioning) and [utilities.md](utilities.md#database-seeders).
3. Waits for every warm-up that gates readiness, up to `warmupTimeout` (default 60 seconds). See [utilities.md](utilities.md#warm-up).
4. Runs each `AddReadinessCheck` probe in registration order, each under its own timeout (default 30 seconds). A probe that times out throws `TimeoutException`.

### API

| Member | Behavior |
| --- | --- |
| `TimeProvider` | The clock the host resolves: the `FakeTimeProvider`, unless `configureTestServices` registered another one. |
| `AdvanceTime(TimeSpan)`, `SetTime(DateTimeOffset)` | Move the fake clock and return the resulting UTC time. Both throw `InvalidOperationException` when the host runs on another clock. |
| `ExecuteScopeAsync(...)` | Runs a delegate in a new DI scope, optionally with a `ClaimsPrincipal`. Returns the delegate's result when it has one. |
| `CreateClient()` | An `HttpClient` to the in-memory server. |
| `AddReadinessCheck(check, timeout)` | Adds a post-start probe. Call it before `InitializeAsync()`. |
| `ConfigureDatabaseReset(configure)` | Turns on Respawn-based reset. Call it before `InitializeAsync()`. Reset is off until you call it. |
| `ResetDatabaseAsync(cancellationToken)` | Clears application tables. See [Database reset](#database-reset). |
| `DeriveAsync(configureTestServices, configureWebHost, configureHost)` | Starts an initialized variant host. See [Varying host settings per test](#varying-host-settings-per-test). |
| `serviceProvider.SetHttpContext(principal, remoteIp, userAgent)` | Creates a `DefaultHttpContext` on a scoped provider and assigns it to `IHttpContextAccessor`, for code that reads the current request outside a real request. Its feature collection is minimal, so code that reads `IHttpConnectionFeature` directly gets `null`. |

### Configuration

`HeadlessTestServer<TProgram>` takes no options class. Configure it through its constructor and the methods above.

| Constructor parameter | Default | Effect |
| --- | --- | --- |
| `configureTestServices` | `null` | DI registrations applied after the application's own. Register another `TimeProvider` here, such as `TimeProvider.System`, to run the host on that clock. |
| `configureWebHost` | `null` | Web host settings, such as the environment and configuration values. |
| `configureHost` | `null` | Generic-host settings with no web-host equivalent: `UseDefaultServiceProvider(...)`, `ConfigureHostOptions(...)`, or a logging provider for `IHostBuilder`. Runs after `configureWebHost`, just before the host is built. |
| `warmupTimeout` | 60 seconds | How long the gating warm-ups may take before a `TimeoutException` names each pending one. |
| `setupResources` | `true` | Provisions every resource on start. Pass `false` for a test whose resources already exist or that tests the startup check. |
| `seed` | `true` | Runs every seeder on start, so definitions and scheduled jobs exist before the tests. |

`DatabaseResetOptions`, passed to `ConfigureDatabaseReset`:

| Property | Default | Effect |
| --- | --- | --- |
| `ConnectionProvider` | `null` | Required for `ResetDatabaseAsync()`. Returns an unopened `DbConnection` to the test database. |
| `DbAdapter` | `DbAdapter.Postgres` | The Respawn adapter for the database engine. |
| `TablesToPreserve` | empty | Your tables whose rows survive a reset, such as reference data seeded once at startup. A `Table` without a schema matches the name in every schema. |
| `PreserveHostStateTables` | `true` | Also keeps the host-state tables framework features declare. Set `false` only for a test that re-runs the startup work that writes them. |
| `AdditionalTransientExceptionFilter` | `null` | Adds a provider-specific transient exception, such as a bare `InvalidOperationException`, to the retry set. |

### Database reset

- `ResetDatabaseAsync()` defaults to the running test's cancellation token.
- It retries `DbException`, `IOException`, `SocketException`, and any exception wrapping one of them up to three times, replacing the reset connection between attempts. Other exceptions fail at once.
- Respawn's own commands take no cancellation token. On cancellation, the server closes the reset connection and holds the next reset until Respawn unwinds. A cancelled reset on a connection you own leaves that connection closed.

#### Tables a Reset Preserves

`ResetDatabaseAsync()` clears application data and keeps the bookkeeping the running host depends on:

| Tables | Kept | Why |
| --- | --- | --- |
| `__EFMigrationsHistory`, `headless_schema_history` in every schema, and each registered feature's history table under the name its dialect uses (on SQLite, `<schema>_headless_schema_history`) | Always | They record schema work the reset leaves in place. Without `headless_schema_history`, the next host on the database (a `DeriveAsync` variant) replays every schema step against existing tables. |
| Host-state tables that framework features declare | When `PreserveHostStateTables` is `true` (default) | The host writes them at startup or keeps them live. Today: the feature, permission, and setting definition tables, including the feature and permission group tables, and the coordination membership tables (on PostgreSQL `coordination_node_generation`, `coordination_descriptor`, and `coordination_liveness`; on SQLite the same names with the schema prefix). Deleting membership rows makes the next heartbeat report the membership as lost. |
| `TablesToPreserve` | Always | Your own tables. |

Everything else is cleared, including Messaging's outbox and inbox, Jobs, idempotency, audit, and sequence tables. Jobs keeps its tables in your `DbContext` and declares no host-state table, so the cron definitions its seeder writes from `[Job(Cron = ...)]` are cleared too. A test that relies on them adds the cron-job table to `TablesToPreserve`. Host-state tables are matched by their configured names, so a renamed table stays preserved.

A suite that drives its own `WebApplicationFactory` gets the same protection by passing the host's root service provider to `DatabaseReset.CreateAsync`:

```csharp
await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
var reset = await DatabaseReset.CreateAsync(connection, factory.Services, new DatabaseResetOptions());
await reset.ResetAsync(connection);
```

The overload without a service provider keeps only `__EFMigrationsHistory`, `headless_schema_history`, and `TablesToPreserve`, which on SQLite misses the prefixed history table. Use it only for a database no host runs against.

```csharp
App.ConfigureDatabaseReset(options =>
{
    options.ConnectionProvider = _ => new NpgsqlConnection(connectionString);
    options.TablesToPreserve.Add(new Table("app", "countries")); // seeded once at startup
});
```

#### State that database reset does not clear

Clear this state in `ResetStateAsync()`, or observations from one test leak into the next:

- **Distributed caches.** Register `Headless.Caching.InMemory` for integration tests so the cache dies with the host. When a test needs Redis, clear it explicitly (`HeadlessRedisFixture.FlushAllAsync()`).
- **Singleton state.** Caches, registries, and schedulers held by singletons survive. Reset them, or seed them through the public API.
- **`MessagingTestHarness` observations.** Call `await App.Services.ResetMessagingHarnessAsync()`. Otherwise `WaitForPublishedAsync<T>()` can match a message from an earlier test.
- **Ambient `ICurrentTenant` and `ICurrentUser` scopes.** Close each scope inside the test that opened it.
- **External fakes** such as WireMock. Reset their recorded requests and stubs.

### Varying host settings per test

```csharp
[Fact]
public async Task should_limit_attempts_with_redis_cache()
{
    await using var variant = await App.DeriveAsync(
        configureWebHost: builder => builder.UseSetting("App:Cache:Type", "Redis")
    );

    using var client = variant.CreateClient();
    // ...
}
```

- The variant runs the server's own `configureTestServices`, `configureWebHost`, and `configureHost` first, then yours, so your settings win.
- It waits for warm-ups, runs the server's readiness checks, and supports `ResetDatabaseAsync()` with the server's reset configuration.
- It shares the server's `TimeProvider`, so `App.AdvanceTime(...)` moves both clocks.
- It is a separate host with its own background services and connection pools. Dispose it at the end of the test. Disposing it never stops the shared server.
- Each variant costs a host start. Put additive settings, such as an extra test controller, in the shared server's configuration instead.
- `DeriveAsync` throws `InvalidOperationException` when the server is not initialized.

### Time in integration tests

Advance time before you seed data, so timestamps have a known reference point:

```csharp
var now = App.AdvanceTime(TimeSpan.FromMinutes(30));
App.SetTime(now.AddSeconds(5));
```

`App.TimeProvider` is typed `TimeProvider`. Cast it to `FakeTimeProvider` for members the server does not expose, such as `AutoAdvanceAmount`.

PostgreSQL `timestamptz` stores microseconds, while `DateTimeOffset` ticks every 100 ns, so exact equality across a database round trip fails intermittently. The same applies to MySQL `DATETIME(6)` and SQL Server `datetime2(N)` with `N < 7`. Match the tolerance to the column precision:

```csharp
result.CreatedAt.Should().BeCloseTo(expected, TimeSpan.FromMicroseconds(1));
```

### EF Core query filters in tests

`Headless.EntityFramework` applies two global query filters in tests exactly as in production:

| Interface | Filter | Effect |
| --- | --- | --- |
| `IMultiTenant` | `TenantId == ICurrentTenant.Id` | Rows of other tenants are hidden. |
| `IDeleteAudit` | `IsDeleted == false` | Soft-deleted rows are hidden. |

Use `IgnoreQueryFilters()` only when a test seeds `IsDeleted = true` and reads the row back, or tests the filter itself. To act as another tenant, change the tenant in a scope instead of bypassing the filter:

```csharp
var tenant = serviceProvider.GetRequiredService<ICurrentTenant>();
using (tenant.Change(tenantId, "Test Tenant"))
{
    // Code here runs as tenantId; the previous tenant is restored on dispose.
}
```

See [multi-tenancy.md](multi-tenancy.md) for the ownership and bypass model.

---

## Headless.Testing.Testcontainers

xUnit v3 fixtures that start a real database, cache, broker, or blob store in Docker.

### Setup

```bash
dotnet add package Headless.Testing.Testcontainers
```

```csharp
using Headless.Testing.Testcontainers;
using StackExchange.Redis;

public sealed class CacheIntegrationTests(HeadlessRedisFixture redis) : IClassFixture<HeadlessRedisFixture>
{
    [Fact]
    public async Task should_cache_value()
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(redis.Container.GetConnectionString());
        var db = multiplexer.GetDatabase();

        await db.StringSetAsync("key", "value");
        var result = await db.StringGetAsync("key");

        result.ToString().Should().Be("value");
    }
}
```

**Verify:** the fixture starts and the round-trip assertion passes. With Docker stopped, the fixture fails before any test runs.

### Fixtures

| Fixture | Isolation | Shared container by default |
| --- | --- | --- |
| `HeadlessPostgreSqlFixture` | A database per fixture | Yes |
| `HeadlessSqlServerFixture` | A database per fixture. SQL Server 2022 on x86_64, Azure SQL Edge on ARM64. | Yes |
| `HeadlessRabbitMqFixture` | A virtual host per fixture | Yes |
| `HeadlessRedisFixture` | A private container | No |
| `HeadlessRedisClusterFixture` | A private container: three primaries, no replicas. See [Redis Cluster](#redis-cluster). | No |
| `HeadlessNatsFixture`, `HeadlessKafkaFixture`, `HeadlessAzuriteFixture`, `HeadlessLocalStackFixture`, `HeadlessMinioFixture` | A private container | No |

Other types:

- `TestImages` pins every image tag. No fixture uses `:latest`.
- `HeadlessRedisFixture.FlushAllAsync(ct)` and the `RedisContainer.FlushAllAsync(ct)` extension (namespace `Testcontainers.Redis`) run `redis-cli FLUSHALL` inside the container, so the next test starts clean without a client connection or `allowAdmin`.
- `RedisBuilder.WithHostPingWaitStrategy()` holds the container start until this container's Redis answers through the mapped host port, compared by its `run_id`. Call it on a `RedisBuilder` you build yourself: the stock check pings inside the container, so a client that connects right after `StartAsync` can be refused while Docker still sets up the port forward, and StackExchange.Redis's default `abortConnect=true` turns that into `RedisConnectionException`. When another Redis or listener answers on the host port, the start fails with `InvalidOperationException`. To restart on another port instead, start the container through `PublishedPortCheck` (see [Port check after start](#port-check-after-start)).
- `HeadlessMinioFixture` runs a plain `ContainerBuilder` container, because Testcontainers removed its MinIO module in 4.16. Build a client from its `ConnectionString` and its `AccessKey` and `SecretKey` constants, not from `Container`.
- `HeadlessResourceReaperFixture` starts Testcontainers' resource reaper (Ryuk) before any test runs and retries the start once. See [Resource reaper start](#resource-reaper-start).
- `TestContextMessageSink` forwards Testcontainers diagnostics to xUnit v3.

### Redis Cluster

`HeadlessRedisClusterFixture` runs three primaries (`PrimaryCount`), with no replicas, in one container. Use it for code that only runs on a cluster, such as per-hash-slot command splitting. Connect with `ConnectionString`, which seeds every primary; `Endpoints` lists them.

- Each node announces `127.0.0.1` and its host-mapped port, so the fixture needs a local Docker engine. On a remote engine it fails the start with `NotSupportedException`.
- `UnassignSlotAsync(slot)` takes one hash slot from its primary, so commands for that slot fail with `CLUSTERDOWN Hash slot not served` while the rest of the cluster keeps serving. Dispose the returned handle to give the slot back; its keys are kept.
- `FlushAllAsync()` runs `FLUSHALL` on every primary.

### Customize a fixture

- xUnit calls each fixture through `IAsyncLifetime`. A subclass overrides `protected virtual ValueTask InitializeAsync()` (call `base.InitializeAsync()` first, then seed its database) and `protected virtual ValueTask DisposeAsyncCore()`.
- The Redis, NATS, Kafka, Azurite, LocalStack, and MinIO fixtures take `protected override TBuilder Configure()`.
- The SQL Server, PostgreSQL, and RabbitMQ fixtures fix their server configuration so one server serves every fixture. They expose `ConnectionString` for the fixture's own database or virtual host. The PostgreSQL and RabbitMQ fixtures accept `ConfigureContainer(builder)` only together with `ShareContainer => false`.
- Every fixture takes `protected override string Image`, defaulting to its `TestImages` pin. Override it for a server with extensions, such as PostGIS. The image must keep the official entrypoint the fixture drives: for PostgreSQL, `-c` settings and `POSTGRES_USER`/`POSTGRES_PASSWORD` (as `postgis/postgis` does); for RabbitMQ, `rabbitmqctl`; for SQL Server, the "SQL Server is now ready" log line and the `sa` password. For the shared fixtures, the image is part of the shared container's name, so a fixture on another image gets a server of its own.

### Logical isolation

- `HeadlessSqlServerFixture` and `HeadlessPostgreSqlFixture` create a database named `hf_` plus 12 hex digits per fixture, and `ConnectionString` points at it. Use `AdminConnectionString` only for server-level statements.
- `HeadlessRabbitMqFixture` creates a virtual host per fixture. Pass `VirtualHost` to the client options and `-p` to any `rabbitmqctl` command.
- For a second database, take its name from `DerivedDatabaseName("suffix")` and create it with `CreateDatabaseAsync`, so it carries the owner's id and is cleaned up with it.
- A fixture drops its own databases or virtual host on dispose. Every fixture start also drops those left by a dead test process: a database whose owner's session lock (`sp_getapplock`, `pg_try_advisory_lock`) is free, and a RabbitMQ virtual host with no connection that is at least two minutes old.

### Shared containers

The SQL Server, PostgreSQL, and RabbitMQ fixtures attach to one container per image, named `headless-testing-<kind>-<hash>` and labelled `org.headless.testing.shared=true`, and leave it running on dispose. Concurrent fixtures, test processes, and checkouts run against one server, and a rerun starts against a warm one.

- Set `HEADLESS_TESTCONTAINERS_SHARED=false` to give every fixture a fresh container that Ryuk removes when the run ends, even after a crash. Use it on a host that must leave no container behind.
- Two processes that find no shared container both try to create it. Docker refuses the second name with 409 Conflict, and the loser attaches to the winner's container.
- Ryuk never removes a shared container. Remove idle ones by label: `docker rm -f -v $(docker ps -aq --filter label=org.headless.testing.shared=true)`. A CI runner discarded after the job needs nothing.
- Override `ShareContainer => false` for a fixture that stops, pauses, or restarts its server, changes server-wide settings, creates logins or roles, or asserts on server-wide views (`pg_stat_activity`, `sys.dm_exec_sessions`).
- Do not use Testcontainers' `WithReuse`: a reused container keeps one run's schema and rows for the next. The shared fixtures already reuse the server with isolation around it.

### Port check after start

A desktop Docker engine (OrbStack, Docker Desktop) hands out host ports in sequence without looking at the host. When a test process already listens on that port on `127.0.0.1`, every connection to the container's published port reaches that process instead, for the container's whole run. A forward that fails outright refuses every connection instead. So every fixture checks, after the start, that each port clients dial answers with a value only its own container gives, and starts the container again on other ports while one does not.

| Fixture | What the port must answer |
| --- | --- |
| `HeadlessPostgreSqlFixture` | The server's `system_identifier`, compared with `psql` inside the container |
| `HeadlessSqlServerFixture` | `SERVERPROPERTY('MachineName')`, the container's host name, after a login |
| `HeadlessRabbitMqFixture` | An AMQP cluster name `rabbit@<container host name>` |
| `HeadlessRedisFixture` | The `INFO server` `run_id`, compared with `redis-cli` inside the container |
| `HeadlessNatsFixture` | The `server_name` the fixture started the server with (`ServerName`) |
| `HeadlessKafkaFixture` | The cluster id the broker stored in `meta.properties` inside the container |
| `HeadlessLocalStackFixture` | The `/_localstack/info` `session_id`, compared with the one read inside the container |
| `HeadlessAzuriteFixture` | An account only this container serves, on the blob, queue, and table ports |
| `HeadlessMinioFixture` | Any reply, while the container shows the probe's connection as established on its port |

- Each fixture exposes the check as `PortCheck`, except Kafka. After a test stops a private container, start it with `await fixture.PortCheck.StartAsync(fixture.Container, ct)`, not `Container.StartAsync`, so a restart that lands on a held port moves on.
- A port refused for 15 seconds, or one that accepts but never answers as the container for two minutes, counts as lost too. Three starts that all lose a port fail the fixture with `InvalidOperationException` naming each port and why.
- A shared container (SQL Server, PostgreSQL, RabbitMQ with sharing on) and the Kafka fixture are never restarted. They fail at once with `InvalidOperationException`: a restart would cut every other run on the shared server, and Kafka keeps advertising the port it started on.
- For a container you build yourself, create `new PublishedPortCheck(containerPort, probe)` with a probe from `PublishedPortProbes` and start the container through `check.StartAsync(container, ct)`. Start a KRaft Kafka with `CLUSTER_ID` set from `PublishedPortProbes.NewKafkaClusterId()`, because a fixed id is the same in every broker started with it, and a NATS server with its own `--server_name`. `PublishedPortProbes.ConnectionSeenInside(hello)` suits a server with no value of its own to compare, but needs `cat` in the image and no other client on the port while it starts.
- `HeadlessRedisClusterFixture` checks every node's `run_id` but cannot restart, because the nodes announce the ports they started on. A held port fails its start.

### Resource reaper start

Testcontainers starts Ryuk with the first container of a test process and waits up to 60 seconds for it; no setting changes that wait. When several test processes start containers at once, Docker can miss the window, and `ResourceReaperException` ("Initialization has been cancelled.") fails every test of the process. To avoid it, register `HeadlessResourceReaperFixture` as an assembly fixture:

```csharp
[assembly: AssemblyFixture(typeof(Headless.Testing.Testcontainers.HeadlessResourceReaperFixture))]
```

It starts Ryuk before any fixture does and starts a new Ryuk container once after a `ResourceReaperException`. A retry shows as an xUnit diagnostic message. When the second attempt fails, the run fails with an error that starts "Docker infrastructure failure, not a test failure". A cancelled run is not retried. The fixture does nothing when Ryuk is disabled or no Docker endpoint is found.

### SQL Server image override

On ARM64, `HeadlessSqlServerFixture` runs Azure SQL Edge, a different engine build: deadlock detection, catalog visibility, and error numbers can differ. To test against real SQL Server on an ARM64 host that emulates x86_64 (OrbStack, or Docker Desktop with Rosetta), set `HEADLESS_SQLSERVER_IMAGE` before the test process starts:

```bash
HEADLESS_SQLSERVER_IMAGE=mcr.microsoft.com/mssql/server:2022-latest dotnet test
```

Emulated startup is slower. The fixture still waits for the "SQL Server is now ready" log line and a successful login.

### Configuration

| Environment variable | Default | Effect |
| --- | --- | --- |
| `HEADLESS_TESTCONTAINERS_SHARED` | on | `false` gives every fixture a private container. |
| `HEADLESS_SQLSERVER_IMAGE` | the architecture's image | The SQL Server image. |

### Runtime behavior

- Shared containers stay running after the run. Private containers are removed when the tests complete, or by Ryuk when the test process dies first.
- In a shared container, each fixture drops its own databases or virtual host, and the next fixture start sweeps those of a run that died.

## Tenant isolation

A tenant-isolation test seeds a row as tenant A and probes it as tenant B. The kit spans two packages so `Headless.Testing` carries no EF Core dependency. The HTTP assertion needs only an `HttpClient`, so it works with `HeadlessTestServer.CreateClient()` or any other client.

| Package | Type | Proves |
| --- | --- | --- |
| `Headless.Testing` | `TenantWorld` | Nothing by itself. It supplies tenants A and B and the scopes that make one current. |
| `Headless.Testing` | `TenantIsolationHttpAssertions` | A route answers a request for A's resource exactly as it answers a missing id. |
| `Headless.EntityFramework.Testing` | `TenantIsolationDbAssertions` | Tenant B's query for A's row is empty, and B's update and delete of it throw `CrossTenantWriteException`. |

### What the kit asserts, and what the framework guarantees

The HTTP assertion checks an application convention, not a framework guarantee: answer another tenant's id with the same 404 a missing id gets, so a response never confirms the resource exists. The framework's own tenant answers are narrower:

| Situation | Framework answer |
| --- | --- |
| Tenant resolution rejects the request (unknown, disabled, or claim-mismatched identifier) | 404 `g:tenant_resolution_failed` |
| The EF write guard refuses a cross-tenant write | 409 `g:cross_tenant_write` |
| A tenant-required path runs with no tenant | 403 `g:tenant_required` |

An endpoint meets the convention when the tenant query filter hides the other tenant's row, so the handler takes its normal not-found path. A 403 `g:tenant_required` is not a leak: the request carried no tenant, which usually means the test client was not authenticated as tenant B.

The kit does not cover surfaces that have their own enforcement: catalog [mismatch enforcement](multi-tenancy/identifier-resolution.md#mismatch-enforcement) for forged tenant claims, `RejectCrossTenantEnqueue()` for [background jobs](multi-tenancy/background-work.md#background-jobs), and [strict publish tenancy](messaging/publishing.md#strict-publish-tenancy) for messaging.

### The two-tenant world

`TenantWorld` wraps the `ICurrentTenant` the code under test reads.

- `TenantWorld.Create()` builds one over a new `TestCurrentTenant`. `new TenantWorld(currentTenant)` wraps an existing one, including the application's ambient `CurrentTenant`.
- Tenant ids default to `TenantWorld.DefaultTenantA` (`tenant-a`) and `TenantWorld.DefaultTenantB` (`tenant-b`). Blank or equal ids throw.
- `AsTenantA()`, `AsTenantB()`, `AsTenant(id)`, and `AsHost()` return scopes that nest and restore the previous tenant on dispose.
- `TenantWorld.CreatePrincipal(tenantId, userId)` returns an authenticated `ClaimsPrincipal` with the `UserClaimTypes.TenantId` claim, for a test authentication handler that signs a client in as one tenant's user.

The world writes nothing to the tenant catalog. EF Core's filter and guard key on the tenant id alone. A test that also exercises HTTP resolution by identifier seeds the same ids into the catalog:

```csharp
builder.AddHeadlessTenancy(tenancy => tenancy.Catalog(catalog => catalog.UseInMemory(options =>
{
    options.Tenants.Add(new TenantInfo(TenantWorld.DefaultTenantA, "tenant-a", name: null, isEnabled: true));
    options.Tenants.Add(new TenantInfo(TenantWorld.DefaultTenantB, "tenant-b", name: null, isEnabled: true));
})));
```

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

- `ShouldNotReadAcrossTenantsAsync<TEntity>` fails when tenant B's untracked query finds the row, which means the entity has no tenant query filter.
- `ShouldRefuseWritesAcrossTenantsAsync<TEntity>` loads the row as tenant B with only `HeadlessQueryFilters.MultiTenancyFilter` ignored, then expects `CrossTenantWriteException` from an update and from a delete. An optional `Action<TEntity>` mutates the row before the update. It fails when either save succeeds, which means `GuardTenantWrites()` is off, and names any other exception a save throws.
- `ShouldNotSeeAcrossTenantsAsync<TEntity>` runs both.
- Every assertion first reads the row as tenant A and fails when it finds nothing, so a wrong key or a row seeded under the wrong tenant cannot pass.
- The key is the entity's single primary-key value, of the key property's CLR type. A composite key throws `ArgumentException`.
- The assertion calls the context factory inside each tenant scope and disposes each context. The factory must return a new context on each call. A factory that picks a schema or database by the current tenant gets the probing tenant's placement.
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

- The missing-id request runs first as a control and must answer 404. Otherwise the comparison proves nothing.
- Either request answering `g:tenant_required` or `g:tenant_resolution_failed` fails the assertion as a test-setup problem: the request never reached the endpoint as the probing tenant. Seed the probing tenant in the catalog, and authenticate the client as that tenant.
- The cross-tenant request must answer 404 with the same content type and the same body, after `traceId`, `timestamp`, and `instance` are removed and each request's own path is replaced with a placeholder. Pass `ignoredMembers` for application-specific per-request members.
- The `Func<HttpRequestMessage>` overload sends any verb or body.
- Failure messages name the cause: a 2xx returned another tenant's resource, a bare 403 is an existence leak under the convention, and 403 `g:tenant_required` and 409 `g:cross_tenant_write` are reported as the framework answers they are.
- Build the test host with a production-style environment. The common ASP.NET Core pattern calls `UseExceptionHandler()` only outside Development, so a Development host answers framework exceptions with the developer error page instead of the ProblemDetails bodies this assertion reads.

---

## Headless.EntityFramework.Testing

`TenantIsolationDbAssertions` for Headless EF Core contexts. The API and behavior are in [EF Core assertions](#ef-core-assertions).

### Setup

```bash
dotnet add package Headless.EntityFramework.Testing
```

**Verify:** `ShouldNotSeeAcrossTenantsAsync<TEntity>` passes for a tenant-owned entity, and fails for an entity that has no tenant query filter.

### Runtime behavior

Test-only. It queries and saves through the contexts the caller's factory returns, and disposes each one.

---

## Messaging test harness

`Headless.Messaging.Testing` is documented in [messaging.md](messaging/test-harness.md#headlessmessagingtesting), because its delivery, unit-of-work, and observation semantics depend on the Messaging runtime.
