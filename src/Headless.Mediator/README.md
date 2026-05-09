# Headless.Mediator

HTTP-agnostic Mediator behaviors for Headless applications.

## Problem Solved

Provides a tenant-required request boundary for Mediator pipelines so API, worker, console,
and messaging dispatch surfaces can share the same ambient tenant invariant.

## Key Features

- `TenantRequiredBehavior<TRequest, TResponse>` enforces `ICurrentTenant.Id`
- `[AllowMissingTenant]` opt-out marker for host-level and public requests
- `MissingTenantContextException` reuse for existing HTTP 400 failure mapping
- Idempotent `AddTenantRequiredBehavior()` registration

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

builder.Services.AddTenantRequiredBehavior();
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

This package does not register `ICurrentTenant`. Use `Headless.Api` multi-tenancy
setup for HTTP applications or register your own `ICurrentTenant` implementation
for workers and console hosts.

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

## Failure Behavior

When a request is not marked with `[AllowMissingTenant]` and `ICurrentTenant.Id`
is null, empty, or whitespace, the behavior throws `MissingTenantContextException`
and sets:

```csharp
exception.Data["Headless.Mediator.FailureCode"] = "MissingTenantContext";
```

`Headless.Api` already maps this exception type to the standard tenant-required
400 ProblemDetails response when `UseExceptionHandler()` is configured.

## Configuration

No options are exposed. `[AllowMissingTenant]` is the only opt-out surface.

## Dependencies

- `Headless.Core`
- `Mediator.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

## Side Effects

Registers one transient open-generic `IPipelineBehavior<,>` descriptor when
`AddTenantRequiredBehavior()` is called.
