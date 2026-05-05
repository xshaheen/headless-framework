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

- R1. `IProblemDetailsCreator` gains a `TenantRequired(string typeUriPrefix, string errorCode)` factory method that builds a 400 `ProblemDetails` with the canonical title (`HeadlessProblemDetailsConstants.Titles.TenantContextRequired`), composed `Type` URL (`{prefix}/tenant-required`), and `Extensions["code"]`, then runs `Normalize` to inject `traceId`/`buildNumber`/`commitNumber`/`timestamp`/`Instance`.
- R2. `TenantContextProblemDetailsOptions` exposes `TypeUriPrefix` (consumer error-doc namespace) and `ErrorCode` (stable client-routing code). No `Title` field — the title is a centralized constant. No `EntityType` / `ExposeEntityType` field — entity names are not exposed in API responses (information disclosure: see Key Technical Decisions).
- R3. `TenantContextExceptionHandler` implements `IExceptionHandler`. Returns `false` for any exception other than `MissingTenantContextException`. For matches: delegates to `IProblemDetailsCreator.TenantRequired(...)`, sets `HttpContext.Response.StatusCode = 400`, calls `IProblemDetailsService.TryWriteAsync` so consumer customizations layer on top, and falls back to `WriteAsJsonAsync` only when the service returns `false` AND `Response.HasStarted == false`.
- R4. Single-call registration helper `services.AddTenantContextProblemDetails(...)` exists with three overloads (`IConfiguration`, `Action<TOptions>`, `Action<TOptions, IServiceProvider>`) — matching the framework's option-registration convention. Idempotent: calling twice does not register the handler twice.
- R5. Default `Title` is kebab-case (`"tenant-context-required"`), centralized in `HeadlessProblemDetailsConstants.Titles.TenantContextRequired`.
- R6. Integration test proves: throwing `MissingTenantContextException` from a request handler yields a 400 with stable `code`, `type`, `title`; `traceId` extension is present (proves normalization ran via `IProblemDetailsService`).
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

