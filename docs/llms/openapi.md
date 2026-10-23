---
domain: OpenAPI
packages: OpenApi, OpenApi.Scalar
---

# OpenAPI

> OpenAPI 3.1 documents from the native `Microsoft.AspNetCore.OpenApi` generator, enriched by Headless transformers, plus Scalar UI rendering.

## Orientation

| Package | Role | When you need it |
|---|---|---|
| `Headless.OpenApi` | **Enriches** the native ASP.NET Core OpenAPI documents through document, operation, and schema transformers | Every API that publishes an OpenAPI document. `Headless.Api.ServiceDefaults` already depends on it |
| `Headless.OpenApi.Scalar` | **Renders** the served documents as an interactive UI | When you want a docs UI |

With `AddHeadless()` there is nothing to add: it registers the `v1` document, or one document per API surface, with the Headless transformers, and `MapHeadlessEndpoints()` serves them at `/openapi/{documentName}.json`.

```csharp
builder.AddHeadless();
builder.Services.ConfigureHeadlessOpenApi(options => options.AddApiKeySecurity = true);

var app = builder.Build();
app.UseHeadless();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapHeadlessEndpoints(); // serves /openapi/{documentName}.json
app.MapScalarOpenApi();     // serves /scalar
```

Without `AddHeadless()`:

```csharp
builder.Services.AddHeadlessOpenApi(); // the "v1" document
var app = builder.Build();
app.MapOpenApi("/openapi/{documentName}.json");
app.MapScalarOpenApi();
```

## Agent Rules

- Register documents with `AddHeadlessOpenApi(documentName, configure)`, `AddHeadlessApiSurfaceDocuments(...)`, or native `AddOpenApi(name, options => options.AddHeadlessTransformers())`. A native `AddOpenApi(...)` without `AddHeadlessTransformers()` publishes an unenriched document.
- Do not register a document name twice. `AddHeadless()` already registers `v1` or the surface documents; to register them yourself, set `options.OpenApi.Enabled = false` first. `AddHeadlessTransformers()` itself is idempotent per document, so calling it inside `AddHeadlessOpenApi`'s callback adds nothing twice.
- Configure the enrichment once, with `ConfigureHeadlessOpenApi(...)`. The options are shared by every Headless document and validated at startup. Call order relative to the document registrations does not matter.
- Call `AddHeadlessTransformers()` before adding your own transformers in the same callback. Transformers run in registration order, so yours then see the Headless output and can override it. `AddHeadlessOpenApi`'s `configure` callback and `AddHeadlessApiSurfaceDocuments`' per-surface callback already run after the Headless transformers.
- `MapOpenApi()` and `MapHeadlessEndpoints()` map endpoints, so authorization applies to the documents. Under a fallback authorization policy they answer 401; chain `.AllowAnonymous()` on `MapOpenApi()` to publish them. There is no middleware ordering rule any more.
- The document is OpenAPI 3.1. Exclusive bounds are numbers (`exclusiveMinimum: 1`), nullability is a `null` type or a `oneOf` with `null`, and examples are JSON values. Do not set `OpenApiOptions.OpenApiVersion` to 3.0 unless a client generator requires it.
- XML documentation of your own types reaches the document only when your project calls `AddOpenApi(...)` itself. The native XML-comment source generator intercepts `AddOpenApi` call sites in the compiling project, so a document registered through `AddHeadlessOpenApi` or `AddHeadless()` carries the Headless types' comments but not yours. To include yours, reference `Microsoft.AspNetCore.OpenApi` directly, set `GenerateDocumentationFile`, disable the `AddHeadless()` registration, and register `builder.Services.AddOpenApi("v1", options => options.AddHeadlessTransformers())`.
- A project that calls `AddOpenApi(...)` while referencing `Microsoft.AspNetCore.OpenApi` only transitively fails with CS9137 (interceptors not enabled), because the package's build targets do not flow transitively. Reference the package directly.
- Swagger UI is gone. Use `MapScalarOpenApi()` or any UI that reads `/openapi/{documentName}.json`.

## Core Concepts

**Transformers, in order.** `AddHeadlessTransformers()` adds them in an order that matters:

