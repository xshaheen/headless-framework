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

With `AddHeadless()` you add nothing: it registers the `v1` document, or one document per API surface, with the Headless transformers, and `MapHeadlessEndpoints()` serves them at `/openapi/{documentName}.json`.

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

**Verify:** `GET /openapi/v1.json` (or `/openapi/{surface document name}.json`) answers 200 with an `openapi` field that starts with `3.1` and an `info.title` of `{Entry assembly product} API`. `GET /scalar` answers 200 with the UI. In a test, request the document through `WebApplicationFactory` and assert both.

## Agent Rules

- Register documents with `AddHeadlessOpenApi(documentName, configure)`, `AddHeadlessApiSurfaceDocuments(...)`, or native `AddOpenApi(name, options => options.AddHeadlessTransformers())`. A native `AddOpenApi(...)` without `AddHeadlessTransformers()` publishes an unenriched document.
- Do not register a document name twice. `AddHeadless()` already registers `v1` or the surface documents. To register them yourself, set `options.OpenApi.Enabled = false` first. `AddHeadlessTransformers()` is idempotent per document, so calling it inside `AddHeadlessOpenApi`'s callback adds nothing twice.
- Configure the enrichment once, with `ConfigureHeadlessOpenApi(...)`. Every Headless document shares these options, and startup validates them. Call order relative to the document registrations does not matter.
- Call `AddHeadlessTransformers()` before you add your own transformers in the same callback. Transformers run in registration order, so yours then see the Headless output and can override it. `AddHeadlessOpenApi`'s `configure` callback and `AddHeadlessApiSurfaceDocuments`' per-surface callback already run after the Headless transformers.
- `MapOpenApi()` and `MapHeadlessEndpoints()` map endpoints, so authorization applies to the documents. Under a fallback authorization policy they answer 401. To publish them anonymously, chain `.AllowAnonymous()` on `MapOpenApi()`. The documents need no middleware ordering.
- The document is OpenAPI 3.1. Exclusive bounds are numbers (`exclusiveMinimum: 1`), nullability is a `null` type or a `oneOf` with `null`, and examples are JSON values. Do not set `OpenApiOptions.OpenApiVersion` to 3.0 unless a client generator requires it.
- Turn on `GenerateDocumentationFile` in the project that declares a documented type or action. Every Headless document reads XML comments from the `.xml` file each assembly ships, and an assembly without the file contributes nothing:
  - A type's and a property's summary (or a positional record's `param`) become schema descriptions. This includes a type that only a property reaches, such as an enum or a nested object: its shared component takes the type's summary, and each property that refers to it keeps its own comment beside the `$ref` (on the `oneOf` of a nullable one).
  - An action's summary, remarks, and parameter comments become the operation's.
  - A package's `.xml` file reaches `bin` only through `CopyDocumentationFilesFromPackages`. The `Headless.OpenApi` package turns it on in every project that references it, directly or through another package, so the comments on Headless types come through too. Set it to `false` to keep package documentation out of the build and publish output.
  - A description already set by an attribute or by the native XML-comment generator wins.
- Reference `Microsoft.AspNetCore.OpenApi` directly in a project that calls `AddOpenApi(...)`. With only a transitive reference the build fails with CS9137 (interceptors not enabled), because the package's build targets do not flow transitively.
- Headless ships no Swagger UI. Use `MapScalarOpenApi()` or any UI that reads `/openapi/{documentName}.json`.

## Core Concepts

**Transformers, in order.** `AddHeadlessTransformers()` adds them in an order that matters:

| Kind | Transformer behavior |
|---|---|
| Schema | Headless primitives (`IPrimitive<T>`: `MoneyAmount`, `Month`, `AccountId`, `UserId`, and every generated primitive) become their underlying JSON type, inlined rather than published as components. A collection of them (`IReadOnlyList<UserId>`) gets that type as its `items`, which the native generator leaves out |
| Schema | Numbers become plain `integer` or `number`. The Headless web JSON options read numbers from strings, so the native generator writes `["integer", "string"]` plus a digits pattern. The string alternative and its pattern are removed, except where `JsonNumberHandling.WriteAsString` applies (property, declaring type, or options), because those numbers are written as strings |
| Schema | FluentValidation rules become constraints (see below) |
| Schema | Every non-nullable property joins the object's `required` set |
| Operation | A member typed by a generic type parameter, in every response and JSON body schema, follows the annotation of the type argument where the type is used: `DataEnvelope<User>` publishes `data` as non-nullable and required, `DataEnvelope<User?>` as nullable, each in its own component (see below) |
| Operation | Operation ids for operations the native generator leaves unnamed |
| Operation | `deprecated` from `[Obsolete]` or an Asp.Versioning deprecated version. Parameter descriptions from model metadata |
| Operation | Query parameter names in camelCase, except names that start with `$` |
| Operation | 401 when the endpoint requires authentication, including through the fallback policy. 403 when its named policy, roles, required tenant, or applied policy requirements can refuse an authenticated user |
| Operation | `If-Match` header plus 400, 409, and 428 on `[RequireIfMatch]` endpoints. A header the endpoint binds itself is marked required instead of repeated |
| Operation | The rate limiter's rejection status (429 under `UseHeadlessProblemDetails()`, else 503) on endpoints a limiter applies to |
| Operation | `ETag` header on 2xx responses whose body type implements `IHasEntityTag` |
| Operation | 400 on operations that bind a body or a route, query, header, or form value. 413 on operations with a request body. The antiforgery token header and its 400 on endpoints that validate one |
| Operation | The FluentValidation constraints and `required` of each part of an MVC `[FromForm]` model, dotted nested parts included (see below) |
| Operation | Root-validator rules on dotted JSON member paths as use-site `allOf` overlays, without changing the nested type's shared component |
| Operation | The multipart encoding `contentType` and a description of each file part that a form's `ContentTypes`, `FileNotEmpty`, `GreaterThanOrEqualTo`, or `LessThanOrEqualTo` rule constrains |
| Operation | Security requirements on operations that require authentication |
| Operation | 404, 409, and 422 on every operation, the standard problem responses, unless a marker, the surface, or `StandardProblemResponses` turns them off |
| Operation | Typed problem-details schema and example under `application/problem+json` for 400, 401, 403, 404, 408, 409, 413, 422, 428, 429, 500, 501, and 503, plus the `Retry-After` header on 429 and 503 |
| Operation | `tenantRequired` 403 example on endpoints that require a tenant |
| Operation | Under tenant catalog resolution: the tenant header, and the 400, 404, and (with detailed errors) 403 rejections with their examples and `Cache-Control` header |
| Operation | `4XX` and `5XX` range responses typed with `HeadlessProblemDetails` on every operation, last, so they cover only the statuses the operation does not list |
| Document | Title `{Entry assembly product} API` and the framework response-conventions description |
| Document | Every configured security scheme, and one per credential of each authentication scheme that describes its credentials, under `components.securitySchemes` |
| Document | A `servers` entry with a `tenant` variable per tenant host template |
| Document | Duplicate operation ids numbered (`Orders_Find`, `Orders_Find2`) |
| Document | A generic component removed when every use moved to its nullable variant |
| Document | The `null` alternative of every `oneOf` or `anyOf` listed last, so a nullable object reads as the object first in generated clients |
| Document | Native `ProblemDetails`, `HttpValidationProblemDetails`, and `ValidationProblemDetails` components removed once no response references them |

An endpoint that answers 404 in the current host environment, because its `IEnvironmentRestrictionMetadata` (`Headless.Api.Abstractions`, for example `[RequireEnvironment]` or `[BlockInEnvironment]`) says so, is left out of every Headless document, path and schemas alike.

**What the native generator already does.** Response content types come from API Explorer's supported formats, parameter defaults and `required` come from binding metadata, inheritance is flattened, and string enums list their names. The transformers do not repeat that work.

**A `MapControllers()` convention documents a controller action.** A convention on `MapControllers()` (`RequireAuthorization(...)`, `RequireRateLimiting(...)`, `WithMetadata(...)`, `RequireIdempotencyKey()`) documents the action the way the matching attribute or Minimal API call does. `AddHeadlessOpenApi()`, `AddHeadlessApiSurfaceDocuments()`, and `ConfigureHeadlessOpenApi(...)` turn this on. A document built with native `AddOpenApi()` and only `AddHeadlessTransformers()` reads the controller and action attributes alone. The native generator's own parameters and responses still come from the action attributes.

---
## Headless.OpenApi

Headless document, operation, and schema transformers for the native ASP.NET Core OpenAPI generator.

### API and behavior

- `AddHeadlessOpenApi(string documentName = "v1", Action<OpenApiOptions>? configure = null)` registers one native document with the Headless transformers, then runs `configure`.
- `AddHeadlessApiSurfaceDocuments(IEnumerable<string>? documentNames = null, Action<OpenApiOptions, ApiSurfaceDescriptor>? configure = null)` registers one document per API surface, filtered to that surface's endpoints and titled from `surface.OpenApi.Title`.
- `ConfigureHeadlessOpenApi(Action<HeadlessOpenApiOptions>)` and `ConfigureHeadlessOpenApi(Action<HeadlessOpenApiOptions, IServiceProvider>)` configure the shared enrichment.
- `OpenApiOptions.AddHeadlessTransformers()` adds the transformers to a natively registered document.
- `FluentValidationRule` and `RuleContext` are the extension points for FluentValidation-to-schema rules.
- `HeadlessOpenApiOptions.StandardProblemResponses` (default `true`) documents 404, 409, and 422 on every operation. `false` turns them off API-wide.
- `HeadlessOpenApiOptions.ProblemResponseRanges` (default `true`) documents the `4XX` and `5XX` range responses on every operation. `false` turns them off API-wide, for a client generator that mishandles range keys.
- `StandardProblemResponsesAttribute` (`[StandardProblemResponses]`, `[StandardProblemResponses(false)]`) on a controller or action, and `WithStandardProblemResponses()` or `WithoutStandardProblemResponses()` on a Minimal API endpoint, route group, `MapControllers()`, or `MapApiSurface(...)`, from `Headless.Api`, turn the standard problem responses on or off for that endpoint. `ApiSurfaceOpenApiOptions.StandardProblemResponses` (`bool?`, `null` inherits) sets a surface default.
- To document your own endpoint or feature without a reference to this package, attach the metadata it reads. From `Headless.Api.Abstractions`: `HttpContractMetadata` (endpoint headers and responses), `IEnvironmentRestrictionMetadata` (environment-restricted endpoints), `IHttpCredentialSchemeOptions` (on authentication scheme options, the credentials the scheme reads), `StandardProblemResponsesAttribute`, and `TenantCatalogResolutionDescription`. From `Headless.FluentValidation`: `IFileSizeValidator` and `IFileContentTypeValidator` (file rules).
- Problem-details schema types: `HeadlessProblemDetails`, `BadRequestProblemDetails`, `BadRequestValidationProblemDetails`, `UnauthorizedProblemDetails`, `ForbiddenProblemDetails`, `EntityNotFoundProblemDetails`, `RequestTimeoutProblemDetails`, `ConflictProblemDetails`, `PayloadTooLargeProblemDetails`, `UnprocessableEntityProblemDetails`, `PreconditionRequiredProblemDetails`, `TooManyRequestsProblemDetails`, `InternalServerErrorProblemDetails`, `NotImplementedProblemDetails`, and `ServiceUnavailableProblemDetails`.

### Install

```bash
dotnet add package Headless.OpenApi
```

### Setup and use

For versioned documents, register one document per version. Asp.Versioning's API Explorer sets each description's group name, and the native default `ShouldInclude` keeps the descriptions whose group matches the document name:

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

**Verify:** `GET /openapi/v1.json` and `GET /openapi/v2.json` each answer 200 and list only their version's paths. A customization shows in the document, here `info.version` of `2024-01` and the `servers` entry.

#### API surfaces

`AddHeadlessApiSurfaceDocuments()` infers every surface registered before it and closes surface registration, so a later surface registration throws. An explicit list publishes only the named documents. Each name must match a configured `OpenApi.DocumentName`, and an unknown name fails startup. The surface filter runs before schema generation, so another surface's paths and schemas cannot leak into a document. The filter is independent of API Explorer groups. To narrow a surface to one version, set `ShouldInclude` in the callback, which composes with the surface filter.

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

**Verify:** each surface's `GET /openapi/{surface.OpenApi.DocumentName}.json` answers 200 with that surface's title and only its paths. A path from another surface in the document means the surface was registered after `AddHeadlessApiSurfaceDocuments()`.

#### Security schemes

The document publishes these security schemes under `components.securitySchemes`:

- `AddBearerSecurity` (default `true`) publishes an HTTP bearer scheme named `Bearer`.
- `AddApiKeySecurity` publishes an API-key scheme named `ApiKey` that reads `ApiKeyHeaderName`.
- `SecuritySchemes` adds any number of named schemes.
- Each registered authentication scheme whose options implement `IHttpCredentialSchemeOptions` publishes one scheme per credential: the first under the authentication scheme's name, each further one under that name plus its location. The `Headless.Api.Identity` handlers implement it. `AddApiKey()` publishes `ApiKey` (the configured `ApiKeyHeaderName` header) and, with `AllowApiKeyInQueryString`, `ApiKeyQuery` (the `ApiKeyParamName` query parameter). `AddBasicSchema()` publishes `Basic` (HTTP `basic`).

Leave `AddApiKeySecurity` off when you use those handlers. A configured scheme wins over a discovered one with the same name. Every operation that requires authentication gets one security requirement per accepted scheme, so a client may satisfy any one of them. `SecuritySchemeSelector` chooses the accepted schemes per operation. Without it, every registered scheme is accepted.

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

**Verify:** the document's `components.securitySchemes` lists each scheme name, and an operation that requires authentication carries a `security` entry for each scheme the selector accepts. A selector that returns an unregistered name fails document generation.

`HeadlessOpenApiOptions`:

| Property | Default | Description |
|---|---|---|
| `AddBearerSecurity` | `true` | Publishes the `Bearer` HTTP bearer (JWT) scheme |
| `AddApiKeySecurity` | `false` | Publishes the `ApiKey` header scheme |
| `ApiKeyHeaderName` | `"X-Api-Key"` | Header the built-in `ApiKey` scheme reads, the `Headless.Api.Identity` handler's default |
| `SecuritySchemes` | empty | Additional schemes by component name |
| `SecuritySchemeSelector` | `null` | Per-operation accepted scheme names. `null` accepts every registered scheme |
| `FluentValidationRules` | empty | Rules that add to or replace `FluentValidationRule.DefaultRules` by `RuleName` |
| `ThrowOnSchemaProcessingError` | `false` | Fail generation when a FluentValidation rule throws, instead of logging and skipping it |

Startup validation fails the host on an empty `ApiKeyHeaderName` while the API-key scheme is on, on an empty or `null` scheme entry, and on a custom scheme named `Bearer` or `ApiKey` while the matching built-in scheme is on.

### Design constraints

**Authorization responses follow the policies the middleware applies.**

- 401 and the security requirement need one of these: `[Authorize]` or `RequireAuthorization()` data, an `AuthorizationPolicy` or `IAuthorizationRequirementData` in the metadata, an MVC authorization filter that denies anonymous users, or a `FallbackPolicy` (which applies to every endpoint with no authorization data or policy). `[AllowAnonymous]` removes them.
- 403 also needs a named policy, roles, an effective `RequireTenant`, or a claims, name, operation, roles, assertion, or `TenantRequirement` requirement in an applied policy or requirement data.
- A bare `[Authorize]` or `RequireAuthorization()` applies `DefaultPolicy`. With the default `DefaultPolicy` it only authenticates, so it produces 401 but not 403. A `DefaultPolicy` or `FallbackPolicy` that holds `TenantRequirement` adds 403 and the `tenantRequired` example, unless the endpoint's later tenancy metadata is `AllowMissingTenant`.
- Policies come from `AuthorizationOptions`. Named policies are not resolved and always count as able to refuse. A custom `IAuthorizationPolicyProvider` that builds its default or fallback policy elsewhere is not seen.
- The roles of `[Authorize(Roles = ...)]` become the requirement's scopes, which OpenAPI 3.1 allows for any scheme type.
- A selector that returns an unregistered scheme name fails generation, because the requirement would reference a missing component.

**Errors are documented where the framework writes them.** An operation documents the error responses it declares, those its metadata and options show (authorization, `If-Match`, rate limits, feature contracts, tenancy), and the ones the server writes for an operation's shape before any handler runs:

- 400 goes on every operation that binds a body, route, query, header, form, or model-bound value. A value that fails to bind raises `BadHttpRequestException`, which the Headless exception handler writes as a 400 problem.
- 413 goes on every operation with a request body, because the server's body size limit applies to it. An endpoint whose `IRequestSizeLimitMetadata` (`[RequestSizeLimit]`) sets a limit names it. An endpoint that lifts the limit (`[DisableRequestSizeLimit]`) gets no 413.
- An endpoint whose last `IAntiforgeryMetadata` requires validation, in an app that registers antiforgery, gets an optional `AntiforgeryOptions.HeaderName` header parameter and the token reason in its 400. The token may come in the `FormFieldName` form field instead. An invalid token ends the request with 400, which the status-codes rewriter writes as a problem.

A status the operation already documents keeps its description. Every operation also documents the standard problem responses and the range responses below. Any other status, such as 500, is covered by the `5XX` range and appears as an exact status only when declared.

**Every operation documents 404, 409, and 422.** The exception handler and the `ApiResult` conversion write these statuses for not-found, conflict, and validation failures. Any handler or service can raise them, so endpoint metadata cannot tell which operations return them. They are documented whether the handler returns an `ApiResult` result type or a plain value and throws. An operation that does not raise them over-states its contract, and a client generated from it only gains branches it never takes.

- Every status the application mapped its own exceptions and errors to through `AddHeadlessProblemDetails(problems => ...)` joins them. It is typed with `HeadlessProblemDetails` unless the status already has a Headless problem shape (429, for example), and it follows the same control.
- The innermost setting decides, in both directions: a `StandardProblemResponsesAttribute` on the action, then on its controller, then a marker on the endpoint, its route group, or a `MapControllers()` convention, then the surface's `OpenApi.StandardProblemResponses`, then `HeadlessOpenApiOptions.StandardProblemResponses`. A marker always beats the surface setting, even on a route group around `MapApiSurface(...)`.
- Turning them off changes the document only. The endpoint still returns these statuses, and a `ValidateArguments()` endpoint with them off answers 422 with only the `4XX` range documenting it.
- An app that reads API Explorer metadata without this package (its own Swashbuckle setup, for example) sees no error statuses from the `ApiResult` result types.

**Range responses cover the statuses no rule predicts.** Every operation lists `4XX` and `5XX` responses typed with `HeadlessProblemDetails`, the base shape every Headless problem shares, after its exact statuses, so those keep their specific schemas. Every error the framework writes is problem details, so a range is always true: a timeout, an unavailable dependency, or an application's own mapped exception reaches a generated client as a typed problem. The endpoint markers and the surface setting do not remove the ranges. `HeadlessOpenApiOptions.ProblemResponseRanges = false` does, API-wide.

Client generators differ in how they read range keys. These results come from the upstream trackers and are not checked against this package's output:

- Kiota resolves an exact status, then the range, then `default`.
- NSwag does not support range keys, and its C# generator emits code that does not compile for them ([RicoSuter/NSwag#4141](https://github.com/RicoSuter/NSwag/issues/4141)).
- openapi-generator's `typescript-fetch` ignores them ([OpenAPITools/openapi-generator#16146](https://github.com/OpenAPITools/openapi-generator/issues/16146)).

For a document that feeds NSwag or `typescript-fetch`, set `ProblemResponseRanges = false`. Its clients then read an unlisted status as an untyped error.

**Tenant catalog resolution documents what it does before authentication.** It applies when the tenant posture records the catalog resolution capability and an identifier source is registered (`Http(http => http.ResolveFromCatalog(...))`). The document reads this from registrations, so a document generated without running the pipeline is the same. An endpoint with `[SkipTenantResolution]` gets none of it.

- Each `HeaderTenantIdentifierSourceOptions.HeaderNames` entry becomes an optional header parameter. A single header becomes required when the endpoint requires a tenant and no other identifier source is registered.
- Every resolving operation documents 400 `g:tenant_identifier_invalid`. As `TenantCatalogOptions.DetailedResolutionErrors` decides, it also documents either the collapsed 404 `g:tenant_resolution_failed`, or 404 `g:tenant_unknown` plus 403 `g:tenant_disabled` and `g:tenant_identifier_mismatch`. Each carries an optional `Cache-Control` header (`no-store`).
- A status the operation already documents keeps its description, and its example stays under a name (`badRequest`, `notFound`, `forbidden`) beside the tenant examples. With the standard problem responses on, the tenant 404 always joins the standard 404. A 400 declared as a validation problem gets no tenant example, because its schema has no `error` member.
- Each host template of `AddHostSource` without a `?` or `*` label adds a server `https://{template}` with a `tenant` variable, after the servers the document already lists.

**Rate-limit and entity-tag responses follow metadata and options.** A limiter applies when the endpoint carries `EnableRateLimitingAttribute` (`RequireRateLimiting(...)`, `[EnableRateLimiting]`) or `RateLimiterOptions.GlobalLimiter` is set. `DisableRateLimiting` anywhere in the metadata removes it, as in the middleware. The documented status is `RateLimiterOptions.RejectionStatusCode`: 429 after `UseHeadlessProblemDetails()`, the native 503 otherwise. The document cannot see whether `UseRateLimiter()` runs. A 2xx response whose body type implements `IHasEntityTag` documents an optional `ETag` header. The body type decides, because the document cannot see whether the entity-tag profile (`AddHeadlessMvcEntityTagConcurrency()`, or `AddHeadlessMinimalApiEntityTagConcurrency()` with `.WithEntityTag()`) is on.

**Feature contracts render without feature references.** An endpoint's `HttpContractMetadata` (`Headless.Api.Abstractions`) becomes header parameters, responses, and response headers:

- Each request header becomes a `header` parameter with a `string` schema.
- Each response becomes an operation response. Its error statuses then get their typed problem-details schema and example.
- Each response header becomes a `string` header on every documented response whose status falls in its range.

An entry the operation already has keeps its own description. A header the handler binds is reused and becomes required only when the metadata requires it. A declared response or response header is not replaced. `.RequireIdempotencyKey()` and `.WithIdempotency(...)` attach this metadata, so an idempotent endpoint documents its key header and rejections. See [Headless.Idempotency.ResponseReplay](idempotency.md#headlessidempotencyresponsereplay). A controller action renders the metadata a `MapControllers()` convention attaches (`MapControllers().WithMetadata(...)`, `.RequireIdempotencyKey()`) as well as its attributes.

**`ApiResult` result types publish only their success response.** These types publish 200 with `T`, 201, 202 with `T` or without a body, or a bodyless 204:

- MVC actions declared as `ApiActionResult<T>` or `ApiActionResult` (what `ApiResult.ToActionResult(...)` returns in `Headless.Api.Mvc`), with `ApiCreatedActionResult<T>` for 201 and `ApiAcceptedActionResult<T>` or `ApiAcceptedActionResult` for 202.
- Minimal API endpoints that return `ApiResultHttpResult<T>`, `ApiResultHttpResult`, `ApiResultCreatedHttpResult<T>`, `ApiResultAcceptedHttpResult<T>`, or `ApiResultAcceptedHttpResult`.

Their error responses come from the transformers like any other operation's, so an anonymous action lists no 401 or 403.

**Every non-nullable property is required.** The native generator requires only `required` members and constructor parameters. Headless treats a non-nullable property as always present, so a generated client gets a non-optional member for it, and a request body must send it. A nullable property of a component type (`Address? ShippingAddress`) stays out of `required` and is published as `oneOf: [null, $ref]`.

**A type parameter member follows the type argument at the use site.** `DataEnvelope<User>` and `DataEnvelope<User?>` are one runtime type, so the native generator publishes one schema for both, with every reference-typed `T` member nullable. Headless reads the annotation where the type is used: the MVC action's or Minimal API handler's declared return type (through `Task<>`, `ActionResult<>`, `Results<...>`, `Ok<>`, and similar wrappers), the JSON body parameter, and the members of every type those reach, nested generic types and generic base types included.

- The component named for the type (`DataEnvelopeOfUser`) describes the use with non-nullable arguments: its `T` member is non-nullable and `required`.
- A use with a nullable reference argument gets a component named with `Nullable` before that argument (`DataEnvelopeOfNullableUser`) whose `T` member is nullable. The plain component is dropped when nothing refers to it any more.
- A use whose schema comes out the same, such as `Page<User?>` whose only `T` use is in `IReadOnlyList<T>`, keeps the plain component.
- A member declared `T?` stays nullable for every argument.
- A value-type argument is part of the runtime type. `DataEnvelope<int>` publishes `DataEnvelopeOfint`, and `DataEnvelope<int?>` publishes `DataEnvelopeOfNullableint`. Native naming gives both `DataEnvelopeOfint`, and the second reuses the first one's schema. A custom `OpenApiOptions.CreateSchemaReferenceId` keeps its own ids for these types. Nullable reference variants always use the default naming.
- A use with no annotation reads as non-nullable, as the native generator reads an oblivious type. This covers `[ProducesResponseType(typeof(DataEnvelope<User>), 200)]` on an `IActionResult` action, and a Minimal API lambda whose return type is inferred, because the compiler emits no nullable annotations for an inferred lambda return type. To publish a nullable argument, declare the return type (`DataEnvelope<User?> () => ...`) or use a method.
- The element of a collection (`IReadOnlyList<T>` items) is always published non-nullable by the native generator, whatever the argument.

**Primitives are inlined.** A type that implements `IPrimitive<T>` serializes as its underlying value through its own converter, which the native generator cannot see through. Its schema becomes the underlying JSON type and format (`MoneyAmount` is `number`/`decimal`, `Month` is `integer`/`int32`, `AccountId` and `UserId` are `string`), nullable for `T?`, and it gets no component. A string primitive with `[StringLength(min, max)]` (`Headless.Generator.Primitives`) also gets `minLength` and `maxLength`, unless the attribute sets `validate: false`. A primitive that wraps another primitive (`Price : IPrimitive<Amount>`) takes the innermost value's type and format, and the tightest `[StringLength]` bounds across the chain, because a value must pass every primitive in it. Nothing else about a primitive reaches the document. Primitives from every assembly need no registration.

**FluentValidation rules.** The type's `IValidator<T>` is resolved from the document-generation scope. Rules map to schema keywords as follows:

| Rule | Schema effect |
|---|---|
| `NotNull`, `NotEmpty` | Adds the property to `required` and removes nullability (a nullable reference collapses from `oneOf: [null, $ref]` to the reference) |
| `NotEmpty` | Also sets `minLength: 1`, or `minItems: 1` on an array |
| `Length`, `MinimumLength`, `MaximumLength`, `ExactLength` | `minLength`, `maxLength` |
| `Matches` | `pattern` |
| Comparisons, `InclusiveBetween` | `minimum`, `maximum` |
| `GreaterThan`, `LessThan`, `ExclusiveBetween` | The 3.1 numeric `exclusiveMinimum`, `exclusiveMaximum`, with no `minimum` or `maximum` |
| `EmailAddress` (ASP.NET Core-compatible mode) | `pattern: ^[^@]+@[^@]+$` |

- Unconditional `Include()` chains are followed, each validator once, so an include cycle ends.
- A child validator chosen from the instance (`SetValidator(x => new NameValidator(x.Kind))`, `Include(x => ...)`) cannot be built without a request. It is logged as a warning and skipped. With `ThrowOnSchemaProcessingError` set, it fails generation instead.
- A rule or component under `When` or `Unless` is skipped, because it does not hold for every request.
- `RuleForEach` constraints land on `items`. A `RuleForEach` `NotNull` or `NotEmpty` never makes the collection itself required, because FluentValidation skips a null collection.
- A parameter bound from one property of an `[AsParameters]` or `[FromQuery]` type takes that type's rules for the property.
- An MVC `[FromForm]` model publishes each leaf property as its own multipart part (`Avatar`, `Name.First`). Each part takes the same rules and `required` that a JSON body member gets, from the model's validator under the part's path (`RuleFor(x => x.Name.First)`) and, at each nested level, from the validator registered for that level's type or attached with `SetValidator`. A part is `required` when its property is non-nullable or a `NotNull` or `NotEmpty` rule applies. The exception is a part below a nullable or optional object (`Backup.Number` with `ProfilePhone? Backup`), because a flat form cannot express "required when the parent is sent". A file part stays a `$ref` to the shared `IFormFile` component, so its constraints are only `required` and the file description below.
- A Minimal API `[FromForm]` model is published as a component, which takes the rules like a JSON body.
- The `Headless.Api.FluentValidation` file rules on a form model (`[FromForm]` in a Minimal API, or the type that declares an MVC form property) describe its file parts. `ContentTypes` sets the part's multipart encoding `contentType`, and several rules intersect. The accepted types and the size bounds of `FileNotEmpty`, `GreaterThanOrEqualTo`, and `LessThanOrEqualTo` (the tightest win) join the part's description, beside its `$ref`, so the shared `IFormFile` component stays unchanged. OpenAPI has no keyword for the size of a binary part (`maxLength` counts characters), so the bounds are text only. A custom rule takes part by implementing `IFileSizeValidator` or `IFileContentTypeValidator`.

**Explicit MVC binder names prefix property paths.** `[FromForm(Name = "upload")] UpdateAvatar form` publishes `upload.Avatar`, the name MVC binds, rather than `Avatar`. `[FromQuery(Name = "filter")]` on a complex parameter likewise publishes `filter.First` and its other property paths. A parameter's explicit name wins over the type's `[ModelBinder(Name = "...")]` name. With neither name, the paths stay unprefixed. Prefixed form parts keep their validation constraints, `required` status, file descriptions, and encoding keys. JSON bodies are not expanded into parameter paths.

**Nested JSON rules constrain only their use site.** A root rule such as `RuleFor(x => x.Name.First).NotEmpty().MaximumLength(20)` constrains the `name` property of that root type, not the shared `PersonName` component. The property becomes `allOf: [$ref, overlay]`: the first entry keeps the `PersonName` reference, and the overlay's `properties.first` has `type: string`, `minLength: 1`, and `maxLength: 20`.

- The overlay lists `first` in `required` only when a `NotNull` or `NotEmpty` rule applies.
- Another type that uses `PersonName` without the root rule keeps its plain reference.
- Deeper member chains nest the overlay's properties, and property names follow the configured JSON names.
- A nullable `Name?` keeps its null alternative outside the `allOf`, so a nested required rule does not make `Name` itself required or non-nullable.
- The rules apply to JSON request and response types and follow unconditional `Include()` chains.
- A nested `RuleForEach` constrains the items of the overlay's array. `RuleForEach(x => x.Address.Lines).MaximumLength(50)` gives the overlay's `properties.lines` `type: array` and `items` with `type: string` and `maxLength: 50`, while the shared `Address` component keeps its plain items. A `RuleForEach` `NotNull` or `NotEmpty` constrains the items and never lists the collection in the overlay's `required`.
- A `RuleForEach` over a collection published without `items`, such as a dictionary, is skipped.

**Problem-details schemas describe what the response carries.** Each known status gets its typed schema as a shared component, referenced from every operation, with a nested `ErrorDescriptor` component. The `4XX` and `5XX` ranges reference `HeadlessProblemDetails`, which carries only the members every problem shares.

- The `error` member of 400, 401, 403, 404, 408, 429, 501, and 503 is optional and nullable. 413 and 500 carry no `error`.
- `retryAfter` is required on 429 and optional on 503, matching the `Retry-After` header (required on 429, optional on 503).
- A 400 declared as `HttpValidationProblemDetails` (`ProducesValidationProblem()`, or the .NET `AddValidation()` filter) gets `BadRequestValidationProblemDetails` instead: the native title, no `detail`, and `errors` as member names mapped to plain message arrays, plus the Headless diagnostics members.
- The native `ProblemDetails` and `HttpValidationProblemDetails` components a declaration published are removed when no response references them any more.
- Other media types the endpoint declared for an error response keep their schema. Only `application/problem+json` is typed, and only when the endpoint declared no problem type of its own there. `Produces<SeatTakenProblem>(409, "application/problem+json")` keeps the `SeatTakenProblem` schema and gets no example. The native `ProblemDetails` and `HttpValidationProblemDetails` that `ProducesProblem()`, `ProducesValidationProblem()`, and `[ProducesResponseType<ProblemDetails>]` declare are replaced.
- Example text is English, because a cached document is served to readers of every culture.

**Operation ids.** A route or endpoint name wins (`WithName`, `[HttpGet(Name = ...)]`). An unnamed MVC action becomes `{Controller}_{Action}` without an `Async` suffix. An unnamed Minimal API endpoint becomes its method plus its path segments:

- A literal segment adds its words.
- A segment that is only a parameter adds `By` and the parameter name, with its constraint, default, optional, and catch-all markers dropped. The list and the item of one resource therefore get distinct ids whatever order they are mapped in: `GET /things` is `GetThings`, `GET /things/{id:guid}` is `GetThingsById`, `GET /orders/{orderId}/items/{itemId?}` is `GetOrdersByOrderIdItemsByItemId`, and `GET /files/{**path}` is `GetFilesByPath`.
- A parameter that shares its segment with literal text adds nothing. It is a prefix every operation of a group shares, such as an API version, so `GET /v{version}/things` is `GetVThings` and `GET /v{version}/things/{id}` is `GetVThingsById`.
- Ids that still repeat are numbered in path order, as a last resort. Name the endpoint (`WithName`) when an id must not depend on the path.

### Runtime behavior

- `AddHeadlessOpenApi` and `AddHeadlessApiSurfaceDocuments` call native `AddOpenApi` and register `HeadlessOpenApiOptions` with startup validation.
- The environment-restriction filter applies to a document registered with native `AddOpenApi(...)` and `AddHeadlessTransformers()` only once `AddHeadlessOpenApi`, `AddHeadlessApiSurfaceDocuments`, or `ConfigureHeadlessOpenApi` is called. It runs after every `ShouldInclude` you set in a configure callback.
- Authentication schemes are read on every generation, so a scheme added at run time appears in the next document generated rather than served from a cache.
- The native generator builds the document per request. `MapHeadlessEndpoints()` caches the response with output caching unless `OpenApi.CacheDocument` is `false`.

---
## Headless.OpenApi.Scalar

Scalar API documentation UI that renders the OpenAPI documents served at `/openapi/{documentName}.json` as an interactive browser UI.

### API and behavior

- `MapScalarOpenApi(Action<ScalarOptions>?, string endpointPrefix)` mounts Scalar at the given prefix (default `/scalar`) and reads documents from `/openapi/{documentName}.json`.
- Every Scalar option is overridable in the `setupAction` callback. The [Configuration](#configuration) table lists the defaults the framework sets.

### Design constraints

**The UI reads a fixed document route.** `MapScalarOpenApi` sets `options.OpenApiRoutePattern = "/openapi/{documentName}.json"` before it calls your callback. This matches `MapHeadlessEndpoints()`'s default `OpenApi.RoutePattern`, `MapOpenApi("/openapi/{documentName}.json")`, and native `MapOpenApi()` without a pattern. If you change the document route, override `OpenApiRoutePattern` in `setupAction`, or the UI points at a 404.

**Any OpenAPI JSON source works.** The package does not reference `Headless.OpenApi`. It calls `Scalar.AspNetCore`'s `MapScalarApiReference`, which works with any OpenAPI JSON served at the route pattern. For surface documents, add one Scalar document per surface with `options.AddDocument(surface.OpenApi.DocumentName, surface.OpenApi.Title)`.

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

**Verify:** `GET /scalar` (or the custom `endpointPrefix`, here `/docs`) answers 200, and the page loads the document from `/openapi/{documentName}.json` without a 404.

### Configuration

`MapScalarOpenApi` accepts an optional `Action<ScalarOptions>` and an `endpointPrefix` string (default `"/scalar"`) that sets where Scalar is mounted. The framework sets these values before it calls the callback, and you can override any of them in `setupAction`:

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

### Runtime behavior

- Mounts the Scalar UI at `{endpointPrefix}` (default `/scalar`) through `MapScalarApiReference`.
