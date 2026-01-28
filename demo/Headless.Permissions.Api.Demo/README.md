# Headless.Permissions.Api.Demo

Demo project showing HTTP API endpoints for permission management operations.

## Overview

This demo illustrates how to expose Framework.Permissions interfaces via REST API endpoints using Minimal API. Useful for agent-native architectures, webhooks, and cross-service permission management.

## Interactive Documentation

Run the demo and navigate to:
- **Swagger UI**: `http://localhost:5000/swagger` (or configured port)
- **OpenAPI JSON**: `http://localhost:5000/swagger/v1/swagger.json`

## Endpoints

### List All Permission Definitions

```http
GET /api/permissions
Authorization: Bearer {token}
```

Returns all registered permission definitions.

**Response:** `PermissionDefinition[]`

### Get Single Permission Definition

```http
GET /api/permissions/{name}
Authorization: Bearer {token}
```

**Validation:** `name` parameter required, max 256 chars

**Response:** `PermissionDefinition`

**404:** Permission not found

### Check Permissions

```http
GET /api/permissions/check?names=Permission1&names=Permission2
Authorization: Bearer {token}
```

Checks if current user has specified permissions.

**Validation:** At least one name required, each max 256 chars

**Response:** `GrantedPermissionResult[]`

```json
[
  {
    "name": "Permission1",
    "isGranted": true,
    "providers": [
      {
        "providerName": "User",
        "providerKey": "user-123"
      }
    ]
  }
]
```

### Grant Permission

```http
POST /api/permissions/grants
Authorization: Bearer {token}
Content-Type: application/json
X-CSRF-TOKEN: {csrf-token}
```

**Request:**

```json
{
  "name": "Permission.Name",
  "providerName": "User",
  "providerKey": "user-123"
}
```

**Validation Rules:**
- `name`: Required, 1-256 chars, pattern: `^[a-zA-Z0-9._-]+$`
- `providerName`: Required, 1-128 chars, pattern: `^[a-zA-Z0-9_-]+$`
- `providerKey`: Required, 1-256 chars

**Validation prevents:**
- Empty strings / whitespace-only values
- Oversized payloads (DoS protection)
- SQL injection / special characters
- Memory exhaustion attacks

**Authorization:** Requires `PermissionsManage` policy (Admin or PermissionsAdmin role)

**CSRF Protection:** Requires valid CSRF token in `X-CSRF-TOKEN` header (obtained from `X-CSRF-TOKEN` cookie)

**Responses:**
- 204 No Content - Success
- 400 Bad Request - Validation failed or invalid CSRF token
- 409 Conflict - Self-grant attempt (cannot grant permissions to yourself)

**400 Validation Error Example:**

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Validation Failed",
  "status": 400,
  "errors": {
    "Name": "Permission name can only contain alphanumeric characters, dots, underscores, and hyphens",
    "ProviderKey": "Provider key must be between 1 and 256 characters"
  }
}
```

**409 Self-Grant Error:**

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Conflict",
  "status": 409,
  "detail": "Cannot grant permissions to yourself",
  "code": "SelfGrantProhibited"
}
```

### Revoke Permission

```http
DELETE /api/permissions/grants?name=Permission.Name&providerName=User&providerKey=user-123
Authorization: Bearer {token}
X-CSRF-TOKEN: {csrf-token}
```

**Validation:**
- `name`: Required, max 256 chars
- `providerName`: Required, max 128 chars
- `providerKey`: Required, max 256 chars

**Authorization:** Requires `PermissionsManage` policy

**CSRF Protection:** Requires valid CSRF token in `X-CSRF-TOKEN` header

**Response:** 204 No Content

**400 Bad Request:** Validation failed or invalid CSRF token

### Revoke All Permissions for Provider

```http
DELETE /api/permissions/grants/{providerName}/{providerKey}
Authorization: Bearer {token}
X-CSRF-TOKEN: {csrf-token}
```

