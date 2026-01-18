# Framework.Testing

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
dotnet add package Framework.Testing
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