| Kind | Transformer behavior |
|---|---|
| Schema | Headless primitives (`IPrimitive<T>`: `MoneyAmount`, `Month`, `AccountId`, `UserId`, and every generated primitive) become their underlying JSON type, inlined rather than published as components |
| Schema | Numbers become plain `integer`/`number`. The Headless web JSON options read numbers from strings, so the native generator writes `["integer", "string"]` plus a digits pattern; the string alternative and its pattern are removed, except where `JsonNumberHandling.WriteAsString` applies (property, declaring type, or options), because those numbers are written as strings |
| Schema | FluentValidation rules become constraints (see below) |
| Schema | Every non-nullable property joins the object's `required` set |
| Operation | Operation ids for operations the native generator leaves unnamed |
| Operation | `deprecated` from `[Obsolete]` or an Asp.Versioning deprecated version; parameter descriptions from model metadata |
| Operation | Query parameter names in camelCase, except names starting with `$` |
| Operation | 401 when the endpoint requires authentication, including through the fallback policy; 403 when its named policy, roles, required tenant, or applied policy requirements can refuse an authenticated user |
| Operation | `If-Match` header plus 400, 409, and 428 on `[RequireIfMatch]` endpoints; a header the endpoint binds itself is marked required instead of repeated |
| Operation | The rate limiter's rejection status (429 under `UseHeadlessProblemDetails()`, else 503) on endpoints a limiter applies to |
| Operation | `ETag` header on 2xx responses whose body type implements `IHasEntityTag` |
| Operation | 400 on operations that bind a body or a route, query, header, or form value; 413 on operations with a request body; the antiforgery token header and its 400 on endpoints that validate one |
| Operation | The multipart encoding `contentType` and a description of each file part a form's `ContentTypes`, `FileNotEmpty`, `GreaterThanOrEqualTo`, or `LessThanOrEqualTo` rule constrains |
| Operation | Security requirements on operations that require authentication |
| Operation | 404, 409, and 422 on every operation, the standard problem responses, unless a marker, the surface, or `StandardProblemResponses` turns them off |
| Operation | Typed problem-details schema and example under `application/problem+json` for 400, 401, 403, 404, 408, 409, 413, 422, 428, 429, 500, 501, 503, plus the `Retry-After` header on 429 and 503 |
| Operation | `tenantRequired` 403 example on endpoints that require a tenant |
| Operation | Under tenant catalog resolution: the tenant header, and the 400, 404, and (with detailed errors) 403 rejections with their examples and `Cache-Control` header |
| Operation | `4XX` and `5XX` range responses typed with `HeadlessProblemDetails` on every operation, last, so they cover only the statuses the operation does not list |
| Document | Title `{Entry assembly product} API` and the framework response-conventions description |
| Document | Every configured security scheme, and one per credential of each authentication scheme that describes its credentials, under `components.securitySchemes` |
| Document | A `servers` entry with a `tenant` variable per tenant host template |
| Document | Duplicate operation ids numbered (`Orders_Find`, `Orders_Find2`) |
| Document | Native `ProblemDetails`, `HttpValidationProblemDetails`, and `ValidationProblemDetails` components removed once no response references them |

Before any transformer runs, an endpoint whose `IEnvironmentRestrictionMetadata` (`Headless.Api.Abstractions`) says it answers 404 in the current host environment is left out of every Headless document, path and schemas alike.

**What the native generator already does.** Response content types come from API Explorer's supported formats, parameter defaults and `required` come from binding metadata, inheritance is flattened, and string enums list their names. The transformers do not repeat that work.

**The transformers read the metadata of the endpoint that serves the operation.** MVC's API Explorer describes an action from its descriptor, which holds only the controller and action attributes, so `Headless.OpenApi` also registers an API Explorer provider that attaches the metadata of the route endpoint MVC builds for the action. A convention on `MapControllers()` (`RequireAuthorization(...)`, `RequireRateLimiting(...)`, `WithMetadata(...)`, `RequireIdempotencyKey()`) then documents a controller action the way the matching attribute or Minimal API call does. `AddHeadlessOpenApi()`, `AddHeadlessApiSurfaceDocuments()`, and `ConfigureHeadlessOpenApi(...)` register the provider; a document built with the native `AddOpenApi()` and only `AddHeadlessTransformers()` reads the action attributes alone. The native generator's own parameters and responses still come from the action descriptor.

---
## Headless.OpenApi