**Validation:**
- `providerName`: Required, max 128 chars
- `providerKey`: Required, max 256 chars

**Authorization:** Requires `PermissionsManage` policy

**CSRF Protection:** Requires valid CSRF token in `X-CSRF-TOKEN` header

**Response:** 204 No Content

**400 Bad Request:** Validation failed or invalid CSRF token

## Authorization

- All endpoints require authentication (`.RequireAuthorization()`)
- Grant/Revoke operations require `PermissionsManage` policy (`.RequireAuthorization("PermissionsManage")`)
- Policy grants access to users with `Admin` or `PermissionsAdmin` roles

## Security Features

### Input Validation

All endpoints implement comprehensive input validation to prevent security vulnerabilities:

**Data Annotations (GrantPermissionRequest):**
```csharp
[Required(AllowEmptyStrings = false)]
[StringLength(256, MinimumLength = 1)]
[RegularExpression(@"^[a-zA-Z0-9._-]+$")]
public required string Name { get; init; }
```

**Query Parameter Validation:**
- Length limits prevent DoS via oversized payloads
- Empty string checks prevent bypass attacks
- Consistent validation across all endpoints

**Prevented Attack Vectors:**
- Memory exhaustion: `{"name":"X".repeat(1000000),...}`
- Empty string bypass: `{"name":"","providerName":"","providerKey":""}`
- SQL injection: `{"name":"'; DROP TABLE--",...}`
- Special characters in permission names

**OWASP Mapping:** Addresses A03:2021 - Injection, A04:2021 - Insecure Design

### CSRF Protection

All state-changing endpoints (POST, DELETE) require antiforgery tokens to prevent cross-site request forgery attacks.

**Configuration:**
```csharp
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "X-CSRF-TOKEN";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true;
});
```

**Usage:**
1. Server sets `X-CSRF-TOKEN` cookie on first request
2. Client reads token from cookie and includes in `X-CSRF-TOKEN` header for POST/DELETE requests
3. Server validates token via `.RequireAntiforgery()` on endpoints

**Protected endpoints:**
- `POST /api/permissions/grants`
- `DELETE /api/permissions/grants`
- `DELETE /api/permissions/grants/{providerName}/{providerKey}`

**Note:** For JWT Bearer authentication (no cookies), CSRF protection is less critical as tokens are in Authorization headers. However, protection is configured per defense-in-depth principles.

**OWASP Mapping:** Addresses OWASP A01:2021 - Broken Access Control

### Self-Grant Prevention

The grant endpoint prevents users from granting permissions to themselves to avoid privilege escalation attacks and maintain separation of duties.

**Validation:**
- Checks if `providerName == "User"` and `providerKey == currentUser.UserId`
- Returns `409 Conflict` if self-grant attempt detected
- Role-based grants (`providerName == "Role"`) are not affected by this validation

**Security rationale:**
- Prevents privilege escalation (admin granting themselves super-admin rights)
- Enforces separation of duties principle
- Maintains audit trail integrity
- Aligns with OWASP A01:2021 - Broken Access Control guidelines

## Authentication Setup

The API uses JWT Bearer authentication. Configure your identity provider settings in `appsettings.json`:

```json
{
  "Auth": {
    "Authority": "https://your-identity-provider.com",
    "Audience": "permissions-api"
  }
}
```

### Configuration Parameters

- **Authority**: URL of your JWT token issuer (identity provider)
  - Examples: Auth0 (`https://your-tenant.auth0.com`), Azure AD (`https://login.microsoftonline.com/{tenant-id}/v2.0`), IdentityServer
- **Audience**: Expected audience claim in JWT tokens (typically your API identifier)

### Development Setup

For local development, `appsettings.Development.json` is pre-configured with localhost settings:

```json
{
  "Auth": {
    "Authority": "https://localhost:5001",
    "Audience": "permissions-api"
  }
}
```

### Obtaining JWT Tokens

