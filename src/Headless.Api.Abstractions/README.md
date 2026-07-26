# Headless.Api.Abstractions

Defines core interfaces and contracts for HTTP request context, user identity, web client information, ProblemDetails construction, and absolute-URL building in ASP.NET Core applications.

## Problem Solved

Provides a standardized abstraction layer for accessing request-scoped context (user, tenant, locale, timezone, client info) without coupling application code to ASP.NET Core's `HttpContext` directly.

## Key Features

- `IRequestContext` - Unified access to request-scoped information (user, tenant, locale, timezone, correlation ID)
- `IWebClientInfoProvider` - Client detection (IP address, user agent, device info)
- `IUserAgentParser` - `GetDeviceInfo(userAgent)`, the User-Agent → `"Windows Chrome"` parse behind `IWebClientInfoProvider.DeviceInfo` (implemented in `Headless.Api.Core` over DeviceDetector.NET). Substitute it to stub device detection in tests, or to swap the parser entirely. The default implementation owns a bounded private `MemoryCache`: parsing is local CPU work, entries come from untrusted request headers, and neither the keys nor results need to cross a process boundary or consume the host application's cache budget. Negatives (unidentifiable agents) are cached too, so subsequent calls reuse the memoized result while it remains valid. `UserAgentParserOptions`: `MaxEntries` 1,000, `SlidingExpiration` 6h, `Duration` (absolute cap) 24h, and `MaxUserAgentLength` 512 (longer values are truncated before parsing and keying). The singleton parser disposes its cache with its own lifetime and never registers or consumes the shared `IMemoryCache`.
- `IRequestedApiVersion` - API versioning abstraction
- `IProblemDetailsCreator` - Contract for building normalized RFC 7807 `ProblemDetails` responses (implemented in `Headless.Api.Core`)
- `IAbsoluteUrlFactory` - Contract for building absolute URLs from the current request (implemented in `Headless.Api.Core`)
- Framework constants for HTTP headers and common values

## Installation

```bash
dotnet add package Headless.Api.Abstractions
```

## Quick Start

Inject `IRequestContext` to access request-scoped information:

```csharp
public sealed class OrderService(IRequestContext context)
{
    public async Task<Order> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var userId = context.User.Id;
        var tenantId = context.Tenant.Id;
        var correlationId = context.CorrelationId;

        // Use context information for auditing, logging, multi-tenancy
        return await _repository
            .CreateAsync(
                new Order
                {
                    UserId = userId,
                    TenantId = tenantId,
                    CreatedAt = context.StartedAt,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
```

## Configuration

No configuration required. This package contains interfaces only.

## Dependencies

- `Headless.Core`
- `Microsoft.AspNetCore.App` (framework reference) - required by `IProblemDetailsCreator` (`ProblemDetails`) and `IAbsoluteUrlFactory` (`HttpContext`)

## Side Effects

None. This is an abstractions-only package.
