# Headless.Testing.AspNetCore

ASP.NET Core integration-test server with controllable time, DI scope helpers, and database reset.

## Problem Solved

Wraps `WebApplicationFactory<TProgram>` with the infrastructure most integration tests need: an
auto-registered `FakeTimeProvider`, readiness/initializer waiting, idempotent async disposal, DI
scope execution helpers, and Respawner-based database reset — so test fixtures don't re-implement
this plumbing per project.

## Key Features

- `HeadlessTestServer<TProgram>` - test host wrapping `WebApplicationFactory<TProgram>` with fake
  time, readiness checks, scope execution, and database reset; safe to use as a collection fixture
  (`IAsyncLifetime`) or with `await using`.
- `DatabaseReset` - Respawner wrapper that always excludes `__EFMigrationsHistory` and resets all
  other tables; usable standalone.
- `DatabaseResetOptions` - adapter, table exclusions, and connection provider for `DatabaseReset`.
- Database reset APIs default to the active xUnit test's cancellation token. The server retries
  database, I/O, socket, and broken-connection failures up to three times, replacing the reset
  connection between attempts.
- `TestHttpContextExtensions.SetHttpContext(...)` - wire a `ClaimsPrincipal` / remote IP / user agent
  onto `IHttpContextAccessor` from a scoped provider.

## Design Notes

Respawn 7 does not expose cancellation tokens for its internal database commands. Headless closes
the active reset connection when cancellation is requested and keeps the reset gate held until
Respawn unwinds, preventing abandoned commands from racing the next reset. A cancelled standalone
reset therefore leaves its caller-owned connection closed. The server replaces closed connections
and transiently failed connections through `ConnectionProvider` before the next attempt.

The built-in retry set covers `DbException`, `IOException`, `SocketException`, and exceptions that
wrap one of those types. Use `AdditionalTransientExceptionFilter` for a provider-specific transient
shape such as a bare `InvalidOperationException`; deterministic exceptions fail immediately.

## Installation

```bash
dotnet add package Headless.Testing.AspNetCore
```

## Quick Start

```csharp
await using var server = new HeadlessTestServer<Program>();
await server.InitializeAsync();

var client = server.CreateClient();
var response = await client.GetAsync("/health", AbortToken);

response.EnsureSuccessStatusCode();
```

### Collection fixture with time control and DB reset

```csharp
public sealed class ApiFixture : HeadlessTestServer<Program>
{
    public ApiFixture()
        : base(configureTestServices: services =>
        { /* swap test doubles */
        })
    {
        ConfigureDatabaseReset(o =>
        {
            o.DbAdapter = DbAdapter.Postgres; // default; override for SQL Server
            o.ConnectionProvider =
                sp => new NpgsqlConnection( /* test connection string */
                );
        });
    }
}
```

```csharp
// In a test:
fixture.SetTime(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
fixture.AdvanceTime(TimeSpan.FromDays(30));

await fixture.ExecuteScopeAsync(async sp =>
{
    var sut = sp.GetRequiredService<IOrderService>();
    await sut.CreateAsync( /* ... */
    );
});

await fixture.ResetDatabaseAsync(); // between tests
```

### Standalone database reset

```csharp
await connection.OpenAsync(TestContext.Current.CancellationToken); // requires an open connection
var reset = await DatabaseReset.CreateAsync(connection); // after migrations; uses the xUnit token
await reset.ResetAsync(connection); // uses the xUnit token when no token is supplied
```

## Configuration

- `ConfigureDatabaseReset(...)` - opt into Respawner reset; requires
  `DatabaseResetOptions.ConnectionProvider`.
- `WaitForReadiness(check, timeout)` - register post-startup readiness probes (default 30s each).
- Constructor `initializerTimeout` - per-`IInitializer` wait budget (default 60s).
- `DatabaseResetOptions.DbAdapter` defaults to Postgres; set it explicitly for SQL Server.
- `DatabaseResetOptions.AdditionalTransientExceptionFilter` adds provider-specific transient
  exception shapes to the built-in database and transport retry set.
- `ResetDatabaseAsync`, `DatabaseReset.CreateAsync`, and `DatabaseReset.ResetAsync` accept an optional
  cancellation token and otherwise use `TestContext.Current.CancellationToken`.

## Dependencies

- `Microsoft.AspNetCore.Mvc.Testing`
- `Microsoft.EntityFrameworkCore.Relational`
- `Respawn`
- `Headless.Hosting`, `Headless.Testing`, `Headless.Messaging.Testing`

## Side Effects

`DatabaseReset.ResetAsync` issues destructive `DELETE` statements against every non-excluded table in
the target database. Point it only at disposable test databases.
