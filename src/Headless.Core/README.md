# Headless.Core

Core abstractions and utilities for building applications with multi-tenancy, user context, and cross-cutting concerns.

## Problem Solved

Provides standardized interfaces for common cross-cutting concerns (clock, user, tenant, locale, timezone conversion) and utilities (compression, structured logging) enabling consistent patterns across all application layers.

## Key Features

- **Abstractions**:
  - `IClock` - Testable time abstraction (wraps `TimeProvider`)
  - `ICurrentUser` - Current authenticated user context with roles and claims
  - `ICurrentTenant` - Multi-tenancy support with scoped tenant switching
  - `ITenantWriteGuardBypass` - Explicit bypass scope for audited host/admin tenant writes
  - `CrossTenantWriteException` - Non-transient exception for blocked tenant-owned writes
  - `ICurrentLocale` - Localization context (language, locale, culture)
  - `ICurrentTimeZone` - Timezone handling
  - `ICurrentPrincipalAccessor` - Scoped `ClaimsPrincipal` access with temporary switching
  - `IPasswordGenerator` - Configurable secure password generation
  - `ICancellationTokenProvider` - Cancellation token access with fallback logic
  - `ITimezoneProvider` - Windows/IANA timezone conversion and listing
  - `IApplicationInformationAccessor` / `IBuildInformationAccessor` - Application metadata and build info
  - `IEnumLocaleAccessor` - Localized enum display values

- **Utilities**:
  - `SnappyCompressor` - Snappy compression/decompression with JSON serialization (AOT-compatible)
  - `LogState` / `HeadlessLoggerExtensions` - Structured logging with fluent state builder, tags, and scoped properties
  - `AddHeadlessGuidGenerator()` - registers keyed `IGuidGenerator` strategies for Version7 and SQL Server GUID ordering, plus an unkeyed backend-agnostic default

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
            Total = new Money(request.Amount, request.Currency),
        };
    }
}
```

### Structured Logging

```csharp
logger.LogInformation(s => s.Tag("orders").Property("orderId", orderId), "Order {OrderId} created", orderId);
```

### Retry Logic

For retry semantics, prefer `Polly.Core` (`ResiliencePipelineBuilder().AddRetry(...)`) — it
offers exponential backoff with jitter, exception predicates, telemetry hooks, and
composition with timeouts and circuit breakers.

## Configuration

No configuration required for the abstractions. Host/package setup can call `AddHeadlessGuidGenerator()` when it needs the framework GUID generator defaults.

## Dependencies

- `Headless.Checks`
- `Headless.Extensions`
- `Headless.Serializer.Json`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `Snappier`

## Side Effects

- `AddHeadlessGuidGenerator()` registers keyed singleton `IGuidGenerator` strategies for `SequentialGuidType.Version7` and `SequentialGuidType.SqlServer`
- `AddHeadlessGuidGenerator()` also registers an unkeyed singleton `IGuidGenerator` using `Version7` unless a caller supplies another default strategy
