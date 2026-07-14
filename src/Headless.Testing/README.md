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
- `AddTestTimeProvider()` - Replaces the container's `TimeProvider` with a `FakeTimeProvider` and returns it
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

Time is controlled through the BCL `TimeProvider` — the framework ships no clock wrapper of its own. Use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`:

```csharp
var timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
var service = new ExpirationService(timeProvider); // takes a TimeProvider

timeProvider.Advance(TimeSpan.FromDays(30));
var isExpired = service.IsExpired(); // true
```

To swap the double into a whole container, call `AddTestTimeProvider()` (from `Headless.Testing.DependencyInjection`). It uses `RemoveAll<TimeProvider>()` + `AddSingleton<TimeProvider>(fake)`, so it overrides the `TryAddSingleton(TimeProvider.System)` that provider packages register defensively, and returns the instance:

```csharp
var services = new ServiceCollection();
services.AddMyFeature();

var timeProvider = services.AddTestTimeProvider(); // returns the registered FakeTimeProvider

var provider = services.BuildServiceProvider();
timeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
```

This fakes the **app clock** only — the authority for "when did this happen?" timestamps. Lease, lock, and TTL expiry are owned by the store's clock (PostgreSQL, SQL Server, Redis) and are unaffected by advancing the fake.

## Configuration

No configuration required.

## Dependencies

- `xunit.v3`
- `Bogus`
- `Microsoft.Extensions.Logging`

## Side Effects

None.