- **Factory method on `IProblemDetailsCreator`, not a standalone handler shape.** The handler delegates to `creator.TenantRequired(typeUriPrefix, errorCode)`. Rationale: matches existing `EntityNotFound` / `Conflict` / `Forbidden` factory shape; any caller with the creator injected can produce the same response without going through the global exception pipeline; keeps the canonical ProblemDetails-construction logic in one class.
- **No entity name in the response.** The original issue proposed an `entityType` extension gated by an `ExposeEntityType` option. **Dropped** — exposing CLR entity type names to API clients is information disclosure: external callers can enumerate the persistence model from error responses. The `code` extension (`tenancy.tenant-required`) is sufficient for client-side routing; entity names belong in server logs (via `Exception.Data` tags), not HTTP bodies. If a downstream consumer needs entity context for error tracking, they catch and log on the throwing side.
- **`MissingTenantContextException` is not modified.** No new `EntityType` property, no new constructor. Exception identity is "tenancy guard fired"; per-layer context is in `Exception.Data` (server-only).
- **`Title` is a centralized constant, not configurable.** Defaults across the framework's ProblemDetails (`EntityNotFound`, `Conflict`, etc.) are fixed strings in `HeadlessProblemDetailsConstants.Titles`. The tenant-required title joins that pattern. Consumers who need a different title can write their own factory; the framework keeps one canonical title per error class.
- **Sub-namespace, not sub-project.** New code lives at `src/Headless.Api/MultiTenancy/` with namespace `Headless.Api.MultiTenancy`. Splitting into a `Headless.Api.MultiTenancy` package would fight the existing `MultiTenancySetup.cs` placement and add a NuGet release for one handler. Rationale: minimum diff, consistent with how tenancy already lives.
- **Use `IProblemDetailsService.TryWriteAsync` (ASP.NET Core's built-in service), not a direct write.** This is the only way `ProblemDetailsOptions.CustomizeProblemDetails` runs (so consumer customizations layer on top of `Normalize`). The fallback `WriteAsJsonAsync` path runs only when the service returns `false` AND `Response.HasStarted == false`. Because the factory already calls `Normalize` before returning, the fallback path does not need to re-call it — the `ProblemDetails` is already normalized.
- **`ErrorCode` default is `"tenancy.tenant-required"`** (per issue). Stamped into `ProblemDetails.Extensions["code"]` by the factory.
- **Type URL is built as `$"{TypeUriPrefix.TrimEnd('/')}/tenant-required"`.** Default prefix `"https://errors.headless/tenancy"` → final URL `"https://errors.headless/tenancy/tenant-required"`. Prefix-based composition lets each consumer route to its own error-doc namespace without rewriting the path segment.
- **Logging.** Use source-generated `LoggerMessage` (per project `.NET` rules). `EventId` allocated in the existing `Headless.Api` 5xxx range — picking `5005` to avoid collision with `5003/5004` already used in `MinimalApiExceptionFilter`. Log level `Warning` (operator-actionable but expected per consumer error). Structured properties: `ErrorCode`, plus the layer tag from `exception.Data` if present, so log dashboards can group by layer without the response leaking it.

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
- Add to the interface: `ProblemDetails TenantRequired(string typeUriPrefix, string errorCode);`. Both parameters required (no overload); the handler reads them from `TenantContextProblemDetailsOptions` and the registration extension provides sensible defaults.
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

- U3. **`TenantContextProblemDetailsOptions` + inline validator**

**Goal:** Define the configurable surface (just `TypeUriPrefix` + `ErrorCode`) and validate invariants at startup.

**Requirements:** R2

**Dependencies:** None.

**Files:**
- Create: `src/Headless.Api/MultiTenancy/TenantContextProblemDetailsOptions.cs`
- Test: `tests/Headless.Api.Tests.Unit/MultiTenancy/TenantContextProblemDetailsOptionsValidatorTests.cs`

**Approach:**
- Namespace `Headless.Api.MultiTenancy`.
- `public sealed class TenantContextProblemDetailsOptions` with two `init`-only properties:
  - `TypeUriPrefix` (default `"https://errors.headless/tenancy"`)
  - `ErrorCode` (default `"tenancy.tenant-required"`)
- No `Title` field — title is centralized in `HeadlessProblemDetailsConstants.Titles.TenantContextRequired`.
- No `EntityType` / `ExposeEntityType` field — entity names are not exposed in the response.
- Inline `internal sealed class TenantContextProblemDetailsOptionsValidator : AbstractValidator<...>` directly below the options class.
- Validator rules: `TypeUriPrefix` not empty, must be a well-formed absolute URI (use FluentValidation's `Must(...)` with `Uri.TryCreate(value, UriKind.Absolute, out _)`); `ErrorCode` not empty.

**Patterns to follow:**
- `src/Headless.Api/MultiTenancySetup.cs` — exact options + inline validator structure.
- FluentValidation rule style used across other validators in the framework.

**Test scenarios:**
- Happy path: defaults pass validation.
- Edge case: empty `TypeUriPrefix` fails validation with a clear error.
- Edge case: malformed `TypeUriPrefix` (e.g., `"not a url"`) fails validation.
- Edge case: `TypeUriPrefix` with trailing slash is accepted (factory trims).
- Edge case: empty `ErrorCode` fails validation.

**Verification:**
- Validator catches each invalid configuration before the app starts (verified via DI `ValidateOnStart()` in U5's test).

---

- U4. **`TenantContextExceptionHandler`**

**Goal:** Map `MissingTenantContextException` to the normalized 400 ProblemDetails response by delegating to `IProblemDetailsCreator.TenantRequired(...)`.

**Requirements:** R3, R6

**Dependencies:** U2 (factory), U3 (options).

**Files:**
- Create: `src/Headless.Api/MultiTenancy/TenantContextExceptionHandler.cs`
- Test: `tests/Headless.Api.Tests.Unit/MultiTenancy/TenantContextExceptionHandlerTests.cs` (fallback-path branch coverage)

**Approach:**
- Namespace `Headless.Api.MultiTenancy`. `internal sealed class` (consumers register via the public `AddTenantContextProblemDetails` extension; the type itself does not need to be public).
- Implements `Microsoft.AspNetCore.Diagnostics.IExceptionHandler`.
- Primary constructor injects `IOptions<TenantContextProblemDetailsOptions> options`, `IProblemDetailsService problemDetailsService`, `IProblemDetailsCreator problemDetailsCreator`, `ILogger<TenantContextExceptionHandler> logger`.
- `TryHandleAsync(httpContext, exception, cancellationToken)`:
  - If `exception is not MissingTenantContextException`, return `false`.
  - Read `TypeUriPrefix` and `ErrorCode` from `options.Value`.
  - Delegate: `var problemDetails = problemDetailsCreator.TenantRequired(prefix, code);`. The factory already calls `Normalize`.
  - Set `httpContext.Response.StatusCode = StatusCodes.Status400BadRequest` before the write attempt.
  - Try `await problemDetailsService.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problemDetails })`. If it returns `true`, log at `Warning` (source-generated) and return `true`.
  - Fallback: only if `!httpContext.Response.HasStarted`, write directly: `await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken: cancellationToken)` with `ContentType = "application/problem+json"`. Log and return `true`. (No need to re-call `Normalize` — the factory already did.)
  - If `Response.HasStarted == true` and `TryWriteAsync` failed, log at `Error` (`EventId 5006`) and return `false` (cannot safely write).

**Technical design (directional):**

```
TryHandleAsync(ctx, exception, ct):
  if exception is not MissingTenantContextException: return false
  pd = creator.TenantRequired(options.Value.TypeUriPrefix, options.Value.ErrorCode)  # already normalized
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
- Happy path: `MissingTenantContextException` thrown → `TryHandleAsync` returns `true`, response status is 400, `pd.Status == 400`, `pd.Type` ends with `/tenant-required`, `pd.Title == "tenant-context-required"`, `pd.Extensions["code"] == "tenancy.tenant-required"`. (Integration coverage in U6 verifies the full pipeline including normalization.)
- Edge case: any exception other than `MissingTenantContextException` → `TryHandleAsync` returns `false` and writes nothing.
- Edge case: custom `TypeUriPrefix = "https://zad.org/errors/tenancy/"` (trailing slash) → final `Type == "https://zad.org/errors/tenancy/tenant-required"` (single slash; verified via the U2 factory test, but also asserted end-to-end here).
- Error path: `IProblemDetailsService.TryWriteAsync` returns `false` AND `Response.HasStarted == false` → fallback `WriteAsJsonAsync` runs with `application/problem+json`. Use NSubstitute to mock `IProblemDetailsService` returning `false`; verify response body and content type.
- Error path: `IProblemDetailsService.TryWriteAsync` returns `false` AND `Response.HasStarted == true` → handler returns `false` (no write attempted, no exception thrown). Verify the `EventId = 5006` `Error` log entry was emitted so operators have the signal.
- Error path: handler emits the `EventId = 5005` `Warning` log entry on the happy path with structured `ErrorCode` property present (verify with a test logger).
- Negative assertion: response body never contains an `entityType` extension, an entity CLR name, the exception message, or any value from `exception.Data` — only `code` plus the standard normalized extensions. This is the no-information-disclosure invariant.

**Verification:**
- Throwing `MissingTenantContextException` from a request handler produces a 400 with the expected ProblemDetails shape and a `traceId` extension.
- Other exceptions are passed through unchanged.
- No exception-internal data leaks into the response.

---

- U5. **`AddTenantContextProblemDetails` registration extension**

**Goal:** Provide the consumer-facing single-call API with the standard 3-overload shape.

**Requirements:** R4

**Dependencies:** U3 (registers options + validator), U4 (registers handler).

**Files:**
- Create: `src/Headless.Api/MultiTenancy/TenantContextProblemDetailsSetup.cs`
- Test: `tests/Headless.Api.Tests.Integration/MultiTenancy/TenantContextProblemDetailsSetupTests.cs`

**Approach:**
- Namespace `Headless.Api.MultiTenancy` (or `Microsoft.Extensions.DependencyInjection` if the codebase prefers DI extensions there — `RedisCacheSetup` uses `Headless.Caching` so we'll match by keeping the new namespace alongside the type). Decide during implementation by mirroring whichever sibling pattern dominates in `Headless.Api`.
- `[PublicAPI] public static class TenantContextProblemDetailsSetup` with a C# 14 `extension(IServiceCollection services)` block.
- Three overloads in this order (matching `RedisCacheSetup`):
  1. `AddTenantContextProblemDetails(Action<TenantContextProblemDetailsOptions, IServiceProvider> setupAction)`
  2. `AddTenantContextProblemDetails(IConfiguration configuration)`
  3. `AddTenantContextProblemDetails(Action<TenantContextProblemDetailsOptions> setupAction)`
- Each overload calls `services.Configure<TenantContextProblemDetailsOptions, TenantContextProblemDetailsOptionsValidator>(...)` then a private `_AddCore()` helper that registers the handler.
- The `_AddCore()` helper must be idempotent — `AddExceptionHandler<T>` registers a transient, but multiple calls would add multiple registrations. Use `services.TryAddEnumerable(ServiceDescriptor.Transient<IExceptionHandler, TenantContextExceptionHandler>())` directly, OR guard the `AddExceptionHandler<T>()` call with a `services.Any(...)` check.

**Patterns to follow:**
- `src/Headless.Caching.Redis/Setup.cs:13-74` — exact 3-overload shape with private core helper and `services.Configure<TOption, TValidator>(...)` calls.
- `Headless.Hosting.OptionsServiceCollectionExtensions.Configure<TOption, TValidator>(...)` — the typed `Configure` overload with FluentValidation + `ValidateOnStart()`.

**XML documentation requirements (R7 prerequisite):**
- Each public overload of `AddTenantContextProblemDetails` carries an XML `<summary>` plus a `<remarks>` block stating two prerequisites:
  1. `services.AddHeadlessProblemDetails()` must also be registered (DI will throw `Unable to resolve service for type 'IProblemDetailsCreator'` at handler construction otherwise — the message is sufficient, no custom check is added).
  2. The consumer must call `app.UseExceptionHandler()` themselves; this helper only registers the handler in the chain.
- Document handler-chain ordering: ASP.NET Core invokes `IExceptionHandler` instances in registration order. Consumers with multiple handlers should register `AddTenantContextProblemDetails(...)` **before** any catch-all handler that returns `true` for every exception, otherwise the catch-all will swallow `MissingTenantContextException` first and the tenancy mapping will never run. Mirror this guidance in `docs/llms/api.md` (U7).

**Test scenarios:**
- Happy path: `services.AddTenantContextProblemDetails(o => o.TypeUriPrefix = "https://example.com/errors")` registers the handler and resolves it from DI.
- Happy path: `services.AddTenantContextProblemDetails(configuration)` binds from a config section.
- Happy path: `services.AddTenantContextProblemDetails((o, sp) => ...)` resolves a service-provider-aware delegate.
- Error path: invalid options (empty `TypeUriPrefix`) → `Host.Start()` throws `OptionsValidationException` (proves `ValidateOnStart` ran).
- Edge case: calling `AddTenantContextProblemDetails(...)` twice does not register the handler twice (no duplicate handler invocations).

**Verification:**
- Each of the three overloads compiles, registers the handler, and the handler is invoked end-to-end for a request that throws `MissingTenantContextException`.

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
- Happy path: endpoint throws `new MissingTenantContextException()` → response is 400, `Content-Type: application/problem+json`, body has `type` ending with `/tenant-required`, `title == "tenant-context-required"`, `status == 400`, `detail == HeadlessProblemDetailsConstants.Details.TenantContextRequired`, `extensions.code == "tenancy.tenant-required"`, `extensions.traceId` is present (proves normalization).
- Edge case: custom `TypeUriPrefix = "https://zad.org/errors/tenancy"` → response `type == "https://zad.org/errors/tenancy/tenant-required"`.
- Edge case: exception thrown with `Exception.Data["Headless.Messaging.FailureCode"] = "MissingTenantContext"` → the data tag does NOT appear in the response body. Information-disclosure invariant.
- Edge case: exception thrown with a custom message → the message does NOT appear as `detail`. The framework-owned `HeadlessProblemDetailsConstants.Details.TenantContextRequired` is used regardless. Information-disclosure invariant.
- Error path: endpoint throws `InvalidOperationException` (not the tenancy exception) → handler does NOT process it; response is the framework default 500 from the rest of the pipeline (or whatever Minimal API filter is configured). Asserts the new handler did not steal the response.
- Integration: response after `await response.Content.ReadFromJsonAsync<ProblemDetails>()` contains `traceId` matching `Activity.Current?.Id` or the request's `TraceIdentifier` — proves `IProblemDetailsCreator.Normalize` ran via the factory.

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
- Keep documentation factual, code-snippet-light. Show one minimal registration example with default options and one customized example (`TypeUriPrefix` override).
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
