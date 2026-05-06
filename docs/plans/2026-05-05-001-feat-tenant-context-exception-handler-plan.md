---
title: "feat: TenantContextExceptionHandler maps MissingTenantContextException to 400 ProblemDetails"
type: feat
status: active
date: 2026-05-05
origin: https://github.com/xshaheen/headless-framework/issues/237
---

# feat: TenantContextExceptionHandler maps MissingTenantContextException to 400 ProblemDetails

## Overview

Ship an ASP.NET Core `IExceptionHandler` in the `Headless.Api` package that maps the cross-layer `MissingTenantContextException` to a normalized 400 `ProblemDetails`. The `ProblemDetails` is built by a new factory method on `IProblemDetailsCreator` (matching the existing `EntityNotFound` / `Conflict` / `Forbidden` shape), so any caller with the creator injected can produce the same response without going through the handler. The handler integrates with the existing `AddHeadlessProblemDetails` pipeline so `traceId`, `buildNumber`, `commitNumber`, `timestamp`, and `Instance` extensions are injected automatically. The type-URL prefix and error code are configurable so consumers can map to their own error-doc namespace.

This is the missing HTTP-side half of the Strict-Tenancy work: zad-ngo already shipped a local copy (`xshaheen/zad-ngo#152` / FOUND-02 / U4). Promoting it to the framework removes a hand-rolled handler from every consumer.

## Problem Frame

Headless ships `AddHeadlessProblemDetails` (`Headless.Api`) and an `IProblemDetailsCreator` that normalizes responses with `traceId`, `buildNumber`, `commitNumber`, `timestamp`, and `Instance`. Headless also ships `MissingTenantContextException` (`Headless.Core.Abstractions`) as a shared cross-layer guard. What's missing is the bridge: a standard exception handler that consumers register once to map the typed exception to the framework's normalized 400 response, plus a factory method on the creator so the same response shape is reachable without the handler when needed (e.g., from a request-pipeline pre-check that wants to return `ProblemDetails` directly).

Each consumer rewriting this mapping locally produces drift — different type URLs, different titles, sometimes-missing `Response.HasStarted` guards, and occasionally bypassed normalization. One framework-owned factory + handler with an options surface fixes all of that.

## Requirements

- R1. `IProblemDetailsCreator` gains a parameterless `TenantRequired()` factory method that builds a 400 `ProblemDetails` with the canonical title (`HeadlessProblemDetailsConstants.Titles.TenantContextRequired`), the framework-owned detail message, and the framework-owned `Extensions["code"] = HeadlessProblemDetailsConstants.Codes.TenantContextRequired`, then runs `Normalize` to inject `traceId`/`buildNumber`/`commitNumber`/`timestamp`/`Instance`. The `Type` URL is filled by `Normalize` from `ApiBehaviorOptions.ClientErrorMapping[400]` — the same standard 400 RFC URL the rest of the framework uses (matching `Conflict`, `EntityNotFound`, `MalformedSyntax`).
- R2. ~~No options class~~ The framework owns the response shape end-to-end. `TypeUriPrefix` and `ErrorCode` are not configurable per-app — consumers branch on the stable `code` extension (`HeadlessProblemDetailsConstants.Codes.TenantContextRequired`), matching how `Forbidden`/`Conflict`/`EntityNotFound` work today. Consumers who need a different response shape write their own factory.
- R3. `TenantContextExceptionHandler` implements `IExceptionHandler`. Returns `false` for any exception other than `MissingTenantContextException`. For matches: delegates to `IProblemDetailsCreator.TenantRequired()`, sets `HttpContext.Response.StatusCode = 400`, calls `IProblemDetailsService.TryWriteAsync` so consumer customizations layer on top, and falls back to `WriteAsJsonAsync` only when the service returns `false` AND `Response.HasStarted == false`.
- R4. The handler is registered automatically by `AddHeadlessProblemDetails()` (called by `AddHeadless()`). Idempotent registration via `TryAddEnumerable`. No standalone helper.
- R5. Default `Title` is kebab-case (`"tenant-context-required"`), centralized in `HeadlessProblemDetailsConstants.Titles.TenantContextRequired`. Default `Code` is `"tenancy.tenant-required"`, centralized in `HeadlessProblemDetailsConstants.Codes.TenantContextRequired`.
- R6. Integration test proves: throwing `MissingTenantContextException` from a request handler yields a 400 with stable `code`, `title`; `traceId` extension is present (proves normalization ran via `IProblemDetailsService`).
- R7. Documentation surfaces are updated in lockstep: `docs/llms/api.md`, `docs/llms/multi-tenancy.md`, `src/Headless.Api/README.md`. Each surface documents the no-entity-leakage choice so consumers understand the response shape is intentional.

## Scope Boundaries

- The handler maps **only** `MissingTenantContextException`. It does not catch `InvalidOperationException`, `ArgumentException`, or any layer-specific subtype. Other exceptions remain the responsibility of the existing MVC/MinimalApi exception filters or downstream handlers.
- `MissingTenantContextException` is **not modified**. No new properties, no new constructors. The exception's existing `Exception.Data` dictionary remains the channel for layer-specific tags (e.g., `Headless.Messaging.FailureCode = "MissingTenantContext"`), and that data is **not** copied to the HTTP response — it is for server-side log aggregation only.
- The HTTP response surfaces only `code` (stable client-routing identifier) plus the standard normalized extensions. **No entity name, message body, or layer tag is exposed** to API clients. Server-side debugging context belongs in logs, not the response body.
- No new package or `.csproj` is created. The new code lives in a `MultiTenancy/` sub-folder of `Headless.Api`, namespace `Headless.Api.MultiTenancy` — consistent with the existing `Headless.Api/MultiTenancySetup.cs` precedent.
- No correlation-id propagation work. `IProblemDetailsCreator.Normalize` only injects `traceId` today; a correlation-id extension is a separate concern.

### Deferred to Separate Tasks

- **NSwag schema for `code` extension on `BadRequestProblemDetails`**: Tracked separately on the OpenAPI/NSwag side per the issue's "Notes" section. No framework change required by this plan.
- **Auto-registration via `AddHeadlessProblemDetails`**: The issue prescribes a separate `AddTenantContextProblemDetails` call. Keep the registrations independent so consumers can opt in.

## Context & Research

### Relevant Code and Patterns