**Option 1: Use an existing identity provider**
- Configure with your Auth0, Azure AD, Okta, or other OAuth 2.0 provider
- Obtain tokens via OAuth 2.0 flows (authorization code, client credentials, etc.)

**Option 2: Run a local identity server** (for development/testing)
- Use Duende IdentityServer, IdentityServer4, or similar
- Configure to issue tokens with required claims (roles: "Admin" or "PermissionsAdmin")

**Option 3: Generate test tokens** (development only)
```bash
# Example using https://jwt.io or similar tools
# Include claims: { "role": ["Admin"], "sub": "user-123" }
```

### Required JWT Claims

For full API access, JWT tokens must include:
- **Role claim**: `Admin` or `PermissionsAdmin` (for grant/revoke operations)
- **Subject claim** (`sub`): User identifier (for permission checks and self-grant prevention)

### Example API Request

```bash
curl -X GET https://localhost:5000/api/permissions \
  -H "Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9..."
```

### Authentication Failure Responses

- **401 Unauthorized**: Missing or invalid JWT token
- **403 Forbidden**: Valid token but insufficient permissions (missing required role)

## Rate Limiting

API implements DoS protection via fixed window rate limiting:

- **Global limit**: 100 requests/minute per user or IP
- **Check endpoint**: 50 requests/minute (expensive query operations)
- **Grant/Revoke operations**: 10 requests/minute (sensitive operations)

**429 Response** (rate limit exceeded):

```json
{
  "type": "https://tools.ietf.org/html/rfc6585#section-4",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Please try again later."
}
```

**Headers**:
- `Retry-After`: Seconds until rate limit window resets

Rate limits partition by:
1. Authenticated user identity (`context.User.Identity.Name`)
2. IP address (if unauthenticated)
3. "anonymous" (fallback)

**OWASP Mapping:** Addresses A04:2021 - Insecure Design (Denial of Service)

## Configuration

Update `Program.cs` to customize authorization or rate limiting:

```csharp
// Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PermissionsManage", policy =>
        policy.RequireAssertion(context =>
            context.User.Identity?.IsAuthenticated == true &&
            (context.User.IsInRole("Admin") || context.User.IsInRole("PermissionsAdmin"))));
});

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
});
```

## Running the Demo

```bash
cd demo/Headless.Permissions.Api.Demo
dotnet run
```

## Key Implementation Details

- **Minimal API endpoints** - Uses `app.MapGet()`, `app.MapPost()`, `app.MapDelete()`
- **Input validation** - Data Annotations for request models, helper functions for query parameters
- **Returns domain models directly** - No DTOs for responses (PermissionDefinition, GrantedPermissionResult)
- **Minimal DTO** - Only GrantPermissionRequest for POST operations
- **Problem Details** - Manual construction for error responses (NotFound, BadRequest, ValidationProblem)
- **ICurrentUser injection** - Passed directly to IPermissionManager operations
- **Policy-based authorization** - Separates read (`.RequireAuthorization()`) and write (`.RequireAuthorization("PermissionsManage")`) permissions

**Example endpoint with validation:**

```csharp
app.MapPost("/api/permissions/grants", async (
    [FromBody] GrantPermissionRequest request,
    IPermissionManager permissionManager,
    ICurrentUser currentUser,
    CancellationToken ct) =>
{
    // Validate request model
    var validationError = ValidateInput(request);
    if (validationError is not null)
    {
        return validationError;
    }

    // Business logic...
    await permissionManager.SetAsync(request.Name, request.ProviderName, request.ProviderKey, isGranted: true, ct);
    return Results.NoContent();
}).RequireAuthorization("PermissionsManage")
  .RequireAntiforgery();
```

## References

- IPermissionManager: `src/Headless.Permissions.Abstractions/Grants/IPermissionManager.cs`
- IPermissionDefinitionManager: `src/Headless.Permissions.Abstractions/Definitions/IPermissionDefinitionManager.cs`
- Minimal API docs: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis
- ASP.NET Core Model Validation: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation
