# Headless.Testing.Testcontainers

Testcontainers fixtures for integration testing.

## Problem Solved

Provides pre-configured Testcontainers fixtures for common infrastructure (Redis, databases) enabling reliable integration tests with real dependencies running in Docker.

## Key Features

- `TestImages` — single source of truth for all container image tags (pinned, no `:latest`)
- Shared `ContainerFixture` subclasses for every backing service used in the framework:
  - `HeadlessPostgreSqlFixture`
  - `HeadlessRedisFixture`
  - `HeadlessRabbitMqFixture`
  - `HeadlessNatsFixture`
  - `HeadlessAzuriteFixture`
  - `HeadlessLocalStackFixture`
  - `HeadlessSqlServerFixture` (architecture-aware: SQL Server 2022 on x86_64, Azure SQL Edge on ARM64)
- `TestContextMessageSink` — xUnit v3 diagnostic-message forwarder
- Automatic container lifecycle management via `Testcontainers.Xunit`

## Why pin image tags

Floating tags such as `:latest` force Docker to hit the registry on every pull to
check the digest, even when the local image is current. Pinning each image in
`TestImages` keeps the working set small and reproducible across CI and local runs.
Bump versions in one place when you want a refresh.

## Container reuse

The fixtures create their containers with Testcontainers reuse enabled (except `HeadlessRabbitMqFixture` — see
its remarks; the broker does not survive a warm reattach). When the host opts in —
`testcontainers.reuse.enable=true` in `~/.testcontainers.properties`, or the `TESTCONTAINERS_REUSE_ENABLE=true`
environment variable — repeated local runs reattach to the already-warm container instead of paying the
cold-start boot each time (most impactful for the slow-booting SQL Server fixtures). CI leaves reuse disabled,
so reuse becomes a no-op and Ryuk reaps containers as usual.

Because a reused container keeps state between runs, tests must be idempotent across runs: drop-before-create
(`DROP TABLE IF EXISTS` / `IF OBJECT_ID(...) IS NOT NULL DROP ...`) or guarded create (`CREATE ... IF NOT EXISTS`),
rather than assuming a clean database. Each integration project reuses its own container, keyed by the test
assembly name, so projects never share state.

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
- Containers are stopped after tests complete; with reuse enabled on the host they are kept stopped for the next run to reattach
