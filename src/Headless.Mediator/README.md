# Headless.Mediator

## Problem Solved

Adds pipeline behaviors for FluentValidation pre-processing and structured request/response/slow-request logging to any Mediator pipeline. These behaviors are transport-agnostic: the same registrations work in ASP.NET Core API hosts, background workers, message consumers, and console applications.

## Key Features

- `ValidationRequestPreProcessor<TMessage, TResponse>` — runs all registered `IValidator<TMessage>` concurrently before the handler; throws `ValidationException` on any failure.
- `RequestLoggingBehavior<TMessage, TResponse>` — logs the message name and payload at Debug level before handler execution.
- `ResponseLoggingBehavior<TMessage, TResponse>` — logs the message name, payload, and response at Debug level after handler execution.
- `CriticalRequestLoggingBehavior<TMessage, TResponse>` — logs elapsed time and type names at Warning level when a handler takes ≥ 1 second; the full request/response payloads go to a separate Debug-level entry so sensitive data stays out of production logs.
- Idempotent composite setup extensions: `AddMediatorValidationRequestBehavior()` and `AddMediatorLoggingBehaviors()`.
- Fine-grained split: `AddMediatorRequestResponseLoggingBehaviors()` (request + response only) and `AddMediatorSlowRequestsLoggingBehaviors()` (slow-request only).
- Every setup extension accepts an optional `ServiceLifetime` parameter (default `Scoped`).

## Design Notes

**`ICurrentUser` instead of `IHttpContextAccessor`** — all logging behaviors resolve the current user through `ICurrentUser` from `Headless.Core` rather than reading `HttpContext`. This preserves host-agnosticism: the same handler and behavior registrations run identically from a web host and a worker service. Callers that have no real user (background processes) should register `NullCurrentUser`.

**`TryAddEnumerable` for idempotency** — all setup extensions use `TryAddEnumerable` to register the open-generic `IPipelineBehavior<,>` descriptor. Calling the same extension twice does not produce duplicate behaviors in the pipeline.

## Installation

```bash
dotnet add package Headless.Mediator
```

## Quick Start

```csharp
using Headless.Mediator;

// Register Mediator (source-generator library)
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
});

// Register behaviors
builder.Services.AddMediatorValidationRequestBehavior();
builder.Services.AddMediatorLoggingBehaviors();
```

Define a request and handler:

```csharp
using Mediator;

public sealed record CreateOrder(string ProductId) : IRequest<CreateOrderResponse>;

public sealed record CreateOrderResponse(Guid OrderId);

public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, CreateOrderResponse>
{
    public ValueTask<CreateOrderResponse> Handle(CreateOrder request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new CreateOrderResponse(Guid.NewGuid()));
    }
}
```

Add a FluentValidation validator (picked up automatically by the validation pre-processor):

```csharp
using FluentValidation;

public sealed class CreateOrderValidator : AbstractValidator<CreateOrder>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
    }
}
```

## Configuration

All setup extensions accept an optional `ServiceLifetime` parameter:

```csharp
// Default: Scoped (one behavior instance per request scope)
builder.Services.AddMediatorValidationRequestBehavior();
builder.Services.AddMediatorLoggingBehaviors();

// Override lifetime per registration
builder.Services.AddMediatorValidationRequestBehavior(ServiceLifetime.Transient);
builder.Services.AddMediatorLoggingBehaviors(ServiceLifetime.Transient);
```

For finer-grained control over logging behaviors:

```csharp
// Request + response logging only (no slow-request alerting)
builder.Services.AddMediatorRequestResponseLoggingBehaviors();

// Slow-request alerting only (warns when handler takes >= 1 second)
builder.Services.AddMediatorSlowRequestsLoggingBehaviors();
```

For worker/console hosts where no real user exists, register `NullCurrentUser` before the logging behaviors:

```csharp
using Headless.Core;

builder.Services.AddSingleton<ICurrentUser, NullCurrentUser>();
builder.Services.AddMediatorLoggingBehaviors();
```

For non-HTTP paths where tenant scope must be established explicitly, use `currentTenant.Change(tenantId)` around the `mediator.Send()` call. Tenant enforcement is an HTTP authorization concern in `Headless.Api.Core` — see the boundary doctrine in `docs/llms/mediator.md`.

## Dependencies

- `Headless.Core`
- `Headless.Extensions`
- `FluentValidation`
- `Mediator.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

Registers open-generic `IPipelineBehavior<,>` descriptors when the setup extensions are called. Descriptor lifetime is `Scoped` by default; pass `ServiceLifetime.Transient` or `ServiceLifetime.Singleton` to override per registration. All registrations are idempotent — calling the same extension multiple times does not duplicate behaviors in the pipeline.