Headless document, operation, and schema transformers for the native ASP.NET Core OpenAPI generator.

### API and behavior

- `AddHeadlessOpenApi(string documentName = "v1", Action<OpenApiOptions>? configure = null)` — registers one native document with the Headless transformers, then `configure`.
- `AddHeadlessApiSurfaceDocuments(IEnumerable<string>? documentNames = null, Action<OpenApiOptions, ApiSurfaceDescriptor>? configure = null)` — registers one document per API surface, filtered to that surface's endpoints, titled from `surface.OpenApi.Title`.
- `ConfigureHeadlessOpenApi(Action<HeadlessOpenApiOptions>)` and `ConfigureHeadlessOpenApi(Action<HeadlessOpenApiOptions, IServiceProvider>)` — configure the shared enrichment.
- `OpenApiOptions.AddHeadlessTransformers()` — adds the transformers to a natively registered document.
- `FluentValidationRule`, `RuleContext` — extension points for FluentValidation-to-schema rules.
- `HeadlessOpenApiOptions.StandardProblemResponses` (default `true`) — documents 404, 409, and 422 on every operation; `false` turns them off API-wide.
- `HeadlessOpenApiOptions.ProblemResponseRanges` (default `true`) — documents the `4XX` and `5XX` range responses on every operation; `false` turns them off API-wide, for a client generator that mishandles range keys.
- `StandardProblemResponsesAttribute` (`[StandardProblemResponses]`, `[StandardProblemResponses(false)]`) on a controller or action, and `WithStandardProblemResponses()` / `WithoutStandardProblemResponses()` on a Minimal API endpoint, route group, `MapControllers()`, or `MapApiSurface(...)`, from `Headless.Api` — turn the standard problem responses on or off for that endpoint; `ApiSurfaceOpenApiOptions.StandardProblemResponses` (`bool?`, `null` inherits) sets a surface default.
- Contracts it reads, so a feature describes itself without a reference to this package: from `Headless.Api.Abstractions`, `HttpContractMetadata` (endpoint headers and responses), `IEnvironmentRestrictionMetadata` (environment-restricted endpoints), `IHttpCredentialSchemeOptions` (credentials an authentication scheme reads), `StandardProblemResponsesAttribute` (standard problem responses on or off), and `TenantCatalogResolutionDescription` (tenant catalog resolution); from `Headless.FluentValidation`, `IFileSizeValidator` / `IFileContentTypeValidator` (file rules).
- Problem-details schema types: `HeadlessProblemDetails`, `BadRequestProblemDetails`, `BadRequestValidationProblemDetails`, `UnauthorizedProblemDetails`, `ForbiddenProblemDetails`, `EntityNotFoundProblemDetails`, `RequestTimeoutProblemDetails`, `ConflictProblemDetails`, `PayloadTooLargeProblemDetails`, `UnprocessableEntityProblemDetails`, `PreconditionRequiredProblemDetails`, `TooManyRequestsProblemDetails`, `InternalServerErrorProblemDetails`, `NotImplementedProblemDetails`, `ServiceUnavailableProblemDetails`.

### Install

```bash
dotnet add package Headless.OpenApi
```

### Setup and use

Versioned documents use the native group filter. Asp.Versioning's API Explorer sets each description's group name; the native default `ShouldInclude` keeps the descriptions whose group matches the document name:

```csharp
builder.Services.AddHeadlessOpenApi("v1");
builder.Services.AddHeadlessOpenApi("v2");
```

Per-document customization runs after the Headless transformers:

```csharp
builder.Services.AddHeadlessOpenApi(
    configure: options =>
        options.AddDocumentTransformer(
            (document, _, _) =>
            {
                document.Info.Version = "2024-01";
                document.Servers = [new OpenApiServer { Url = "https://api.example.com" }];
                return Task.CompletedTask;
            }
        )
);
```

#### API surfaces

`AddHeadlessApiSurfaceDocuments()` infers every surface registered before it and closes surface registration, so later additions throw. An explicit list publishes only the named documents; each name must match a configured `OpenApi.DocumentName`, and an unknown name fails startup. The surface filter runs before schema generation, so another surface's paths and schemas cannot leak into a document. It is independent of API Explorer groups: narrow a surface to one version with `ShouldInclude` in the callback, which composes with the surface filter.

```csharp
builder.Services.AddHeadlessApiSurfaceDocuments(
    configure: (options, surface) =>
    {
        options.ShouldInclude = description => description.GroupName is null or "v1";
        options.AddDocumentTransformer(
            (document, _, _) =>
            {
                document.Info.Description = $"The {surface.SurfaceName} API.";
                return Task.CompletedTask;
            }
        );
    }
);
```

#### Security schemes

`AddBearerSecurity` (default `true`) publishes an HTTP bearer scheme named `Bearer`; `AddApiKeySecurity` publishes an API-key scheme named `ApiKey` that reads `ApiKeyHeaderName`. `SecuritySchemes` adds any number of named schemes. Each registered authentication scheme whose options implement `IHttpCredentialSchemeOptions` also publishes one scheme per credential: the first under the authentication scheme's name, each further one under that name plus its location. The `Headless.Api.Identity` handlers implement it, so `AddApiKey()` publishes `ApiKey` (the configured `ApiKeyHeaderName` header) and, with `AllowApiKeyInQueryString`, `ApiKeyQuery` (the `ApiKeyParamName` query parameter), and `AddBasicSchema()` publishes `Basic` (HTTP `basic`). Leave `AddApiKeySecurity` off with those handlers; a configured scheme wins over a discovered one with the same name. Every operation that requires authentication gets one security requirement per accepted scheme, so a client may satisfy any one of them. `SecuritySchemeSelector` chooses the accepted schemes per operation; without it, every registered scheme is accepted.

```csharp
builder.Services.ConfigureHeadlessOpenApi(options =>
{
    options.AddBearerSecurity = false;
    options.SecuritySchemes["TenantBearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Tenant access token.",
    };
    options.SecuritySchemes["AdministratorBearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    };
    options.SecuritySchemeSelector = description =>
        description
            .ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>()
            .Any(data => data.Policy?.StartsWith("Administrator", StringComparison.Ordinal) == true)
            ? ["AdministratorBearer"]
            : ["TenantBearer"];
});
```

`HeadlessOpenApiOptions`:

| Property | Default | Description |
|---|---|---|
| `AddBearerSecurity` | `true` | Publishes the `Bearer` HTTP bearer (JWT) scheme |
| `AddApiKeySecurity` | `false` | Publishes the `ApiKey` header scheme |
| `ApiKeyHeaderName` | `"X-Api-Key"` | Header the built-in `ApiKey` scheme reads, the `Headless.Api.Identity` handler's default |
| `SecuritySchemes` | empty | Additional schemes by component name |
| `SecuritySchemeSelector` | `null` | Per-operation accepted scheme names; `null` accepts every registered scheme |
| `FluentValidationRules` | empty | Rules that add to or replace `FluentValidationRule.DefaultRules` by `RuleName` |
| `ThrowOnSchemaProcessingError` | `false` | Fail generation when a FluentValidation rule throws, instead of logging and skipping it |

Startup validation rejects an empty `ApiKeyHeaderName` when the API-key scheme is on, an empty or `null` scheme entry, and a custom scheme named `Bearer` or `ApiKey` while the matching built-in scheme is on.

### Design constraints

**Authorization responses follow the policies the middleware applies.** 401 and the security requirement need `[Authorize]`/`RequireAuthorization()` data, an `AuthorizationPolicy` or `IAuthorizationRequirementData` in the metadata, an MVC authorization filter that denies anonymous users, or a `FallbackPolicy` (which applies to every endpoint with no authorization data or policy); `[AllowAnonymous]` removes them. 403 additionally needs a named policy, roles, an effective `RequireTenant`, or a claims, name, operation, roles, assertion, or `TenantRequirement` requirement in an applied policy or requirement data. A bare `[Authorize]` or `RequireAuthorization()` applies `DefaultPolicy`, so with the default `DefaultPolicy` it only authenticates and produces 401 but not 403; a `DefaultPolicy` or `FallbackPolicy` holding `TenantRequirement` adds 403 and the `tenantRequired` example unless the endpoint's later tenancy metadata is `AllowMissingTenant`. Policies come from `AuthorizationOptions`; named policies are not resolved and always count as able to refuse, and a custom `IAuthorizationPolicyProvider` that builds its default or fallback policy elsewhere is not seen. The roles of `[Authorize(Roles = ...)]` become the requirement's scopes, which OpenAPI 3.1 allows for any scheme type. A selector that returns an unregistered scheme name fails generation, because the requirement would reference a missing component.

