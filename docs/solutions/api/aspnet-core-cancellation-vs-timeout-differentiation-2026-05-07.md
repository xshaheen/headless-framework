---
title: "Differentiating Client-Cancellation, Server-Timeout, and Code-Thrown Timeout in a Single ASP.NET Core IExceptionHandler"
category: api
date: 2026-05-07
module: Headless.Api
problem_type: design_pattern
component: service_class
severity: medium
tags: [asp-net-core, exception-handling, problem-details, cancellation, timeout, iexceptionhandler, request-aborted, status-codes]
related_components:
  - HeadlessApiExceptionHandler
  - IProblemDetailsCreator
  - HeadlessProblemDetailsConstants
  - RequestTimeoutsMiddleware
applies_when:
  - "Building a global IExceptionHandler that must distinguish OperationCanceledException flavors"
  - "Aligning ProblemDetails body shape between handler-emitted and middleware-emitted timeouts"
  - "Choosing between 499 (client closed request), 408 bare, and 408 with body"
  - "Configuring UseDeveloperExceptionPage without bypassing the IExceptionHandler chain"
research:
  agents: [context-analyzer, solution-extractor, related-docs-finder]
  documented_at: 2026-05-07T15:00:00Z
  conversation_context: "Folded RequestCanceledMiddleware into HeadlessApiExceptionHandler; differentiated three OCE flavors; backfilled 408/501 in IProblemDetailsCreator.Normalize"
---

# Context

