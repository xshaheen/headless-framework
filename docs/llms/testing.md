---
domain: Testing
packages: Testing, Testing.Testcontainers
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
- [Headless.Testing.Testcontainers](#headlesstestingtestcontainers)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start-1)
  - [Prerequisites](#prerequisites)
  - [Configuration](#configuration-1)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)

> Base classes and Docker-backed fixtures for xUnit unit and integration tests.

## Quick Orientation

- `Headless.Testing` -- base classes (`TestBase`), retry attributes, fake helpers (`TestClock`, `TestCurrentUser`, `TestCurrentTenant`), assertion extensions. Used for unit tests.
- `Headless.Testing.Testcontainers` -- pre-configured Docker container fixtures (e.g., `HeadlessRedisFixture`). Used for integration tests requiring real infrastructure.

Typical unit test inherits from `TestBase`, which provides `Logger`, `Faker`, and `AbortToken` out of the box. Integration tests use `IClassFixture<HeadlessRedisFixture>` (or similar) to spin up real services in Docker.

## Agent Instructions

- Use `Headless.Testing` for all unit tests. Inherit from `TestBase` to get `Logger` (ILogger), `Faker` (Bogus), and `AbortToken` (CancellationToken) for free.
- Use `RetryFactAttribute` / `RetryTheoryAttribute` for flaky tests (e.g., network-dependent). Set `MaxRetries` explicitly.
- Use `TestClock` to control time in tests -- call `clock.Advance(TimeSpan)` to simulate time passing. Inject it wherever `TimeProvider` is needed.
- Use `TestCurrentUser` and `TestCurrentTenant` for faking auth/tenant context in unit tests.
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
