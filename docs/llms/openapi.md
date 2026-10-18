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
| Operation | Security requirements on operations that require authentication |
| Operation | Typed problem-details schema and example under `application/problem+json` for 400, 401, 403, 404, 408, 409, 413, 422, 428, 429, 500, 501, 503, plus the `Retry-After` header on 429 and 503 |
| Operation | `tenantRequired` 403 example on endpoints that require a tenant |
| Document | Title `{Entry assembly product} API` and the framework response-conventions description |
| Document | Every registered security scheme under `components.securitySchemes` |
| Document | Duplicate operation ids numbered (`Orders_Find`, `Orders_Find2`) |
| Document | Native `ProblemDetails`, `HttpValidationProblemDetails`, and `ValidationProblemDetails` components removed once no response references them |

**What the native generator already does.** Response content types come from API Explorer's supported formats, parameter defaults and `required` come from binding metadata, inheritance is flattened, and string enums list their names. The transformers do not repeat that work.

---
## Headless.OpenApi

Headless document, operation, and schema transformers for the native ASP.NET Core OpenAPI generator.

### API and behavior

- `AddHeadlessOpenApi(string documentName = "v1", Action<OpenApiOptions>? configure = null)` — registers one native document with the Headless transformers, then `configure`.
- `AddHeadlessApiSurfaceDocuments(IEnumerable<string>? documentNames = null, Action<OpenApiOptions, ApiSurfaceDescriptor>? configure = null)` — registers one document per API surface, filtered to that surface's endpoints, titled from `surface.OpenApi.Title`.
- `ConfigureHeadlessOpenApi(Action<HeadlessOpenApiOptions>)` and `ConfigureHeadlessOpenApi(Action<HeadlessOpenApiOptions, IServiceProvider>)` — configure the shared enrichment.
- `OpenApiOptions.AddHeadlessTransformers()` — adds the transformers to a natively registered document.
- `FluentValidationRule`, `RuleContext` — extension points for FluentValidation-to-schema rules.
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

`AddBearerSecurity` (default `true`) publishes an HTTP bearer scheme named `Bearer`; `AddApiKeySecurity` publishes an API-key scheme named `ApiKey` that reads `ApiKeyHeaderName`. `SecuritySchemes` adds any number of named schemes. Every operation that requires authentication gets one security requirement per accepted scheme, so a client may satisfy any one of them. `SecuritySchemeSelector` chooses the accepted schemes per operation; without it, every registered scheme is accepted.

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
| `ApiKeyHeaderName` | `"X-API-Key"` | Header the `ApiKey` scheme reads |
| `SecuritySchemes` | empty | Additional schemes by component name |
| `SecuritySchemeSelector` | `null` | Per-operation accepted scheme names; `null` accepts every registered scheme |
| `FluentValidationRules` | empty | Rules that add to or replace `FluentValidationRule.DefaultRules` by `RuleName` |
| `ThrowOnSchemaProcessingError` | `false` | Fail generation when a FluentValidation rule throws, instead of logging and skipping it |

Startup validation rejects an empty `ApiKeyHeaderName` when the API-key scheme is on, an empty or `null` scheme entry, and a custom scheme named `Bearer` or `ApiKey` while the matching built-in scheme is on.

### Design constraints

**Authorization responses follow the policies the middleware applies.** 401 and the security requirement need `[Authorize]`/`RequireAuthorization()` data, an `AuthorizationPolicy` or `IAuthorizationRequirementData` in the metadata, an MVC authorization filter that denies anonymous users, or a `FallbackPolicy` (which applies to every endpoint with no authorization data or policy); `[AllowAnonymous]` removes them. 403 additionally needs a named policy, roles, an effective `RequireTenant`, or a claims, name, operation, roles, assertion, or `TenantRequirement` requirement in an applied policy or requirement data. A bare `[Authorize]` or `RequireAuthorization()` applies `DefaultPolicy`, so with the default `DefaultPolicy` it only authenticates and produces 401 but not 403; a `DefaultPolicy` or `FallbackPolicy` holding `TenantRequirement` adds 403 and the `tenantRequired` example unless the endpoint's later tenancy metadata is `AllowMissingTenant`. Policies come from `AuthorizationOptions`; named policies are not resolved and always count as able to refuse, and a custom `IAuthorizationPolicyProvider` that builds its default or fallback policy elsewhere is not seen. The roles of `[Authorize(Roles = ...)]` become the requirement's scopes, which OpenAPI 3.1 allows for any scheme type. A selector that returns an unregistered scheme name fails generation, because the requirement would reference a missing component.

**Rate-limit and entity-tag responses follow metadata and options.** A limiter applies when the endpoint carries `EnableRateLimitingAttribute` (`RequireRateLimiting(...)`, `[EnableRateLimiting]`) or `RateLimiterOptions.GlobalLimiter` is set; `DisableRateLimiting` anywhere in the metadata removes it, as in the middleware. The documented status is `RateLimiterOptions.RejectionStatusCode`: 429 after `UseHeadlessProblemDetails()`, the native 503 otherwise. The document cannot see whether `UseRateLimiter()` runs. A 2xx response whose body type implements `IHasEntityTag` documents an optional `ETag` header: the body type decides, because the document cannot see whether the entity-tag profile (`AddHeadlessMvcEntityTagConcurrency()`, or `AddHeadlessMinimalApiEntityTagConcurrency()` with `.WithEntityTag()`) is on.