**Errors are documented where the framework writes them.** An operation documents the error responses it declares, those its metadata and options show (authorization, `If-Match`, rate limits, feature contracts, tenancy), and two the server writes for an operation's shape before any handler runs. A 400 goes on every operation that binds a body, route, query, header, form, or model-bound value, because a value that fails to bind raises `BadHttpRequestException`, which the Headless exception handler writes as a 400 problem. A 413 goes on every operation with a request body, because the server's body size limit applies to it; an endpoint whose `IRequestSizeLimitMetadata` (`[RequestSizeLimit]`) sets a limit names it, and one that lifts the limit (`[DisableRequestSizeLimit]`) gets no 413. An endpoint whose last `IAntiforgeryMetadata` requires validation, in an app that registers antiforgery, gets an optional `AntiforgeryOptions.HeaderName` header parameter (the token may come in the `FormFieldName` form field instead) and the token reason in its 400: an invalid token ends the request with 400, which the status-codes rewriter writes as a problem. A status the operation already documents keeps its description. Every operation also documents the standard problem responses and the range responses below; any other status, such as 500, is covered by the `5XX` range and appears as an exact status only when declared.

**Every operation documents 404, 409, and 422.** These are the statuses the exception handler and the `ApiResult` conversion write for not-found, conflict, and validation failures, and any handler or service can raise them, so endpoint metadata cannot tell which operations return them. They are documented whether the handler returns an `ApiResult` result type or a plain value and throws, and the result types publish only their success response. An operation that does not raise them over-states its contract; a client generated from it only gains branches it never takes. Every status the application mapped its own exceptions and errors to through `AddHeadlessProblemDetails(problems => ...)` joins them, typed with `HeadlessProblemDetails` unless the status already has a Headless problem shape (429, say), and follows the same control. The innermost setting decides, in both directions: a `StandardProblemResponsesAttribute` on the action, then on its controller, then a marker on the endpoint, its route group, or a `MapControllers()` convention, then the surface's `OpenApi.StandardProblemResponses`, then `HeadlessOpenApiOptions.StandardProblemResponses`. A marker always beats the surface setting, even on a route group around `MapApiSurface(...)`. Turning them off changes the document only: the endpoint still returns these statuses, and a `ValidateArguments()` endpoint with them off answers 422 with only the `4XX` range documenting it. An app that reads API Explorer metadata without this package (its own Swashbuckle setup, say) sees no error statuses from the `ApiResult` result types.

