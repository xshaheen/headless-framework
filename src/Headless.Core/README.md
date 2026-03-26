# Headless.Core

Core abstractions and utilities for building applications with multi-tenancy, user context, and cross-cutting concerns.

## Problem Solved

Provides standardized interfaces for common cross-cutting concerns (clock, user, tenant, locale, timezone conversion) and utilities (retry logic, compression, structured logging) enabling consistent patterns across all application layers.

## Key Features

- **Abstractions**:
  - `IClock` - Testable time abstraction (wraps `TimeProvider`)
  - `ICurrentUser` - Current authenticated user context with roles and claims
  - `ICurrentTenant` - Multi-tenancy support with scoped tenant switching
  - `ICurrentLocale` - Localization context (language, locale, culture)
  - `ICurrentTimeZone` - Timezone handling
  - `ICurrentPrincipalAccessor` - Scoped `ClaimsPrincipal` access with temporary switching
  - `IPasswordGenerator` - Configurable secure password generation
  - `ICancellationTokenProvider` - Cancellation token access with fallback logic
  - `ITimezoneProvider` - Windows/IANA timezone conversion and listing
  - `IApplicationInformationAccessor` / `IBuildInformationAccessor` - Application metadata and build info
  - `IEnumLocaleAccessor` - Localized enum display values
  - `IHaveLogger` / `IHaveTimeProvider` - Mixin interfaces for logger and time provider access

- **Utilities**:
  - `Run` - Retry helper with exponential backoff (`WithRetriesAsync`, `DelayedAsync`)
  - `SnappyCompressor` - Snappy compression/decompression with JSON serialization (AOT-compatible)
  - `LogState` / `LoggerExtensions` - Structured logging with fluent state builder, tags, and scoped properties
## Installation

```bash
dotnet add package Headless.Core
```

## Quick Start

```csharp
public sealed class OrderService(IClock clock, ICurrentUser user, ICurrentTenant tenant)
{
    public Order CreateOrder(CreateOrderRequest request)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId!.Value,
            TenantId = tenant.Id,
            CreatedAt = clock.UtcNow,
            Total = new Money(request.Amount, request.Currency)
        };
    }
}
```

### Structured Logging

```csharp
logger.LogInformation(
    s => s.Tag("orders").Property("orderId", orderId),
    "Order {OrderId} created",
    orderId
);
```

### Retry with Backoff

```csharp
var result = await Run.WithRetriesAsync(
    async ct => await httpClient.GetAsync(url, ct),
    maxAttempts: 3,
    logger: logger
);
```

## Configuration

No configuration required. Implementations are registered by `Headless.Api` or other host packages.

## Dependencies

- `Headless.Checks`
- `Headless.Extensions`
- `Headless.Serializer.Json`
- `Microsoft.Extensions.Logging.Abstractions`
- `Snappier`

## Side Effects

None.