- `src/Headless.Api/Setup.cs:206-220` — `AddHeadlessProblemDetails` wires `IProblemDetailsCreator.Normalize` into `ProblemDetailsOptions.CustomizeProblemDetails`. This is what makes `IProblemDetailsService.TryWriteAsync` produce normalized responses.
- `src/Headless.Api/Abstractions/IProblemDetailsCreator.cs` — Public `Normalize(ProblemDetails)` method. Adds `traceId`, `buildNumber`, `commitNumber`, `timestamp`, `Instance`. The new `TenantRequired(...)` factory follows the existing `EntityNotFound` / `Conflict` / `Forbidden` pattern: build `ProblemDetails`, call `_Normalize`, return.
- `src/Headless.Api/MultiTenancySetup.cs` — Closest registration precedent in the same package: `AddHeadlessMultiTenancy` on `IHostApplicationBuilder`, options + inline `internal sealed class` validator below the options class. Mirrors the convention required by project `CLAUDE.md`.
- `src/Headless.Caching.Redis/Setup.cs:13-74` — Canonical 3-overload registration pattern: C# 14 `extension(IServiceCollection services)` block, three overloads (`Action<TOption, IServiceProvider>`, `IConfiguration`, `Action<TOption>`) each delegating to `services.Configure<TOption, TValidator>(...)` then a private core helper.
- `src/Headless.Api.Abstractions/Constants/ProblemDetailTitles.cs` — `HeadlessProblemDetailsConstants` with nested `Titles` (kebab-case) / `Details` (sentence-case messages). New `Titles.TenantContextRequired` and `Details.TenantContextRequired` constants extend the existing nested classes.
- `src/Headless.Api.MinimalApi/Filters/MinimalApiExceptionFilter.cs` — Closest exception-to-ProblemDetails idiom in the framework today. Source-generated `LoggerMessage` with explicit `EventId` (5003/5004). The new handler should follow the same logging pattern.
- `tests/Headless.Api.Tests.Integration/TenantResolutionMiddlewareTests.cs` — Integration test pattern using inline `WebApplication.CreateBuilder` per test. Lighter than `ProblemDetailsTests.cs`'s shared `Program` factory and a better fit for a focused exception-handler test.
- `src/Headless.Core/Abstractions/MissingTenantContextException.cs` — The exception is `sealed : Exception` (deliberately not `InvalidOperationException`). The plan does **not** modify it; the handler keys off the type alone and surfaces no exception-internal data to the response.
- `src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs:170` — Existing call site that throws the exception and stamps `Exception.Data["Headless.Messaging.FailureCode"] = "MissingTenantContext"`. The data tag stays on the exception for server-side logging; it is not surfaced in the HTTP response.

### Institutional Learnings

- `docs/plans/2026-05-03-002-feat-messaging-phase1-foundations-plan.md` — Defines the cross-layer contract for `MissingTenantContextException` and explicitly names #237 as the HTTP `ProblemDetails` sibling. Confirms the exception is shared (HTTP, EF write guard, mediator behavior, messaging publish) so the handler must catch the base type, not a layer-specific subclass.
- `docs/plans/2026-05-01-001-feat-tenant-id-envelope-plan.md:57,302,354` — Records the issue map and reinforces "no exception-detail leakage to HTTP responses" — the framework-owned remediation message is keyed off the exception's stable surface, not interpolated stack data. The no-entity-leakage decision in this plan extends that posture.
- `docs/llms/multi-tenancy.md` (Strict Publish Tenancy section) — Already documents the `Headless.Messaging.FailureCode = "MissingTenantContext"` data key. The new HTTP failure-mapping section should cross-reference it without duplicating, and call out that the data tag is server-side only.

### External References

External research was not needed: the codebase has strong local patterns for exception filters (MVC/MinimalApi), options validation (`AddOptions<TOption, TValidator>` from `Headless.Hosting`), 3-overload extension registration (`RedisCacheSetup`), and integration tests (`TenantResolutionMiddlewareTests`). `IExceptionHandler` is a standard ASP.NET Core 8+ surface; no new pattern discovery required.

## Key Technical Decisions

