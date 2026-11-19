---
domain: Mediator
packages: Mediator
---

# Mediator

> Validation, request and response logging, and slow-request alerts as pipeline behaviors for the Mediator source-generator library, usable from any host.

## Orientation

`Headless.Mediator` registers pipeline behaviors for the [Mediator](https://github.com/martinothamar/Mediator) source-generator library. It ships two kinds of behavior and no more:

- **Validation.** A pre-processor runs every `IValidator<TMessage>` before the handler and throws `FluentValidation.ValidationException` on any failure.
- **Logging.** Request and response entries at Debug, and a Warning when a handler takes 1 second or longer.

The behavior types are internal. Register them through the `SetupMediator` extensions on `IServiceCollection`. Authentication, tenancy enforcement, idempotency, and HTTP response shaping belong at the HTTP boundary, not inside `mediator.Send()`. See [Put each concern at the right layer](#put-each-concern-at-the-right-layer).

This domain has one package and no provider choice.

## Agent Rules

- Register the behaviors with `services.AddMediatorValidationRequestBehavior()` and `services.AddMediatorLoggingBehaviors()`. Every extension uses `TryAddEnumerable`, so a second call adds no duplicate behavior.
- Register your `IValidator<T>` implementations yourself. The validation pre-processor resolves every `IValidator<TMessage>` from DI and registers none.
- Register `ICurrentUser` before you send any message through the logging behaviors. All of them inject `ICurrentUser` (namespace `Headless.Context`) to log the user ID. In a worker or console host with no real user, register `NullCurrentUser`.
- Keep authentication and authorization out of pipeline behaviors. Use `app.UseAuthentication()`, `app.UseAuthorization()`, and `[Authorize]` or `RequireAuthorization()` on the endpoint. A behavior runs after model binding, so an abusive payload is already deserialized, and it runs on every internal `Send()`, so handler-to-handler calls fail with spurious 401 or 403 errors.
- Keep tenancy enforcement out of pipeline behaviors. Use the `RequireTenant()` endpoint convention or `[RequireTenant]` from `Headless.Api`. On a worker, console, or consumer path, set the tenant explicitly with `using (currentTenant.Change(tenantId))` around `mediator.Send()`.
- Do not write an idempotency pipeline behavior. A behavior sees only the typed response, so it cannot replay the original status code, headers, or body, and it fires on internal `Send()` calls too. Use `Headless.Idempotency.ResponseReplay`: `services.AddHeadlessHttpIdempotency(configuration)` and `app.UseHeadlessHttpIdempotency()`. A handler that must fence its own writes reads `HttpContext.GetHttpIdempotencyContext()` and calls `unit.Idempotency.FenceAsync(context.Admission)` inside its own unit of work.
- Keep HTTP response shaping out of behaviors. Exception handlers, problem-details factories, and response compression belong in `Headless.Api` or ASP.NET Core middleware.
- Put a concern in the pipeline only when it works on the typed message and response with no HTTP semantics: validation, logging, slow-request alerts, typed response transforms, retries of transient handler faults, and instrumentation.
- Expect message and response payloads in logs only in the Development environment. See [Logged fields and payloads](#logged-fields-and-payloads). A validator message that embeds `{PropertyValue}` writes the rejected value into the validation entry in every environment, so keep such messages off sensitive properties.

## Core Concepts

### Behaviors and their contracts

Mediator runs each message through an ordered pipeline of `IPipelineBehavior<TMessage, TResponse>` instances before the handler. Messages implement `IMessage`, or `IRequest<TResponse>` for request-response. Handlers implement `IRequestHandler<TMessage, TResponse>`.

| Behavior | Registered by | Applies to | Runs | Throws or logs |
| --- | --- | --- | --- | --- |
| `ValidationRequestPreProcessor` | `AddMediatorValidationRequestBehavior` | `IMessage` | Before the handler | Throws `FluentValidation.ValidationException` with every failure from every validator. Logs a Debug `Mediator:Validation` entry first. |
| `RequestLoggingBehavior` | `AddMediatorRequestResponseLoggingBehaviors` | `IMessage` | Before the handler | Debug `Mediator:Message` |
| `ResponseLoggingBehavior` | `AddMediatorRequestResponseLoggingBehaviors` | `IRequest<TResponse>` | After the handler returns | Debug `Mediator:Response` |
| `CriticalRequestLoggingBehavior` | `AddMediatorSlowRequestsLoggingBehaviors` | `IRequest<TResponse>` | Around the handler | Warning `Mediator:SlowMessage` when the handler takes 1 second or longer |

`AddMediatorLoggingBehaviors` calls both logging extensions. The validation pre-processor runs validators one at a time, in registration order, because validators often share a scoped `DbContext` that is not safe for concurrent use. When `Headless.Api`'s exception handler is configured, it maps `ValidationException` to a 422 problem-details response.

### Logged fields and payloads

| Event (ID) | Level | Fields outside Development | Added in Development |
| --- | --- | --- | --- |
| `Mediator:Message` (1) | Debug | user ID, message type name | `{@Message}` |
| `Mediator:Response` (2) | Debug | user ID, message and response type names | `{@Message}`, `{@Response}` |
| `Mediator:SlowMessage` (3) | Warning | user ID, elapsed time, message and response type names | none |
| `Mediator:Validation` (4) | Debug | message type name, each failure as `Property: message` | `{@Message}` |
| `Mediator:SlowMessagePayload` (5) | Debug | not written | message and response type names, `{@Message}`, `{@Response}` |

Payloads appear only when `IHostEnvironment.IsDevelopment()` is true. A host with no `IHostEnvironment` registered counts as Production. The 1-second slow-request threshold is fixed. Treat `Mediator:SlowMessage` as an alert for unusually slow handlers, not as a performance monitor.

### Put each concern at the right layer

A cross-cutting concern works on the typed `IRequest<TResponse>` and behaves the same from an API, a queue consumer, a job, or a CLI. A boundary concern depends on the HTTP request, the wire bytes, or the security context set before model binding. A pipeline behavior cannot run before the body is deserialized and cannot see the raw HTTP response, so a boundary concern cannot live there.

| Concern | Where it lives | Why |
| --- | --- | --- |
| Validation | Mediator pre-processor | Works on the typed message. |
| Request and response logging | Mediator behavior | Works on the typed message and response. |
| Slow-request alerts | Mediator behavior | Works on the typed message. |
| Typed response transforms | Mediator behavior | Works on the typed response. |
| Authentication and authorization | ASP.NET Core middleware | Decided before model binding; needs `HttpContext`. |
| Tenant enforcement | ASP.NET Core authorization (`Headless.Api`) | Decided before model binding; needs `HttpContext`. |
| Idempotency replay | `Headless.Idempotency.ResponseReplay` middleware | Replays status, headers, and body bytes; caps the body before binding. |
| Response compression | ASP.NET Core middleware | Transforms bytes after serialization. |
| Problem-details shaping | `Headless.Api` factories and exception handler | Shapes the HTTP response. |

---

## Headless.Mediator

Pipeline behaviors and their `IServiceCollection` registration extensions for a Mediator-based application.

### Setup

```bash
dotnet add package Headless.Mediator
dotnet add package Mediator.SourceGenerator # generates AddMediator into your project
```

```csharp
using Headless.Mediator;

builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
});

builder.Services.AddMediatorValidationRequestBehavior();
builder.Services.AddMediatorLoggingBehaviors();
```

Define a request, its handler, and a validator. The pre-processor picks up the validator once it is registered in DI.

```csharp
using FluentValidation;
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

public sealed class CreateOrderValidator : AbstractValidator<CreateOrder>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
    }
}
```

**Verify:** in a test, `mediator.Send(new CreateOrder(""))` throws `FluentValidation.ValidationException` before the handler runs. With Debug logging on, a valid send writes a `Mediator:Message` entry and a `Mediator:Response` entry.

### Configuration

Every extension takes an optional `ServiceLifetime`, default `Scoped`. A composite call such as `AddMediatorLoggingBehaviors` applies the one lifetime to every behavior it registers. Pass `Transient` or `Singleton` when a behavior must not share state across the request scope.

```csharp
builder.Services.AddMediatorValidationRequestBehavior(ServiceLifetime.Transient);
builder.Services.AddMediatorLoggingBehaviors(ServiceLifetime.Transient);
```

To register only part of the logging set:

```csharp
// Request and response logging, no slow-request alert
builder.Services.AddMediatorRequestResponseLoggingBehaviors();

// Slow-request alert only
builder.Services.AddMediatorSlowRequestsLoggingBehaviors();
```

In a worker or console host with no real user, register `NullCurrentUser` first:

```csharp
using Headless.Context;

builder.Services.AddSingleton<ICurrentUser, NullCurrentUser>();
builder.Services.AddMediatorLoggingBehaviors();
```

On a non-HTTP path, set the tenant around the send:

```csharp
using (currentTenant.Change(tenantId))
{
    await mediator.Send(new CreateOrder(productId), cancellationToken);
}
```

### Design and runtime behavior

- The extensions register open-generic `IPipelineBehavior<,>` descriptors. Calling an extension again adds nothing.
- A behavior that resolves `ICurrentUser` fails at resolution when no `ICurrentUser` is registered.
- The validation pre-processor aggregates the failures of every validator into one `ValidationException`. With no validator registered for a message, it does nothing.