ASP.NET Core surfaces `OperationCanceledException` (OCE) from at least three structurally different paths, each carrying different semantics. A naive global `IExceptionHandler` that maps "any OCE" to HTTP 499 ("Client Closed Request") is wrong in two of those three cases — it reports a server-side timeout as a client abort, and it swallows library-thrown OCEs whose intent was never client-related. The fix is a single handler that gates on the actual cancellation source (`HttpContext.RequestAborted`), recurses correctly through `AggregateException`, and routes `TimeoutException` separately to a normalized 408 ProblemDetails. Pair this with `Normalize()` backfilling for 408/501 (which ASP.NET Core's default `ClientErrorMapping` does not cover) so bare-status responses from middleware (e.g. `RequestTimeoutsMiddleware`) and factory-built bodies share one wire shape.

# The Three Flavors

| Scenario | Cancellation source | Correct status | Body | Who handles it |
|---|---|---|---|---|
| Client closed connection (TCP disconnect, browser cancel, `AbortController.abort`) | `HttpContext.RequestAborted` fires | 499 Client Closed Request | none — return early | global `IExceptionHandler` (return `true`, no body) |
| Server-side request timeout (`RequestTimeoutsMiddleware`) | The middleware's own CTS fires; `RequestAborted` is NOT signaled | 408 Request Timeout | bare status; body filled by `UseStatusCodePages` + `IProblemDetailsService` (uses `Normalize` 408 backfill) | `RequestTimeoutsMiddleware` sets the status, `UseStatusCodePages` writes the body |
| Code-thrown `TimeoutException` (HTTP client timeout, downstream call, custom timer) | App or library code throws | 408 Request Timeout | full normalized ProblemDetails | global `IExceptionHandler` `case TimeoutException` arm |

The first row is the OCE path. The second is *also* an OCE underneath, but it never escapes `RequestTimeoutsMiddleware` as an OCE — the middleware translates it to a 408 status. The third is not an OCE at all; the framework throws `TimeoutException` so consumers can map it explicitly.

# Pattern

## 1. The OCE arm with `RequestAborted` gate

`src/Headless.Api.Core/Middlewares/HeadlessApiExceptionHandler.cs`:

```csharp
// Cancellation handled first. Only treat OCE as client-cancelled when RequestAborted
// signaled (matches the contract a per-pipeline RequestCanceled middleware would have
// applied). Server-side cancellations and library-thrown OCE fall through to default.
case Exception when _IsCancellationException(exception):
    if (!httpContext.RequestAborted.IsCancellationRequested)
    {
        return false;
    }
    if (httpContext.Response.HasStarted)
    {
        return false;
    }
    httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
    httpContext
        .Features.Get<IHttpActivityFeature>()
        ?.Activity?.AddEvent(new ActivityEvent("Client cancelled the request"));
    _LogRequestCanceled(logger);
    return true;
```

Returning `false` when `RequestAborted` is not set lets the next handler (or the platform default) take over for non-client-aborted OCEs. Returning `false` when the response has already started avoids corrupting the wire.

## 2. The recursive `_IsCancellationException` helper

```csharp
private static bool _IsCancellationException(Exception? ex)
{
    if (ex is null)
    {
        return false;
    }

    if (ex is OperationCanceledException)
    {
        return true;
    }

    if (ex is AggregateException aggregate)
    {
        foreach (var inner in aggregate.InnerExceptions)
        {
            if (_IsCancellationException(inner))
            {
                return true;
            }
        }
        return false;
    }

    return _IsCancellationException(ex.InnerException);
}
```

The `AggregateException` branch iterates `InnerExceptions` (the full collection), not just `.InnerException` (which returns only the first child).

## 3. The `case TimeoutException` arm

```csharp
case TimeoutException:
    _LogRequestTimeoutException(logger, exception);
    problemDetails = problemDetailsCreator.RequestTimeout();
    statusCode = StatusCodes.Status408RequestTimeout;
    break;
```

This routes any code-thrown `TimeoutException` to the 408 ProblemDetails factory — distinct from the OCE-driven paths above.

## 4. `Normalize()` backfilling for 408/501

`src/Headless.Api.Core/Abstractions/IProblemDetailsCreator.cs`:

```csharp
public void Normalize(ProblemDetails problemDetails)
{
    Argument.IsNotNull(problemDetails);

    if (problemDetails.Status.HasValue
        && apiOptionsAccessor.Value.ClientErrorMapping.TryGetValue(
            problemDetails.Status.Value, out var clientErrorData))
    {
        problemDetails.Title ??= clientErrorData.Title;
        problemDetails.Type ??= clientErrorData.Link;
    }

    switch (problemDetails.Status)
    {
        case 500:
            problemDetails.Title = HeadlessProblemDetailsConstants.Titles.InternalError;
            problemDetails.Detail ??= HeadlessProblemDetailsConstants.Details.InternalError;
            break;
        case 404 when !string.Equals(problemDetails.Title,
                HeadlessProblemDetailsConstants.Titles.EntityNotFound, StringComparison.Ordinal):
            problemDetails.Title = HeadlessProblemDetailsConstants.Titles.EndpointNotFound;
            problemDetails.Detail ??= HeadlessProblemDetailsConstants.Details.EndpointNotFound(
                httpContextAccessor.HttpContext?.Request.Path.Value ?? "");
            break;
        // 408 and 501 are not in ASP.NET Core's default ApiBehaviorOptions.ClientErrorMapping,
        // so the lookup above leaves Title and Type null. Backfill from framework constants here.
        case 408:
            problemDetails.Title ??= HeadlessProblemDetailsConstants.Titles.RequestTimeout;
            problemDetails.Type ??= HeadlessProblemDetailsConstants.Types.RequestTimeout;
            problemDetails.Detail ??= HeadlessProblemDetailsConstants.Details.RequestTimeout;
            break;
        case 501:
            problemDetails.Title ??= HeadlessProblemDetailsConstants.Titles.NotImplemented;
            problemDetails.Type ??= HeadlessProblemDetailsConstants.Types.NotImplemented;
            problemDetails.Detail ??= HeadlessProblemDetailsConstants.Details.NotImplemented;
            break;
    }
    // ... extension fields (traceId, buildNumber, commitNumber, timestamp, instance)
}
```

The `??=` is load-bearing: the `IExceptionHandler` factory (`RequestTimeout()`) fills `Title`/`Type`/`Detail` itself, and `Normalize` runs again via `CustomizeProblemDetails`. With `??=`, the second pass leaves factory-supplied values intact. For the bare-408 path (where `RequestTimeoutsMiddleware` sets only the status), all three fields start null and `??=` fills them.

## 5. DI registration via `TryAddEnumerable`

`src/Headless.Api.Core/SetupApiServices.cs`:

```csharp
public static IServiceCollection AddHeadlessProblemDetails(this IServiceCollection services)
{
    services.TryAddSingleton<IProblemDetailsCreator, ProblemDetailsCreator>();

    services.AddProblemDetails(options =>
    {
        options.CustomizeProblemDetails += context =>
        {
            var normalizer = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>();
            normalizer.Normalize(context.ProblemDetails);
        };
    });

    services.TryAddEnumerable(
        ServiceDescriptor.Singleton<IExceptionHandler, HeadlessApiExceptionHandler>());

    return services;
}
```

Why `TryAddEnumerable` and not `AddSingleton` / `AddExceptionHandler<T>`: framework setup methods are commonly called twice (host + sub-host, test fixture composition). Plain `AddSingleton` would register the same handler twice, doubling logging and metrics. `TryAddEnumerable` keys on the implementation type, so duplicate calls collapse to one descriptor.

# Pipeline Composition

Critical for consumers — the global handler only fires if `UseExceptionHandler()` is in the pipeline.

**Wrong** (handler skipped in Dev):

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();  // never runs in Dev/Test
}
```

**Right** (handler runs in all envs; dev page wraps it for unmapped exceptions):

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();   // outer: catches what UseExceptionHandler rethrows
}
app.UseStatusCodePages();              // outer: re-execute on bare statuses set later in pipeline
app.UseExceptionHandler();             // framework handlers run; rethrows on no match
app.UseRequestTimeouts();              // sets bare 408; UseStatusCodePages above fills the body
```

