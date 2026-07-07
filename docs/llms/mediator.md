---
domain: Mediator
packages: Mediator
---

# Mediator

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Pipeline and Behavior Model](#pipeline-and-behavior-model)
    - [Request and Handler Contract](#request-and-handler-contract)
    - [Boundary vs. Cross-Cut Distinction](#boundary-vs-cross-cut-distinction)
- [Headless.Mediator](#headlessmediator)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)

> HTTP-agnostic Mediator pipeline behaviors — validation, logging, and slow-request alerting — without coupling to ASP.NET Core.

## Quick Orientation

`Headless.Mediator` provides `IPipelineBehavior<,>` registrations and setup extensions for the [Mediator](https://github.com/martinothamar/Mediator) source-generator library. The package is intentionally narrow: it ships behaviors for **validation** (`ValidationRequestPreProcessor<TMessage, TResponse>`) and **request/response logging** (`RequestLoggingBehavior`, `ResponseLoggingBehavior`, `CriticalRequestLoggingBehavior`), and no more. Everything else — authentication, tenancy enforcement, idempotency, HTTP response shaping — belongs at the HTTP boundary, not inside `mediator.Send()`.

There is exactly one package in this domain. No provider choice is required.

## Agent Instructions

- **Register behaviors with the canonical setup extensions, not manually:** use `services.AddMediatorValidationRequestBehavior()` and `services.AddMediatorLoggingBehaviors()`. Both extensions are idempotent (`TryAddEnumerable`).
- **Register `IValidator<T>` implementations separately.** The validation pre-processor picks up every `IValidator<TMessage>` from DI; it does not self-register validators.
- **Register `ICurrentUser` before calling any logging behavior extension.** All three logging behaviors inject `ICurrentUser` from `Headless.Core`. For background/worker hosts where no real user exists, register `NullCurrentUser`.
- **Do NOT put authentication or authorization in a pipeline behavior.** Auth belongs at `app.UseAuthentication()` + `app.UseAuthorization()` + endpoint-level `[Authorize]` / `RequireAuthorization()`. A Mediator-side auth check runs after model binding (abusive payloads already deserialized), fires on every internal `Send()` (handler-to-handler composition produces spurious 401/403), and requires `IHttpContextAccessor` (breaks host-agnosticism).
- **Do NOT put tenancy enforcement in a pipeline behavior.** Use `[RequireTenant]` / `.RequireTenant()` endpoint conventions and `TenantRequirement` authorization policy from `Headless.Api.Core`. For worker/console/consumer paths, set tenant scope explicitly with `currentTenant.Change(tenantId)` around the `mediator.Send()` call.
- **Do NOT implement an idempotency pipeline behavior.** The Mediator abstraction only sees the typed response object — it cannot cache the original HTTP status code, response headers, or byte body. It also fires on every internal `Send()` producing false positives on handler composition. Use `Headless.Api.Idempotency` (`services.AddIdempotency()` + `app.UseIdempotency()`) instead.
- **Do NOT push HTTP response shaping into behaviors.** Filters, exception handlers, problem-details factories, and response compression belong in `Headless.Api.Core` or standard ASP.NET Core middleware.
- **What does belong in the pipeline:** FluentValidation pre-processors, request/response logging, slow-request alerts, typed response transforms, domain-specific retry policies for transient handler faults, instrumentation hooks — anything that operates purely on `IRequest<TResponse>` without HTTP semantics.
- **`ServiceLifetime` is `Scoped` by default.** Pass `ServiceLifetime.Transient` or `ServiceLifetime.Singleton` explicitly if a behavior must not share state across the request scope. The same lifetime applies to all behaviors registered by a composite call like `AddMediatorLoggingBehaviors`.
- **`CriticalRequestLoggingBehavior` logs at `Warning`.** The threshold is hard-coded at 1 second. Do not use it as a general performance monitor — it is an alert for unusually slow handlers. The Warning entry carries only the message/response type names, elapsed time, and user ID; the full request/response payloads are emitted in a separate Debug-level entry (`Mediator:SlowMessagePayload`) so credentials or PII inside commands and responses never reach production logs at Warning.

## Core Concepts

### Pipeline and Behavior Model

Mediator dispatches messages through an ordered pipeline of `IPipelineBehavior<TMessage, TResponse>` instances before reaching the handler. Behaviors wrap the handler call; pre-processors (`MessagePreProcessor<,>`) are a convenience that run unconditionally before the next step without needing to call `next.Invoke(...)` explicitly.

`ValidationRequestPreProcessor<TMessage, TResponse>` is a pre-processor: it runs all `IValidator<TMessage>` implementations concurrently via `Task.WhenAll`, collects any `ValidationFailure`s, and throws `FluentValidation.ValidationException` before the handler is invoked. The HTTP boundary maps that exception to a 422 response when `Headless.Api`'s exception handler is configured.

The three logging behaviors are registered by `AddMediatorLoggingBehaviors()`:

| Behavior | Base class | Log level | Logs |
| --- | --- | --- | --- |
| `RequestLoggingBehavior<TMessage, TResponse>` | `MessagePreProcessor<,>` | Debug | Message name + payload before handler |
| `ResponseLoggingBehavior<TMessage, TResponse>` | `MessagePostProcessor<,>` | Debug | Message + response after handler |
| `CriticalRequestLoggingBehavior<TMessage, TResponse>` | `IPipelineBehavior<,>` | Warning (names) + Debug (payload) | Elapsed time + type names at Warning; full payloads in a separate Debug entry when handler takes ≥ 1 second |

All three inject `ICurrentUser` so they include the current user ID in log entries and work identically in API, worker, and console hosts.

### Request and Handler Contract

This package targets the [Mediator](https://github.com/martinothamar/Mediator) source-generator library. Messages implement `IMessage` (or `IRequest<TResponse>` for request-response). Handlers implement `IRequestHandler<TMessage, TResponse>`. The pipeline behavior contract is `IPipelineBehavior<TMessage, TResponse>`.

`ValidationRequestPreProcessor` constrains `TMessage : IMessage`. `ResponseLoggingBehavior` and `CriticalRequestLoggingBehavior` constrain `TMessage : IRequest<TResponse>` (response-bearing messages only).

### Boundary vs. Cross-Cut Distinction

A **cross-cutting concern** operates purely on the typed `IRequest<TResponse>` and produces a `TResponse`. It has no HTTP semantics and works identically from any host: API, queue consumer, scheduled job, or CLI.

A **boundary concern** is a property of the transport layer — the HTTP request, the wire representation, the response bytes, or the security context established before model binding. Boundary concerns must run before the framework deserializes the request body, or they must observe the raw HTTP response. Neither is possible from inside a Mediator pipeline behavior.

The four concerns commonly proposed as pipeline behaviors that this framework rejects:

**Authentication/authorization** — enforcement is a pre-bind decision requiring `HttpContext`. Moving it into the pipeline means abusive payloads are deserialized before rejection, and every internal `Send()` from one handler to another triggers spurious 401/403 responses in business code.

**Tenancy enforcement** — same boundary argument. The enforcement decision is a property of the HTTP request (resolved at the middleware level before routing), not of the typed message. Issue #279 removed `TenantRequiredBehavior` on these grounds.

**Idempotency** — four structural reasons: (1) fires after model binding, so oversized/malformed bodies are already deserialized; (2) fires on every internal `Send()`, producing false positives on handler-to-handler composition; (3) cannot cache HTTP status codes, headers, or byte body — only the typed response object; (4) requires reading `Idempotency-Key` from `HttpContext`.

**HTTP response shape** — serialization format, content negotiation, response compression, and problem-details shaping are post-serializer byte-level transforms. They must observe the HTTP response stream, not the typed response object.

| Concern | Where it lives | Why |
| --- | --- | --- |
| Validation | Mediator pre-processor | Operates on typed message; HTTP-agnostic. |
| Request/response logging | Mediator behavior | Operates on typed message; HTTP-agnostic. |
| Slow-request alerting | Mediator behavior | Operates on typed message; HTTP-agnostic. |
| Typed response transforms | Mediator behavior | Operates on typed response; no HTTP semantics. |
| Authentication/authorization | ASP.NET Core middleware | Pre-bind decision; needs `HttpContext`. |
| Tenant enforcement | ASP.NET Core authorization (`Headless.Api.Core`) | Pre-bind decision; needs `HttpContext`. |
| Idempotency replay | ASP.NET Core middleware (`Headless.Api.Idempotency`) | Caches HTTP status/headers/byte body; pre-bind cap enforcement. |
| Response compression | ASP.NET Core middleware | Post-serializer byte transform. |
| Problem-details shaping | `Headless.Api.Core` factories + exception handler | HTTP-bound response shape. |

---

## Headless.Mediator

### Problem Solved

Adds pipeline behaviors for FluentValidation pre-processing and structured request/response/slow-request logging to any Mediator pipeline. These behaviors are transport-agnostic: the same registrations work in ASP.NET Core API hosts, background workers, message consumers, and console applications.

### Key Features

- `ValidationRequestPreProcessor<TMessage, TResponse>` — runs all registered `IValidator<TMessage>` concurrently before the handler; throws `ValidationException` on any failure.
- `RequestLoggingBehavior<TMessage, TResponse>` — logs the message name and payload at Debug level before handler execution.
- `ResponseLoggingBehavior<TMessage, TResponse>` — logs the message name, payload, and response at Debug level after handler execution.
- `CriticalRequestLoggingBehavior<TMessage, TResponse>` — logs elapsed time and type names at Warning level when a handler takes ≥ 1 second; the full request/response payloads go to a separate Debug-level entry so sensitive data stays out of production logs.
- Idempotent composite setup extensions: `AddMediatorValidationRequestBehavior()` and `AddMediatorLoggingBehaviors()`.
- Fine-grained split: `AddMediatorRequestResponseLoggingBehaviors()` (request + response only) and `AddMediatorSlowRequestsLoggingBehaviors()` (slow-request only).
- Every setup extension accepts an optional `ServiceLifetime` parameter (default `Scoped`).

### Design Notes

**`ICurrentUser` instead of `IHttpContextAccessor`** — all logging behaviors resolve the current user through `ICurrentUser` from `Headless.Core` rather than reading `HttpContext`. This preserves host-agnosticism: the same handler and behavior registrations run identically from a web host and a worker service. Callers that have no real user (background processes) should register `NullCurrentUser`.

**`TryAddEnumerable` for idempotency** — all setup extensions use `TryAddEnumerable` to register the open-generic `IPipelineBehavior<,>` descriptor. Calling the same extension twice does not produce duplicate behaviors in the pipeline.

### Installation

```bash
dotnet add package Headless.Mediator
```

### Quick Start

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

### Configuration

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

For non-HTTP paths where tenant scope must be established explicitly:

```csharp
using (currentTenant.Change(tenantId))
{
    await mediator.Send(new CreateOrder(productId), cancellationToken);
}
```

### Dependencies

- `Headless.Core`
- `Headless.Extensions`
- `FluentValidation`
- `Mediator.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

Registers open-generic `IPipelineBehavior<,>` descriptors when the setup extensions are called. Descriptor lifetime is `Scoped` by default; pass `ServiceLifetime.Transient` or `ServiceLifetime.Singleton` to override per registration. All registrations are idempotent — calling the same extension multiple times does not duplicate behaviors in the pipeline.
