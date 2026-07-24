# Headless.Api.Abstractions

Defines core interfaces and contracts for HTTP request context, user identity, web client information, ProblemDetails construction, and absolute-URL building in ASP.NET Core applications.

## Problem Solved

Provides a standardized abstraction layer for accessing request-scoped context (user, tenant, locale, timezone, client info) without coupling application code to ASP.NET Core's `HttpContext` directly.

## Key Features

- `IRequestContext` - Unified access to request-scoped information (user, tenant, locale, timezone, correlation ID)
- `IWebClientInfoProvider` - Client detection (IP address, user agent, device info)
- `IUserAgentParser` - `GetDeviceInfoAsync(userAgent, ct)`, the User-Agent parse behind `IWebClientInfoProvider.GetDeviceInfoAsync` (implemented in `Headless.Api.Core`); memoizes in the host's optional `ICache` under the `api:user-agent:` prefix. Substitute it to stub device detection in tests or replace the parser
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
                    CreatedAt = context.DateStarted,
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