`UseStatusCodePages` must be registered **before** any middleware whose status codes it should observe. Middleware ordering executes top-down on request and bottom-up on response, so for `UseStatusCodePages` to see the bare 408 written by `UseRequestTimeouts`, it has to be the outer of the two. Same reasoning for `UseExceptionHandler` — if the handler itself sets a status with no body, the outer `UseStatusCodePages` re-executes and lets `IProblemDetailsService` fill `Title`/`Type`/`Detail` via the `Normalize` 408 backfill case, producing the same wire shape as the `TimeoutException` path.

# Why ClientErrorMapping isn't enough

ASP.NET Core's `ApiBehaviorOptions.ClientErrorMapping` ships with entries for: 400, 401, 403, 404, 406, 409, 415, 422, 500. **No 408 and no 501.** A `ProblemDetails { Status = 408 }` written by `IProblemDetailsService` therefore has null `Title` and null `Type` until something else fills them.

Putting the 408/501 backfill inside `Normalize()` (rather than via `Configure<ApiBehaviorOptions>(o => o.ClientErrorMapping[408] = ...)`) is preferred because:

- **Single source of truth.** Sits next to the existing 500/404 cases — the same place a maintainer goes when the framework's "what does this status look like" question comes up.
- **No MVC dependency.** `AddHeadlessProblemDetails` doesn't have to add an MVC option configurator, keeping the registration usable from non-MVC hosts (Minimal API, generic host).
- **`Detail` is fillable.** `ClientErrorData` carries only `Title` and `Link` — it cannot supply a default `Detail`. `Normalize` can.

# AggregateException Trap

The naive walker

```csharp
for (var e = ex; e is not null; e = e.InnerException)
    if (e is OperationCanceledException) return true;
```

is wrong because `AggregateException.InnerException` returns only the *first* element of `InnerExceptions`. `Task.WhenAll(t1, t2, t3)` where `t2` cancelled will produce an `AggregateException` whose `InnerException` is `t1`'s exception (or `null`); the OCE is hidden in `InnerExceptions[1]`. Worse, the OCE may itself be wrapped in another non-aggregate exception nested inside a non-first child.

The recursive helper above handles all three cases: direct OCE, aggregate with OCE in any sibling, and OCE nested inside the chain of any sibling.

# Verification

Unit test coverage for `HeadlessApiExceptionHandler`:

- Direct `OperationCanceledException` + `RequestAborted.IsCancellationRequested = true` → status 499, returns `true`, no body, EventId 5002 logged.
- Direct `OperationCanceledException` + `RequestAborted` not signaled → returns `false` (let downstream/default render).
- `Response.HasStarted` is true at handler entry → returns `false` (don't corrupt the wire).
- `AggregateException` with OCE as `InnerExceptions[1]` (not first) + aborted → 499.
- `AggregateException` with OCE nested inside a non-aggregate child at index > 0 + aborted → 499.
- `TimeoutException` → status 408, body is the `RequestTimeout()` factory output, `Title`/`Type`/`Detail` populated from constants.
- Wire-shape equivalence: response from `case TimeoutException` and the bare-status-via-`UseStatusCodePages` path must produce the same `Title`/`Type`/`Detail`/extension shape.

# Pitfalls

1. **"Any OCE → 499."** Wrong for `RequestTimeoutsMiddleware`-cancelled requests (server-side, should be 408) and library-thrown OCEs. Always gate on `HttpContext.RequestAborted.IsCancellationRequested`.
2. **Walking only `.InnerException`.** Misses `AggregateException` siblings beyond index 0. Iterate `InnerExceptions`; recurse.
3. **`UseExceptionHandler` only in non-Dev.** Hides production behavior from developers and from integration tests that build with the Dev environment. Run the global handler in all envs; layer the dev page on top.
4. **Adding 408/501 to `ClientErrorMapping` instead of `Normalize`.** Works, but couples `AddHeadlessProblemDetails` to MVC and can't fill `Detail`. Prefer one source of truth in `Normalize`.
5. **`AddExceptionHandler<T>()` called from a setup method that may run twice.** It uses plain `AddSingleton` and is not idempotent. Use `services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionHandler, T>())` for idempotency.
6. **Simple-name match for EF Core's `DbUpdateConcurrencyException`.** Walks any user-defined type with the same name into the 409 path. Use `Type.FullName` and walk `BaseType` — tangential but the same "looks similar, semantically different" trap.

# Related Documentation

- `docs/llms/api.md` (Exception mapping table) — canonical reference for the framework's status-code contract and the `HeadlessApiExceptionHandler` chain ordering rule.
- `docs/llms/multi-tenancy.md` — describes the `MissingTenantContextException` → 400 path that motivated the unified handler.

# Related GitHub Issues

- [#237](https://github.com/xshaheen/headless-framework/issues/237) — "TenantContextExceptionHandler: map MissingTenantContextException to 400 ProblemDetails with stable code" (the work that became the unified handler).
- [#201](https://github.com/xshaheen/headless-framework/issues/201) — "ProblemDetails 404 response missing 'detail' property in .NET 10" (prior instance of the same `ClientErrorMapping` defaults gap).

# Files Referenced

- `src/Headless.Api.Core/Middlewares/HeadlessApiExceptionHandler.cs` — handler, OCE arm, `_IsCancellationException`, `case TimeoutException`
- `src/Headless.Api.Core/Abstractions/IProblemDetailsCreator.cs` — `Normalize` with 408/501 backfill, `RequestTimeout()` / `NotImplemented()` factories
- `src/Headless.Api.Abstractions/Constants/ProblemDetailTitles.cs` — Types/Titles/Details constants
- `src/Headless.Api.Core/SetupApiServices.cs` — `AddHeadlessProblemDetails` + `TryAddEnumerable` registration
- `demo/Headless.Api.Demo/Program.cs` — pipeline-ordering example
