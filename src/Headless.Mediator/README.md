# Headless.Mediator

HTTP-agnostic Mediator behaviors for Headless applications.

## Problem Solved

Provides a tenant-required request boundary for Mediator pipelines so API, worker, console,
and messaging dispatch surfaces can share the same ambient tenant invariant.

## Key Features

- `TenantRequiredBehavior<TRequest, TResponse>` enforces `ICurrentTenant.Id`
- `[AllowMissingTenant]` opt-out marker for host-level and public requests
- `ValidationRequestPreProcessor<TMessage, TResponse>` runs FluentValidation validators before handlers
- Request, response, and slow-request logging behaviors for Mediator pipelines
- `MissingTenantContextException` reuse for existing HTTP 400 failure mapping
- Idempotent setup extensions for tenant, validation, and logging pipeline registration; every extension accepts an optional `ServiceLifetime` (default `Scoped`)

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

builder.AddHeadlessTenancy(
    tenancy => tenancy.Mediator(mediator => mediator.RequireTenant())
);

builder.Services.AddMediatorValidationRequestBehavior();
builder.Services.AddMediatorLoggingBehaviors();
```

```csharp
public sealed record CreateOrder(string ProductId) : IRequest<CreateOrderResponse>;

public sealed record CreateOrderResponse(Guid OrderId);
```

When `CreateOrder` is dispatched, `TenantRequiredBehavior<TRequest, TResponse>`
requires `ICurrentTenant.Id` to contain a non-blank tenant identifier.

## Usage

### Opt Out for Host-Level Requests

Use `[AllowMissingTenant]` only for requests that intentionally run outside tenant
scope, such as public endpoints, system commands, or console-host bootstrap work.

```csharp
[AllowMissingTenant]
public sealed record RebuildSearchIndex : IRequest<RebuildSearchIndexResponse>;

public sealed record RebuildSearchIndexResponse(int DocumentCount);
```

The attribute is evaluated once per closed request generic and cached in a
`static readonly` field. Runtime weaving or dynamic attribute changes are outside
the package contract.

### Register Tenant Context Separately

This package does not resolve tenants by itself. Use `Headless.Api` HTTP tenancy
setup for web applications or register your own `ICurrentTenant` implementation
for workers and console hosts. For package-level wiring without the root surface,
`builder.Services.AddMediatorTenantRequiredBehavior()` remains available.

```csharp
using (currentTenant.Change(tenantId))
{
    await mediator.Send(new CreateOrder(productId), cancellationToken);
}
```

### Pipeline Ordering

Register the tenant-required behavior after authentication/identity context is
available and before idempotency or caching behaviors:

```text
Auth -> TenantRequired -> Idempotency
```

Ordering is the consumer's responsibility. The framework does not validate the
pipeline position.

### Request Validation

`AddMediatorValidationRequestBehavior()` registers a Mediator pre-processor that runs every
registered `IValidator<TMessage>` for the dispatched message. If any validator returns
failures, the pre-processor logs the validation event and throws FluentValidation's
`ValidationException`.

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

Register validators through your normal FluentValidation DI setup. The validation
pipeline is HTTP-agnostic; `Headless.Api` maps `ValidationException` to the standard
422 response when its exception handler is configured.

### Request and Response Logging

`AddMediatorLoggingBehaviors()` is the composite that registers:

- `RequestLoggingBehavior<TMessage, TResponse>` — logs before handler execution
- `ResponseLoggingBehavior<TMessage, TResponse>` — logs after handler execution
- `CriticalRequestLoggingBehavior<TMessage, TResponse>` — logs requests slower than one second

For finer-grained control, the composite is split into two registrations you can opt into
independently:

- `AddMediatorRequestResponseLoggingBehaviors()` — request + response logging only
- `AddMediatorSlowRequestsLoggingBehaviors()` — slow-request logging only

These behaviors use `ICurrentUser` from `Headless.Core` instead of ASP.NET request
context, so they work in API, worker, and console hosts. Register a real
`ICurrentUser` where user identity exists, or `NullCurrentUser` for host-level
background processes.

### Service Lifetime

All five setup extensions accept an optional `ServiceLifetime` parameter that controls the
descriptor lifetime of the registered pipeline behaviors. The default is
`ServiceLifetime.Scoped` — previously, the behaviors were registered as `Transient`.

```csharp
// Scoped (default): one behavior instance per request scope.
builder.Services.AddMediatorTenantRequiredBehavior();

// Opt back into Transient if a behavior must capture no state across the request scope.
builder.Services.AddMediatorTenantRequiredBehavior(ServiceLifetime.Transient);
```

The default change is a behavior shift for consumers that previously assumed transient
semantics — any behavior that captures state during `Handle` and re-runs in the same
scope now sees the prior instance. Mediator dispatches each request within its own
scope, so the change is usually a no-op; reach for `Transient` only if your behavior
depends on a fresh instance per dispatch.

## Failure Behavior

When a request is not marked with `[AllowMissingTenant]` and `ICurrentTenant.Id`
is null, empty, or whitespace, the behavior throws `MissingTenantContextException`.

`Headless.Api` already maps this exception type to the standard tenant-required
400 ProblemDetails response when `UseExceptionHandler()` is configured.

## Configuration

No options are exposed. `[AllowMissingTenant]` is the only opt-out surface.

## Dependencies

- `Headless.Core`
- `Headless.Extensions`
- `Headless.MultiTenancy`
- `FluentValidation`
- `Mediator.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

Registers open-generic `IPipelineBehavior<,>` descriptors when the setup extensions are
called. Descriptor lifetime is `Scoped` by default; pass `ServiceLifetime.Transient` (or
`ServiceLifetime.Singleton`) to override per registration.