- **Factory method on `IProblemDetailsCreator`, not a standalone handler shape.** The handler delegates to `creator.TenantRequired()`. Rationale: matches existing `EntityNotFound` / `Conflict` / `Forbidden` factory shape; any caller with the creator injected can produce the same response without going through the global exception pipeline; keeps the canonical ProblemDetails-construction logic in one class.
- **No entity name in the response.** The original issue proposed an `entityType` extension gated by an `ExposeEntityType` option. **Dropped** — exposing CLR entity type names to API clients is information disclosure: external callers can enumerate the persistence model from error responses. The `code` extension (`tenancy.tenant-required`) is sufficient for client-side routing; entity names belong in server logs (via `Exception.Data` tags), not HTTP bodies. If a downstream consumer needs entity context for error tracking, they catch and log on the throwing side.
- **`MissingTenantContextException` is not modified.** No new `EntityType` property, no new constructor. Exception identity is "tenancy guard fired"; per-layer context is in `Exception.Data` (server-only).
- **No options class.** The original design had `TenantContextProblemDetailsOptions { TypeUriPrefix, ErrorCode }`. **Dropped** — `Forbidden`, `Conflict`, `EntityNotFound`, etc. are not configurable per-app, and tenancy joins that pattern. Consumers branch on the stable `code` extension; if a consumer needs a different response shape, they write their own factory and skip the helper. The framework owns one canonical shape per error class.
- **`Title` and `Code` are centralized constants, not configurable.** They live in `HeadlessProblemDetailsConstants.Titles.TenantContextRequired` and `HeadlessProblemDetailsConstants.Codes.TenantContextRequired`.
- **`Type` URL uses the standard 400 RFC URL.** Filled by `Normalize` from `ApiBehaviorOptions.ClientErrorMapping[400].Link` — same as `Conflict`, `MalformedSyntax`, `EntityNotFound`. Differentiation between error classes is done via the `code` extension, not the `type` URL.
- **Sub-namespace, not sub-project.** New code lives at `src/Headless.Api/MultiTenancy/` with namespace `Headless.Api.MultiTenancy`. Splitting into a `Headless.Api.MultiTenancy` package would fight the existing `MultiTenancySetup.cs` placement and add a NuGet release for one handler. Rationale: minimum diff, consistent with how tenancy already lives.
- **Use `IProblemDetailsService.TryWriteAsync` (ASP.NET Core's built-in service), not a direct write.** This is the only way `ProblemDetailsOptions.CustomizeProblemDetails` runs (so consumer customizations layer on top of `Normalize`). The fallback `WriteAsJsonAsync` path runs only when the service returns `false` AND `Response.HasStarted == false`. Because the factory already calls `Normalize` before returning, the fallback path does not need to re-call it — the `ProblemDetails` is already normalized.
- **Logging.** Use source-generated `LoggerMessage` (per project `.NET` rules). `EventId` allocated in the existing `Headless.Api` 5xxx range — picking `5005` to avoid collision with `5003/5004` already used in `MinimalApiExceptionFilter`. Log level `Warning` (operator-actionable but expected per consumer error). Structured properties: `ErrorCode` (the stable framework constant), so log dashboards can group by error class.

## Open Questions

### Resolved During Planning

- **Should the response surface entity context?** Resolved: **no**. Exposing CLR entity names to API clients is information disclosure; the `code` extension is sufficient for client-side routing. Server-side debugging context lives in logs, not the response. User-confirmed during brainstorm (2026-05-04).
- **Standalone handler vs. extend `IProblemDetailsCreator`?** Resolved: extend `IProblemDetailsCreator` with a `TenantRequired(...)` factory method. Handler delegates to the factory. Matches existing `EntityNotFound` / `Conflict` / `Forbidden` shape. User-confirmed during brainstorm.
- **Should `Title` be configurable per-options?** Resolved: no. Title is a centralized constant in `HeadlessProblemDetailsConstants.Titles`, matching every other framework-shipped ProblemDetails. Consumers who need a different title write their own factory.
- **Sub-namespace vs. sub-project?** Resolved: sub-namespace inside `Headless.Api`. Matches `MultiTenancySetup.cs`. No new csproj.
- **Auto-include in `AddHeadlessProblemDetails`?** Resolved: keep separate. The issue explicitly proposes a distinct `AddTenantContextProblemDetails` call so consumers opt in.

### Deferred to Implementation

- **Exact registration site for `IExceptionHandler`.** ASP.NET Core requires `AddExceptionHandler<T>()` to register the handler, plus `UseExceptionHandler()` middleware in the pipeline. The new helper registers the handler; consumers still need to call `app.UseExceptionHandler()` themselves (this is documented). Decide during implementation whether the README example pairs both calls explicitly.
- **Should the handler also catch derived `MissingTenantContextException` types?** The exception is `sealed` today, so this is moot — but if it's ever unsealed, the handler's `if (exception is MissingTenantContextException ex)` pattern automatically picks up subclasses.

## Implementation Units

- U1. **Add tenancy constants to `HeadlessProblemDetailsConstants`**

**Goal:** Expose stable defaults that the factory and tests reference.

**Requirements:** R5

**Dependencies:** None.

**Files:**
- Modify: `src/Headless.Api.Abstractions/Constants/ProblemDetailTitles.cs`

**Approach:**
- Add `TenantContextRequired = "tenant-context-required"` to the nested `Titles` class.
- Add a sentence-case detail message to `Details` (e.g., `TenantContextRequired = "An operation required an ambient tenant context but none was set."`). Used by the factory as the response `Detail`. The exception's own `Message` is not copied to the response — it is developer-facing remediation text and risks leaking implementation context.
- Maintain alphabetical ordering inside each nested class where consistent with the existing file.

**Patterns to follow:**
- Existing entries in `HeadlessProblemDetailsConstants` (kebab-case titles, sentence-case Details).

**Test scenarios:**
- Test expectation: none — pure constants additions, exercised indirectly by U5 integration tests which assert on the title and detail values.

**Verification:**
- Two new constants exist; existing constants are unchanged; project builds with no new analyzer warnings.

---

- U2. **Extend `IProblemDetailsCreator` with `TenantRequired` factory**

**Goal:** Add the canonical `TenantRequired` factory method to the interface and implement it on `ProblemDetailsCreator`, so any caller with the creator can produce a normalized 400 ProblemDetails for the tenancy case.

**Requirements:** R1, R5

**Dependencies:** U1 (uses `HeadlessProblemDetailsConstants` for title and detail).

**Files:**
- Modify: `src/Headless.Api/Abstractions/IProblemDetailsCreator.cs`
- Test: `tests/Headless.Api.Tests.Unit/Abstractions/ProblemDetailsCreatorTests.cs` (TenantRequired branch)

**Approach:**
- Add to the interface: `ProblemDetails TenantRequired();`. No parameters — title, code, and detail come from `HeadlessProblemDetailsConstants`; the type URL is filled by `Normalize` from `ApiBehaviorOptions.ClientErrorMapping[400]`.
- Add to the concrete `ProblemDetailsCreator`:
  ```csharp
  public ProblemDetails TenantRequired(string typeUriPrefix, string errorCode)
  {
      Argument.IsNotNullOrWhiteSpace(typeUriPrefix);
      Argument.IsNotNullOrWhiteSpace(errorCode);

      var problemDetails = new ProblemDetails
      {
          Status = StatusCodes.Status400BadRequest,
          Title = HeadlessProblemDetailsConstants.Titles.TenantContextRequired,
          Detail = HeadlessProblemDetailsConstants.Details.TenantContextRequired,
          Type = $"{typeUriPrefix.TrimEnd('/')}/tenant-required",
          Extensions = { ["code"] = errorCode },
      };

      _Normalize(problemDetails);

      return problemDetails;
  }
  ```
- Use `Headless.Checks.Argument.*` for validation per `CLAUDE.md` (`ArgumentNullException.ThrowIfNull` is forbidden).
- The method joins the existing factory ordering (placement: alphabetical within the existing methods, immediately before `TooManyRequests` if alphabetical, or grouped with the 4xx factories).

**Patterns to follow:**
- Sibling factory methods in `ProblemDetailsCreator` (`EntityNotFound`, `Conflict`, `Forbidden`, `MalformedSyntax`) — same shape: build, `_Normalize`, return.
- `Headless.Checks.Argument` validation per project convention.

**XML documentation requirements (R7 prerequisite):**
- Document `IProblemDetailsCreator.TenantRequired(string typeUriPrefix, string errorCode)` with: summary describing the 400-mapping behavior, `<param>` entries explaining what each parameter contributes to the response, `<returns>` noting that the returned `ProblemDetails` has already been normalized (so callers do not need to call `Normalize` again), and a `<remarks>` callout that this is the canonical factory for the tenancy case — direct callers should prefer it over hand-building a `ProblemDetails`.

**Test scenarios:**
- Happy path: `creator.TenantRequired("https://errors.example.com/tenancy", "tenancy.tenant-required")` returns `ProblemDetails` with `Status == 400`, `Title == "tenant-context-required"`, `Type == "https://errors.example.com/tenancy/tenant-required"`, `Extensions["code"] == "tenancy.tenant-required"`, `Detail` matches the constant, plus `traceId`/`buildNumber`/`commitNumber`/`timestamp`/`Instance` from `_Normalize`.
- Edge case: prefix with trailing slash (`"https://example.com/errors/"`) — final `Type` has single slash before `tenant-required`.
- Edge case: empty / whitespace `typeUriPrefix` throws `ArgumentException` (via `Argument.IsNotNullOrWhiteSpace`).
- Edge case: empty / whitespace `errorCode` throws.

**Verification:**
- Interface exposes the new method; concrete implements it; existing methods untouched; tests pass.

---

- U3. **~~Options class~~ — removed during implementation**

The originally-planned `TenantContextProblemDetailsOptions` was dropped. Rationale: the rest of the framework's ProblemDetails factories (`Forbidden`, `Conflict`, `EntityNotFound`, `MalformedSyntax`) are not configurable per-app — title, type, and detail are framework-owned constants. Tenancy joins that pattern. Consumers branch on the stable `code` extension (`HeadlessProblemDetailsConstants.Codes.TenantContextRequired`); if they need a different shape they write their own factory and skip the helper.

The two values that were going to be configurable are now framework constants:
- `HeadlessProblemDetailsConstants.Titles.TenantContextRequired = "tenant-context-required"` (added in U1)
- `HeadlessProblemDetailsConstants.Codes.TenantContextRequired = "tenancy.tenant-required"` (added alongside U1)
- The `Type` URL is the standard 400 RFC URL, populated by `Normalize` from `ApiBehaviorOptions.ClientErrorMapping[400]` — no per-app override.

R2 was rewritten to reflect this.

---

- U4. **`TenantContextExceptionHandler`**

**Goal:** Map `MissingTenantContextException` to the normalized 400 ProblemDetails response by delegating to `IProblemDetailsCreator.TenantRequired(...)`.

**Requirements:** R3, R6

**Dependencies:** U2 (factory).

**Files:**
- Create: `src/Headless.Api/MultiTenancy/TenantContextExceptionHandler.cs`
- Test: `tests/Headless.Api.Tests.Unit/MultiTenancy/TenantContextExceptionHandlerTests.cs` (fallback-path branch coverage)

**Approach:**
- Namespace `Headless.Api.MultiTenancy`. `internal sealed class` (consumers register via the public `AddTenantContextProblemDetails` extension; the type itself does not need to be public).
- Implements `Microsoft.AspNetCore.Diagnostics.IExceptionHandler`.
- Primary constructor injects `IOptions<JsonOptions> jsonOptions`, `IProblemDetailsService problemDetailsService`, `IProblemDetailsCreator problemDetailsCreator`, `ILogger<TenantContextExceptionHandler> logger`. No `IOptions<TenantContextProblemDetailsOptions>` — the response shape is framework-owned constants.
- `TryHandleAsync(httpContext, exception, cancellationToken)`:
  - If `exception is not MissingTenantContextException`, return `false`.
  - Delegate: `var problemDetails = problemDetailsCreator.TenantRequired();`. The factory already calls `Normalize`.
  - Set `httpContext.Response.StatusCode = StatusCodes.Status400BadRequest` before the write attempt.
  - Try `await problemDetailsService.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problemDetails })`. If it returns `true`, log at `Warning` (source-generated) and return `true`.
  - Fallback: only if `!httpContext.Response.HasStarted`, write directly: `await httpContext.Response.WriteAsJsonAsync(problemDetails, jsonOptions.Value.SerializerOptions, contentType: "application/problem+json", cancellationToken)`. Log and return `true`. (No need to re-call `Normalize` — the factory already did.)
  - If `Response.HasStarted == true` and `TryWriteAsync` failed, log at `Error` (`EventId 5006`) and return `false` (cannot safely write).

**Technical design (directional):**

```
TryHandleAsync(ctx, exception, ct):
  if exception is not MissingTenantContextException: return false
  pd = creator.TenantRequired()  # already normalized; uses framework-owned title/code constants
  ctx.Response.StatusCode = 400
  if await pds.TryWriteAsync({ ctx, pd }): log Warning(5005); return true
  if ctx.Response.HasStarted: log Error(5006); return false
  await ctx.Response.WriteAsJsonAsync(pd, contentType: "application/problem+json", ct)
  log Warning(5005); return true
```

**Patterns to follow:**
- `src/Headless.Api.MinimalApi/Filters/MinimalApiExceptionFilter.cs` — `LoggerMessage` source generation, EventId allocation in 5xxx range. Use `EventId = 5005` to avoid collision (5003/5004 used by MinimalApiExceptionFilter).
- Primary-constructor DI style consistent with the rest of `Headless.Api`.

**Logging design:**
- Define two `LoggerMessage` source-generated entries:
  - `EventId = 5005`, level `Warning`: emitted on successful handling. Include `ErrorCode` as a structured-log property. If `exception.Data` carries a layer tag (e.g., `Headless.Messaging.FailureCode`), include it too — it stays in logs only, never the response.
  - `EventId = 5006`, level `Error`: emitted only when `IProblemDetailsService.TryWriteAsync` returns `false` AND `Response.HasStarted == true` (the unrecoverable branch where the handler returns `false` without writing). Indicates the response was already in flight and could not be mapped — operators need this signal because the client receives whatever the upstream code had partially produced.
- **EventId allocation note:** the framework does not maintain a centralized `EventId` registry. Each `Headless.Api*` package allocates its own range (e.g., `Headless.Api.MinimalApi` uses 5003/5004 in `MinimalApiExceptionFilter`; `Headless.Api.DataProtection` uses 1-8). 5005/5006 continue the `Headless.Api.MinimalApi` range without collision. If a future contributor adds another exception handler in `Headless.Api`, allocate the next free EventId in this range and grep `EventId = 5` to verify no collision before merging.

**Test scenarios:**
- Happy path: `MissingTenantContextException` thrown → `TryHandleAsync` returns `true`, response status is 400, `pd.Status == 400`, `pd.Title == HeadlessProblemDetailsConstants.Titles.TenantContextRequired`, `pd.Extensions["code"] == HeadlessProblemDetailsConstants.Codes.TenantContextRequired`. (Integration coverage in U6 verifies the full pipeline including normalization.)
- Edge case: any exception other than `MissingTenantContextException` → `TryHandleAsync` returns `false` and writes nothing.
- Error path: `IProblemDetailsService.TryWriteAsync` returns `false` AND `Response.HasStarted == false` → fallback `WriteAsJsonAsync` runs with `application/problem+json`. Use NSubstitute to mock `IProblemDetailsService` returning `false`; verify response body and content type.
- Error path: `IProblemDetailsService.TryWriteAsync` returns `false` AND `Response.HasStarted == true` → handler returns `false` (no write attempted, no exception thrown). Verify the `EventId = 5006` `Error` log entry was emitted so operators have the signal.
- Error path: handler emits the `EventId = 5005` `Warning` log entry on the happy path with structured `ErrorCode` property present (verify with a test logger).
- Negative assertion: response body never contains an `entityType` extension, an entity CLR name, the exception message, the exception's `InnerException` data, or any value from `exception.Data` — only `code` plus the standard normalized extensions. This is the no-information-disclosure invariant. Cover with a test that throws `new MissingTenantContextException("outer", new InvalidOperationException("inner sensitive detail"))` and asserts the inner message does not appear anywhere in the response body.

**Verification:**
- Throwing `MissingTenantContextException` from a request handler produces a 400 with the expected ProblemDetails shape and a `traceId` extension.
- Other exceptions are passed through unchanged.
- No exception-internal data leaks into the response.

---

- U5. **~~Standalone registration helper~~ — replaced with auto-registration in `AddHeadlessProblemDetails()`**

The originally-planned `AddTenantContextProblemDetails()` extension was dropped. The handler is now registered automatically by `AddHeadlessProblemDetails()` (and therefore by `AddHeadless()`, which calls it). Rationale: consumers who already use the framework's ProblemDetails infrastructure get the tenancy mapping for free; the handler depends only on services that `AddHeadlessProblemDetails()` already registers (`IProblemDetailsCreator`, `IProblemDetailsService`); no per-app configuration exists, so an opt-in helper added friction without value.

**Files:**
- Modify: `src/Headless.Api/Setup.cs` — `AddHeadlessProblemDetails()` adds `services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionHandler, TenantContextExceptionHandler>())`.

**Approach:**
- Use `TryAddEnumerable` directly. ASP.NET Core's `AddExceptionHandler<T>()` uses plain `AddSingleton` which is not idempotent — `TryAddEnumerable` collapses duplicate registrations to a single descriptor.

**Patterns to follow:**
- `src/Headless.Caching.Redis/Setup.cs:13-74` — exact 3-overload shape with private core helper and `services.Configure<TOption, TValidator>(...)` calls.
- `Headless.Hosting.OptionsServiceCollectionExtensions.Configure<TOption, TValidator>(...)` — the typed `Configure` overload with FluentValidation + `ValidateOnStart()`.

**XML documentation requirements (R7 prerequisite):**
- The `AddTenantContextProblemDetails()` extension carries an XML `<summary>` plus a `<remarks>` block stating two prerequisites:
  1. `services.AddHeadlessProblemDetails()` must also be registered (DI will throw `Unable to resolve service for type 'IProblemDetailsCreator'` at handler construction otherwise — the message is sufficient, no custom check is added).
  2. The consumer must call `app.UseExceptionHandler()` themselves; this helper only registers the handler in the chain.
- Document handler-chain ordering: ASP.NET Core invokes `IExceptionHandler` instances in registration order. Consumers with multiple handlers should register `AddTenantContextProblemDetails()` **before** any catch-all handler that returns `true` for every exception, otherwise the catch-all will swallow `MissingTenantContextException` first and the tenancy mapping will never run. Mirror this guidance in `docs/llms/api.md` (U7).
- Document that the framework's MVC and Minimal-API exception filters already map `MissingTenantContextException` for endpoint code; this handler is primarily a safety net for non-endpoint paths (middleware, hosted services, hubs).

**Test scenarios:**
- Happy path: `services.AddTenantContextProblemDetails()` registers the handler and resolves it from DI.
- Edge case: calling `AddTenantContextProblemDetails()` twice does not register the handler twice (no duplicate handler invocations).

**Verification:**
- The single overload compiles, registers the handler, and the handler is invoked end-to-end for a request that throws `MissingTenantContextException`.

---

- U6. **Integration tests**

**Goal:** Prove the full pipeline: request handler throws → ASP.NET Core `UseExceptionHandler` routes to `TenantContextExceptionHandler` → 400 ProblemDetails with normalized extensions reaches the client.

**Requirements:** R3, R6

**Dependencies:** U1, U2, U3, U4, U5.

**Files:**
- Create: `tests/Headless.Api.Tests.Integration/MultiTenancy/TenantContextExceptionHandlerTests.cs`

**Approach:**
- Use the inline `WebApplication.CreateBuilder` per-test pattern from `tests/Headless.Api.Tests.Integration/TenantResolutionMiddlewareTests.cs` — simpler than the `Program`-coupled `CustomWebApplicationFactory` and self-contained.
- Each test builds a minimal app with `AddHeadlessProblemDetails()` + `AddTenantContextProblemDetails(...)`, maps a single endpoint that throws the exception, calls `UseExceptionHandler()`, and asserts on the JSON response.
- Parse responses with `JsonDocument` (or AwesomeAssertions' JSON support) and assert on `type`, `title`, `status`, `extensions["code"]`, `extensions["traceId"]`, and **negative**: the absence of any `entityType` / message / data-tag fields.

**Patterns to follow:**
- `tests/Headless.Api.Tests.Integration/TenantResolutionMiddlewareTests.cs` — inline app builder, `_CreateAppAsync(...)`.
- `tests/Headless.Api.Tests.Integration/ProblemDetailsTests.cs` — assertion shape for ProblemDetails responses (especially the `_ValidateCoreProblemDetails` helper for traceId/timestamp).

**Test scenarios:**
- Happy path: endpoint throws `new MissingTenantContextException()` → response is 400, `Content-Type: application/problem+json`, body has `title == HeadlessProblemDetailsConstants.Titles.TenantContextRequired`, `status == 400`, `detail == HeadlessProblemDetailsConstants.Details.TenantContextRequired`, `extensions.code == HeadlessProblemDetailsConstants.Codes.TenantContextRequired`, `extensions.traceId` is present (proves normalization).
- Edge case: exception thrown with `Exception.Data["Headless.Messaging.FailureCode"] = "MissingTenantContext"` → the data tag does NOT appear in the response body. Information-disclosure invariant.
- Edge case: exception thrown with a custom message → the message does NOT appear as `detail`. The framework-owned `HeadlessProblemDetailsConstants.Details.TenantContextRequired` is used regardless. Information-disclosure invariant.
- Error path: endpoint throws `InvalidOperationException` (not the tenancy exception) → handler does NOT process it; response is the framework default 500 from the rest of the pipeline. Asserts the new handler did not steal the response.
- Edge case: handler-chain ordering — register `TenantContextExceptionHandler` followed by a stub catch-all `IExceptionHandler` that returns `true` for any exception. Throw `MissingTenantContextException`. Assert the tenancy handler wins. Also register them in reverse order and assert the catch-all wins, proving order matters and the documentation guidance is correct.

**Verification:**
- All scenarios above pass against a running ASP.NET Core test host.

---

- U7. **Documentation updates**

**Goal:** Keep all four documentation surfaces in lockstep so consumers see consistent guidance.

**Requirements:** R7

**Dependencies:** U1-U6.

**Files:**
- Modify: `docs/llms/api.md` (`Headless.Api` section — add `Tenant Context Exception Handler` sub-section under Quick Start or as a new heading; mention in Key Features; add `IProblemDetailsCreator.TenantRequired` to the API reference)
- Modify: `docs/llms/multi-tenancy.md` (add `## HTTP Failure Mapping` section that documents the handler, with cross-reference to existing "Strict Publish Tenancy" section that names `Exception.Data["Headless.Messaging.FailureCode"]`. Explicitly call out: server-side data tag, NOT in HTTP response body)
- Modify: `src/Headless.Api/README.md` (add to `## Key Features` bullets and add a sub-section under existing `## Multi-Tenancy` heading showing `services.AddTenantContextProblemDetails(...)` and the resulting 400 shape)
- Modify: `docs/llms/api.md` TOC and `docs/llms/multi-tenancy.md` TOC to include the new headings

**Approach:**
- Keep documentation factual, code-snippet-light. Show the default-coverage path (filters via `AddHeadless()`) and the opt-in `AddTenantContextProblemDetails()` registration for non-endpoint paths.
- Cross-link between the API doc and the multi-tenancy doc — readers landing on either should be able to find the other in two clicks.
- Document the dependency: `AddTenantContextProblemDetails` requires `AddHeadlessProblemDetails` to also be registered (so normalization runs); call this out explicitly in both docs.
- Document that consumers must call `app.UseExceptionHandler()` themselves — the helper only registers the handler; pipeline middleware is the consumer's responsibility. Note that `UseExceptionHandler()` should be placed early in the pipeline so it covers downstream middleware that may throw `MissingTenantContextException` during request execution.
- **Document handler-chain ordering** (mirrors U5 XML docs): if the consumer has multiple `IExceptionHandler` registrations, `AddTenantContextProblemDetails(...)` must be called **before** any catch-all handler that unconditionally returns `true`. ASP.NET Core's chain stops at the first handler that returns `true`, so a catch-all registered earlier will swallow `MissingTenantContextException` before the tenancy mapping runs. Recommended order: framework-specific handlers first (this one), generic fallbacks last.
- **Document the no-information-disclosure shape explicitly.** The response body contains `code`, `type`, `title`, `status`, `detail` (framework constant), and the standard normalized extensions. It does **not** contain entity names, the exception's `Message`, or any `Exception.Data` tags. Server-side debugging context belongs in logs. Consumers expecting to surface entity-level routing should use the `code` extension and route on their own request payload. This is intentional, not an oversight.

**Patterns to follow:**
- Existing `## Multi-Tenancy` section in `src/Headless.Api/README.md`.
- Existing "Strict Publish Tenancy" section in `docs/llms/multi-tenancy.md`.
- API doc per-package structure: Problem Solved / Key Features / Installation / Quick Start / Configuration / Dependencies / Side Effects.

**Test scenarios:**
- Test expectation: none — pure documentation. Lint with the existing markdown formatter; verify all internal links resolve; review for cross-link consistency.

**Verification:**
- All four documentation surfaces mention the new helper, the new `IProblemDetailsCreator.TenantRequired` factory, and the no-information-disclosure response shape.
- Cross-references between API doc and multi-tenancy doc are present.
- No internal links are broken.

- U9. **Consolidate exception mapping into a single global `HeadlessApiExceptionHandler`**

**Goal:** Replace the per-package `MvcApiExceptionFilter` and `MinimalApiExceptionFilter` with one `IExceptionHandler` (`HeadlessApiExceptionHandler`) in `Headless.Api`. ASP.NET Core's `IExceptionHandler` chain is the modern story; consolidating eliminates parallel switch statements and gives middleware/hosted-services/hubs the same exception-to-ProblemDetails coverage that endpoints get.

**Requirements:** R1, R3 (extends both surfaces; same response shape).

**Dependencies:** U1-U7 (the tenancy handler is the seed; this unit absorbs the rest of the exception list).

**Files:**
- Create: `src/Headless.Api/HeadlessApiExceptionHandler.cs`
- Delete: `src/Headless.Api/MultiTenancy/TenantContextExceptionHandler.cs`
- Delete: `src/Headless.Api.Mvc/Filters/MvcApiExceptionFilter.cs`
- Delete: `src/Headless.Api.MinimalApi/Filters/MinimalApiExceptionFilter.cs`
- Modify: `src/Headless.Api/Setup.cs` — register `HeadlessApiExceptionHandler` instead of `TenantContextExceptionHandler`.
- Modify: `src/Headless.Api.Mvc/Options/ConfigureMvcApiOptions.cs` — remove the `options.Filters.Add<MvcApiExceptionFilter>()` line.
- Modify: `src/Headless.Api.MinimalApi/Filters/RouteBuilderExtensions.cs` — remove the now-unused `AddExceptionFilter` extension method.
- Modify: `demo/Headless.Api.Demo/Endpoints/ProblemsEndpoints.cs` — drop the `.AddExceptionFilter()` call.
- Create: `tests/Headless.Api.Tests.Unit/HeadlessApiExceptionHandlerTests.cs` covering all exception cases.
- Delete: `tests/Headless.Api.Mvc.Tests.Unit/Filters/MvcApiExceptionFilterTests.cs`, `tests/Headless.Api.MinimalApi.Tests.Unit/Filters/MinimalApiExceptionFilterTests.cs`, `tests/Headless.Api.Tests.Unit/MultiTenancy/TenantContextExceptionHandlerTests.cs`.
- Rename: `tests/Headless.Api.Tests.Integration/MultiTenancy/TenantContextExceptionHandlerEndToEndTests.cs` → `tests/Headless.Api.Tests.Integration/HeadlessApiExceptionHandlerEndToEndTests.cs`.

**Approach:**
- The unified handler covers the same set the filters covered: `MissingTenantContextException` (400 via `TenantRequired()`), `ConflictException` (409), `FluentValidation.ValidationException` (422), `EntityNotFoundException` (404), EF Core `DbUpdateConcurrencyException` matched by type name (409), `TimeoutException` (408), `NotImplementedException` (501), `OperationCanceledException` and inner-OCE (499 with no body).
- Use `IProblemDetailsService.TryWriteAsync` for the body cases, with `WriteAsJsonAsync` fallback when the service returns `false` and `Response.HasStarted == false`.
- For 499 cancellation, write status only — the client is gone; a body is wasted bandwidth and matches what the old `MinimalApiExceptionFilter` did.
- EF Core's `DbUpdateConcurrencyException` is matched by `exception.GetType().Name == "DbUpdateConcurrencyException"` to avoid pulling EF Core into `Headless.Api`'s dependency graph.
- Logging: keep `EventId 5003` for DB concurrency (Warning), `5004` for timeout (Debug), and `5006` for response-already-started (Error). `5005` is intentionally not used since we no longer log per-success on the tenancy path.

**Patterns to follow:**
- Existing `TenantContextExceptionHandler` for the `IProblemDetailsService.TryWriteAsync` + fallback shape and `LoggerMessage` style.
- The existing factories in `IProblemDetailsCreator` already produce normalized ProblemDetails — the handler just selects which factory to call per exception type.

**Test scenarios:**
- One unit test per exception type asserting status code and (where applicable) `code`/`title` extensions.
- Cancellation path produces no body and does not invoke `IProblemDetailsService`.
- Fallback path writes `application/problem+json` with the framework-owned content when the service returns `false`.
- Information-disclosure invariant test: an exception with sensitive `Message`/`Data`/`InnerException` does not leak into the response.

**Verification:**
- `dotnet build` clean across solution.
- All unit tests pass; the new unit-test file covers the exception list end-to-end.
- The integration end-to-end test (`HeadlessApiExceptionHandlerEndToEndTests`) covers the full pipeline including `IProblemDetailsCreator.Normalize` extensions.

---

- U8. **Filter catch arms for `MissingTenantContextException` (defense-in-depth)**

> **Superseded by U9.** This unit shipped briefly as a parallel-coverage step; U9 replaced both filters with a single global handler, so the filter catch arms no longer exist. Kept here for plan continuity.

**Goal:** Map `MissingTenantContextException` thrown from inside endpoint code to the same normalized 400 ProblemDetails shape, without requiring consumers to register `AddTenantContextProblemDetails(...)` and wire `UseExceptionHandler()`. The global `IExceptionHandler` (U4) remains the safety net for non-endpoint paths (middleware, hosted services, hubs); the filters cover the common endpoint case.

**Requirements:** R1, R3 (extends both surfaces; same response shape).

**Dependencies:** U2 (factory), U3 (options).

**Files:**
- Modify: `src/Headless.Api.MinimalApi/Filters/MinimalApiExceptionFilter.cs`
- Modify: `src/Headless.Api.Mvc/Filters/MvcApiExceptionFilter.cs`
- Test: `tests/Headless.Api.MinimalApi.Tests.Unit/Filters/MinimalApiExceptionFilterTests.cs`
- Test: `tests/Headless.Api.Mvc.Tests.Unit/Filters/MvcApiExceptionFilterTests.cs`

**Approach:**
- Both filters call `creator.TenantRequired()` directly — no options injection, since the response shape is framework-owned constants (`HeadlessProblemDetailsConstants.Titles.TenantContextRequired`, `HeadlessProblemDetailsConstants.Codes.TenantContextRequired`).
- `MinimalApiExceptionFilter` adds a new `catch (MissingTenantContextException)` arm before `ConflictException`. Body: call `creator.TenantRequired()`, return `TypedResults.Problem(details)`.
- `MvcApiExceptionFilter` adds a new switch arm `MissingTenantContextException e => _Handle(httpContext, e)` first in the switch, plus a private `_Handle(HttpContext, MissingTenantContextException)` method that calls the same factory and returns `Results.Problem(problemDetails).ExecuteAsync(context)`.
- No logging on this branch — `MissingTenantContextException` is operator-actionable through the issuer (EF write guard, mediator behavior, messaging publish guard) which already log; double-logging on the response side adds noise.

**Patterns to follow:**
- Existing arms in both filters (e.g., `ConflictException`, `EntityNotFoundException`) — same primary-constructor-injected dependency style, same `creator.<X>(...)` + `Results.Problem(...)` shape.

**Test scenarios:**
- Happy path (MinimalApi): `MissingTenantContextException` thrown inside an endpoint → filter returns `ProblemHttpResult` with `StatusCode == 400`. Verify `creator.TenantRequired()` was called.
- Happy path (Mvc): `MissingTenantContextException` thrown inside an action → filter sets `ExceptionHandled = true`, response status `400`, response body's `title` matches `HeadlessProblemDetailsConstants.Titles.TenantContextRequired`, `code` extension matches `HeadlessProblemDetailsConstants.Codes.TenantContextRequired`.

**Verification:**
- Both filters' unit tests pass with new arms.
- Both filters build and run cleanly with the existing `IProblemDetailsCreator` registration — no extra DI setup required.
- The global `IExceptionHandler` from U4 still handles `MissingTenantContextException` raised outside endpoint code.

---

## System-Wide Impact

- **Interaction graph:** New code interacts with ASP.NET Core's `IExceptionHandler` chain (consumed by `UseExceptionHandler` middleware) and with `IProblemDetailsService` + `IProblemDetailsCreator`. The handler delegates ProblemDetails construction to `IProblemDetailsCreator.TenantRequired(...)` — the canonical creation path. No interaction with existing `MinimalApiExceptionFilter` or `MvcApiExceptionFilter` — those filters run inside endpoint handlers; `IExceptionHandler` runs in the global error pipeline. If both are registered, the endpoint filters catch first; the global handler catches anything that escapes them. This is the correct ordering.
- **Error propagation:** The handler fully owns the response for `MissingTenantContextException`. For any other exception, it returns `false` and the next handler in the chain (or the default 500 page) takes over. Exception-internal data (`Message`, `Data`) does not flow to the response — only the framework-owned defaults do.
- **State lifecycle risks:** The fallback `WriteAsJsonAsync` path is guarded by `Response.HasStarted` to avoid corrupting partial responses. If both `IProblemDetailsService.TryWriteAsync` and the fallback fail, the handler returns `false` so the framework can attempt its default response — no half-written body.
- **API surface parity:** The handler addresses the HTTP surface only. Other exception-routing surfaces (mediator behavior #236, EF write guard #234, messaging publish guard #238) handle the same exception in their own contexts.
- **Integration coverage:** The integration tests in U6 cover the full pipeline (HTTP → exception → handler → factory → normalized response). Mocking `IProblemDetailsService` in U4's unit tests is fine for the fallback branch, but the happy-path traceId assertion requires the real ASP.NET Core pipeline.
- **Public-API change to `IProblemDetailsCreator`:** Adding `TenantRequired(string, string)` extends the interface. Any existing consumer that implements `IProblemDetailsCreator` directly (rather than using the framework's `ProblemDetailsCreator`) gets a compile error and must add the method. Project is greenfield (per `CLAUDE.md`); this is acceptable. Documented in the change notes.
- **Unchanged invariants:** `AddHeadlessProblemDetails` is unchanged. `MissingTenantContextException` is unchanged (no new properties, no new constructors). The exception's `Exception.Data` channel for layer-specific tags is preserved and explicitly server-side only.

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Adding `TenantRequired` to `IProblemDetailsCreator` is a public-API change. Custom implementations of the interface (if any consumer has one) break at compile time. | Project is greenfield (per `CLAUDE.md`); this is acceptable. The framework's own `ProblemDetailsCreator` is the canonical implementation and gets the method added at the same time. Document the interface change in release notes. |
| Consumers might forget `AddHeadlessProblemDetails` is a prerequisite, leading to responses without `traceId`. | Document the dependency prominently in both `docs/llms/api.md` and the README. Consider adding an XML doc warning on `AddTenantContextProblemDetails`. |
| The 7-day quarantine on new packages does not apply (no new packages). | N/A — confirmed no new csproj. |
| `IProblemDetailsService` may not be registered in some test or minimal hosting scenarios, causing `TryWriteAsync` to throw or return false. | The fallback path with `Response.HasStarted` guard is exactly the safety net for this. Integration tests cover both branches. |
| Calling `AddTenantContextProblemDetails(...)` multiple times could register the handler twice, causing double-write attempts. | Use idempotent registration (either `TryAddEnumerable` or guard inside `_AddCore`). U5 has a test scenario for this. |
| A consumer expects entity-level routing in the response and is surprised by its absence. | Documentation explicitly calls out the no-information-disclosure shape and points consumers to the `code` extension for client-side routing. The `EntityNotFound(string entity, string key)` factory is the non-tenancy precedent for entity-aware responses where the entity name is part of the contract — tenancy is intentionally different. |

## Documentation / Operational Notes

- Four doc surfaces updated (U7): `docs/llms/api.md`, `docs/llms/multi-tenancy.md`, `src/Headless.Api/README.md`, plus inline XML docs on the new public types and the new `IProblemDetailsCreator.TenantRequired` method.
- No rollout / migration concerns: this is a new opt-in helper and an additive interface method. Existing consumers who use the framework's `ProblemDetailsCreator` continue to work unchanged.
- No monitoring impact beyond: when the handler fires, it emits a structured `Warning` log entry (EventId 5005) that operators can dashboard against.
- Capture a `docs/solutions/api/` learning entry post-implementation for the `Response.HasStarted` guard pattern + the `IProblemDetailsService.TryWriteAsync` + factory-driven ProblemDetails shape — the learnings researcher noted this is uncaptured prior knowledge in the repo.

## Sources & References

- **Origin issue:** https://github.com/xshaheen/headless-framework/issues/237
- **Brainstorm decisions (2026-05-04):** locked design via `/dev-brainstorm` — extend `IProblemDetailsCreator`, drop `EntityType` exposure, centralize `Title`, options reduced to `TypeUriPrefix` + `ErrorCode`. Conversation captured in this plan's Open Questions / Resolved section.
- **Precedent (downstream):** https://github.com/xshaheen/zad-ngo/pull/152 (FOUND-02 / U4 — local hand-rolled copy; upstream version intentionally narrower on response surface)
- **Related plans:**
  - `docs/plans/2026-05-03-002-feat-messaging-phase1-foundations-plan.md` (defines `MissingTenantContextException` contract; explicitly names #237 as the HTTP sibling)
  - `docs/plans/2026-05-01-001-feat-tenant-id-envelope-plan.md` (cross-layer tenancy issue map; reinforces no-leakage posture)
- **Related code:**
  - `src/Headless.Api/Setup.cs` — `AddHeadlessProblemDetails`
  - `src/Headless.Api/Abstractions/IProblemDetailsCreator.cs` — `Normalize` contract; new `TenantRequired` factory lands here
  - `src/Headless.Api/MultiTenancySetup.cs` — options + validator pattern
  - `src/Headless.Caching.Redis/Setup.cs` — 3-overload registration pattern
  - `src/Headless.Api.Abstractions/Constants/ProblemDetailTitles.cs` — constants location
  - `src/Headless.Core/Abstractions/MissingTenantContextException.cs` — exception contract (unchanged)
  - `src/Headless.Api.MinimalApi/Filters/MinimalApiExceptionFilter.cs` — closest exception-mapping precedent + LoggerMessage style
  - `tests/Headless.Api.Tests.Integration/TenantResolutionMiddlewareTests.cs` — inline-app integration test pattern
- **Related issues:** #234 (EF write guard), #236 (mediator behavior), #238 (messaging publish guard) — siblings in the cross-layer tenancy work
