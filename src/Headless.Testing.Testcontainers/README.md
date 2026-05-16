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