**Range responses cover the statuses no rule predicts.** Every operation lists `4XX` and `5XX` responses typed with `HeadlessProblemDetails`, the base shape every Headless problem shares, after its exact statuses, so those keep their specific schemas. Every error the framework writes is problem details, so a range is always true: a timeout, an unavailable dependency, or an application's own mapped exception reaches a generated client as a typed problem. The endpoint markers and the surface setting do not remove the ranges; `HeadlessOpenApiOptions.ProblemResponseRanges = false` does, API-wide. Client generators differ in how they read range keys: Kiota resolves an exact status, then the range, then `default`; NSwag does not support range keys and its C# generator emits code that does not compile for them ([RicoSuter/NSwag#4141](https://github.com/RicoSuter/NSwag/issues/4141)); openapi-generator's `typescript-fetch` ignores them ([OpenAPITools/openapi-generator#16146](https://github.com/OpenAPITools/openapi-generator/issues/16146)). These generator results are from the upstream trackers, not checked against this package's output. A document that feeds NSwag or `typescript-fetch` sets `ProblemResponseRanges = false`; its clients then read an unlisted status as an untyped error.

**Tenant catalog resolution documents what it does before authentication.** It applies when the tenant posture records the catalog resolution capability and an identifier source is registered (`Http(http => http.ResolveFromCatalog(...))`), as the `TenantCatalogResolutionDescription` that call registers reports, read from registrations so a document generated without running the pipeline is the same; an endpoint with `[SkipTenantResolution]` gets none of it. Each `HeaderTenantIdentifierSourceOptions.HeaderNames` entry becomes an optional header parameter; a single header becomes required when the endpoint requires a tenant and no other identifier source is registered. Every resolving operation documents 400 `g:tenant_identifier_invalid` and, as `TenantCatalogOptions.DetailedResolutionErrors` decides, either the collapsed 404 `g:tenant_resolution_failed` or 404 `g:tenant_unknown` plus 403 `g:tenant_disabled` and `g:tenant_identifier_mismatch`, each with an optional `Cache-Control` header (`no-store`). A status the operation already documents keeps its description and its example under a name (`badRequest`, `notFound`, `forbidden`) beside the tenant examples, so with the standard problem responses on, the tenant 404 always joins the standard 404; a 400 declared as a validation problem gets no tenant example, because its schema has no `error` member. Each host template of `AddHostSource` without a `?` or `*` label adds a server `https://{template}` with a `tenant` variable after the servers the document already lists.

**Rate-limit and entity-tag responses follow metadata and options.** A limiter applies when the endpoint carries `EnableRateLimitingAttribute` (`RequireRateLimiting(...)`, `[EnableRateLimiting]`) or `RateLimiterOptions.GlobalLimiter` is set; `DisableRateLimiting` anywhere in the metadata removes it, as in the middleware. The documented status is `RateLimiterOptions.RejectionStatusCode`: 429 after `UseHeadlessProblemDetails()`, the native 503 otherwise. The document cannot see whether `UseRateLimiter()` runs. A 2xx response whose body type implements `IHasEntityTag` documents an optional `ETag` header: the body type decides, because the document cannot see whether the entity-tag profile (`AddHeadlessMvcEntityTagConcurrency()`, or `AddHeadlessMinimalApiEntityTagConcurrency()` with `.WithEntityTag()`) is on.

**Feature contracts render without feature references.** An endpoint's `HttpContractMetadata` (`Headless.Api.Abstractions`) becomes header parameters, responses, and response headers: each request header a `header` parameter with a `string` schema, each response an operation response whose error statuses then get their typed problem-details schema and example, and each response header a `string` header on every documented response whose status falls in its range. An entry the operation already has keeps its own description: a header the handler binds is reused and only becomes required when the metadata requires it, and a declared response or response header is not replaced. `.RequireIdempotencyKey()` and `.WithIdempotency(...)` attach it, so an idempotent endpoint documents its key header and rejections; see [Headless.Idempotency.ResponseReplay](idempotency.md#headlessidempotencyresponsereplay). A controller action renders the metadata a `MapControllers()` convention attaches (`MapControllers().WithMetadata(...)`, `.RequireIdempotencyKey()`) as well as its attributes.

**`ApiResult` result types publish only their success response.** An MVC action declared as `ApiActionResult<T>` or `ApiActionResult` (what `ApiResult.ToActionResult(...)` returns in `Headless.Api.Mvc`, with `ApiCreatedActionResult<T>` and `ApiAcceptedActionResult<T>` / `ApiAcceptedActionResult` for 201 and 202) and a Minimal API endpoint returning `ApiResultHttpResult<T>`, `ApiResultHttpResult`, `ApiResultCreatedHttpResult<T>`, or `ApiResultAcceptedHttpResult<T>` / `ApiResultAcceptedHttpResult` publish 200 with `T`, 201, 202 with `T` or without a body, or a bodyless 204. Their error responses come from the transformers like any other operation's, so an anonymous action lists no 401 or 403.

**Every non-nullable property is required.** The native generator requires only `required` members and constructor parameters. Headless treats a non-nullable property as always present, so a generated client gets a non-optional member for it, and a request body must send it. A generic type shares one schema across its instantiations (`DataEnvelope<string>` and `DataEnvelope<string?>` are one runtime type, published as one `DataEnvelopeOfstring` component), and the native generator describes an unconstrained `T` property as nullable in both; a constructor parameter keeps it in `required`. A nullable property of a component type (`Address? ShippingAddress`) stays out of `required` and is published as `oneOf: [null, $ref]`.

**Primitives are inlined.** A type implementing `IPrimitive<T>` serializes as its underlying value through its own converter, which the native generator cannot see through. Its schema becomes the underlying JSON type and format (`MoneyAmount` is `number`/`decimal`, `Month` `integer`/`int32`, `AccountId` and `UserId` `string`), nullable for `T?`, and it gets no component. A string primitive with `[StringLength(min, max)]` (`Headless.Generator.Primitives`) also gets `minLength`/`maxLength`, unless the attribute sets `validate: false`; nothing else is generated for OpenAPI, since the primitives generator emits no schema mapping. Primitives from every assembly need no registration: the interface identifies them.

**FluentValidation rules.** The type's `IValidator<T>` is resolved from the document-generation scope. `NotNull`/`NotEmpty` add the property to `required` and remove nullability (a nullable reference collapses from `oneOf: [null, $ref]` to the reference); `NotEmpty` also sets `minLength: 1`, or `minItems: 1` on an array; `Length`, `MinimumLength`, `MaximumLength`, and `ExactLength` set `minLength`/`maxLength`; `Matches` sets `pattern`; comparisons and `InclusiveBetween` set `minimum`/`maximum`, and `GreaterThan`, `LessThan`, and `ExclusiveBetween` set the 3.1 numeric `exclusiveMinimum`/`exclusiveMaximum` with no `minimum`/`maximum`; the ASP.NET Core-compatible `EmailAddress` sets `pattern: ^[^@]+@[^@]+$`. Unconditional `Include()` chains are followed. A rule or component under `When`/`Unless` is skipped, because it does not hold for every request. `RuleForEach` constraints land on `items`, and a `RuleForEach` `NotNull`/`NotEmpty` never makes the collection itself required, because FluentValidation skips a null collection. A parameter bound from one property of an `[AsParameters]` or `[FromQuery]` type takes that type's rules for the property. The `Headless.Api.FluentValidation` file rules on a form model (`[FromForm]` in a Minimal API, or the type declaring an MVC form property) describe its file parts: `ContentTypes` sets the part's multipart encoding `contentType` (several rules intersect), and the accepted types and the size bounds of `FileNotEmpty`, `GreaterThanOrEqualTo`, and `LessThanOrEqualTo` (the tightest win) join the part's description, beside its `$ref` so the shared `IFormFile` component stays unchanged. OpenAPI has no keyword for the size of a binary part, since `maxLength` counts characters, so the bounds are text only. A custom rule takes part by implementing `IFileSizeValidator` or `IFileContentTypeValidator`.

**Problem-details schemas describe what the response carries.** Each known status gets its typed schema as a shared component, referenced from every operation, with a nested `ErrorDescriptor` component; the `4XX` and `5XX` ranges reference `HeadlessProblemDetails`, which carries only the members every problem shares. The `error` member of 400, 401, 403, 404, 408, 429, 501, and 503 is optional and nullable; `retryAfter` on 429 is required and on 503 optional, matching the `Retry-After` header (required on 429, optional on 503). 413 and 500 carry no `error`. A 400 declared as `HttpValidationProblemDetails` (`ProducesValidationProblem()`, or the .NET `AddValidation()` filter) gets `BadRequestValidationProblemDetails` instead: the native title, no `detail`, and `errors` as member names mapped to plain message arrays, plus the Headless diagnostics members. The native `ProblemDetails` and `HttpValidationProblemDetails` components a declaration published are removed when no response references them any more. Other media types the endpoint declared for an error response keep their schema; only `application/problem+json` is typed, and only when the endpoint declared no problem type of its own there: `Produces<SeatTakenProblem>(409, "application/problem+json")` keeps the `SeatTakenProblem` schema and gets no example, while the native `ProblemDetails` and `HttpValidationProblemDetails` that `ProducesProblem()`, `ProducesValidationProblem()`, and `[ProducesResponseType<ProblemDetails>]` declare are replaced. Example text is English, because a cached document is served to readers of every culture.

**Operation ids.** A route or endpoint name wins (`WithName`, `[HttpGet(Name = ...)]`). An unnamed MVC action becomes `{Controller}_{Action}` without an `Async` suffix; an unnamed Minimal API endpoint becomes its method plus its path segments. A literal segment adds its words; a segment that is only a parameter adds `By` and the parameter name, with its constraint, default, optional, and catch-all markers dropped, so the list and the item of one resource get distinct ids whatever order they are mapped in (`GET /things` is `GetThings`, `GET /things/{id:guid}` is `GetThingsById`, `GET /orders/{orderId}/items/{itemId?}` is `GetOrdersByOrderIdItemsByItemId`, `GET /files/{**path}` is `GetFilesByPath`). A parameter that shares its segment with literal text adds nothing: it is a prefix every operation of a group shares, such as an API version, so `GET /v{version}/things` is `GetVThings` and `GET /v{version}/things/{id}` is `GetVThingsById`. Ids that still repeat are numbered in path order, as a last resort; name the endpoint (`WithName`) for an id that must not depend on the path.

### Runtime behavior

- `AddHeadlessOpenApi` and `AddHeadlessApiSurfaceDocuments` call native `AddOpenApi` and register `HeadlessOpenApiOptions` with startup validation.
- `AddHeadlessOpenApi`, `AddHeadlessApiSurfaceDocuments`, and `ConfigureHeadlessOpenApi` register the environment filter, which wraps each Headless document's `ShouldInclude` after every configure callback ran. A document registered with native `AddOpenApi(...)` and `AddHeadlessTransformers()` alone gets it only once one of them is called.
- Authentication schemes are read on every generation, so a scheme added at run time appears in the next document generated rather than served from a cache.
- The native generator builds the document per request. `MapHeadlessEndpoints()` caches the response with output caching unless `OpenApi.CacheDocument` is `false`.

---
## Headless.OpenApi.Scalar

Scalar API documentation UI integration — renders the OpenAPI documents served by `MapOpenApi()` as an interactive browser-based UI.

### API and behavior

- `MapScalarOpenApi(Action<ScalarOptions>?, string endpointPrefix)` — mounts Scalar at the given prefix (default `/scalar`), reading documents from `/openapi/{documentName}.json`
- Dark mode enabled by default with toggle visible
- Layout: `ScalarLayout.Modern`
- Tag sort: alphabetical (`TagSorter.Alpha`)
- Operation sort: by HTTP method (`OperationSorter.Method`)
- Code generation targets: C#, Go, JavaScript, Node, PowerShell, Shell
- HTTP client targets: `HttpClient`, `Curl`, `Axios`, `Fetch`, `XHR`, `WebRequest`, `Wget`, `HTTPie`
- All Scalar options are overridable via the `setupAction` callback

### Design constraints

**Route pattern coupling.** `MapScalarOpenApi` sets `options.OpenApiRoutePattern = "/openapi/{documentName}.json"` before passing control to the user callback. This matches `MapHeadlessEndpoints()`'s default `OpenApi.RoutePattern` and `MapOpenApi("/openapi/{documentName}.json")`. Native `MapOpenApi()` without a pattern serves `/openapi/{documentName}.json` too. If you change the document route, override `OpenApiRoutePattern` in `setupAction` or the UI points at a 404.

**No generator dependency.** The package does not reference `Headless.OpenApi`. It calls `Scalar.AspNetCore`'s `MapScalarApiReference`, which works with any OpenAPI JSON source served at the route pattern. Add one Scalar document per surface with `options.AddDocument(surface.OpenApi.DocumentName, surface.OpenApi.Title)`.

### Install

```bash
dotnet add package Headless.OpenApi.Scalar
```

### Setup and use

```csharp
var app = builder.Build();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapScalarOpenApi();
```

With custom options:

```csharp
app.MapScalarOpenApi(
    setupAction: options =>
    {
        options.DarkMode = false;
        options.Layout = ScalarLayout.Classic;
    },
    endpointPrefix: "/docs"
);
```

### Configuration

`MapScalarOpenApi` accepts an optional `Action<ScalarOptions>` and an `endpointPrefix` string. The framework pre-sets the following before invoking the callback (override any of them inside `setupAction`):

| Setting | Framework default |
|---|---|
| `OpenApiRoutePattern` | `/openapi/{documentName}.json` |
| `DarkMode` | `true` |
| `HideDarkModeToggle` | `false` |
| `Layout` | `ScalarLayout.Modern` |
| `TagSorter` | `TagSorter.Alpha` |
| `OperationSorter` | `OperationSorter.Method` |
| `EnabledTargets` | C#, Go, JavaScript, Node, PowerShell, Shell |
| `EnabledClients` | HttpClient, Curl, Axios, Fetch, XHR, WebRequest, Wget, HTTPie |

The `endpointPrefix` parameter (default `"/scalar"`) controls where Scalar is mounted.

### Runtime behavior

- Mounts Scalar UI at `{endpointPrefix}` (default `/scalar`) via `MapScalarApiReference`
