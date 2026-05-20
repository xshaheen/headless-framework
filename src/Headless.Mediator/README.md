# Headless.Mediator

HTTP-agnostic Mediator behaviors for Headless applications.

## Problem Solved

Provides validation and request logging behaviors for Mediator pipelines without tying dispatch to ASP.NET Core.

## Key Features

- `ValidationRequestPreProcessor<TMessage, TResponse>` runs FluentValidation validators before handlers
- Request, response, and slow-request logging behaviors for Mediator pipelines
- Idempotent setup extensions for validation and logging pipeline registration; every extension accepts an optional `ServiceLifetime` (default `Scoped`)

## Installation

```bash
dotnet add package Headless.Mediator
```

## Quick Start

```csharp
using Headless.Mediator;
using Mediator;

builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
});

builder.Services.AddMediatorValidationRequestBehavior();
builder.Services.AddMediatorLoggingBehaviors();
```

```csharp
public sealed record CreateOrder(string ProductId) : IRequest<CreateOrderResponse>;

public sealed record CreateOrderResponse(Guid OrderId);
```

## Usage

### Request Validation

`AddMediatorValidationRequestBehavior()` registers a Mediator pre-processor that runs every registered `IValidator<TMessage>` for the dispatched message. If any validator returns failures, the pre-processor logs the validation event and throws FluentValidation's `ValidationException`.

```csharp
public sealed record CreateOrder(string ProductId) : IRequest<CreateOrderResponse>;

public sealed class CreateOrderValidator : AbstractValidator<CreateOrder>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
    }
}
```

Register validators through your normal FluentValidation DI setup. The validation pipeline is HTTP-agnostic; `Headless.Api` maps `ValidationException` to the standard 422 response when its exception handler is configured.

### Request and Response Logging

`AddMediatorLoggingBehaviors()` is the composite that registers:

- `RequestLoggingBehavior<TMessage, TResponse>` — logs before handler execution
- `ResponseLoggingBehavior<TMessage, TResponse>` — logs after handler execution
- `CriticalRequestLoggingBehavior<TMessage, TResponse>` — logs requests slower than one second

For finer-grained control, the composite is split into two registrations you can opt into independently:

- `AddMediatorRequestResponseLoggingBehaviors()` — request + response logging only
- `AddMediatorSlowRequestsLoggingBehaviors()` — slow-request logging only

These behaviors use `ICurrentUser` from `Headless.Core` instead of ASP.NET request context, so they work in API, worker, and console hosts. Register a real `ICurrentUser` where user identity exists, or `NullCurrentUser` for host-level background processes.

### Service Lifetime

All setup extensions accept an optional `ServiceLifetime` parameter that controls the descriptor lifetime of the registered pipeline behaviors. The default is `ServiceLifetime.Scoped`.

```csharp
// Scoped (default): one behavior instance per request scope.
builder.Services.AddMediatorValidationRequestBehavior();
builder.Services.AddMediatorLoggingBehaviors();

// Opt into Transient if a behavior must capture no state across the request scope.
builder.Services.AddMediatorValidationRequestBehavior(ServiceLifetime.Transient);
builder.Services.AddMediatorLoggingBehaviors(ServiceLifetime.Transient);
```

## Boundary doctrine

Some cross-cuts look like Mediator behaviors but belong at the HTTP boundary instead. The framework rejects pipeline behaviors for **authentication/authorization**, **tenancy enforcement**, and **idempotency** — see [`docs/llms/mediator.md`](../../docs/llms/mediator.md) for the full reasoning.

What belongs in the Mediator pipeline: FluentValidation pre-processors, request/response logging, slow-request alerts, response-shape transforms — concerns that have no HTTP semantics. What does NOT: anything requiring `HttpContext`, anything that should reject before model binding, anything that needs to cache HTTP status/headers/byte body rather than the typed CQRS response.

## Tenancy

Tenant enforcement is an HTTP authorization concern in `Headless.Api.Core`. Use `.Authorization(auth => auth.RequireTenant())`, `TenantRequirement`, endpoint-level `[AllowMissingTenant]` / `.AllowMissingTenant()`, and `[RequireTenant]` / `.RequireTenant()` for tenant-aware ASP.NET Core hosts.

For worker, console, and message-consumer paths, establish tenant scope explicitly around the work:

```csharp
using (currentTenant.Change(tenantId))
{
    await mediator.Send(new CreateOrder(productId), cancellationToken);
}
```

## Dependencies

- `Headless.Core`
- `Headless.Extensions`
- `FluentValidation`
- `Mediator.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

Registers open-generic `IPipelineBehavior<,>` descriptors when the setup extensions are called. Descriptor lifetime is `Scoped` by default; pass `ServiceLifetime.Transient` or `ServiceLifetime.Singleton` to override per registration.
