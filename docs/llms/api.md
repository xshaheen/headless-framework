---
domain: API & Web
packages: Api.Abstractions, Api, Api.ServiceDefaults, DataProtection.Blobs, Api.FluentValidation, Api.Identity, Api.Logging.Serilog, Api.MinimalApi, Api.Mvc, Api.UserAgent
---

# API & Web

> ASP.NET Core API infrastructure: service registration, middleware, problem details, validation, logging, and endpoint integration for Minimal API and MVC.

## Orientation

Start every new Headless API with `Headless.Api.ServiceDefaults`. It brings in `Headless.Api` and has three entry points:

- `builder.AddHeadless()` registers compression, health checks, problem details, validation, JSON defaults, OpenTelemetry, OpenAPI, service discovery, and HttpClient resilience.
- `app.UseHeadless()` applies the edge of the middleware pipeline in a fixed order.
- `app.MapHeadlessEndpoints()` maps `/health`, `/alive`, the OpenAPI JSON documents, and static assets.

Reference `Headless.Api` alone only when you compose your own pipeline from primitives such as `AddHeadlessProblemDetails()`, `AddHeadlessAntiforgery()`, or `AddHeadlessApiResponseCompression()`.

Choose an endpoint style:

- **Minimal API** (the default for new projects): add `Headless.Api.MinimalApi` and call `ConfigureHeadlessMinimalApi()`. It adds mediator dispatch (`MapPostRequest<TRequest, TResponse>`, `sender.DispatchAsync(request, ct)`), `ApiResult` results with complete OpenAPI metadata, and group-level validation.
- **MVC controllers**: add `Headless.Api.Mvc` and call `ConfigureHeadlessMvc()` for base controllers, exception filters, and URL canonicalization.

Other packages:

- `Headless.Api.Abstractions`: the interfaces alone (`IRequestContext`, `IWebClientInfoProvider`, `IProblemDetailsCreator`), for libraries that must not pull the API stack.
- `Headless.Api.FluentValidation`: file rules (size, content type, magic bytes) for `IFormFile` uploads, plus the request contracts `PhoneNumberRequest`, `GeoCoordinateRequest`, and `PageMetadataRequest`.
- `Headless.DataProtection.Blobs`: persists ASP.NET Core Data Protection keys to any `IBlobStorage` provider.
- `Headless.Api.Identity`: ASP.NET Core Identity helpers: token providers, localized `auth:` and `user:` errors, `HeadlessLookupNormalizer`, and the `Basic` and `ApiKey` authentication schemes.
- `Headless.Api.UserAgent`: the DeviceDetector.NET-backed `IUserAgentParser`. Without it, `IWebClientInfoProvider.DeviceInfo` is `null`.
- `Headless.Api.Logging.Serilog`: Serilog enrichment with per-request user, account, and correlation ID, plus client IP and selected request headers.
- `Headless.Idempotency.ResponseReplay`: idempotency middleware that replays the captured HTTP response on identical retries. [idempotency.md § Headless.Idempotency.ResponseReplay](idempotency.md#headlessidempotencyresponsereplay) owns its setup.

## Agent Rules

- Install `Headless.Api.ServiceDefaults` for a new API. Do not register compression, security headers, JSON defaults, OpenTelemetry, OpenAPI, or problem details by hand: `AddHeadless()` does it. Pass toggles through `AddHeadless(configureServices: options => ...)`.
- Call `UseHeadless()` first in the pipeline, then add the app's own middleware after it. To drop an edge step, turn off its switch in `UseHeadless(options => ...)`. Do not turn a step off and re-add it by hand, because that breaks the fixed order. See [Standard middleware order](#standard-middleware-order).
- Configure proxies (`KnownProxies`, `KnownIPNetworks`, `ForwardLimit`, `ForwardedHeaders`) with `builder.Services.Configure<ForwardedHeadersOptions>(...)`. Do not call `app.UseForwardedHeaders()` a second time: each pass consumes one more `X-Forwarded-For` hop.
- Keep `TrustForwardedHeadersFromAnyProxy` off unless only trusted proxy infrastructure can reach the service. With it on, any client can spoof the forwarded host and scheme.
- Antiforgery is off by default. For cookie-authenticated apps, set `options.Antiforgery.Enabled = true` and call `app.UseAntiforgery()` yourself after `UseAuthentication()` and `UseAuthorization()`. Bearer-token APIs leave it off.
- Inject `IRequestContext` for the request's user, tenant, locale, time zone, and correlation ID. Do not read `HttpContext` in service code.
- Branch on a problem's `error.code`, `title`, or `type`, never on `detail` or a description: those are localized. Call a `*MessageDescriber` factory per response, never cache its result in a `static`. See [What clients branch on, and what is localized](#what-clients-branch-on-and-what-is-localized).
- Write a CORS policy in code with ASP.NET Core's `AddCors`, or bind it from configuration with `services.AddHeadlessCors(configuration.GetSection("Cors"))`. Never combine `SetIsOriginAllowed(_ => true)` with `AllowCredentials()`: that reflects every origin and hands it the user's session. Approve origins known only at request time (tenant custom domains) through an `ICorsOriginSource`. See [CORS](#cors).
- Use `AddHeadlessAllowAnyCors()` in Development only. It never allows credentials and fails startup outside Development unless `AllowAnyOriginOutsideDevelopment` is set for a deliberately public API.
- Do not treat CORS as protection from non-browser callers. Native mobile clients send no `Origin` header, so no policy applies to them.
- For tenant-aware HTTP apps, configure `builder.AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims()))` and place `app.UseHeadlessTenancy()` after `UseRouting()` and `UseAuthentication()`, and before `UseAuthorization()`. Put `TenantRequirement` in `DefaultPolicy` or `FallbackPolicy`: the startup check ignores named policies. See [multi-tenancy.md](multi-tenancy/http.md#http-setup).
- For identifier-based (pre-authentication) tenant resolution, place `app.UseHeadlessTenantCatalogResolution()` after `UseRouting()` and before `UseAuthentication()`, with `UseForwardedHeaders()` (behind a proxy), host filtering, and `UseCors()` ahead of it. Register the host source before a header source where hostnames carry perimeter controls. See [multi-tenancy.md](multi-tenancy/catalog.md#tenant-catalog).
- For response replay, register `services.AddHeadlessIdempotency(...)` with a provider and `services.AddHeadlessHttpIdempotency(...)`, then place `app.UseHeadlessHttpIdempotency()` after `UseAuthorization()` and `UseHeadlessTenancy()`, so a request that fails authentication or authorization never admits a key. Keep response compression outside it (`UseHeadless()` already does). Change the key header only globally through `AddHeadlessHttpIdempotency(o => o.HeaderName = ...)`: a per-endpoint `HeaderName` set through `.WithIdempotency()` is ignored.
- `Basic` and `ApiKey` handlers authenticate only credentials sent for their own scheme. A cookie or bearer principal does not satisfy an endpoint that requires `Basic` or `ApiKey`. Query-string API keys work only with `AllowApiKeyInQueryString = true`.
- `AddHeadless()` registers neither `IFieldEncryptor` nor `IBlindIndexer`. An app that encrypts values, including encrypted `Headless.Settings` definitions, calls `AddHeadlessEncryption(...)`. See [Encryption](encryption.md).
- Prefer `Headless.Api.MinimalApi` for new projects. Call `.ValidateArguments()` on a route group. Use `.Validate<T>()` on one endpoint only when the group convention does not fit, and never both for one argument. Return `ApiResult` through `ToHttpResult()`, `sender.DispatchAsync(...)`, or a `Map*Request` helper, not a hand-written bridge.
- Validate a mediator request in one place: either at the endpoint (`ValidateArguments()`, `AddHeadlessMvcArgumentValidation()`) or in the mediator (`AddMediatorValidationRequestBehavior()`), never both. With both, every validator runs twice.
- For MVC, inherit from `ApiControllerBase` and call `ConfigureHeadlessMvc()`. Do not configure `MvcOptions` by hand for what it covers.
- Use the `Headless.Api.FluentValidation` rules (`FileNotEmpty()`, `LessThanOrEqualTo()`, `ContentTypes()`, `HaveSignatures()`, `PhoneNumber()`, `GeoCoordinate()`, `PageMetadata()`) instead of hand-written file or request-contract validation.
- In a distributed or containerized deployment, persist Data Protection keys with `PersistKeysToBlobStorage()` and give the key container a name unique to the app and environment.
- Call `UseHeadlessSerilogEnrichers()` after `UseAuthentication()`, so the enrichers can read the user.

## Core Concepts

### Bootstrap model: ServiceDefaults vs Core

`Headless.Api.ServiceDefaults` composes the `Headless.Api` primitives with Aspire-style host conventions (OpenTelemetry, OpenAPI, service discovery, HttpClient resilience) behind `AddHeadless()`, `UseHeadless()`, and `MapHeadlessEndpoints()`. `Headless.Api` exposes the same primitives one by one (`AddHeadlessProblemDetails()`, `AddHeadlessApiResponseCompression()`, `AddHeadlessAntiforgery()`, `ConfigureHeadlessDefaultApi()`). Use `Headless.Api` directly only when you must compose the pipeline yourself.

`AddHeadless()` calls `SetupApi.ConfigureGlobalSettings()`, which sets the regex timeout and the FluentValidation defaults, including the Headless formal Arabic (`ar`) and Egyptian Arabic (`ar-EG`) wording of FluentValidation's built-in messages. The call is idempotent. Call it yourself only when you need those defaults before `AddHeadless()` runs. It does not change the `JsonWebTokenHandler` statics.

### Request context: `IRequestContext`

`IRequestContext` (in `Headless.Api.Abstractions`) gives one view of the request: user ID and claims, tenant ID, preferred locale, time zone, correlation ID, and request start time. `AddHeadless()` registers it as scoped. Substitute the interface in unit tests instead of building an `HttpContext`.

### Problem details and error codes

`AddHeadlessProblemDetails()`, which `AddHeadless()` calls, registers `IProblemDetailsCreator` and one exception handler for every exception the framework knows (see [the exception table](#map-your-own-exceptions-and-errors)). Every problem body carries the extensions `traceId`, `buildNumber`, `commitNumber`, `instance`, and `timestamp`. Every framework error code, FluentValidation failures included, has the shape `g:lower_snake_case` (`g:tenant_required`, `g:cross_tenant_write`, `g:idempotency_key_reused`) and appears in `error.code` or `errors[].code`. Branch on the `public const string` values of the `*ErrorCodes` holders: `GeneralErrorCodes` and `IdentityErrorCodes` (in `Headless.Api.Resources`), and `HttpIdempotencyErrorCodes`.

#### What clients branch on, and what is localized

`type`, `status`, `title`, and every error `code` never vary by culture or release. `title` is a kebab-case identifier from `HeadlessProblemDetailsConstants.Titles` (`entity-not-found`, `validation-problem`), not prose. Clients branch on `code`, `title`, and `type`, never on `detail` or an error `description`.

- **Default details are localized per call.** Every `IProblemDetailsCreator` factory, and `Normalize` when it fills a missing `Detail` for 404, 408, 413, 500, or 501, resolves the default text under `CultureInfo.CurrentUICulture` at the moment it runs. English, formal Arabic (`ar`), and Egyptian Arabic (`ar-EG`) ship with the framework. See [Regional Arabic](utilities.md#regional-arabic). A culture without a translation, or an app without `UseRequestLocalization()`, gets English.
- **Responses written by `UseHeadless()` follow the request culture.** The exception handler, the status-code rewriter, and status-code pages re-apply the culture the request negotiated (`IRequestCultureFeature`), so an Arabic request gets Arabic problem details whether `UseRequestLocalization()` runs before or after `UseHeadless()`. A request that request localization never reached keeps `CurrentUICulture`. Your own `CustomizeProblemDetails` delegates and `IProblemDetailsWriter` implementations do not get this. Read `IRequestCultureFeature` there yourself.
- **A caller-supplied `detail` passes through unchanged.** `creator.Forbidden(detail: "...")` writes exactly that string under every culture, and `Normalize` never replaces a `Detail` that is already set. A decorator that rewrites `Detail` unconditionally overwrites the caller's text.
- **Framework error descriptions are localized the same way.** `GeneralMessageDescriber`, `IdentityMessageDescriber`, `TenancyMessageDescriber`, and the other `*MessageDescriber` factories build a new descriptor per call. Call them per response: a descriptor cached in a `static` keeps the culture of its first caller.
- **Localize your own codes through `IErrorDescriptionLocalizer`.** `Normalize`, which every factory and the `ToHttpResult` and `ToActionResult` bridges run, passes each `ErrorDescriptor` in the `error` and `errors` extensions to the localizer. A returned string replaces the description on a copy of the descriptor: code, severity, and params are kept, and the caller's instance is never changed. `null` keeps the original. `AddHeadlessProblemDetails()` registers a no-op default with `TryAddSingleton`, so register yours before or after it. The localizer is a singleton called concurrently, so read the culture per call.

```csharp
public sealed class AppErrorLocalizer(IStringLocalizer<AppErrors> strings) : IErrorDescriptionLocalizer
{
    public string? Localize(ErrorDescriptor error)
    {
        // Key on the stable code; a missing resource keeps the original description.
        var localized = strings[error.Code];
        return localized.ResourceNotFound ? null : localized.Value;
    }
}

builder.Services.AddLocalization();
builder.Services.AddSingleton<IErrorDescriptionLocalizer, AppErrorLocalizer>();
```

**Verify:** send a request with `Accept-Language: ar` to an endpoint that returns one of your codes, and assert that `errors[].description` is the Arabic text while `errors[].code` is unchanged.

The framework logs problem-details failures by event ID and exception type, never by the localized `detail` or `description`. Log your own failures by `code` for the same reason.

The exception handler covers MVC actions, Minimal API endpoints, and middleware that runs inside `UseExceptionHandler()`. Middleware before it, hosted and background services, and SignalR hubs need their own catch sites.

### Standard middleware order

`UseHeadless()` applies the edge of the pipeline in a fixed order. An earlier step runs outside a later one: it sees the request first and the response last. Turn a step off with its switch in `UseHeadless(options => ...)` (`HeadlessApiDefaultsOptions`).

| Step | Middleware | Switch |
| --- | --- | --- |
| 1 | `UseForwardedHeaders()` with the app's `ForwardedHeadersOptions` from DI, then `TrustForwardedHeadersFromAnyProxy` and `ConfigureForwardedHeaders` | `UseForwardedHeaders` |
| 2 | `UseResponseCompression()` | `UseResponseCompression` |
| 3 | `UseStatusCodePages()`, then `UseStatusCodesRewriter()`, which writes bare 400, 401, 403, and 404 responses as `application/problem+json` | `UseStatusCodePages` |
| 4 | `UseExceptionHandler()`, which maps framework-known exceptions | `UseExceptionHandler` |
| 5 | `UseHttpsRedirection()`, never in Development or Test | `UseHttpsRedirection` |
| 6 | `UseHsts()`, never in Development or Test | `UseHsts` |
| 7 | Adds `Cache-Control: no-cache,no-store,must-revalidate` when the response sets no `Cache-Control` | `SetNoCacheWhenMissingCacheHeaders` |

- Status-code pages run outside the exception handler, so bare status responses from middleware, including the 408s from ASP.NET Core's request-timeout middleware, become problem details.
- HTTPS redirection and HSTS never run in Development or Test, because those hosts usually serve plain HTTP and `TestServer` cannot follow the redirect.
- Middleware you add after `UseHeadless()` runs inside every step, so the exception handler and the status-code rewriter cover it.
- Add `UseRequestLocalization()` after `UseHeadless()` and before any middleware that writes localized text itself, such as `UseRateLimiter()` and `UseAuthorization()`. When a `RouteDataRequestCultureProvider` reads the culture from route values, add it after `UseRouting()` instead.
- Put middleware that must see every request and response before the edge, such as `UseHttpLogging()`, before `UseHeadless()`. It then sees the proxy's address, not the forwarded client's.
- Requirement gates such as `[RequiresFeature]` and permission policies run in `UseAuthorization()`. A host that calls `UseRouting()` without `UseAuthorization()` skips them silently. ASP.NET Core's own startup check covers only `[Authorize]`.
- `UseHeadless()` and `MapHeadlessEndpoints()` are idempotent.

A typical host continues after the edge in this order:

```csharp
app.UseHeadless();
app.UseRequestLocalization(); // before every middleware that writes localized text
app.UseRouting();
app.UseCors(HeadlessCorsConstants.RestrictedCors);
app.UseResponseCaching(); // after UseCors, which must see the request first
app.UseRequestTimeouts();
app.UseHeadlessTenantCatalogResolution(); // only with catalog tenancy: after routing, before authentication
app.UseAuthentication();
app.UseRateLimiter();
app.UseHeadlessSerilogEnrichers(); // after authentication, so the user is known
app.UseHeadlessTenancy();
app.UseAuthorization();
app.UseHeadlessHttpIdempotency();
app.MapHeadlessEndpoints();
app.MapControllers();
```

**Verify:** the host starts. With `Headless.Api.ServiceDefaults`, a host that never called `UseHeadless()`, `UseStatusCodesRewriter()`, or `MapHeadlessEndpoints()` fails at start with an `InvalidOperationException` naming each missing call.

### Idempotency as HTTP middleware

Idempotency is HTTP middleware, not a Mediator pipeline behavior, because only the middleware sees the full HTTP response (status, allowlisted headers, body bytes) it must replay byte for byte. See [idempotency.md § Headless.Idempotency.ResponseReplay](idempotency.md#headlessidempotencyresponsereplay) for setup and the admission contract, and [mediator.md](mediator.md) for the request pipeline.

---

## Headless.Api.Abstractions

Interfaces and contracts for request context, client information, problem details, absolute URLs, and API surfaces. Reference it from a library that must not depend on the full API stack.

### API and behavior

- `IRequestContext`: the request's user, tenant, locale, time zone, correlation ID, and start time.
- `IWebClientInfoProvider`: the client's IP address, user agent, and `DeviceInfo`. `AddHeadless()` also registers an `IAuditClientInfoProvider` over it, so audit entries record the request's IP address and user agent when the writer leaves them unset.
- `IUserAgentParser`: parses a raw `User-Agent` header. `Headless.Api.UserAgent` implements it. Without that package, `AddHeadless()` registers a parser that identifies nothing. See [Headless.Api.UserAgent](#headlessapiuseragent).
- `IRequestedApiVersion`: the requested API version.
- `ITimezoneProvider` and `TimezoneOption`: time-zone enumeration and conversion.
- `IEnumLocaleAccessor`: localized enum display names.
- `IErrorDescriptionLocalizer`: `string? Localize(ErrorDescriptor error)`, the hook that localizes your own error codes. See [What clients branch on, and what is localized](#what-clients-branch-on-and-what-is-localized).
- `IProblemDetailsCreator`: builds normalized RFC 7807 `ProblemDetails`. Every single-error factory takes its type-specific arguments first, then an optional `string? detail` that replaces the default `Detail`, then an optional `ErrorDescriptor? error` written to `Extensions["error"]`: `EndpointNotFound`, `EntityNotFound`, `BadRequest`, `Forbidden`, `Unauthorized`, `RequestTimeout`, `NotImplemented`, `TooManyRequests(retryAfterSeconds, detail, error)`, and `ServiceUnavailable(retryAfterSeconds, detail, error)`. To attach only a descriptor, name it: `EntityNotFound(error: descriptor)`. `Conflict(errors)` and `UnprocessableEntity(errors)` write a descriptor collection to `Extensions["errors"]` and keep the default `Detail`.
  - Never put an entity name or key in an `EntityNotFound` detail. It belongs in server logs.
  - `ServiceUnavailable` is the 503 for a control that fails closed, such as a limiter whose counter store is unreachable. It writes `Extensions["retryAfter"]` only when you pass `retryAfterSeconds`. Set the matching `Retry-After` header yourself, as with `TooManyRequests`.
- `IAbsoluteUrlFactory`: builds absolute URLs from the current request.
- `RequireIfMatchAttribute`: marks a conditional write for the MVC and OpenAPI concurrency profiles.
- `HttpContractMetadata`: endpoint metadata that a middleware or filter you write attaches to describe HTTP behavior the handler's signature does not show. It has `RequestHeaders` (`HttpRequestHeaderDescription(name, required, description)`), `Responses` (`HttpResponseDescription(statusCode, description)`), and `ResponseHeaders` (`HttpResponseHeaderDescription(name, statusCode, description)`, or `(name, minStatusCode, maxStatusCode, description)` for a range such as `200` to `299`, with an optional `Required`). It describes and never enforces. An endpoint may carry several. `Headless.OpenApi` renders them as header parameters, problem responses, and response headers.
- Documentation contracts that `Headless.OpenApi` reads:
  - `IEnvironmentRestrictionMetadata.IsAvailableIn(IHostEnvironment)`: endpoint metadata for an endpoint that answers 404 outside some environments. The endpoint is left out of documents generated there.
  - `IHttpCredentialSchemeOptions.Credentials`: implement it on an authentication scheme's options. Each `HttpCredentialDescription.Header(name, description)`, `.Query(...)`, or `.Authorization(scheme, description)` becomes a security scheme.
  - `TenantCatalogResolutionDescription`: registered by `ResolveFromCatalog(...)`, so a build-time document describes the tenant headers and hosts the running app accepts.

### Install

```bash
dotnet add package Headless.Api.Abstractions
```

### Setup and use

Inject `IRequestContext` to read request-scoped information:

```csharp
public sealed class OrderService(IRequestContext context, IOrderRepository repository)
{
    public async Task<Order> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var userId = context.User.UserId;
        var tenantId = context.Tenant.Id;
        var correlationId = context.CorrelationId;

        return await repository
            .CreateAsync(
                new Order
                {
                    UserId = userId,
                    TenantId = tenantId,
                    CreatedAt = context.StartedAt,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
```

**Verify:** this package registers nothing. `IRequestContext` resolves only in a host that called `AddHeadless()`. Otherwise, activating `OrderService` throws the container's `InvalidOperationException` for the missing service.

### Configuration

#### API surfaces

`IApiSurfaceMetadata` and `[ApiSurface("portal")]` identify an endpoint's API surface. `ApiSurfaceDescriptor` holds the immutable `RoutePrefix`, `DefaultAuthorizationPolicy`, `DefaultTenancyMode`, and `OpenApi` settings. `HttpContext.GetApiSurface()` (in `Headless.Api`) returns the selected endpoint's descriptor after routing. It does not describe the endpoint's effective authorization policy.

`ApiSurfaceTenancyMode` has the values `Unspecified`, `RequireTenant`, `AllowMissingTenant`, and `SkipTenantResolution`. A mode adds metadata defaults only. `RequireTenant` needs the tenancy authorization handler and a policy containing `TenantRequirement`, and selecting the mode registers neither. Skipping resolution does not permit a missing tenant. Explicit endpoint metadata wins over the mode.

### Runtime behavior

None. This is an abstractions-only package.

---

## Headless.Api

The ASP.NET Core building blocks that `Headless.Api.ServiceDefaults` composes: service registration helpers, middleware, problem details, CORS, tenancy, and API surfaces. ASP.NET Core Identity and user-agent parsing ship in `Headless.Api.Identity` and `Headless.Api.UserAgent`. The framework issues no JWTs: a host that needs them registers ASP.NET Core's `AddJwtBearer` itself.

### API and behavior

- `AddHeadlessProblemDetails()`: registers `IProblemDetailsCreator`, a no-op `IErrorDescriptionLocalizer` (`TryAddSingleton`), the framework exception handler, and the `CustomizeProblemDetails` hook that normalizes every response. `AddHeadlessProblemDetails(problems => ...)` adds the application's own mappings (`MapException<TException>(status, describe)`, `MapError<TError>(status)`, read-only `MappedStatusCodes`). See [Map your own exceptions and errors](#map-your-own-exceptions-and-errors).
- `GeneralMessageDescriber.TenantRequired()` (`g:tenant_required`), `CrossTenantWrite()` (`g:cross_tenant_write`), `TenantMismatch()` (`g:tenant_mismatch`), `InvalidRequestType()` (`g:invalid_request_type`), and `InvalidParameterValue(parameter)` (`g:invalid_parameter_value`, which MVC answers when a request value cannot be bound): per-call localized descriptors. Their codes are on `GeneralErrorCodes`.
- `AddHeadlessApiResponseCompression()`: Brotli and Gzip at `Fastest` level. Adds `application/problem+json`, `image/svg+xml`, and `image/x-icon` to the compressed MIME types.
- `AddHeadlessAntiforgery()`: registers antiforgery services.
- `AddStatusCodesRewriterMiddleware()` and `UseStatusCodesRewriter()`: rewrite bare 400, 401, 403, and 404 responses as `application/problem+json`.
  - The 400 case covers a Minimal API body that cannot be bound while `RouteHandlerOptions.ThrowOnBadRequest` is off (the default outside Development), so it answers with the same `bad-request` problem as the thrown path.
  - When authorization forbids a request and a handler called `Fail(new AuthorizationFailureReason(...))`, the 403 problem carries the reasons' messages as `detail`. The caller sees those messages, so keep internal detail out of them.
  - A challenge (401) is unchanged. A handler that set its own rejection keeps its response.
  - It wraps the registered `IAuthorizationMiddlewareResultHandler`. A result handler registered after this call replaces the wrapper.
- `IStatusCodeRejectionFeature`: lets a handler that fails a request (an authorization handler, a filter) supply the response the rewriter writes. Set it with `httpContext.TrySetStatusCodeRejection(rejection)`, which keeps the first rejection set. `TryWriteResponseAsync` returns `false` to fall back to the bare-status rewrite. The tenant identifier mismatch rejection always replaces an earlier rejection, because it must match the unknown-tenant response byte for byte.
- `ConfigureHeadlessDefaultApi()`: Kestrel limits (no `Server` header, 30 MB body, 40 headers), HSTS (365-day max-age, subdomains, preload), lowercase route URLs with no trailing slash, form limits (4 MB value, 16 KB multipart headers, 30 MB multipart body), and the `self` liveness health check tagged `live`.
- `AddHeadlessJsonService()`: `IJsonOptionsProvider`, `IJsonSerializer`, `ITextSerializer`, and `ISerializer`, all `TryAddSingleton`, so a registration you make first wins.
- `AddHeadlessTimeService()`: `TimeProvider.System` and `ITimezoneProvider` as `TzConvertTimezoneProvider`, both `TryAddSingleton`. Windows and IANA ID mapping comes from the TimeZoneConverter package's embedded CLDR data. Offsets and DST rules come from the host OS time-zone database.
- `DefaultEnumLocaleAccessor`: the `IEnumLocaleAccessor` implementation.
- `MapHostRedirects(mainHost, redirectHosts)`: maps an anonymous catch-all `GET` that answers 301 to the same path and query under `mainHost` for requests whose host is in `redirectHosts`. The entries are host names (`www.example.com`, `example.net:8080`), not URLs.
- `UseNoCacheWhenMissingCacheHeaders()`: adds `Cache-Control: no-cache,no-store,must-revalidate` when the response sets no `Cache-Control`.
- HTTP tenant resolution: `ResolveFromClaims()`, `UseHeadlessTenancy()`, `[SkipTenantResolution]`, and `.SkipTenantResolution()`.
- HTTP tenant catalog resolution (pre-authentication): `ResolveFromCatalog(...)`, `UseHeadlessTenantCatalogResolution()`, and `ITenantIdentifierSource` returning `TenantIdentifierSourceResult` (`None`, `Found`, or `Invalid`). The `HeadlessTenantCatalogResolutionBuilder` members are `AddHostSource(...)`, `AddRouteSource(...)`, `AddHeaderSource(...)`, `AddSource<T>()`, `AddSource(instance)`, and `AddSource(Func<HttpContext, string?>)`. See [Tenant identifier sources](#tenant-identifier-sources).
- HTTP tenant authorization: `TenantRequirement`, `[AllowMissingTenant]`, `.AllowMissingTenant()`, `[RequireTenant]`, and `.RequireTenant()`.
- `AddHeadlessAuthorizationDenialAudit<TContext>()`: opt-in. Writes an `authorization.challenged` or `authorization.forbidden` audit entry (method, route template, policy names, never the body or path) through `IAuditLogWriter<TContext>`. It needs an audit log storage provider. See [Authorization denial entries](audit-log.md#authorization-denial-entries).
- `AddHeadlessCors(...)`, `AddHeadlessAllowAnyCors(...)`, and `AddHeadlessCorsOriginSource<TSource>(...)`: opt-in CORS registration. `AddHeadless()` calls none of them. See [CORS](#cors).
- Single-claim authorization: `RequireSingleClaimValue(claimType, value)`, `RequireSingleClaim(claimType, predicate)`, and `SingleClaimRequirement`.
- HTTP edge rate limiting: `RateLimiterOptions.UseHeadlessProblemDetails()` rejects with a 429 problem (`g:rate_limit_exceeded`, `Retry-After`, `Cache-Control: no-store`). `HttpContext.GetIpAddressPartition(ipv6PrefixLength = 64)` keys anonymous callers by IPv4 address or IPv6 /64. The application owns the limiter. See [HTTP edge limits](rate-limiting.md#http-edge-limits).
- `AddHeadlessApiDiagnosticListeners()`: logs Kestrel bad-request events and middleware-analysis events. Dispose the returned handle to unsubscribe.
- `IServiceCollection.FreezeHeadlessApiSurfaces()`: freezes the set of API surfaces and returns their OpenAPI document names, so a later `AddHeadlessApiSurface(...)` throws. `AddHeadlessApiSurfaceDocuments()` and `AddHeadless()` call it. Call it from a custom document provider while the service collection is still writable.
- The framework does not write `Server-Timing`. Use the community [`Lib.AspNetCore.ServerTiming`](https://www.nuget.org/packages/Lib.AspNetCore.ServerTiming) package for it.

### Design constraints

- The exception handler and the status-code rewriter run your `ProblemDetailsOptions.CustomizeProblemDetails` callback once, through ASP.NET Core's `IProblemDetailsService`. `IProblemDetailsCreator` factories do not run it. `Headless.Api.Mvc` runs it once for an MVC `ObjectResult` that carries a Headless problem.
- The exception handler honors `Accept` quality values. A request that rejects JSON, or rejects `application/problem+json` with `q=0`, gets no JSON body from it and falls to the next handler.
- Batch `IFormFile.SaveAsync(...)` keeps result order and copies at most `Environment.ProcessorCount` file streams at once.
- `IFormFile.GetAllBytesAsync(CancellationToken cancellationToken = default)` honors cancellation while it buffers the upload.
- `HttpRequest.IsAjaxRequest()` is `true` only for the `X-Requested-With: XMLHttpRequest` header. A query-string parameter of that name does not count, because anyone can put one in a link.

### Install

```bash
dotnet add package Headless.Api
```

### Setup and use

Compose the primitives without ServiceDefaults:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Clock, build-information, and HTTP-context services the problem-details factory resolves; AddHeadless() registers them.
builder.Services.AddHeadlessTimeService();
builder.Services.AddHeadlessHostIdentity();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHeadlessProblemDetails();
builder.Services.AddHeadlessApiResponseCompression();
builder.Services.ConfigureHeadlessDefaultApi(); // Kestrel limits + HSTS + health check + routing
builder.Services.AddStatusCodesRewriterMiddleware();

var app = builder.Build();
app.UseResponseCompression();
app.UseStatusCodesRewriter(); // before UseExceptionHandler
app.UseExceptionHandler();
app.MapHealthChecks("/health");
app.Run();
```

**Verify:** `GET /health` answers 200 with the `self` check, and a request to an unmapped path answers 404 with `Content-Type: application/problem+json`.

Claim-based HTTP tenant resolution:

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy.Http(http => http.ResolveFromClaims()).Authorization(auth => auth.RequireTenant())
);

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new TenantRequirement())
        .Build();
});

// In pipeline — after UseAuthentication(), before UseAuthorization()
app.UseAuthentication();
app.UseHeadlessTenancy();
app.UseAuthorization();

// Opt out of tenant claim extraction for a single endpoint
app.MapGet("/webhook", handler).SkipTenantResolution().AllowMissingTenant();
```

**Verify:** startup fails when neither `DefaultPolicy` nor `FallbackPolicy` contains `TenantRequirement`, or when `UseHeadlessTenancy()` is missing. An authenticated request without a tenant claim answers 403 with `error.code` `g:tenant_required`.

Single-claim policies require exactly one claim of the type and check its value. ASP.NET Core's `RequireClaim` passes when any claim of the type matches:

```csharp
builder.Services.AddAuthorization(options =>
{
    // Passes only when the principal has exactly one 'tier' claim whose value equals 'bronze' (ordinal)
    options.AddPolicy("BronzeOnly", policy =>
        policy.RequireSingleClaimValue("tier", "bronze")
    );

    // Passes only when the principal has exactly one 'age' claim satisfying the predicate
    options.AddPolicy("Adult", policy =>
        policy.RequireSingleClaim("age", value => int.TryParse(value, CultureInfo.InvariantCulture, out var age) && age >= 18)
    );
});
```

**Verify:** in a test, a principal with two `tier` claims gets 403 from an endpoint under `BronzeOnly`.

Identifier-based (pre-authentication) tenant resolution through the tenant catalog. Sources run in registration order:

```csharp
builder.Services.AddHeadlessCaching(caching => caching.UseInMemory()); // catalog prerequisite

builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Catalog(catalog =>
            catalog
                .Configure(options => options.IgnoredIdentifiers.Add("www")) // www.example.com -> host context
                .UseInMemory(options => options.Tenants.Add(new TenantInfo(id: "ten_123", identifier: "acme", name: "Acme", isEnabled: true)))
        )
        .Http(http =>
            http.ResolveFromCatalog(sources =>
                sources
                    .AddHostSource("{tenant}.example.com") // acme.example.com -> "acme"
                    .AddRouteSource()                      // /{tenant}/orders  -> route value "tenant"
                    .AddHeaderSource()                     // X-Tenant: acme
                    .AddSource(context => context.Request.Query["tenant"].ToString()) // null/blank -> not mine
            )
        )
);

app.UseForwardedHeaders();                // behind a proxy only, with KnownProxies/KnownNetworks configured
app.UseStatusCodesRewriter();             // must wrap UseAuthorization()
app.UseRouting();
app.UseCors();                            // before resolution so preflights short-circuit
app.UseHeadlessTenantCatalogResolution(); // after UseRouting, before UseAuthentication
app.UseAuthentication();
app.UseHeadlessTenancy();
app.UseAuthorization();

// [SkipTenantResolution] also bypasses catalog resolution and rejection for the endpoint.
```

**Verify:** startup fails with `CATALOG_RESOLUTION_WITHOUT_REWRITER` when `UseStatusCodesRewriter()` is missing. A request to `unknown.example.com` answers 404 with `error.code` `g:tenant_resolution_failed`.

A custom source implements `ITenantIdentifierSource` and returns `TenantIdentifierSourceResult.None`, `.Found(value)`, or `.Invalid`. The delegate overload cannot return `Invalid`.

### Configuration

#### Tenant identifier sources

Each built-in source has a validated options type. Startup fails on an invalid value.

| Options | Property | Default | Notes |
|---|---|---|---|
| `HostTenantIdentifierSourceOptions` | `Templates` | `[]` (at least one required) | Matched in order against the post-forwarding `Request.Host`. `{tenant}` captures one label, `?` matches one label, `*` matches zero or more labels, and a bare `{tenant}` captures the whole host. Case-insensitive, port excluded, one trailing dot stripped. IP literals and hosts over 253 characters yield `None`. Each template has a 100 ms match timeout. The string overload appends to the list. |
| `RouteTenantIdentifierSourceOptions` | `RouteValueName` | `tenant` (`DefaultRouteValueName`) | Read from `Request.RouteValues`. A non-string value is `None`. Needs `UseHeadlessTenantCatalogResolution()` after `UseRouting()`. The last contribution wins. |
| `RouteTenantIdentifierSourceOptions` | `PromoteAmbientRouteValue` | `true` | Keeps the current request's tenant segment in generated links. Read per call. |
| `HeaderTenantIdentifierSourceOptions` | `HeaderNames` | `["X-Tenant"]` (`DefaultHeaderName`) | Names must be HTTP tokens. A requested name replaces the untouched default, and later names append (`AddHeaderSource("X-Legacy")` reads only `X-Legacy`). A second value across all configured names is `Invalid`. A single line `a,b` is one value. Blank lines count as absent. |

Each source also accepts an `IConfiguration` section: `AddHostSource(section)` with a `Templates` array, `AddRouteSource(section)` with `RouteValueName`, and `AddHeaderSource(section)` with a `HeaderNames` array. A listed array replaces the untouched default. A section without it keeps the default.

Source behavior:

- **Order.** Sources run in first-registration order, and the first `Found` result wins. `AddSource<T>()` and the built-in sources deduplicate by type: the first call fixes the position, and every call's options still apply. `AddSource(instance)` and the delegate overload always append.
- **Results.** `Invalid` (present but ambiguous input, such as two `X-Tenant` values) rejects with 400 `g:tenant_identifier_invalid` before any catalog call, and later sources do not run. `Found` with a blank value becomes `None`. Do not trim, lowercase, or validate the shape inside a source: the catalog does that.
- **Header source and perimeter controls.** The header source can select only an existing enabled tenant, and the mismatch check still rejects an authenticated caller whose tenant claim disagrees. It does bypass any WAF rule, mTLS policy, IP allowlist, or CDN configuration bound to a tenant's hostname. Where hostnames carry such controls, register the host source first, or strip or overwrite the header at the edge.
- **Caching headers.** Every catalog rejection carries `Cache-Control: no-store`. Every consult of the header source appends the configured names to `Vary`. A CDN cache key that omits the host breaks host tenancy, and many CDNs refuse to cache on unknown `Vary` values.
- **Forwarded host.** The host source reads the post-forwarding `Request.Host`, never `X-Forwarded-Host`.
- **Logs.** No tenancy log event carries a raw host, route value, header value, or identifier. A host template match timeout maps to `Invalid` and logs one warning per process that names only the template.
- **Route links.** `AddRouteSource` makes `Url.Action`, `GetPathByAction`, and `GetPathByName` keep the current request's `{tenant}` segment, which ASP.NET Core would otherwise drop. An explicit `tenant` value always wins. `PromoteAmbientRouteValue = false` turns this off per call. A link from a tenant request to an endpoint without a `{tenant}` segment carries the value as a query string (`/plain?tenant=acme`). A link from a request with no tenant segment to a tenant endpoint returns `null` unless you pass a value.
- **Middleware order.** Placed before `UseRouting()`, `UseHeadlessTenantCatalogResolution()` still resolves host, header, and delegate sources but loses the `[SkipTenantResolution]` opt-out (one warning per host). A route source then finds nothing and every request runs as host context. The host logs that once at Error level as `HEADLESS_TENANT_CATALOG_ROUTE_SOURCE_MISORDERED`, but only after a request reaches the middleware, so a host whose requests all 404 or all reject never logs it.
- **Mismatch-check limits.** A principal with no tenant claim, or an endpoint where authorization never runs (`[AllowAnonymous]`, or no policy and no fallback policy), passes a source-selected tenant unchecked. Map such credentials to a tenant claim so the check applies, and never derive authorization on such endpoints from the ambient tenant.

Ignored identifiers (for example `www`) live on `TenantCatalogOptions.IgnoredIdentifiers` in `Headless.MultiTenancy`. No source has its own list. An ignored identifier, an apex host, and an unmatched host all fall through to host context with no store call, so put every endpoint that needs a tenant under `TenantRequirement`. A whole-host (bare `{tenant}`) template needs `TenantCatalogOptions.MaxIdentifierLength = 253` and an `IdentifierPattern` shaped for hostnames that carries a match timeout. Both settings are catalog-wide, so mixing a subdomain source with a custom-domain source relaxes the subdomain shape too. Bound hostile identifier cardinality with your own rate limiting: negative caching covers only repeated identifiers.

#### CORS

Write a policy in code with ASP.NET Core's `AddCors`. Headless adds a policy bound from configuration and validated at startup, and origins approved at request time. Each `AddHeadlessCors([policyName,] IConfiguration | Action<HeadlessCorsOptions>)` call registers one named `HeadlessCorsOptions` policy, validated with `ValidateOnStart`. Without a name it registers `HeadlessCorsConstants.RestrictedCors`. Registering the same name twice throws `InvalidOperationException`. `AddHeadlessAllowAnyCors(Action<HeadlessCorsOptions>?)` registers `HeadlessCorsConstants.AllowAnyCors`, which admits any origin and never credentials.

| Property | Default | Notes |
|---|---|---|
| `AllowedOrigins` | `[]` | Exact serialized origins: scheme, host, optional port. Any scheme with a host is accepted, so `capacitor://localhost` and `ionic://localhost` work. |
| `AllowedOriginTemplates` | `[]` | `https://*.example.com` matches any subdomain at any depth with the same scheme and port, never the bare suffix. |
| `AllowAnyOrigin` | `false` | Admits every origin. Excludes origin lists, origin sources, and credentials. |
| `AllowAnyOriginOutsideDevelopment` | `false` | Required for an `AllowAnyOrigin` policy in every environment other than Development, whatever the policy's name. Set it only for a public API that any site may call. |
| `AllowCredentials` | `false` | Sends `Access-Control-Allow-Credentials: true`. |
| `AllowedHeaders` | `[]` = any | A `*` entry also means any. |
| `AllowedMethods` | `[]` = any | A `*` entry also means any. |
| `ExposedHeaders` | `[]` | Response headers scripts may read, beyond the framework's own. |
| `ExposeFrameworkHeaders` | `true` | Also exposes `HeadlessCorsOptions.FrameworkExposedHeaders`: `ETag`, `Location`, `Retry-After`, `Content-Disposition`, `Idempotent-Replayed`. Pass that list to `WithExposedHeaders` in an `AddCors` policy. |
| `MaxAge` | 10 minutes | Preflight cache lifetime. Without the header Chromium caches a preflight for five seconds. `00:00:00` sends `0` and turns caching off. Browsers cap it (Chromium at two hours). |

```csharp
builder.Services.AddHeadlessCors(builder.Configuration.GetSection("Cors"));    // RestrictedCors
builder.Services.AddHeadlessCors("public", o => { o.AllowAnyOrigin = true; o.AllowAnyOriginOutsideDevelopment = true; o.AllowedMethods = ["GET"]; });
builder.Services.AddHeadlessCorsOriginSource<TenantDomainOriginSource>();       // tenant custom domains

// A policy written in code needs no Headless options; the origin source still applies to it.
builder.Services.AddCors(o => o.AddPolicy("storefront", p => p
    .WithOrigins("https://shop.example.com")
    .WithExposedHeaders([.. HeadlessCorsOptions.FrameworkExposedHeaders])));
builder.Services.AddHeadlessCorsOriginSource<TenantDomainOriginSource>("storefront");

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHeadlessAllowAnyCors(o => o.ExposedHeaders = ["ETag", "Link"]);
}

app.UseRouting();
app.UseCors();                                         // policies chosen per endpoint below
app.MapGroup("/api").RequireCors(HeadlessCorsConstants.RestrictedCors);
app.MapGroup("/public").RequireCors("public");
```

```json
{
  "Cors": {
    "AllowedOrigins": [ "https://app.example.com", "http://localhost:8081" ],
    "AllowedOriginTemplates": [ "https://*.tenants.example.com" ],
    "AllowCredentials": true
  }
}
```

**Verify:** an invalid policy fails startup with `OptionsValidationException`. A preflight `OPTIONS` request with `Origin: https://app.example.com` and `Access-Control-Request-Method: POST` answers with `Access-Control-Allow-Origin: https://app.example.com`.

**Origins known only at request time.** Implement `ICorsOriginSource.IsOriginAllowedAsync(origin, context, cancellationToken)` and register it per policy with `AddHeadlessCorsOriginSource<TSource>(policyName = RestrictedCors, lifetime = Scoped)`. It resolves from the request's services, so a scoped source may use a `DbContext`. The policy may come from `AddCors` or `AddHeadlessCors`. Register a custom `ICorsPolicyProvider` before the source.

- The source runs only when a request carries exactly one `Origin` header that the policy's static origins do not admit. It never runs for the opaque origin `null` or for another policy.
- An approved origin gets that policy's headers, methods, and credentials setting.
- Every allowed response of a policy with a source carries `Vary: Origin`, so a shared cache cannot replay it to another origin.
- Preflight and actual requests both call the source, so cache your lookups.

**Native and mobile clients.** Browsers enforce CORS, so it neither blocks nor protects non-browser callers. React Native on iOS and Android sends no `Origin`, and the CORS middleware passes such requests through unchanged. Anything running in a browser engine needs an origin: Expo and React Native Web (`http://localhost:8081` for the Metro dev server, `http://localhost:19006` for older Expo web, then the deployed web origin), and Capacitor or Ionic webviews. A `react-native-webview` page loaded from a local file has the opaque origin `null`, which no policy can safely admit. Serve that content from an `http(s)` origin instead. WebSocket upgrades bypass CORS. Restrict them with `WebSocketOptions.AllowedOrigins`.

**Origins per environment.** Configuration merges arrays by index, not by replacement. An environment variable `Cors__AllowedOrigins__0` replaces only the first entry, and every later entry in `appsettings.json` still reaches production. Keep development origins such as `http://localhost:5173` in `appsettings.Development.json`, and leave only the origins every environment needs in the base file. The validator cannot catch this, because a loopback origin is legitimate for some production clients, such as a Capacitor webview on `http://localhost`.

Startup fails with `OptionsValidationException` when:

- A policy has no `AllowedOrigins`, no `AllowedOriginTemplates`, no origin source, and no `AllowAnyOrigin`.
- `AllowAnyOrigin` is combined with origin lists, an origin source, or `AllowCredentials`, or is registered outside Development without `AllowAnyOriginOutsideDevelopment`.
- An origin contains `*`. Wildcards belong in `AllowedOriginTemplates`, and any-origin access belongs to `AllowAnyOrigin`.
- An origin or template is not a bare `scheme://host[:port]` origin: it carries a path or trailing slash, user info, a query, a fragment, surrounding whitespace, or an explicit default port (`https://app.example.com:443`), has no host, is a `file:` URL, or is not absolute (including the literal `null`). The browser's `Origin` header never carries these parts, so such an entry would match nothing.
- A template does not start with `<scheme>://*.`, has a second `*`, or has a literal suffix of fewer than two labels (`https://*`, `https://*.`, `https://*.com`). A two-label public suffix such as `https://*.co.uk` passes and admits every site under it. Never configure one.
- A header, method, or exposed-header entry is blank, or `MaxAge` is negative.

#### API surfaces

An API surface is a named set of endpoints that share routing, authorization, and tenancy defaults, plus an OpenAPI document. MVC, Minimal API, telemetry, and OpenAPI read the same definitions.

```csharp
using Headless.Api;

builder.Services.AddHeadlessApiSurface("portal", surface =>
{
    surface.RoutePrefix = "api/portal";
    surface.DefaultAuthorizationPolicy = "tenant";
    surface.DefaultTenancyMode = ApiSurfaceTenancyMode.RequireTenant;
});
```

**Verify:** after routing, `HttpContext.GetApiSurface()` returns the `portal` descriptor for an endpoint mapped under the surface. An invalid definition fails at the `AddHeadlessApiSurface` call.

Registration:

- `AddHeadlessApiSurface(name, configure)` runs its callback immediately and validates the definition. Changes to a builder you keep after the call have no effect.
- `AddHeadlessApiSurfaces(surfaces => surfaces.Add("portal").Add("console"))` validates the whole batch before it registers any surface. Both methods return `IServiceCollection` and may be combined.
- Register every surface before `AddHeadless()` or a parameterless OpenAPI surface registration. Document inference closes registration, and a later addition throws.
- Surface and document names are case-insensitive and contain only ASCII letters, digits, periods, hyphens, or underscores. `.` and `..` are invalid. The surface names `unknown`, `unclassified`, and `infrastructure` are reserved. Duplicate names and invalid configuration fail validation.
- The document name defaults to the lowercase surface name, and the title to `<surfaceName> API`. Override `surface.OpenApi.DocumentName` and `.Title` only when needed.
- `surface.OpenApi.StandardProblemResponses` (`null` by default, inheriting the API-wide setting) turns the 404, 409, and 422 problem responses on or off in the surface's documents. An endpoint, group, controller, or action marker overrides it.

Defaults:

- `DefaultAuthorizationPolicy` adds a named policy alongside the endpoint's own policies. `[AllowAnonymous]` and `.AllowAnonymous()` still bypass authorization.
- `DefaultTenancyMode = ApiSurfaceTenancyMode.RequireTenant` needs a policy containing `TenantRequirement`, the tenant authorization handler, and the current-tenant services. Configure them through `AddHeadlessTenancy(...)`. The mode alone enforces nothing.
- Explicit `RequireTenant` or `AllowMissingTenant` metadata on a controller or endpoint wins over surface defaults, and also cancels a surface's skip-resolution default. `SkipTenantResolution` skips tenant extraction. It does not permit a missing tenant.

Lookup:

- Surfaces need no middleware. `HttpContext.GetApiSurface()` returns `null` for an unmatched or unmarked endpoint and follows endpoint changes during re-execution. A marked endpoint whose surface is not registered throws.
- `ApiSurfaceRegistry.GetRequiredSurfaceForDocument(documentName)` finds the surface that owns a document, case-insensitively, and throws when none does.
- With OpenTelemetry enabled in `Headless.Api.ServiceDefaults`, completed request spans carry `headless.api.surface.name`: `unknown` for unmatched requests and `unclassified` for endpoints without surface metadata. The tag reflects the endpoint at response completion and is not set during authentication. A telemetry callback that runs after the request's services are disposed must capture `ApiSurfaceRegistry` from the host services and read the endpoint's `IApiSurfaceMetadata`, because `GetApiSurface()` uses request services.

#### Map your own exceptions and errors

Exception mapping from `AddHeadlessProblemDetails()`:

| Exception | Response |
|-----------|----------|
| `MissingTenantContextException` | 403 with `error.code: g:tenant_required` |
| `TenantMismatchException` (thrown by `ICurrentTenant.EnterOrVerify`) | 403 with `error.code: g:tenant_mismatch`. Neither tenant id reaches the response |
| `CrossTenantWriteException` | 409 with `error.code: g:cross_tenant_write` |
| `UnauthorizedException` | 401. A descriptor's description becomes `detail` |
| `ForbiddenException` | 403 with `error`. Its description becomes `detail` |
| `ConflictException` | 409 with `errors` |
| `TooManyRequestsException` | 429 with a `Retry-After` header (whole seconds, rounded up, at least 1), `retryAfter`, and an optional `error` |
| `FluentValidation.ValidationException` | 422 with field errors |
| `EntityNotFoundException` | 404 |
| EF Core `DbUpdateConcurrencyException` | 409 with a concurrency-failure error |
| EF Core `DbUpdateException` whose provider exception is a unique-constraint violation: PostgreSQL SQLSTATE `23505`, SQL Server error `2627` or `2601`, SQLite extended code `2067` or `1555` (`RelationalFaults.IsUniqueViolation` in `Headless.Extensions` tests the same) | 409 with `errors[].code: g:unique_violation`. Event 5013 (Warning) logs only the provider exception type and SQLSTATE, never the message, which on SQL Server carries the duplicate value |
| `LockAcquisitionTimeoutException` from `Headless.DistributedLocks` | 503 with `error.code: g:resource_busy` and no `Retry-After`, because the lock wait says nothing about when the holder finishes. The resource name stays in the logs (event 5014) |
| `ServiceUnavailableException` and derived types, such as `AttemptLimiterUnavailableException` | 503 with `Cache-Control: no-store`, the exception's `Error` descriptor when it has one, and no `Retry-After`. The message and inner exception stay in the logs (event 5016). Throw it, or a derived type, when a dependency the request needs cannot answer. Keep its message safe to log |
| `TimeoutException` | 503 with no `Retry-After`. A timeout while serving the request (a cache factory, Redis, a database command) is the server's wait running out. A 408 would invite clients to resend a request whose writes may have committed. Only ASP.NET Core's request-timeout middleware answers 408 |
| `NotImplementedException` | 501 |
| `BadHttpRequestException` (a Minimal API body that cannot be bound while `ThrowOnBadRequest` is on, as in Development, an unsupported content type, or an oversized body) | Its `StatusCode`: 400 `bad-request`, or 413 `payload-too-large`. The exception message, which names the failed parameter or the JSON parse position, stays out of the body and is logged at Information (event 5015) |
| `OperationCanceledException`, or an inner one at any depth, when `HttpContext.RequestAborted` is the source | 499 with no body |

An exception you mapped answers with its mapped status. The handler passes every other exception on, and the host default or a later handler renders it.

Map your own exceptions and result errors with `AddHeadlessProblemDetails(problems => ...)`. It is safe to call alongside `AddHeadless()`, and the mappings from every call add up:

```csharp
builder.Services.AddHeadlessProblemDetails(problems =>
{
    // Thrown anywhere in a request: 402 with the descriptor in `error` and its description as `detail`.
    problems.MapException<PaymentDeclinedException>(
        StatusCodes.Status402PaymentRequired,
        e => new ErrorDescriptor("billing:declined", e.Reason)
    );

    // Returned as ApiResult.Fail(new QuotaExceededError()): 429 instead of the default 409.
    problems.MapError<QuotaExceededError>(StatusCodes.Status429TooManyRequests);
});
```

**Verify:** an endpoint that throws `PaymentDeclinedException` answers 402 with `error.code` `billing:declined`.

- A mapping covers the registered type and every type derived from it. The most derived registration wins, and registering a type again replaces its mapping.
- Without `describe`, a mapped exception writes only the standard fields. Its message never reaches the response. A mapped error always carries its `ToErrorDescriptor()`.
- Mapping a failure the framework already maps throws `InvalidOperationException`: every exception in the table above or derived from one, and `NotFoundError`, `ValidationError`, `ForbiddenError`, `UnauthorizedError`, `ConflictError`, and `AggregateError`. The framework's statuses never change.
- The status must be 400 to 599. `Headless.OpenApi` lists every mapped status on every operation, under the same switch as 404, 409, and 422. See [openapi.md](openapi.md#headlessopenapi).

`TenantRequirement` and `[AllowMissingTenant]` work whether authorization passes the `HttpContext` or, under the `Microsoft.AspNetCore.Authorization.SuppressUseHttpContextAsAuthorizationResource` switch, the `Endpoint` as its resource.

### Runtime behavior

- `AddHeadlessProblemDetails()` registers `IHttpContextAccessor`.
- `AddHeadlessCors(...)` and `AddHeadlessCorsOriginSource(...)` call `AddCors()`. An origin source decorates `ICorsPolicyProvider` once, and an approved origin gets a per-request policy that admits exactly that origin.
- `AddRouteSource` calls `AddRouting()`.
- `ConfigureHeadlessDefaultApi()` configures Kestrel limits, turns off the `Server` header, sets lowercase routes with no trailing slash, sets form and HSTS options, and registers the `self` liveness check tagged `live`.
- `AddHeadlessApiResponseCompression()` configures the Brotli and Gzip providers.

---

## Headless.Api.ServiceDefaults

The one-line bootstrap for Headless APIs. It composes the `Headless.Api` primitives with Aspire-style host conventions: OpenTelemetry, OpenAPI, service discovery, and HttpClient resilience.

### API and behavior

- `AddHeadless()`: registers every primitive and convention in one call.
- `UseHeadless()`: applies the edge middleware in a fixed order. See [Standard middleware order](#standard-middleware-order).
- `MapHeadlessEndpoints()`: maps `/health`, `/alive`, the OpenAPI JSON documents, and static assets.
- `MapHeadlessHostManifest(pattern, authorizationPolicy)`: opt-in host manifest endpoint, behind authorization by default.
- OpenTelemetry logging, metrics, and tracing. Traces and meters subscribe to `Headless.*`, the application name (`IHostEnvironment.ApplicationName`), and every source and meter under it (`{ApplicationName}.*`). Name your `ActivitySource` and `Meter` like `MyApp.Api.Orders` and you need no `AddSource` call.
- OpenAPI document registration and mapping, service discovery, HttpClient resilience, MVC and Minimal API JSON defaults, and ASP.NET Core source-generated input validation.

### Install

```bash
dotnet add package Headless.Api.ServiceDefaults
```

### Setup and use

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register all framework API services + Aspire conventions.
// Also applies global defaults (regex timeout, FluentValidation) via ConfigureGlobalSettings().
builder.AddHeadless();

var app = builder.Build();

// Optional: Add diagnostic listeners for debugging
using var _ = app.AddHeadlessApiDiagnosticListeners();

app.UseHeadless();
app.MapHeadlessEndpoints();

app.Run();
```

**Verify:** `GET /alive` answers 200, and `GET /health` answers a JSON body with `status` and a `results` entry for `self`. A host that skips `UseHeadless()` or `MapHeadlessEndpoints()` fails at start with an `InvalidOperationException` naming each missing call. When the host runs through `RunHeadlessAsync(args)`, `dotnet MyApp.dll check` runs the health checks once and exits non-zero on an `Unhealthy` check. See [Health check command](utilities.md#health-check-command).

The middleware-analysis listener logs only after `services.AddMiddlewareAnalyzerFilter()` is registered.

### Configuration

#### Pipeline and Endpoint Behavior

`UseHeadless(options => ...)` takes `HeadlessApiDefaultsOptions`: one `Use*` switch per edge step, plus `TrustForwardedHeadersFromAnyProxy` and `ConfigureForwardedHeaders`. The order is fixed. [Standard middleware order](#standard-middleware-order) lists the steps and a full host pipeline.

Forwarded headers:

- The step starts from the app's `IOptions<ForwardedHeadersOptions>`, so `KnownProxies`, `KnownIPNetworks`, `ForwardLimit`, and `ForwardedHeaders` set through `builder.Services.Configure<ForwardedHeadersOptions>(...)` apply, before or after `AddHeadless()`.
- When the app leaves `ForwardedHeaders` unset, the step uses `XForwardedFor | XForwardedProto | XForwardedHost`.
- To process no forwarded headers, set `UseForwardedHeaders = false`.
- `TrustForwardedHeadersFromAnyProxy` (default `false`) and `ConfigureForwardedHeaders` adjust the same options when `UseHeadless()` runs. Turn the trust switch on only when untrusted clients cannot reach the app directly. Otherwise clients can spoof the forwarded host and scheme.
- Do not add a second `app.UseForwardedHeaders()`. Each pass consumes one more `X-Forwarded-For` hop, and a pass with default options trusts only loopback.

Antiforgery is opt-in and app-owned. `AddHeadless()` registers antiforgery services only when `options.Antiforgery.Enabled = true`, and `UseHeadless()` never calls `app.UseAntiforgery()`. A cookie-authenticated app calls it after `UseAuthentication()` and `UseAuthorization()`, so the middleware sees the authenticated principal. Bearer-token APIs have no CSRF exposure and leave it off.

`MapHeadlessEndpoints(options => ...)` takes `HeadlessApiDefaultEndpointOptions`:

- `/health` (`HealthPath`) runs every health check and writes a JSON body with `status` and per-check `results`. `/alive` (`AlivePath`) runs only checks tagged `live` (`AliveTag`).
- Provider packages (SQL, EF Core contexts, Redis, NATS, Azure blobs) and the Messaging and Jobs hosts add readiness checks that appear on `/health` but not on `/alive`. See [Health checks](utilities.md#health-checks).
- The health endpoints are named, left out of OpenAPI documents, and anonymous by default (`AllowAnonymous`). They carry `DisableRateLimiting` metadata, so neither a global limiter nor an endpoint policy rejects a probe. See [HTTP edge limits](rate-limiting.md#http-edge-limits).
- The OpenAPI documents are ordinary endpoints, so authorization applies: under a fallback authorization policy they answer 401. To publish them anonymously, set `OpenApi.Enabled = false`, register the documents yourself, and map them with `app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous()`.

#### Host manifest endpoint

`MapHeadlessHostManifest(pattern, authorizationPolicy = null)` maps `GET` at `pattern` to the host manifest JSON that `RunHeadlessAsync(args)` prints for `describe`: what each feature registered in this host, such as Messaging consumers and Jobs functions. [Utilities](utilities.md#host-manifest) owns the document. `MapHeadlessEndpoints()` does not map it.

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("operators", policy => policy.RequireRole("operator"));

app.MapHeadlessHostManifest("/_headless/describe", authorizationPolicy: "operators");
```

**Verify:** an operator's `GET /_headless/describe` answers 200 with the manifest JSON, and an anonymous request answers 401.

- **Fails closed.** The endpoint always requires authorization, because an open endpoint publishes the host's whole consumer and job topology. With no `authorizationPolicy`, the default policy applies, which admits any authenticated principal. A host that accepts end-user tokens must pass an operator policy. To serve it anonymously, for example on a port only the cluster reaches, call `.AllowAnonymous()` on the returned `RouteHandlerBuilder`.
- **Response.** `200` with the document as `application/json`. When a contributor fails, the endpoint logs `HEADLESS_HOST_MANIFEST_FAILED` and returns a fixed `500` problem that never carries the error text.
- **Not in OpenAPI.** The returned builder takes other conventions, such as a rate-limiting policy.
- An empty or whitespace `pattern` or `authorizationPolicy` throws `ArgumentException`.

#### Service Options

```csharp
builder.AddHeadless(configureServices: options =>
{
    options.OpenTelemetry.Enabled = true;
    options.OpenTelemetry.RecordException = true; // record the request span's exception, masked by MaskText patterns (default: true)
    // Tracing filter: null (default) skips /health and /alive via SkipOperationalEndpointFunc.
    // Set to a custom predicate to fully replace — compose SkipOperationalEndpointFunc to keep the default skip:
    //   options.OpenTelemetry.Filter = ctx => !options.OpenTelemetry.SkipOperationalEndpointFunc(ctx) && ...;
    // For full AspNetCoreTraceInstrumentationOptions control (runs AFTER framework defaults):
    // Leave instr.RecordException off there: AddHeadless records a masked event itself, and the instrumentation's would be unmasked.
    //   options.OpenTelemetry.ConfigureAspNetCoreInstrumentation = instr => { instr.EnrichWithHttpRequest = (activity, request) => { }; };

    options.OpenApi.Enabled = true;
    options.HttpClient.UseServiceDiscovery = true;
    options.HttpClient.UseStandardResilienceHandler = true;
    options.StaticAssets.Enabled = true;
    options.Validation.ValidateServiceProviderOnStartup = true;
    options.Validation.RequireUseHeadless = true;
    options.Validation.RequireMapHeadlessEndpoints = true;
    options.Validation.RequireStatusCodesRewriter = true;
    // Antiforgery defaults to false. Set to true for cookie-auth apps; bearer-token APIs leave it off.
    options.Antiforgery.Enabled = false;
});
```

For a custom pipeline that does not call `UseHeadless()`, `UseStatusCodesRewriter()`, or `MapHeadlessEndpoints()`, turn off the matching `options.Validation.Require*` switch.

Telemetry:

- With HTTP tenancy configured, the request span and every span started during the request carry `tenant.id`, and every log record written during the request carries `TenantId`. Every exporter sees both. Do not add them yourself. `TenantTelemetryOptions` holds the names and switches. See [Multi-tenancy observability](multi-tenancy/observability.md#observability).
- With API surfaces registered, the default response enricher adds `headless.api.surface.name` to completed request spans. Replacing `EnrichWithHttpResponse` replaces this default too. To keep it, capture the existing delegate and call it from yours.

OpenAPI documents per surface:

```csharp
builder.Services
    .AddHeadlessApiSurface("portal")
    .AddHeadlessApiSurface("console");
builder.AddHeadless();
// After building the application:
app.MapHeadlessEndpoints();
```

**Verify:** `GET /openapi/portal.json` and `GET /openapi/console.json` answer with one document each.

- With OpenAPI enabled, `AddHeadless()` infers document names from the surfaces registered before it. Adding a surface after inference throws.
- `SurfaceDocumentNames` (default `null`) publishes every surface document. Without surfaces, the ordinary `v1` document remains. A nonempty list selects documents, and each name must match a configured `OpenApi.DocumentName`, or startup fails. An empty list selects the ordinary, unfiltered `v1` document.
- Set `OpenApi.Enabled = false` to turn off registration.
- A host that registers services one by one calls `builder.Services.AddHeadlessApiSurfaceDocuments()` from `Headless.OpenApi` after surface registration and pairs it with `app.MapOpenApi()`. An optional document list selects a subset. With no surfaces, or an empty list, it registers no documents. Use it without the `AddHeadless()` OpenAPI registration, or turn that off with `OpenApi.Enabled = false`. Native `AddOpenApi(...)` can register more documents.
- Surface filtering runs before schema generation and is independent of API Explorer version groups. `ConfigureOpenApi` narrows the selected endpoints with `ShouldInclude` and appends document transformers, including a title override. The standalone method takes a per-surface callback, `Action<OpenApiOptions, ApiSurfaceDescriptor>`, for the same purpose.
- Every document these registrations create carries the Headless transformers (security schemes, typed problem details, operation IDs, FluentValidation constraints). Configure them with `builder.Services.ConfigureHeadlessOpenApi(...)`. See [OpenAPI](openapi.md).

### Runtime behavior

- Validates the service provider on build (`ValidateOnBuild`, `ValidateScopes`).
- Registers every `Headless.Api` primitive: problem details, response compression, status-code rewriting, and the default API conventions, including the `self` liveness check, no `Server` header, a 30 MB request body limit, and a 40-header limit.
- Registers `IRequestContext` (scoped).
- Registers `ICurrentUser` over the request principal, reading the `user_id`, `account_id`, `account_type`, and `role` claims. It replaces only the `NullCurrentUser` fallback that EF Core and relational audit-log storage register, in either call order. An `ICurrentUser` you registered is kept.
- Registers a fallback `IUserAgentParser` that identifies nothing. `AddHeadlessUserAgentParser()` replaces it in either call order.
- Registers no JWT, Identity, or authentication-scheme services, and no field-encryption or blind-index service. It needs no `Headless:Encryption` configuration.
- Registers antiforgery services only when `options.Antiforgery.Enabled` is `true`.
- Configures the MVC and Minimal API JSON serializer defaults and registers source-generated input validation (`services.AddValidation()`).
- Registers OpenTelemetry when `OpenTelemetry.Enabled` is `true`, and OpenAPI when `OpenApi.Enabled` is `true`.
- Configures HttpClient defaults: standard resilience, service discovery when `HttpClient.UseServiceDiscovery` is `true`, and the application `User-Agent`.
- Fails host start with an `InvalidOperationException` naming every missing call when `UseHeadless()`, `UseStatusCodesRewriter()`, or `MapHeadlessEndpoints()` was not applied.

---

## Headless.DataProtection.Blobs

Persists ASP.NET Core Data Protection keys to any `IBlobStorage` provider.

### API and behavior

- `PersistKeysToBlobStorage()` on `IDataProtectionBuilder`, with overloads for the registered `IBlobStorage`, an explicit instance, a factory, and a keyed store (`serviceKey`).
- Stores the key ring in the container that `BlobStorageDataProtectionOptions.ContainerName` names (default `DataProtection`).
- Ensures the key container before writes when an `IBlobContainerManager` is registered or passed.
- `ValidateKeyRing()`: opt-in check that runs after the host starts and keeps the host unready until the key ring can be read and written.
- `AddDataProtectionKeyRing()`: opt-in readiness health check that re-checks the key-ring store on every probe.
- Key-ring reads are bounded to 1 MiB per XML blob, 1,000 blobs, and 16 MiB of XML in total. DTD processing is prohibited. Exceeding any limit aborts the whole key-ring load. Malformed XML within the limits is skipped.

### Install

```bash
dotnet add package Headless.DataProtection.Blobs
```

### Setup and use

```csharp
var builder = WebApplication.CreateBuilder(args);

// Resolves the registered IBlobStorage (and IBlobContainerManager, when present); register a blob provider first.
builder.Services.AddDataProtection()
    .PersistKeysToBlobStorage()
    // Opt-in: probe the key ring after the start and stay unready until it passes, so a missing container or bad
    // credentials fails the rollout, not the first key write or the ~90-day rotation months later.
    .ValidateKeyRing();

// Opt-in continuous complement to the one-shot validation: a readiness health check that re-validates the
// key-ring store on every health probe — see "Key-ring health check" in Configuration.
builder.Services.AddHealthChecks().AddDataProtectionKeyRing();

// Or with explicit storage instance. No manager is involved, so for provisioning-requiring backends this throws
// at config time unless you acknowledge that the key container exists — see Configuration.
builder.Services.AddDataProtection().PersistKeysToBlobStorage(storageInstance, provisioning: BlobContainerProvisioning.PreProvisioned);

// Or with explicit storage + container manager (ensures the key container before writes)
builder.Services.AddDataProtection().PersistKeysToBlobStorage(storageInstance, containerManager);

// Or with factory
builder.Services.AddDataProtection().PersistKeysToBlobStorage(sp => sp.GetRequiredService<IBlobStorage>());

// Or against a named/keyed blob store (resolves the keyed IBlobStorage + IBlobContainerManager)
builder.Services.AddDataProtection().PersistKeysToBlobStorage(serviceKey: "keys");

// Any overload: store the key ring in a container of its own (required on S3, whose bucket names are global)
builder.Services.AddDataProtection()
    .PersistKeysToBlobStorage(configure: options => options.ContainerName = "orders-api-prod-keys");
```

**Verify:** with `ValidateKeyRing()`, the `warmup` readiness check on `/health` turns `Healthy` once the key ring is readable and writable, and stays `Unhealthy` with a message naming the key container while it is not. `AddDataProtectionKeyRing()` adds the `dataprotection-keyring` check to the same report.

### Configuration

`BlobStorageDataProtectionOptions` (validated at startup):

| Option | Default | Description |
| --- | --- | --- |
| `ContainerName` | `"DataProtection"` | Top-level container for the key XML blobs. Must not be empty. The storage's naming normalizer applies, so on S3 the default is the bucket `dataprotection`. |

Set it through any overload's `configure` parameter or `services.Configure<BlobStorageDataProtectionOptions>(...)`.

Set `ContainerName` per application and environment whenever the container namespace is shared. S3 bucket names are global across all AWS accounts, so the default `dataprotection` bucket almost certainly belongs to someone else: the ensure or the first write fails, or, if that bucket grants write access, the keys land in a foreign bucket. Applications that share one Azure storage account, file-system root, or Redis server also share the default container, and each one loads every other application's keys.

**Container provisioning.** When no container manager is present and the storage reports `IBlobStorage.RequiresContainerProvisioning == true`, `PersistKeysToBlobStorage` throws `InvalidOperationException` unless you pass `provisioning: BlobContainerProvisioning.PreProvisioned` to confirm the container was created out of band (portal, CLI, infrastructure as code). The storage-instance overload throws at the call. The DI, factory, and keyed overloads throw when the options first resolve. A present manager always ensures the container. `PreProvisioned` never turns it off.

| Mode | When | What to do |
| --- | --- | --- |
| Managed | A manager is registered, keyed, or passed | Nothing. The container is ensured before writes. |
| Pre-provisioned | No manager on AWS, Azure, FileSystem, SSH, or Cloudflare R2 (R2 ships no `IBlobContainerManager`, so this is its only mode) | Create the container out of band and pass `BlobContainerProvisioning.PreProvisioned`. |
| Exempt | Redis (`RequiresContainerProvisioning == false`) | Nothing. The storage-only overload works. |

**Tenant-scoped stores.** On a store wrapped by the blob tenancy seam (`tenancy.Blobs(b => b.ScopeByTenant())`), the key ring stays one host-level ring at the physical container root, not in the prefix of whichever tenant first touched data protection. See [Tenant-Scoped Blobs and Caches](multi-tenancy/blobs-and-caches.md#tenant-scoped-blobs-and-caches).

**Key-ring validation.** Key writes happen lazily, on first boot and at the ~90-day rotation, so a misconfiguration can stay hidden for months. `ValidateKeyRing(Action<DataProtectionKeyRingValidationOptions>? configure = null)` registers an opt-in warm-up named `DataProtectionKeyRing` (see [Warm-up](utilities.md#warm-up)). It runs in the background after the host starts and never delays the start.

- With `KeyManagementOptions.AutoGenerateKeys == true` (the default), it protects and unprotects a payload through the real provider. On a fresh deployment that generates the first key right after boot and drives the full write path.
- With `AutoGenerateKeys == false` (designated key-writer topologies), it reads the keys without generating one. A reachable but empty key ring fails, because the node would have no usable key.
- Unless `ProbeWritePath` is `false` (default `true`), both modes also upload and delete the sentinel blob `startup-write-probe.xml`. That is the only way to detect lost write permission while a valid key exists, and the only write check on read-only nodes. Key-ring loading always ignores the sentinel. A repository that is not blob-backed skips the probe.
- `Check` selects `KeyRingCheck.GateReadiness` (default) or `KeyRingCheck.LogOnly`. `GateReadiness` keeps the `warmup` readiness check `Unhealthy`, with a message that names the key container, the provisioning remediation, and the backend's innermost error, and retries with a jittered 1 s to 30 s back-off until the store works. The host keeps running and liveness is unaffected. `LogOnly` runs once, logs a failure at `Critical`, does not gate readiness, and does not retry.
- A stop request cancels the validation without logging a failure. A second `ValidateKeyRing` call registers nothing new, but its `configure` still applies.

**Key-ring health check.** `ValidateKeyRing` checks once. It cannot see a container deleted or write permission revoked after it passes. `AddDataProtectionKeyRing(this IHealthChecksBuilder, string name = "dataprotection-keyring", HealthStatus? failureStatus = null, IEnumerable<string>? tags = null, KeyRingProbeStyle probeStyle = KeyRingProbeStyle.WriteProbe)` re-checks on every probe:

- `KeyRingProbeStyle.WriteProbe` (default) uploads and deletes the sentinel blob through the same ensure and retry path as key writes. `Healthy` means the key ring can be persisted. Each probe writes and deletes a blob, so choose the probe interval with that in mind.
- `KeyRingProbeStyle.ContainerExistence` only checks that the container exists through the `IBlobContainerManager`. It does not verify write access, so revoked write permission still reports `Healthy`. With no manager wired (pre-provisioned mode), it falls back to the write probe and says so in its description.
- Statuses: `Healthy` when the probe succeeded. `Degraded` when `KeyManagementOptions.XmlRepository` is not the blob-backed repository (a wiring mistake, not an outage). `Unhealthy` (or your `failureStatus`) when the container is missing, the existence check threw, or the sentinel write failed, with the exception attached.
- The check does no caching or throttling of its own.

**Write failures.** The container ensure retries under the same policy as the key upload. A terminal write failure throws `InvalidOperationException` naming the key container, whether a manager was wired, and the remediation, with the backend exception as the inner exception.

### Runtime behavior

- Sets `KeyManagementOptions.XmlRepository` to the blob-backed repository.
- `ValidateKeyRing()` registers a warm-up and, with the first warm-up, the warm-up runner and the `warmup` readiness check.
- `AddDataProtectionKeyRing()` registers a health check named `dataprotection-keyring` by default.

---

## Headless.Api.FluentValidation

FluentValidation rules for ASP.NET Core file uploads and the reusable Headless API request contracts. The file rules are the `Headless.FluentValidation` rules read from an `IFormFile`, with the same checks, error codes, and messages.

### API and behavior

- `FileNotEmpty()`: the file has content.
- `GreaterThanOrEqualTo(bytes)` and `LessThanOrEqualTo(bytes)`: minimum and maximum file size.
- `ContentTypes(list)`: MIME type allowlist. The rule copies the list when it is built. A failure reports `g:file_unexpected_content_type` and lists the accepted types.
- `HaveSignatures(inspector, predicate)`: checks the file's magic bytes with FileSignatures and reports `g:file_unexpected_signature`. It opens a new stream through `OpenReadStream()`, asks the `IFileFormatInspector` for the format, and passes it (or `null`) to the predicate. The inspector needs a seekable stream. The rule is asynchronous, so validate with `ValidateAsync`.
- `AddHeadlessFileFormatInspector()` (`Headless.FluentValidation` namespace) registers a singleton `IFileFormatInspector` over every format FileSignatures ships. It is `TryAdd`, so an inspector you registered first, such as one limited to the formats you accept, is kept. `Headless.Api.ServiceDefaults` does not register one.
- The size and content-type rules implement `IFileSizeValidator` and `IFileContentTypeValidator`, so `Headless.OpenApi` documents a form's accepted part types and size bounds. `HaveSignatures` documents nothing.
- `PhoneNumber()`: validates `PhoneNumberRequest` country code and local subscriber number.
- `GeoCoordinate()`: validates `GeoCoordinateRequest` latitude and longitude ranges.
- `PageMetadata()`: validates `PageMetadataRequest` SEO field lengths and element counts.
- Error messages ship in English, formal Arabic, and Egyptian Arabic.

Validate an upload at the endpoint, where the `IFormFile` is bound. A command handled after the endpoint stores the file carries a reference to the stored content, such as a blob location, not the live upload.

### Install

```bash
dotnet add package Headless.Api.FluentValidation
```

### Setup and use

```csharp
using FileSignatures;
using FileSignatures.Formats;
using FluentValidation;
using Headless.Api;
using Headless.FluentValidation;
using Microsoft.AspNetCore.Http;

// Registers the IFileFormatInspector that HaveSignatures takes.
builder.Services.AddHeadlessFileFormatInspector();

public sealed record ProfileRequest(
    IFormFile? Avatar,
    PhoneNumberRequest? PhoneNumber,
    GeoCoordinateRequest? Location,
    PageMetadataRequest? Metadata
);

public sealed class ProfileRequestValidator : AbstractValidator<ProfileRequest>
{
    public ProfileRequestValidator(IFileFormatInspector inspector)
    {
        RuleFor(x => x.Avatar)
            .FileNotEmpty()
            .LessThanOrEqualTo(5 * 1024 * 1024) // 5MB
            .ContentTypes(["image/jpeg", "image/png"])
            .HaveSignatures(inspector, format => format is Jpeg or Png);

        RuleFor(x => x.PhoneNumber).PhoneNumber();
        RuleFor(x => x.Location).GeoCoordinate();
        RuleFor(x => x.Metadata).PageMetadata();
    }
}
```

**Verify:** in a unit test, `await new ProfileRequestValidator(inspector).ValidateAsync(request)` with a PNG renamed to `.jpg` and declared `text/plain` fails with `g:file_unexpected_content_type`.

### Configuration

No configuration. The file-validation extensions are in the `FluentValidation` namespace. The file error-code constants and localized descriptor factories are in `Headless.FluentValidation.Resources`.

### Runtime behavior

None.

---

## Headless.Api.Identity

ASP.NET Core Identity integration. Apps that do not use ASP.NET Core Identity do not need it.

### API and behavior

- `IdentityBuilder.AddHeadlessLookupNormalizer()`: replaces Identity's upper-invariant `ILookupNormalizer` with `HeadlessLookupNormalizer`, so stored usernames and emails normalize the same way as `Headless.Text.LookupNormalizer`.
- `IdentityBuilder.AddPasswordResetTokenProvider<TUser>()`, `AddPasswordResetCodeProvider<TUser>()`, `AddEmailConfirmationTokenProvider<TUser>()`, and `AddEmailConfirmationCodeProvider<TUser>()`: register the provider and make it Identity's password-reset or email-confirmation provider. The code providers share one RFC 6238 `TotpRfc6238Generator`.
- `HeadlessIdentityErrorDescriber`, `IdentityResultExtensions`, and `IdentityMessageDescriber` with `IdentityErrorCodes` (`Headless.Api.Identity.Resources`): localized `auth:` and `user:` error descriptors.
- `ConfigureApiApplicationCookie()`: answers the Identity cookie's login and access-denied redirects with 401 and 403 instead of a redirect.
- `AddBasicSchema()` and `AddApiKey()`: register the `Basic` and `ApiKey` schemes. Each handler authenticates only credentials sent for its own scheme. Both also register `DynamicAuthenticationSchemeProvider`, which picks `ApiKey`, `Basic`, or `Bearer` per request from the credentials it carries, for authenticate, challenge, and forbid. Sign-in and sign-out keep the configured defaults, because none of those schemes can sign in or out.

### Design constraints

- Basic authentication validates the password through `SignInManager.CheckPasswordSignInAsync(..., lockoutOnFailure: true)`, so Identity lockout policies apply to failed Basic credentials.
- **Credential cache.** `BasicAuthenticationOptions.CredentialCacheDuration` and `ApiKeyAuthenticationSchemeOptions.CredentialCacheDuration` default to `null` (off), and every request runs the full check: the user or key lookup, the Basic password hash (PBKDF2, about 35 ms of CPU per request), the sign-in and lockout checks, and the claims and role queries. Set a lifetime per scheme (above zero, at most one hour) to answer a repeated credential from an in-process cache with no store call.
  - The cost is revocation delay. Until its entry expires, a revoked or deactivated key, an old password, and a user who was locked out, disabled, or had roles or claims changed keep authenticating with the principal the first request built.
  - A Basic cache hit skips the reset of the user's failed-attempt count, so failures from elsewhere add up toward lockout until the next full check.
  - Only successes are cached. A failed credential always runs the full check, so it still counts toward lockout.
  - Entries expire a fixed time after the request that created them. Hits do not extend them. Each scheme holds at most `CredentialCacheMaxEntries` (default 1000) in a private cache, not the application's `IMemoryCache`. The cache never holds the secret and empties on restart. Each request gets its own copy of the cached principal, so claims a request changes do not reach the next cache hit. Expiry follows the registered `TimeProvider`.
- `IdentityMessageDescriber`'s password-reset and email-verification throttling messages format durations with Humanizer in the current UI culture. The package ships `Humanizer.Core.ar`, so any Arabic request, `ar-EG` included, gets Arabic durations. Any other culture gets English unless the application references the matching `Humanizer.Core.<culture>` package.

### Install

```bash
dotnet add package Headless.Api.Identity
```

### Setup and use

```csharp
builder.Services
    .AddIdentityCore<AppUser>()
    .AddHeadlessLookupNormalizer()
    .AddPasswordResetCodeProvider<AppUser>(configureOptions: null)
    .AddEmailConfirmationCodeProvider<AppUser>(configureOptions: null);

builder.Services
    .AddAuthentication(AuthenticationConstants.Schemas.Bearer)
    .AddApiKey<AppUser, Guid, AppApiKeyStore>()
    .AddBasicSchema<AppUser, Guid>();
```

**Verify:** a request with a valid `Authorization: Basic` header reaches an endpoint that requires the `Basic` scheme, and the same endpoint answers 401 to a wrong password. An invalid `CredentialCacheDuration` fails host start.

To skip the store and the password hash for a client that repeats the same credential, opt a scheme into the credential cache. A revoked credential keeps working until its entry expires, so keep the lifetime short:

```csharp
builder.Services
    .AddAuthentication(AuthenticationConstants.Schemas.Bearer)
    .AddApiKey<AppUser, Guid, AppApiKeyStore>(configureOptions: options =>
        options.CredentialCacheDuration = TimeSpan.FromMinutes(1))
    .AddBasicSchema<AppUser, Guid>(configureOptions: options =>
        options.CredentialCacheDuration = TimeSpan.FromMinutes(1));
```

API keys are read from the configured header. Query-string keys are routed and accepted only when `ApiKeyAuthenticationSchemeOptions.AllowApiKeyInQueryString` is `true`. Both options classes implement `IHttpCredentialSchemeOptions`, so `Headless.OpenApi` publishes each registered scheme: an `apiKey` header scheme named after the scheme, an `apiKey` query scheme (`{scheme}Query`) while the query fallback is on, and an HTTP `basic` scheme for `AddBasicSchema()`.

### Runtime behavior

- `AddHeadless()` registers none of these services.
- `AddHeadlessLookupNormalizer()` removes any earlier `ILookupNormalizer` registration, so it wins before or after `AddIdentityCore`.
- `AddApiKey()` and `AddBasicSchema()` set `IdentityOptions.ClaimsIdentity.UserIdClaimType` to `user_id` (`UserClaimTypes.UserId`) and `RoleClaimType` to `role` (`UserClaimTypes.Roles`), so a Basic or API-key principal carries the user ID and roles that `ICurrentUser` reads (idempotency scoping, permission grants, audit actor). They replace only Identity's defaults. A claim type you set explicitly is kept in either order, and then `ICurrentUser` does not see that claim. The setting is global to Identity, so cookie sign-ins through the same `SignInManager` carry the same claim types.
- `AddApiKey()` and `AddBasicSchema()` validate their scheme's options at start: a negative or over-one-hour `CredentialCacheDuration`, or a `CredentialCacheMaxEntries` below 1, fails the host. They register `TimeProvider.System` when no `TimeProvider` is registered.
- `AddApiKey()` and `AddBasicSchema()` replace ASP.NET Core's `AuthenticationSchemeProvider` with `DynamicAuthenticationSchemeProvider` and register `IHttpContextAccessor`. A scheme provider you registered yourself is kept, in either order. A host that uses only a bearer scheme keeps ASP.NET Core's provider and selects the scheme through `AddAuthentication(defaultScheme)`.
- The Basic handler's ticket carries the name the scheme was registered under: `AddBasicSchema("partner-basic")` issues `partner-basic` tickets.

---

## Headless.Api.Logging.Serilog

Serilog enrichment for ASP.NET Core APIs.

### API and behavior

- `UseHeadlessSerilogEnrichers()`: middleware that pushes `UserId`, `AccountId`, and `CorrelationId` from `IRequestContext` into the Serilog `LogContext` for the request. Each property is pushed only when its value is not `null`.
- `ApiSerilogFactory.CreateApiLogger(app)` and `ConfigureApiLoggerConfiguration(...)`: the `Headless.Logging.Serilog` reloadable logger plus enrichers for the client IP and the sanitized `User-Agent`, `X-Client-Version`, and `X-Api-Version` request headers. Each header value is cut to `SerilogOptions.MaxHeaderLength` characters and stripped of control characters and ANSI escape sequences.
- `WithSanitizedRequestHeader(...)`: adds the same sanitized enrichment for another request header.

The enrichers do not add a tenant ID. With HTTP tenancy configured, `Headless.Api.ServiceDefaults` adds `TenantId` to log records through OpenTelemetry. See [Service Options](#service-options).

### Install

```bash
dotnet add package Headless.Api.Logging.Serilog
```

### Setup and use

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless(); // registers IRequestContext, which the enrichers read

var app = builder.Build();

app.UseHeadless();

// After UseAuthentication() in a host that authenticates, so the enrichers can read the user
app.UseHeadlessSerilogEnrichers();

app.MapHeadlessEndpoints();
app.Run();
```

**Verify:** a log event written inside an authenticated request carries the `UserId` property, and one written for a request with a correlation header carries `CorrelationId`.

### Configuration

Sinks, levels, and the base enrichers come from `Headless.Logging.Serilog`. See [logging.md](logging.md#headlessloggingserilog).

### Runtime behavior

- Adds one middleware to the request pipeline. It needs no service registration beyond `AddHeadless()`.

---

## Headless.Api.MinimalApi

ASP.NET Core Minimal API integration: JSON configuration, mediator dispatch, `ApiResult` responses with complete OpenAPI metadata, FluentValidation filters, and entity-tag concurrency.

### API and behavior

- `ConfigureHeadlessMinimalApi()`: applies the Headless JSON conventions to Minimal API `JsonOptions`, indented in Development and Test. It registers nothing else. Exception handling comes from `AddHeadlessProblemDetails()`, and validation is per endpoint or per group.
- `ApiResult<T>.ToHttpResult()`, `ApiResult.ToHttpResult()`, and `ApiResult<T>.ToCreatedHttpResult(value => location)`: on success, 200 with the value, 204, or 201 with a `Location` field. On failure, the error's problem-details response. They return `ApiResultHttpResult<T>`, `ApiResultHttpResult`, and `ApiResultCreatedHttpResult<T>`, which publish only the success type to OpenAPI. `Headless.OpenApi` documents the error responses on every operation. See [openapi.md](openapi.md#headlessopenapi).
  - The problem body is built when the result executes, from the request's `IProblemDetailsCreator`, so the endpoint never injects the creator.
  - `ApiResultError.ToHttpResult(httpContext)` returns a plain `ProblemHttpResult` for your own `Results<...>` union.
  - A custom error's `Metadata` reaches the body as its descriptor's `params` (`errors[].params` for the default 409, `error.params` for a mapped status), never as top-level extensions.
  - Before it executes, a result reports `IStatusCodeHttpResult.StatusCode` for the framework's errors only. An application error reports `null`, because only the request's mappings know its status.
- `ApiResult<T>.ToAcceptedHttpResult()`, `ToAcceptedHttpResult(value => location)`, `ApiResult.ToAcceptedHttpResult()`, and `ToAcceptedHttpResult(location)`: 202 Accepted for work that continues after the response, such as an enqueued job. The valued form writes the value, and the unit form writes no body. The `location` overloads add a `Location` field. They return `ApiResultAcceptedHttpResult<T>` and `ApiResultAcceptedHttpResult`, which publish only the 202 response to OpenAPI. On failure they answer the error's problem with no `Location`, and the location callback never runs.
- `ISender.DispatchAsync(request, ct)`, `ISender.DispatchCreatedAsync(request, value => location, ct)`, and `ISender.DispatchAcceptedAsync(request, ct)` or `DispatchAcceptedAsync(request, location, ct)`: send a `Mediator` request whose handler returns `ApiResult` or `ApiResult<T>`, and return the matching result type. A `null` request answers 400 without reaching the mediator.
- `MapGetRequest`, `MapDeleteRequest`, `MapPostRequest`, `MapPutRequest`, and `MapPatchRequest`: map a route whose whole handler is bind, dispatch, respond. `<TRequest, TResponse>` returns 200 with the value. `<TRequest>` returns 204. `MapPostRequest<TRequest, TResponse>(pattern, value => location)` returns 201.
- `.ValidateArguments()` on a route group or endpoint: validates each handler argument that has a registered `IValidator<T>`. `.Validate<T>()` on one endpoint: validates one argument type with `MinimalApiValidatorFilter<T>`. Both answer 422. `.Validate<T>()` also answers 400 when the argument is missing. `Headless.OpenApi` documents the 400 on every operation that binds a value and the 422 on every operation.
- `.RejectEnumNumbers()` on a route group or endpoint: answers 400 `g:invalid_parameter_value` naming the value when an enum arrives as a number in a route, query, header, or form value. `MapApiSurface(...)` applies it to every surface endpoint.
- `.WithoutStandardProblemResponses()` and `.WithStandardProblemResponses()` on a route group, endpoint, or `MapApiSurface(...)`: leave the 404, 409, and 422 problem responses out of the OpenAPI document, or put them back. The innermost setting wins.
- `app.MapApiSurface(name)`: maps a surface's route group with its prefix, authorization, tenancy, and OpenAPI tag defaults.
- `.WithEntityTag()` and `.RequireIfMatch()`: entity-tag concurrency filters.
- The extensions and result types are in the `Headless.Api` namespace. Import `Headless.Api` and `Headless.Primitives` (for `ApiResult`). Do not write a local bridge.

### Design constraints

- **The dispatch helpers take `IRequest<T>` only.** Send a `Mediator` `ICommand<T>` or `IQuery<T>` with `sender.Send(...)` and call `ToHttpResult()` on the result.
- **No envelope-specific dispatch.** A handler that returns `ApiResult<DataEnvelope<T>>`, `ApiResult<CollectionEnvelope<T>>`, or `ApiResult<MessageEnvelope>` gets that envelope as the 200 body and the OpenAPI schema.
- **The `Map*Request` helpers bind the whole request from one source.** GET and DELETE use `[AsParameters]` (route values and the query string). POST, PUT, and PATCH use the JSON body. A request whose body also needs a route value, such as `PUT /orders/{id}`, needs its own handler that binds both and calls `DispatchAsync`.
- **A failed result's `Value` is `null`.** The problem body exists only once the result executes against a request. To assert a failure's body in a test, execute the result or call `ApiResultError.ToHttpResult(httpContext)`.
- **`ValidateArguments()` decides once per endpoint, when endpoints are built.** It keeps the handler parameters whose type has an `IValidator<T>` registration. An endpoint with none gets no filter. A validator registered after the endpoints are built is not seen. Do not also apply `.Validate<T>()` for the same argument, or its validators run twice.
- **`RejectEnumNumbers()` refuses only enum numbers.** Outside the body, ASP.NET Core binds an enum member name exactly as declared, answers its own generic 400 for anything else (other casing, an unknown name, a `[JsonStringEnumMemberName]` alias), and also binds a number. The convention refuses a value whose text, or any comma-separated part of a `[Flags]` value, starts with a digit or a sign. It checks route, query, and header values, a simple `[FromForm]` enum, the direct enum members of a `[FromForm]` type, and the members of an `[AsParameters]` type. An enum nested deeper in a form type is left to ASP.NET Core, which binds a number there. An endpoint with no enum parameter runs unchanged, and applying it twice has no further effect. Other bad simple values (`?page=abc`) and empty values get ASP.NET Core's generic bad-request. The Headless web preset writes enums by member name, so a value a client copies from a response or the OpenAPI document binds as it is. Do not alias an enum you bind from a route, query, header, or form value. Rename the member instead, because an alias binds only from the body.
- **Validate a mediator request in one place.** With `AddMediatorValidationRequestBehavior()` registered (see [mediator.md](mediator.md)), the validators of a request that a `Map*Request` route or `DispatchAsync` sends run twice: once in the `ValidateArguments()` filter, which answers 422, and again in the mediator, which throws `ValidationException`. An MVC action under `AddHeadlessMvcArgumentValidation()` does the same. An async validator that queries a database doubles its I/O. Pick one. Validate in the mediator when requests also come from message consumers or jobs, and leave HTTP validation off. Otherwise leave the mediator behavior unregistered and validate at the endpoint. Both answer the same 422. The framework does not detect the overlap.

### Install

```bash
dotnet add package Headless.Api.MinimalApi
```

### Setup and use

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless().ConfigureHeadlessMinimalApi();
builder.Services.AddHeadlessMinimalApiEntityTagConcurrency();

var app = builder.Build();

app.UseHeadless();
app.MapHeadlessEndpoints();

// This app validates at the endpoint and does not register AddMediatorValidationRequestBehavior(); an app that
// does should drop ValidateArguments() here, or each request's validators run twice.
var orders = app.MapGroup("/orders").WithTags("Orders").ValidateArguments();

// Bind, dispatch, respond: GetOrder binds {id} with [AsParameters]; CreateOrder binds from the body.
orders.MapGetRequest<GetOrder, OrderView>("/{id:guid}").WithEntityTag();
orders.MapPostRequest<CreateOrder, OrderView>("/", order => $"/orders/{order.Id}");
orders.MapDeleteRequest<DeleteOrder>("/{id:guid}");

// A handler that binds from two sources dispatches itself.
orders.MapPut(
    "/{id:guid}",
    (Guid id, RenameOrderBody body, ISender sender, CancellationToken ct) =>
        sender.DispatchAsync(new RenameOrder(id, body.Name), ct)
);

// Without the mediator, return the ApiResult directly.
orders.MapGet(
    "/{id:guid}/summary",
    async (Guid id, IOrderService service, CancellationToken ct) => (await service.GetSummaryAsync(id, ct)).ToHttpResult()
);

app.MapPut(
    "/orders/{id:guid}/lines",
    async (Guid id, UpdateOrder request, IIfMatchContext ifMatch, IOrderService service, CancellationToken ct) =>
        await service.UpdateAsync(id, request, ifMatch.EntityTag!, ct)
).RequireIfMatch();

app.Run();

public sealed record GetOrder(Guid Id) : IRequest<ApiResult<OrderView>>;

public sealed record CreateOrder(string Name) : IRequest<ApiResult<OrderView>>;

public sealed record DeleteOrder(Guid Id) : IRequest<ApiResult>;

public sealed record RenameOrderBody(string Name);

public sealed record RenameOrder(Guid Id, string Name) : IRequest<ApiResult>;
```

**Verify:** `POST /orders` with an invalid body answers 422 with `errors`, `GET /orders/{id}` answers with an `ETag` header, and `PUT /orders/{id}/lines` without `If-Match` answers 428 with `g:if_match_required`.

### Configuration

#### API surfaces

After `AddHeadlessApiSurface(...)`, `app.MapApiSurface("portal")` returns a `RouteGroupBuilder` with the surface's prefix and named authorization policy. The optional callback and the returned builder take ordinary endpoint conventions, including `ValidateArguments()` and `WithoutStandardProblemResponses()` for the whole surface. The example assumes the app does not register `AddMediatorValidationRequestBehavior()`.

```csharp
var portal = app.MapApiSurface("portal").ValidateArguments();
portal.MapGet("profile", () => "profile");
portal.MapGet("bootstrap", () => "bootstrap").AllowMissingTenant();
portal.MapGet("status", () => "ready").AllowAnonymous();
portal.MapGroup("orders").WithTags("Orders").MapGetRequest<ListOrders, CollectionEnvelope<OrderView>>("/");
```

**Verify:** `GET /api/portal/status` answers 200 anonymously when the surface's `RoutePrefix` is `api/portal`. An unknown surface name throws at `MapApiSurface`.

- Endpoint tenancy choices override surface defaults. Authorization policies add up, and `AllowAnonymous` bypasses them. `RequireTenant` metadata needs the policy and services described under [API surfaces](#api-surfaces-1) in `Headless.Api`.
- An endpoint with no OpenAPI tag of its own, from the endpoint or a nested group, is tagged with the surface's `OpenApi.Title` (default `"{SurfaceName} API"`).
- Nested groups with conflicting surfaces throw when endpoints are built. API Explorer version groups stay independent.

Representation validation is optional. The default accepts any strong entity tag. To require a specific representation format for every conditional write, configure the validator that MVC and Minimal API share:

```csharp
builder.Services.AddHeadlessMinimalApiEntityTagConcurrency(options =>
    options.IfMatchValidator = static tag => tag.TryGetUInt32(out _)
);
```

### Runtime behavior

- `MapApiSurface` adds the surface's route, authorization, tenancy, and default-tag conventions, and applies `RejectEnumNumbers()` to every surface endpoint.
- `ConfigureHeadlessMinimalApi` configures `JsonOptions` and nothing else.
- An `ApiResult` result type publishes its success response to OpenAPI with no `.Produces(...)` call. The error responses come from `Headless.OpenApi`, so no endpoint needs `.ProducesProblem(...)` for 404, 409, or 422.
- A `ForbiddenError` writes its descriptor's description, after `IErrorDescriptionLocalizer`, as the problem `detail`, and so does an `UnauthorizedError` that carries a descriptor. A thrown `UnauthorizedException`, `ForbiddenException`, `ConflictException`, FluentValidation `ValidationException`, or `EntityNotFoundException` writes the same status and body as the returned `UnauthorizedError`, `ForbiddenError`, `ConflictError`, `ValidationError`, or `NotFoundError`. MVC `ToActionResult` shares the mapping.
- An empty body on a `Map{Post,Put,Patch}Request` route, or a JSON `null`, answers a 400 problem before the mediator runs. A body that is not valid JSON gets ASP.NET Core's bare 400, which `UseStatusCodesRewriter()` turns into a problem.
- `ValidateArguments()` runs every validator of every validated argument in turn, each with its own validation context, and returns all failures as one 422. An optional argument the request omitted is skipped.
- The entity-tag filters write an `ETag` for a successful `IHasEntityTag` result and reject a missing or invalid `If-Match` before the handler runs.

---

## Headless.Api.Mvc

ASP.NET Core MVC integration: controllers, filters, JSON configuration, and URL canonicalization.

### API and behavior

- `ApiControllerBase`: base controller with common helpers.
- `BlockInEnvironmentAttribute` and `RequireEnvironmentAttribute`: answer 404 outside their environment, and `Headless.OpenApi` leaves the action out of documents generated there.
- `UseRedirectToCanonicalUrl()`: URL canonicalization (`RedirectToCanonicalUrlRule`). See [URL Canonicalization](#url-canonicalization).
- Preconfigured JSON and MVC options.
- **Enum binding.** An enum bound from a route, query, header, or form value binds by member name in any casing (`?status=Active`, `active`), and as comma-separated names for `[Flags]`. A value with a numeric part (`1`, `-1`, `Read,2`) or an unknown name is a binding failure. A `[JsonStringEnumMemberName]` alias binds only from the body. An explicit `[ModelBinder]` on the parameter still wins.
- **Binding failures.** Under `[ApiController]`, a value that cannot be bound answers 400 before the action runs: `g:invalid_parameter_value` naming the first failing route, query, header, or form value, or the plain bad-request problem for a missing or unreadable body. Headless removes MVC's validator providers, so model state holds only binding failures. A controller without `[ApiController]` gets ASP.NET Core's default behavior.
- **Argument validation (opt-in).** `services.AddHeadlessMvcArgumentValidation()` validates every action argument whose type has a registered `IValidator<T>` before the action runs, and answers all failures as one 422. `ConfigureHeadlessMvc()` does not add it. A request whose binding failed is not validated: under `[ApiController]` it already answered 400, and without it the action receives the model state as ASP.NET Core leaves it. Leave it off when actions send their arguments through a mediator registered with `AddMediatorValidationRequestBehavior()`, or every validator runs twice.
- An `ObjectResult` carrying a Headless problem runs `ProblemDetailsOptions.CustomizeProblemDetails` once before serialization.
- **`ApiResult` bridge.** `ApiResult<T>.ToActionResult(...)` and `ApiResult.ToActionResult(...)` map expected failures to the same problems as the equivalent exceptions, returning `ApiActionResult<T>` and `ApiActionResult`. `ToCreatedActionResult(controller, creator, value => location)` answers 201 with the value and a `Location` field (`ApiCreatedActionResult<T>`). `ToAcceptedActionResult(controller, creator)` answers 202, with the value for `ApiResult<T>` and no body for `ApiResult` (`ApiAcceptedActionResult<T>` and `ApiAcceptedActionResult`). Each has an overload that adds `Location`.
  - An action that declares one of these as its return type (`Task<ApiActionResult<Order>>`) publishes 200 with `T`, or a bodyless 204, to OpenAPI with no `[ProducesResponseType]` attributes. `Headless.OpenApi` documents the error responses.
  - MVC converts the result to the controller helper's result (`OkObjectResult`, `NotFoundObjectResult`) before result filters run. `Result` exposes it to tests.
  - `ApiResultError.ToActionResult(...)` returns a plain `ActionResult` and publishes nothing.
- API versioning integration with API Explorer.
- Opt-in strong `ETag` responses and `If-Match` validation.

### Install

```bash
dotnet add package Headless.Api.Mvc
```

### Setup and use

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless().ConfigureHeadlessMvc();
builder.Services.AddControllers();
builder.Services.AddHeadlessMvcEntityTagConcurrency();

// This app validates at the action and does not register AddMediatorValidationRequestBehavior(); an app that does
// should leave this out, or each request's validators run twice.
builder.Services.AddHeadlessMvcArgumentValidation();

var app = builder.Build();

app.UseHeadless();
app.MapHeadlessEndpoints();
app.MapControllers();
app.Run();
```

**Verify:** an action argument that fails its validator answers 422 with `errors`, and `?status=1` for an enum parameter answers 400 with `g:invalid_parameter_value`.

#### Controller Example

```csharp
[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController(IOrderService service, IProblemDetailsCreator problems) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ApiActionResult<Order>> GetAsync(int id, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct).ConfigureAwait(false);
        return result.ToActionResult(this, problems);
    }
}
```

#### ETag concurrency

`AddHeadlessMvcEntityTagConcurrency()` writes an `ETag` for a successful `ObjectResult` whose value implements `IHasEntityTag`. Add `[RequireIfMatch]` to write actions, or attach it to many actions with a `MapControllers().WithMetadata(new RequireIfMatchAttribute())` convention. The filter accepts exactly one strong tag and exposes it through the scoped `IIfMatchContext`.

| Condition | Response |
| --- | --- |
| `If-Match` missing | 428 `g:if_match_required` |
| `If-Match` invalid | 400 `g:if_match_invalid` |
| Persistence conflict | 409 `g:concurrency_failure` |

To check the tag's representation after it is parsed, pass `AddHeadlessMvcEntityTagConcurrency(options => ...)`. For PostgreSQL `xmin`, set `options.IfMatchValidator = static tag => tag.TryGetUInt32(out _)`. The same option exists for Minimal APIs.

`EntityTag` is an HTTP representation value, independent of persistence. Keep database concurrency versions provider-native (`uint` for PostgreSQL `xmin`, `byte[]` for SQL Server `rowversion`) and convert them with `EntityTag.FromUInt32(...)` or `EntityTag.FromBytes(...)` at the response boundary.

#### URL Canonicalization

`RedirectToCanonicalUrlRule` answers a non-canonical GET with a 301 to one canonical URL: a trailing slash appended or stripped per `RouteOptions.AppendTrailingSlash`, and the path and query string lowercased per `RouteOptions.LowercaseUrls`. `ConfigureHeadlessDefaultApi()` sets those to `false` and `true`.

Register it after `UseRouting()`:

```csharp
app.UseRouting();
app.UseRedirectToCanonicalUrl(); // or: UseRedirectToCanonicalUrl(appendTrailingSlash: false, lowercaseUrls: true)
```

**Verify:** with an `/orders` route mapped, `GET /Orders/` answers 301 to `/orders`.

Two attributes opt an endpoint out. Both are read from endpoint metadata, which exists only after routing matched the request:

- `[NoTrailingSlash]`: stops the rule from appending a trailing slash. The same attribute is a resource filter that answers 404 to trailing-slash requests.
- `[NoLowercaseQueryString]`: keeps query-string casing for case-sensitive tokens such as OAuth `state` and `code` values or signed URLs.

A request with no routed endpoint is left unchanged. Registered before `UseRouting()`, the rule canonicalizes nothing, rather than redirect past an opt-out it cannot see, and logs `HEADLESS_CANONICAL_URL_ENDPOINT_UNAVAILABLE` once.

### Configuration

#### API surfaces

Register `AddHeadlessMvcApiSurfaces()` with `AddControllers()` and `AddHeadlessApiSurface(...)`, and mark controllers with `[ApiSurface("portal")]`. The convention adds the surface's prefix, named authorization policy, and tenancy defaults to their actions.

- Controller and action tenancy metadata wins over surface defaults. Authorization policies add up, and `[AllowAnonymous]` bypasses them. `RequireTenant` metadata needs the policy and services described under [API surfaces](#api-surfaces-1) in `Headless.Api`.
- An unknown surface name fails MVC model construction.
- A controller with a configured prefix must use relative controller and action routes. Absolute templates are rejected, because they escape the prefix.
- Existing `ApiExplorerSettings.GroupName` values are kept for versioning. Calling `AddHeadlessMvcApiSurfaces()` twice adds one convention.

Other MVC features need no configuration.

### Runtime behavior

- Configures `MvcOptions` and `JsonOptions` for controllers.
- Adds a result filter that applies problem-details customization to Headless-generated MVC object results.
- `AddHeadlessMvcApiSurfaces` applies surface defaults during model construction.
- When opted in, writes `ETag` headers for `IHasEntityTag` responses.

---

## Headless.Api.UserAgent

The DeviceDetector.NET-backed `IUserAgentParser`. DeviceDetector.NET ships a large regex database, so it has its own package.

### Install

```bash
dotnet add package Headless.Api.UserAgent
```

### Setup and use

```csharp
builder.AddHeadless();
builder.Services.AddHeadlessUserAgentParser(builder.Configuration.GetSection("UserAgent"));
```

**Verify:** for a request from a desktop Chrome browser, `IWebClientInfoProvider.DeviceInfo` returns a summary such as `"Windows Chrome"` instead of `null`. Invalid options fail startup.

`AddHeadlessUserAgentParser()` has parameterless, `IConfiguration`, `Action<UserAgentParserOptions>`, and `Action<UserAgentParserOptions, IServiceProvider>` overloads.

### Configuration

`UserAgentParserOptions` (validated at startup):

| Option | Default | Description |
| --- | --- | --- |
| `MaxEntries` | 1,000 | Parsed agents kept in the parser's own cache. |
| `SlidingExpiration` | 6 hours | Sliding lifetime of a cached parse. |
| `Duration` | 24 hours | Absolute lifetime of a cached parse. |
| `MaxUserAgentLength` | 512 | Longer values are cut before parsing and caching. `UserAgentInfo.UserAgent` reports the cut value. |

### Runtime behavior

- `IUserAgentParser.Parse(userAgent)` returns a `UserAgentInfo?` with `IsBot`, a coarse `DeviceType` (`Desktop`, `Phone`, `Tablet`, `Bot`, and others), device brand and model, OS name, version, and platform, client name, version, type, and engine, and bot name and category. Unidentified fields are `null`, never `""`.
- `GetDeviceInfo(userAgent)` returns `UserAgentInfo.Summary`, the `"Windows Chrome"` string behind `IWebClientInfoProvider.DeviceInfo`. Call `Parse` when you need a single field instead of splitting the summary.
- Both return `null` for a blank agent or one nothing could be identified from, and both share one cached parse. Unidentifiable agents are cached too.
- The parser uses its own bounded cache, never the application's `IMemoryCache`, and disposes it with the singleton.
- A User-Agent is self-reported and easy to forge. Use it for diagnostics, session display, and analytics, never as an authorization input.
- Store `DeviceType` by name, not by number, so new members do not need a row rewrite.
- To stub device detection in tests, substitute `IUserAgentParser`.
- Replaces any registered `IUserAgentParser`, including the identify-nothing fallback that `AddHeadless()` registers, whichever call runs first.
