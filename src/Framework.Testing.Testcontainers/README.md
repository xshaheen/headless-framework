# Framework.Testing.Testcontainers

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
dotnet add package Framework.Testing.Testcontainers
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

- `Framework.Testing`
- `Testcontainers`
- `Testcontainers.Redis`
- `Testcontainers.Xunit`

## Side Effects

- Starts Docker containers during test execution
- Containers are automatically stopped after tests complete