**Feature contracts render without feature references.** An endpoint's `HttpContractMetadata` (`Headless.Api`) becomes header parameters, responses, and response headers: each request header a `header` parameter with a `string` schema, each response an operation response whose error statuses then get their typed problem-details schema and example, and each response header a `string` header on every documented response whose status falls in its range. An entry the operation already has keeps its own description: a header the handler binds is reused and only becomes required when the metadata requires it, and a declared response or response header is not replaced. `.RequireIdempotencyKey()` and `.WithIdempotency(...)` attach it, so an idempotent endpoint documents its key header and rejections; see [Headless.Idempotency.ResponseReplay](idempotency.md#headlessidempotencyresponsereplay). Metadata an MVC convention attaches (`MapControllers().WithMetadata(...)`) never reaches MVC's API Explorer, which reads the action descriptor, so it renders only from Minimal API endpoints and route groups, or from an attribute on an action.

**MVC actions returning `ApiActionResult<T>` document the ApiResult contract.** An action declared as `ApiActionResult<T>` or `ApiActionResult` (what `ApiResult.ToActionResult(...)` returns in `Headless.Api.Mvc`) publishes 200 with `T` (or a bodyless 204) and problem-details 401, 403, 404, 409, and 422, the set a Minimal API `ApiResultHttpResult` publishes, and each error gets its typed Headless schema here.

**Every non-nullable property is required.** The native generator requires only `required` members and constructor parameters. Headless treats a non-nullable property as always present, so a generated client gets a non-optional member for it, and a request body must send it. A generic type shares one schema across its instantiations (`DataEnvelope<string>` and `DataEnvelope<string?>` are one runtime type, published as one `DataEnvelopeOfstring` component), and the native generator describes an unconstrained `T` property as nullable in both; a constructor parameter keeps it in `required`. A nullable property of a component type (`Address? ShippingAddress`) stays out of `required` and is published as `oneOf: [null, $ref]`.

**Primitives are inlined.** A type implementing `IPrimitive<T>` serializes as its underlying value through its own converter, which the native generator cannot see through. Its schema becomes the underlying JSON type and format (`MoneyAmount` is `number`/`decimal`, `Month` `integer`/`int32`, `AccountId` and `UserId` `string`), nullable for `T?`, and it gets no component. A string primitive with `[StringLength(min, max)]` (`Headless.Generator.Primitives`) also gets `minLength`/`maxLength`, unless the attribute sets `validate: false`; nothing else is generated for OpenAPI, since the primitives generator emits no schema mapping. Primitives from every assembly need no registration: the interface identifies them.

**FluentValidation rules.** The type's `IValidator<T>` is resolved from the document-generation scope. `NotNull`/`NotEmpty` add the property to `required` and remove nullability (a nullable reference collapses from `oneOf: [null, $ref]` to the reference); `NotEmpty` also sets `minLength: 1`, or `minItems: 1` on an array; `Length`, `MinimumLength`, `MaximumLength`, and `ExactLength` set `minLength`/`maxLength`; `Matches` sets `pattern`; comparisons and `InclusiveBetween` set `minimum`/`maximum`, and `GreaterThan`, `LessThan`, and `ExclusiveBetween` set the 3.1 numeric `exclusiveMinimum`/`exclusiveMaximum` with no `minimum`/`maximum`; the ASP.NET Core-compatible `EmailAddress` sets `pattern: ^[^@]+@[^@]+$`. Unconditional `Include()` chains are followed. A rule or component under `When`/`Unless` is skipped, because it does not hold for every request. `RuleForEach` constraints land on `items`, and a `RuleForEach` `NotNull`/`NotEmpty` never makes the collection itself required, because FluentValidation skips a null collection. A parameter bound from one property of an `[AsParameters]` or `[FromQuery]` type takes that type's rules for the property.

**Problem-details schemas describe what the response carries.** Each known status gets its typed schema as a shared component, referenced from every operation, with a nested `ErrorDescriptor` component. The `error` member of 400, 401, 403, 404, 408, 429, 501, and 503 is optional and nullable; `retryAfter` on 429 is required and on 503 optional, matching the `Retry-After` header (required on 429, optional on 503). 413 and 500 carry no `error`. A 400 declared as `HttpValidationProblemDetails` (`ProducesValidationProblem()`, or the .NET `AddValidation()` filter) gets `BadRequestValidationProblemDetails` instead: the native title, no `detail`, and `errors` as member names mapped to plain message arrays, plus the Headless diagnostics members. The native `ProblemDetails` and `HttpValidationProblemDetails` components a declaration published are removed when no response references them any more. Other media types the endpoint declared for an error response keep their schema; only `application/problem+json` is typed. Example text is English, because a cached document is served to readers of every culture.

**Operation ids.** A route or endpoint name wins (`WithName`, `[HttpGet(Name = ...)]`). An unnamed MVC action becomes `{Controller}_{Action}` without an `Async` suffix; an unnamed Minimal API endpoint becomes its method plus the literal text of its path segments (`GET /orders/{id}/items` is `GetOrdersItems`, `GET /v{version}/things` is `GetVThings`). Repeats are numbered in path order.

### Runtime behavior

- `AddHeadlessOpenApi` and `AddHeadlessApiSurfaceDocuments` call native `AddOpenApi` and register `HeadlessOpenApiOptions` with startup validation.
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
