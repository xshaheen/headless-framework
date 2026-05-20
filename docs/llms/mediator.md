# Mediator boundary doctrine

Guidance for AI agents and humans on what belongs in a Mediator pipeline and what does NOT.

The Headless `Mediator` integration is intentionally narrow. Its job is **handler dispatch with cross-cutting domain concerns**, not request lifecycle. The four concerns commonly suggested as pipeline behaviors that this framework rejects — auth, tenancy enforcement, idempotency, and HTTP-shape transforms — are all boundary concerns. They run before model binding, observe the wire, or shape the response in ways the Mediator abstraction cannot represent.

---

## What goes in the Mediator pipeline

Cross-cuts that operate purely on the typed `IRequest<TResponse>` and produce a `TResponse`:

- **Validation behaviors** — `ValidationRequestPreProcessor<TMessage, TResponse>` runs every registered `IValidator<TMessage>` and throws `ValidationException` on failure. HTTP boundary maps the exception to 422.
- **Request/response logging** — `RequestLoggingBehavior<,>`, `ResponseLoggingBehavior<,>`, `CriticalRequestLoggingBehavior<,>` for slow requests. Backed by `ICurrentUser` so they work in API, worker, and console hosts.
- **Domain-specific behaviors** — caching of typed responses keyed by typed request, retry policies for handler-level transient faults, instrumentation hooks.

Use the canonical setup extensions:

```csharp
builder.Services.AddMediatorValidationRequestBehavior();
builder.Services.AddMediatorLoggingBehaviors();
```

---

## What does NOT go in the Mediator pipeline

### Authentication and authorization

`AuthBehavior` was removed from this framework. Enforcement belongs at the auth middleware boundary, not inside `mediator.Send()`. A Mediator-side auth check:

- runs post-model-bind, so abusive payloads are already deserialized before rejection,
- requires `IHttpContextAccessor` and defeats the abstraction layer that lets the same handlers run from a queue consumer or a console command,
- fires on every internal `Send()`, so handler-to-handler composition produces spurious 401/403 in business code.

Replace with the standard ASP.NET Core auth pipeline: `app.UseAuthentication()` + `app.UseAuthorization()` + endpoint-level `[Authorize]` / `RequireAuthorization()`.

### Tenancy enforcement

`TenantRequiredBehavior` was removed (issue #279). Same boundary argument as auth: the enforcement decision is a property of the HTTP request, not of the typed message.

Replacement surface in `Headless.Api.Core`:

- `[RequireTenant]` attribute and `.RequireTenant()` endpoint convention (see `src/Headless.Api.Core/MultiTenancy/RequireTenantAttribute.cs`).
- `[AllowMissingTenant]` and `.AllowMissingTenant()` for opt-out endpoints.
- `TenantRequirement` for explicit authorization policies.

For non-HTTP paths (workers, message consumers, console), set tenant scope explicitly around the work:

```csharp
using (currentTenant.Change(tenantId))
{
    await mediator.Send(new CreateOrder(productId), cancellationToken);
}
```

### Idempotency

An `IdempotencyBehavior<TRequest, TResponse>` was never added — and will not be — for four structural reasons:

1. **Fires post-model-bind.** The pipeline runs after model binding succeeds. Abusive bodies (oversize JSON, malformed payloads) are fully deserialized before the dedupe check rejects them. The HTTP middleware boundary runs first and short-circuits before the framework spends CPU on the body.
2. **Fires on every internal `Send()`.** Mediator dispatches are not exclusive to HTTP entry points; handlers compose via `Send()`. A pipeline-level idempotency check produces false positives on legitimate handler-to-handler composition (the second `Send()` from inside a handler hits the same cache slot and returns the cached response instead of running).
3. **Cannot cache HTTP status/headers/byte body.** The Mediator abstraction only sees the typed response object. Stripe-style replay requires the exact bytes of the original HTTP response: status code, allowlisted headers, response body. A pipeline behavior cannot produce those — it cannot observe the JSON serializer, the status-code overrides downstream of the handler, or the response headers the framework adds.
4. **Requires `IHttpContextAccessor`.** Reading the `Idempotency-Key` header, deriving the cache key from the request path, and writing the `Idempotent-Replayed: true` header all require `HttpContext`. Pulling that into a Mediator behavior defeats the abstraction.

The HTTP-boundary replacement lives in `Headless.Api.Idempotency` — see [`src/Headless.Api.Idempotency/README.md`](../../src/Headless.Api.Idempotency/README.md). Register with `services.AddIdempotency(o => { ... })` and `app.UseIdempotency()` after auth and tenancy.

### HTTP response shape

Filters, exception handlers, problem-details creators, response-compression middleware — all live in `Headless.Api.Core` or as ASP.NET Core middleware. Do not push them into Mediator pipeline behaviors.

---

## Register-time API

See [`src/Headless.Mediator/README.md`](../../src/Headless.Mediator/README.md) for the full registration surface and the optional `ServiceLifetime` override.

---

## Summary table

| Concern | Where it lives | Why |
| --- | --- | --- |
| Validation | Mediator pre-processor | Operates on typed message; HTTP-agnostic. |
| Request/response logging | Mediator behavior | Operates on typed message; HTTP-agnostic. |
| Response transforms (typed) | Mediator behavior | Operates on typed response; no HTTP semantics. |
| Auth (authn + authz) | ASP.NET Core middleware | Pre-bind decision; needs `HttpContext`. |
| Tenant enforcement | ASP.NET Core authorization | Pre-bind decision; needs `HttpContext`. |
| Idempotency replay | ASP.NET Core middleware (`Headless.Api.Idempotency`) | Caches HTTP status/headers/byte body; pre-bind cap enforcement. |
| Response compression | ASP.NET Core middleware | Post-serializer byte transform. |
| Problem-details shaping | `Headless.Api.Core` factories + exception handler | HTTP-bound response shape. |
