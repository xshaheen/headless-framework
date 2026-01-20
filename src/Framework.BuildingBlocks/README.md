# Framework.BuildingBlocks

Core abstractions and primitives for building domain-driven applications with multi-tenancy, user context, and cross-cutting concerns.

## Problem Solved

Provides standardized interfaces for common cross-cutting concerns (clock, user, tenant, locale, encryption) and domain primitives (UserId, AccountId, Money, PhoneNumber) enabling consistent patterns across all application layers.

## Key Features

- **Abstractions**:
  - `IClock` - Testable time abstraction
  - `ICurrentUser` - Current authenticated user context
  - `ICurrentTenant` - Multi-tenancy support
  - `ICurrentLocale` - Localization context
  - `ICurrentTimeZone` - Timezone handling
  - `IGuidGenerator` / `ILongIdGenerator` - ID generation
  - `IStringEncryptionService` / `IStringHashService` - Security utilities
  - `IPasswordGenerator` - Secure password generation
  - `ICancellationTokenProvider` - Cancellation token access

- **Primitives** (Source-generated with JSON/TypeConverter support):
  - `UserId` - Strongly-typed user identifier
  - `AccountId` - Strongly-typed account identifier
  - `Money` - Currency-aware decimal wrapper
  - `Month` - Month representation
  - `PhoneNumber` - E.164 phone number
  - `Image` / `File` - Media metadata
  - `PageMetadata` - SEO metadata
  - `TenantInformation` - Tenant data

- **Constants**: JWT claim types, authentication constants, user claim types

## Installation

```bash
dotnet add package Framework.BuildingBlocks
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

## Configuration

No configuration required. Implementations are registered by `Framework.Api` or other host packages.

## Dependencies

- `Framework.Checks`
- `Framework.Base`
- `Framework.Domain`
- `Framework.Serializer.Json`
- `Framework.Generator.Primitives` (source generator)
- `FluentValidation`
- `Microsoft.Extensions.Logging.Abstractions`
- `Snappier`

## Side Effects

None. This is an abstractions/primitives package.
